using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using CarMecaSim.Mp.Net;

// FakePeer — a headless "player 2" that connects to the mod's loopback transport (LoopbackNet) over
// 127.0.0.1 and speaks the exact co-op wire protocol. Use it to test the host end-to-end on one PC:
//   1. Install the mod built with loopback support, set LoopbackPort in mp_config.json.
//   2. Launch the game, load a save, enter the garage (the mod auto-hosts on the loopback port).
//   3. Run this: dotnet run --project tools/FakePeer -- <port> [name] [seconds]
// It joins, requests the host's save snapshot, walks an avatar next to the host, sends chat, bumps the
// shared money, and answers latency probes — printing everything it exchanges.

namespace CarMecaSim.FakePeer
{
    internal static class Program
    {
        static NetworkStream _stream;
        static readonly object _writeLock = new object();

        // reassembly of the chunked FullState snapshot
        static byte[] _saveBuf; static int _chunksExpected, _chunksGot;

        // latest host avatar position (so we can stand next to the player)
        static volatile bool _haveHost; static float _hx, _hy, _hz;

        // verification tally
        static volatile bool _gotSnapshot, _gotEconomy, _gotHostPresence, _gotLatencyProbe, _gotHostHello;
        static int _snapshotBytes;

        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "selftest") return SelfTest.Run();
            int port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 48900;
            string name = args.Length > 1 ? args[1] : "FakePeer";
            int seconds = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 30;
            return RunClient(port, name, seconds);
        }

        public static int RunClient(int port, string name, int seconds)
        {
            Console.WriteLine($"[fakepeer] connecting to 127.0.0.1:{port} as \"{name}\" for {seconds}s...");
            TcpClient client;
            try { client = new TcpClient(); client.Connect("127.0.0.1", port); client.NoDelay = true; }
            catch (Exception e) { Console.WriteLine("[fakepeer] connect failed: " + e.Message + "\n  -> is the game running, in the garage, with LoopbackPort set?"); return 2; }
            _stream = client.GetStream();
            Console.WriteLine("[fakepeer] connected.");

            var reader = new Thread(() => ReadLoop(client)) { IsBackground = true };
            reader.Start();

            // Greet the host and ask for the full save snapshot (what a real client does on join).
            SendHello(name);
            SendPing();
            Console.WriteLine("[fakepeer] -> Hello + Ping (requested host snapshot)");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool chatted = false, bumped = false;
            double nextPresence = 0, nextPing = 3.0;
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                double t = sw.Elapsed.TotalSeconds;

                // Re-request the snapshot until it arrives — the host only becomes authoritative once the
                // player reaches the garage, so an early Ping (before that) gets no reply (like a real client).
                if (!_gotSnapshot && t >= nextPing) { nextPing = t + 3.0; SendPing(); Console.WriteLine("[fakepeer] -> Ping (relance demande snapshot)"); }

                // ~20 Hz presence: orbit a 1.5 m circle next to the host's avatar.
                if (t >= nextPresence)
                {
                    nextPresence = t + 0.05;
                    float ang = (float)(t * 1.2);
                    float bx = _haveHost ? _hx : 0f, by = _haveHost ? _hy : 0f, bz = _haveHost ? _hz : 0f;
                    float px = bx + (float)Math.Cos(ang) * 1.5f;
                    float pz = bz + (float)Math.Sin(ang) * 1.5f;
                    float yaw = (ang * 57.2958f) % 360f;
                    SendPresence(px, by, pz, yaw, 0f, 0, 1);
                }

                if (!chatted && t >= 2.0) { chatted = true; SendChat(name, "Salut depuis FakePeer, je repare avec toi"); Console.WriteLine("[fakepeer] -> Chat"); }
                if (!bumped && t >= 4.0) { bumped = true; SendEconomyDelta(5000, 100, 25); Console.WriteLine("[fakepeer] -> Economy delta (+5000$ / +100xp / +25 scraps)"); }

                Thread.Sleep(10);
            }

            SendBye();
            Console.WriteLine("[fakepeer] -> Bye");
            Thread.Sleep(200);

            Console.WriteLine("\n==================== RÉSUMÉ DU TEST ====================");
            Check("Host Hello reçu (nom du joueur)",            _gotHostHello);
            Check($"Snapshot save reçu ({_snapshotBytes} octets)", _gotSnapshot);
            Check("Économie absolue reçue (argent partagé)",     _gotEconomy);
            Check("Présence de l'hôte reçue (avatar hôte)",      _gotHostPresence);
            Check("Sonde de latence de l'hôte renvoyée (ping)",  _gotLatencyProbe);
            Console.WriteLine("=======================================================");
            bool ok = _gotSnapshot && _gotEconomy && _gotHostPresence;
            Console.WriteLine(ok ? "[fakepeer] RÉUSSI — l'hôte a bien servi le garage + l'économie + la présence."
                                 : "[fakepeer] INCOMPLET — voir les cases non cochées ci-dessus.");
            return ok ? 0 : 1;
        }

        static void Check(string label, bool ok) => Console.WriteLine($"  [{(ok ? "x" : " ")}] {label}");

        // ---------- send ----------
        static void Frame(byte[] payload, int channel)
        {
            var s = _stream; if (s == null) return;
            var head = new byte[5];
            int len = payload.Length;
            head[0] = (byte)(len & 0xFF); head[1] = (byte)((len >> 8) & 0xFF);
            head[2] = (byte)((len >> 16) & 0xFF); head[3] = (byte)((len >> 24) & 0xFF);
            head[4] = (byte)channel;
            try { lock (_writeLock) { s.Write(head, 0, 5); s.Write(payload, 0, len); s.Flush(); } } catch { }
        }

        static void SendHello(string name)
        { using var w = new PacketWriter(PacketType.Hello); w.Str(name).Int(Protocol.Version); Frame(w.ToArray(), Channels.Reliable); }

        static void SendPing()
        { using var w = new PacketWriter(PacketType.Ping); Frame(w.ToArray(), Channels.Reliable); }

        static void SendBye()
        { using var w = new PacketWriter(PacketType.Bye); Frame(w.ToArray(), Channels.Reliable); }

        static void SendChat(string name, string text)
        { using var w = new PacketWriter(PacketType.Chat); w.Str(name).Str(text); Frame(w.ToArray(), Channels.Reliable); }

        static void SendEconomyDelta(int dMoney, int dExp, int dScraps)
        { using var w = new PacketWriter(PacketType.Economy); w.Byte(1).Int(dMoney).Int(dExp).Int(dScraps); Frame(w.ToArray(), Channels.Reliable); }

        static void SendPresence(float x, float y, float z, float yaw, float pitch, byte tool, byte mode)
        { using var w = new PacketWriter(PacketType.Presence); w.Vec3(x, y, z).Float(yaw).Float(pitch).Byte(tool).Byte(mode); Frame(w.ToArray(), Channels.Unreliable); }

        static void SendLatencyEcho(uint stamp)
        { using var w = new PacketWriter(PacketType.Latency); w.Byte(1).UInt(stamp); Frame(w.ToArray(), Channels.Unreliable); }

        // ---------- receive ----------
        static void ReadLoop(TcpClient client)
        {
            var s = client.GetStream();
            var head = new byte[5];
            try
            {
                while (true)
                {
                    if (!ReadExact(s, head, 5)) break;
                    int len = head[0] | (head[1] << 8) | (head[2] << 16) | (head[3] << 24);
                    int channel = head[4];
                    if (len < 0 || len > 64 * 1024 * 1024) break;
                    var payload = new byte[len];
                    if (len > 0 && !ReadExact(s, payload, len)) break;
                    Handle(payload, channel);
                }
            }
            catch { }
            Console.WriteLine("[fakepeer] connection closed by host.");
        }

        static bool ReadExact(NetworkStream s, byte[] buf, int n)
        { int off = 0; while (off < n) { int r = s.Read(buf, off, n - off); if (r <= 0) return false; off += r; } return true; }

        static int _presenceCount;
        static void Handle(byte[] data, int channel)
        {
            try
            {
                var r = new PacketReader(data);
                switch (r.Type)
                {
                    case PacketType.Hello:
                    { var n = r.Str(); _gotHostHello = true; Console.WriteLine($"[fakepeer] <- Hello de l'hôte: \"{n}\""); break; }

                    case PacketType.Presence:
                    {
                        r.Vec3(out var x, out var y, out var z); r.Float(); r.Float(); r.Byte(); r.Byte();
                        _hx = x; _hy = y; _hz = z; _haveHost = true;
                        if (!_gotHostPresence) { _gotHostPresence = true; Console.WriteLine($"[fakepeer] <- 1re présence hôte @ ({x:0.0},{y:0.0},{z:0.0})"); }
                        if ((_presenceCount++ % 100) == 0) Console.WriteLine($"[fakepeer] <- présence hôte @ ({x:0.0},{y:0.0},{z:0.0})");
                        break;
                    }

                    case PacketType.Chat:
                    { var n = r.Str(); var txt = r.Str(); Console.WriteLine($"[fakepeer] <- CHAT {n}: {txt}"); break; }

                    case PacketType.Economy:
                    {
                        var kind = r.Byte();
                        if (kind == 0)
                        {
                            int m = r.Int(), x = r.Int(), sc = r.Int(), lvl = r.Int();
                            _gotEconomy = true;
                            Console.WriteLine($"[fakepeer] <- ÉCONOMIE  argent={m}  xp={x}  ferraille={sc}  niveau={lvl}");
                        }
                        break;
                    }

                    case PacketType.CarOp:
                    {
                        var op = r.Byte();
                        if (op == 2 /*OpGarage (legacy)*/) { int n = r.Int(); Console.WriteLine($"[fakepeer] <- GARAGE {n} voiture(s)"); for (int k = 0; k < n; k++) { int idx = r.Int(); string nm = r.Str(); Console.WriteLine($"              [{idx}] {nm}"); } }
                        break;
                    }

                    case PacketType.Order:
                    {
                        var op = r.Byte();
                        if (op == 0 /*OpList*/) { int n = r.Int(); Console.WriteLine($"[fakepeer] <- MISSIONS {n} job(s)"); }
                        break;
                    }

                    case PacketType.Latency:
                    {
                        var kind = r.Byte(); var stamp = r.UInt();
                        if (kind == 0) { SendLatencyEcho(stamp); _gotLatencyProbe = true; }   // host probe -> echo so it computes our RTT
                        // (a real client never receives kind==1; only the host computes RTT)
                        break;
                    }

                    case PacketType.FullState:
                    {
                        bool delta = (r.Flags & 1) != 0;
                        int total = r.Int(), ci = r.Int(), chunks = r.Int(), clen = r.Int();
                        var chunk = r.BytesRaw(clen);
                        if (_saveBuf == null || _saveBuf.Length != total || _chunksExpected != chunks)
                        { _saveBuf = new byte[total]; _chunksExpected = chunks; _chunksGot = 0; }
                        int dst = ci * (256 * 1024);
                        if (dst >= 0 && dst + clen <= _saveBuf.Length) Array.Copy(chunk, 0, _saveBuf, dst, clen);
                        if (++_chunksGot >= _chunksExpected)
                        {
                            _gotSnapshot = true; _snapshotBytes = total;
                            var outPath = Path.Combine(Path.GetTempPath(), $"carmeca_snapshot_{(delta ? "delta" : "initial")}.bin");
                            try { File.WriteAllBytes(outPath, _saveBuf); } catch { }
                            Console.WriteLine($"[fakepeer] <- SNAPSHOT {(delta ? "delta" : "initial")} complet: {total} octets en {chunks} chunk(s) -> {outPath}");
                            _saveBuf = null; _chunksExpected = 0; _chunksGot = 0;
                        }
                        break;
                    }
                }
            }
            catch (Exception e) { Console.WriteLine("[fakepeer] parse error: " + e.Message); }
        }
    }
}
