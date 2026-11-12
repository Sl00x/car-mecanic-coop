using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using CarMecaSim.Mp.Net;

namespace CarMecaSim.FakePeer
{
    /// <summary>
    /// Offline validation: spins up a MockHost that replicates LoopbackNet's exact 5-byte framing and
    /// representative host traffic (chunked snapshot, economy, presence, latency probe), then runs the
    /// real FakePeer client against it. Proves the wire format + chunk reassembly without the game.
    /// </summary>
    internal static class SelfTest
    {
        public static int Run()
        {
            const int port = 48977;
            Console.WriteLine("[selftest] starting MockHost on 127.0.0.1:" + port);
            var host = new MockHost(port);
            host.Start();
            Thread.Sleep(300);

            int rc = Program.RunClient(port, "FakePeer-SelfTest", 8);

            Thread.Sleep(300);
            host.Stop();
            Console.WriteLine("\n==================== RÉSUMÉ HÔTE (mock) ====================");
            host.PrintSummary();
            Console.WriteLine("===========================================================");

            bool hostOk = host.GotHello && host.GotPing && host.GotChat && host.GotEcoDelta && host.GotPresence && host.GotLatencyEcho;
            Console.WriteLine(hostOk ? "[selftest] HÔTE OK — a reçu Hello+Ping+Chat+Economy-delta+Présence+écho latence du client."
                                     : "[selftest] HÔTE INCOMPLET — voir ci-dessus.");
            return (rc == 0 && hostOk) ? 0 : 1;
        }

        private sealed class MockHost
        {
            private readonly int _port;
            private TcpListener _listener;
            private NetworkStream _stream;
            private readonly object _wl = new object();
            private volatile bool _run = true;
            private int _snapTotal;

            public bool GotHello, GotPing, GotChat, GotEcoDelta, GotPresence, GotLatencyEcho;

            public MockHost(int port) { _port = port; }

            public void Start()
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
                new Thread(Accept) { IsBackground = true }.Start();
            }

            public void Stop() { _run = false; try { _listener.Stop(); } catch { } }

            private void Accept()
            {
                TcpClient c;
                try { c = _listener.AcceptTcpClient(); } catch { return; }
                c.NoDelay = true;
                _stream = c.GetStream();
                Console.WriteLine("[mockhost] client connected");
                new Thread(() => Read(c)) { IsBackground = true }.Start();
                new Thread(PresenceLoop) { IsBackground = true }.Start();
                // probe latency once so the client echoes it back
                Thread.Sleep(500);
                using (var w = new PacketWriter(PacketType.Latency)) { w.Byte(0).UInt(123456u); Frame(w.ToArray(), Channels.Unreliable); }
            }

            private void PresenceLoop()
            {
                double t = 0;
                while (_run && _stream != null)
                {
                    float x = (float)Math.Cos(t) * 2f, z = (float)Math.Sin(t) * 2f;
                    using (var w = new PacketWriter(PacketType.Presence)) { w.Vec3(x, 1.7f, z).Float(0f).Float(0f).Byte(0).Byte(1); Frame(w.ToArray(), Channels.Unreliable); }
                    t += 0.1; Thread.Sleep(50);
                }
            }

            private void OnPing()
            {
                GotPing = true;
                // 1) host greeting
                using (var w = new PacketWriter(PacketType.Hello)) { w.Str("MockHost").Int(Protocol.Version); Frame(w.ToArray(), Channels.Reliable); }
                // 2) chunked FullState — 600000 bytes over 256KB chunks (exactly MpSession's scheme)
                const int chunkSize = 256 * 1024;
                _snapTotal = 600000;
                var data = new byte[_snapTotal];
                for (int i = 0; i < data.Length; i++) data[i] = (byte)(i * 31 + 7);   // deterministic pattern
                int chunks = (_snapTotal + chunkSize - 1) / chunkSize;
                for (int c = 0; c < chunks; c++)
                {
                    int off = c * chunkSize, len = Math.Min(chunkSize, _snapTotal - off);
                    using var w = new PacketWriter(PacketType.FullState, 0);
                    w.Int(_snapTotal).Int(c).Int(chunks).Int(len).BytesRaw(data, off, len);
                    Frame(w.ToArray(), Channels.Reliable);
                }
                Console.WriteLine($"[mockhost] sent snapshot {_snapTotal}B in {chunks} chunks");
                // 3) economy absolute
                using (var w = new PacketWriter(PacketType.Economy)) { w.Byte(0).Int(123456).Int(4200).Int(88).Int(7); Frame(w.ToArray(), Channels.Reliable); }
                // 4) a job list
                using (var w = new PacketWriter(PacketType.Order)) { w.Byte(0).Int(0); Frame(w.ToArray(), Channels.Reliable); }
            }

            private void Read(TcpClient c)
            {
                var s = c.GetStream(); var head = new byte[5];
                try
                {
                    while (_run)
                    {
                        if (!ReadExact(s, head, 5)) break;
                        int len = head[0] | (head[1] << 8) | (head[2] << 16) | (head[3] << 24);
                        var payload = new byte[len];
                        if (len > 0 && !ReadExact(s, payload, len)) break;
                        var r = new PacketReader(payload);
                        switch (r.Type)
                        {
                            case PacketType.Hello: GotHello = true; Console.WriteLine($"[mockhost] <- Hello: {r.Str()}"); break;
                            case PacketType.Ping: Console.WriteLine("[mockhost] <- Ping (snapshot requested)"); OnPing(); break;
                            case PacketType.Chat: { var n = r.Str(); var t = r.Str(); GotChat = true; Console.WriteLine($"[mockhost] <- Chat {n}: {t}"); break; }
                            case PacketType.Economy: { var k = r.Byte(); if (k == 1) { GotEcoDelta = true; Console.WriteLine($"[mockhost] <- Economy delta +{r.Int()}$/+{r.Int()}xp/+{r.Int()}sc"); } break; }
                            case PacketType.Presence: GotPresence = true; break;
                            case PacketType.Latency: { var k = r.Byte(); if (k == 1) { GotLatencyEcho = true; Console.WriteLine("[mockhost] <- Latency echo (RTT computable)"); } break; }
                            case PacketType.Bye: Console.WriteLine("[mockhost] <- Bye"); break;
                        }
                    }
                }
                catch { }
            }

            private static bool ReadExact(NetworkStream s, byte[] buf, int n)
            { int off = 0; while (off < n) { int r = s.Read(buf, off, n - off); if (r <= 0) return false; off += r; } return true; }

            private void Frame(byte[] payload, int channel)
            {
                var s = _stream; if (s == null) return;
                var head = new byte[5]; int len = payload.Length;
                head[0] = (byte)(len & 0xFF); head[1] = (byte)((len >> 8) & 0xFF);
                head[2] = (byte)((len >> 16) & 0xFF); head[3] = (byte)((len >> 24) & 0xFF); head[4] = (byte)channel;
                try { lock (_wl) { s.Write(head, 0, 5); s.Write(payload, 0, len); s.Flush(); } } catch { }
            }

            public void PrintSummary()
            {
                Console.WriteLine($"  [{(GotHello ? "x" : " ")}] Hello du client reçu");
                Console.WriteLine($"  [{(GotPing ? "x" : " ")}] Ping (demande snapshot) reçu");
                Console.WriteLine($"  [{(GotChat ? "x" : " ")}] Chat du client reçu");
                Console.WriteLine($"  [{(GotEcoDelta ? "x" : " ")}] Delta d'économie reçu (le client a dépensé/gagné)");
                Console.WriteLine($"  [{(GotPresence ? "x" : " ")}] Présence du client reçue (son avatar bouge)");
                Console.WriteLine($"  [{(GotLatencyEcho ? "x" : " ")}] Écho de latence reçu");
            }
        }
    }
}
