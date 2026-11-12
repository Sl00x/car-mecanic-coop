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

        // --- real-time part-action sync (the actual co-op) ---
        // PartSync actions: 0=mount(install) 1=unmount(remove) 2=bolt 3=release-lock
        const byte ActMount = 0, ActUnmount = 1, ActBolt = 2, ActRelease = 3;
        static volatile bool _gotPartAction, _replayedPart;
        static string _lastPartKey; static byte _lastPartAction; static int _lastBolt;
        static readonly object _partLock = new object();
        // every removed-part key the host tells us about (join catch-up + live) = our reconstructed mechanical state
        static readonly System.Collections.Generic.HashSet<string> _removedParts = new();
        static volatile bool _gotHashReport; static volatile bool _lastSyncOk;

        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "unittest") return CoopTests.Run();
            if (args.Length > 0 && args[0] == "selftest") return SelfTest.Run();
            if (args.Length > 0 && args[0] == "ghostmech")
            {
                int gp = args.Length > 1 && int.TryParse(args[1], out var gpp) ? gpp : 48900;
                int gs = args.Length > 2 && int.TryParse(args[2], out var gss) ? gss : 25;
                return RunGhostMech(gp, gs);
            }
            if (args.Length > 0 && args[0] == "ghostclose")
            {
                int gp = args.Length > 1 && int.TryParse(args[1], out var gpp) ? gpp : 48900;
                int gs = args.Length > 2 && int.TryParse(args[2], out var gss) ? gss : 25;
                return RunGhostClose(gp, gs);
            }
            // switchone <port> <name> <open0or1> : open/close one openable by name
            if (args.Length >= 4 && args[0] == "switchone")
            {
                int sp = int.TryParse(args[1], out var spp) ? spp : 48900;
                int so = int.TryParse(args[3], out var soo) ? soo : 0;
                return RunSwitchOne(sp, args[2], so);
            }
            // mountone <port> <type> <key> <index> : send a single targeted MOUNT (host re-shows that exact part)
            if (args.Length >= 5 && args[0] == "mountone")
            {
                int mp = int.TryParse(args[1], out var mpp) ? mpp : 48900;
                byte mt = byte.TryParse(args[2], out var mtt) ? mtt : (byte)0;
                int mk = int.TryParse(args[3], out var mkk) ? mkk : 0;
                int mi = int.TryParse(args[4], out var mii) ? mii : -1;
                return RunMountOne(mp, mt, mk, mi);
            }
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

                // REAL-TIME part co-op (the ghost test): once the host does ANY part action, we send back a
                // batch of TAKE-OFF commands for parts that are usually still attached (right side + bumpers)
                // so the player SEES parts come off their own car without touching anything — proof that a
                // peer's actions apply live on the shared car.
                if (_gotPartAction && !_replayedPart)
                {
                    _replayedPart = true;
                    foreach (var pk in new[] { "bumper_front", "fender_front_right", "door_front_right", "mirror_right", "headlight_right", "bumper_rear", "fender_front_left" })
                    {
                        SendPartAction(ActUnmount, pk, -1);
                        Console.WriteLine($"[fakepeer] -> DÉMONTER \"{pk}\"");
                    }
                    Console.WriteLine("[fakepeer] >>> REGARDE TA VOITURE : des pièces vont se démonter toutes seules <<<");
                }

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
            Check("Action pièce de l'hôte reçue (TEMPS RÉEL)",   _gotPartAction);
            Check("Action pièce renvoyée (tu l'as vue ?)",       _replayedPart);
            Check("Hash de l'hôte reçu (détection désync §14.3)", _gotHashReport);
            if (_gotHashReport) Check("État mécanique EN SYNC avec l'hôte (hash identique)", _lastSyncOk);
            lock (_partLock)
            {
                Console.WriteLine($"  >>> ÉTAT MÉCANIQUE reconstruit : {_removedParts.Count} pièce(s) retirée(s) sur la voiture partagée :");
                foreach (var k in _removedParts) Console.WriteLine($"        - {k} (absente)");
            }
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

        /// <summary>Visual proof of the mechanical apply WITHOUT a second rendered client: connect to the
        /// live host and send a wave of mechanical UNMOUNTs. The host runs ApplyRemote (HideBySavegame) on
        /// its own rendered car → the player watches engine/suspension parts come off by themselves. This is
        /// the exact code that would move a part on a real player 2's screen.</summary>
        public static int RunGhostMech(int port, int seconds)
        {
            Console.WriteLine($"[ghostmech] connexion 127.0.0.1:{port} pour {seconds}s (joueur 2 fantôme)...");
            TcpClient client;
            try { client = new TcpClient(); client.Connect("127.0.0.1", port); client.NoDelay = true; }
            catch (Exception e) { Console.WriteLine("[ghostmech] connexion échouée: " + e.Message + "\n  -> l'hôte est-il au garage, LoopbackPort actif ?"); return 2; }
            _stream = client.GetStream();
            var reader = new Thread(() => ReadLoop(client)) { IsBackground = true }; reader.Start();
            SendHello("Joueur2"); SendPing();
            Thread.Sleep(1500);

            Console.WriteLine("[ghostmech] >>> REGARDE LE MOTEUR : des pièces vont se démonter toutes seules <<<");
            int sent = 0;
            // engine parts (type 0, single per key) — spaced so they visibly come off one by one
            for (int key = 0; key < 45; key++) { SendPartState(0, key, -1, false); sent++; Thread.Sleep(130); }
            // suspension (type 1, grouped: key = root 0..5, index 0..11)
            for (int root = 0; root < 6; root++) for (int idx = 0; idx < 12; idx++) { SendPartState(1, root, idx, false); sent++; }
            Console.WriteLine($"[ghostmech] {sent} démontages mécaniques envoyés. (recharge ta save pour tout remettre)");

            // Openables (tâche 2): ouvrir capot/coffre -> l'hôte applique SwitchCarPart -> ça s'ouvre à l'écran.
            Console.WriteLine("[ghostmech] >>> et des OUVRANTS (capot/coffre) vont s'ouvrir tout seuls <<<");
            foreach (var o in new[] { "hood", "trunk", "engine_cover_openable", "clamshell_front_openable", "clamshell_rear_openable" })
            { SendPartAction(4 /*ActSwitch*/, o, 1); System.Threading.Thread.Sleep(250); }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds) Thread.Sleep(200);
            SendBye();
            Console.WriteLine("[ghostmech] terminé.");
            return 0;
        }

        /// <summary>The reverse of ghostmech: re-MOUNT mechanical parts (so a removed throttle body / parts
        /// come back) and CLOSE the openables (hood/trunk/…). The host applies it on its own rendered car.</summary>
        public static int RunGhostClose(int port, int seconds)
        {
            Console.WriteLine($"[ghostclose] connexion 127.0.0.1:{port} pour {seconds}s (joueur 2 fantôme)...");
            TcpClient client;
            try { client = new TcpClient(); client.Connect("127.0.0.1", port); client.NoDelay = true; }
            catch (Exception e) { Console.WriteLine("[ghostclose] connexion échouée: " + e.Message); return 2; }
            _stream = client.GetStream();
            var reader = new Thread(() => ReadLoop(client)) { IsBackground = true }; reader.Start();
            SendHello("Joueur2"); SendPing();
            Thread.Sleep(1500);

            Console.WriteLine("[ghostclose] >>> REGARDE : des pièces vont se REMONTER (dont le papillon) <<<");
            int sent = 0;
            for (int key = 0; key < 59; key++) { SendPartState(0, key, -1, true); sent++; Thread.Sleep(90); }           // engine
            for (int k = 0; k < 34; k++) for (int idx = 0; idx < 4; idx++) { SendPartState(2, k, idx, true); sent++; } // other (papillon est souvent 'other')
            Console.WriteLine($"[ghostclose] {sent} remontages envoyés.");

            Console.WriteLine("[ghostclose] >>> et le CAPOT / COFFRE vont se FERMER <<<");
            foreach (var o in new[] { "hood", "trunk", "engine_cover_openable", "clamshell_front_openable", "clamshell_rear_openable" })
            { SendPartAction(4 /*ActSwitch*/, o, 0 /*fermé*/); System.Threading.Thread.Sleep(250); }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds) Thread.Sleep(200);
            SendBye();
            Console.WriteLine("[ghostclose] terminé.");
            return 0;
        }

        /// <summary>Open/close engine-area openables (the player's "capot moteur"): tries hood + the common
        /// engine-cover names in one go. open=1 opens, open=0 closes.</summary>
        public static int RunSwitchOne(int port, string name, int open)
        {
            Console.WriteLine($"[switchone] connexion 127.0.0.1:{port} — ouvrant '{name}' open={open}...");
            TcpClient client;
            try { client = new TcpClient(); client.Connect("127.0.0.1", port); client.NoDelay = true; }
            catch (Exception e) { Console.WriteLine("[switchone] connexion échouée: " + e.Message); return 2; }
            _stream = client.GetStream();
            var reader = new Thread(() => ReadLoop(client)) { IsBackground = true }; reader.Start();
            SendHello("Joueur2"); SendPing();
            Thread.Sleep(1500);
            Console.WriteLine($"[switchone] >>> REGARDE le capot moteur ({(open != 0 ? "ouverture" : "fermeture")}) <<<");
            // "all" → close/open every engine-area openable; otherwise just the named one.
            if (name == "all")
                foreach (var o in new[] { "hood", "engine_cover_openable", "clamshell_front_openable" })
                { SendPartAction(4, o, open); Thread.Sleep(300); }
            else SendPartAction(4, name, open);
            Thread.Sleep(6000);
            SendBye();
            Console.WriteLine("[switchone] terminé.");
            return 0;
        }

        /// <summary>Send ONE targeted mechanical MOUNT — the host re-shows exactly that (type,key,index) part.</summary>
        public static int RunMountOne(int port, byte type, int key, int index)
        {
            Console.WriteLine($"[mountone] connexion 127.0.0.1:{port} — remontage ciblé type={type} key={key} idx={index}...");
            TcpClient client;
            try { client = new TcpClient(); client.Connect("127.0.0.1", port); client.NoDelay = true; }
            catch (Exception e) { Console.WriteLine("[mountone] connexion échouée: " + e.Message); return 2; }
            _stream = client.GetStream();
            var reader = new Thread(() => ReadLoop(client)) { IsBackground = true }; reader.Start();
            SendHello("Joueur2"); SendPing();
            Thread.Sleep(1500);
            Console.WriteLine("[mountone] >>> REGARDE : la pièce ciblée doit réapparaître <<<");
            SendPartState(type, key, index, true);
            Thread.Sleep(8000);
            SendBye();
            Console.WriteLine("[mountone] terminé.");
            return 0;
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

        static void SendPartAction(byte action, string key, int boltIdx)
        {
            // Mirror a real client: what WE remove/mount, we also track in our reconstructed set so our
            // desync hash reflects our own actions (not just the host's). Switch (4) isn't a removal.
            lock (_partLock) { if (action == ActUnmount) _removedParts.Add(key); else if (action == ActMount) _removedParts.Remove(key); }
            using var w = new PacketWriter(PacketType.PartAction); w.Byte(action).Str(key).Int(boltIdx); Frame(w.ToArray(), Channels.Reliable);
        }

        // Mechanical part mount/unmount by positional identity — the host applies it visually (ApplyRemote).
        // On a MOUNT we MUST append the data fields (condition/dust/quality/flags/paintType/rgba/tunedId) in
        // the exact order the mod reads them, else the host reads past the packet.
        static void SendPartState(byte type, int key, int index, bool mounted, float cond = 1f)
        {
            using var w = new PacketWriter(PacketType.PartState);
            w.Byte(type).Int(key).Int(index).Bool(mounted);
            if (mounted)
                w.Float(cond).Float(0f).Int(0).Byte(0).Byte(0).Float(1f).Float(0f).Float(1f).Float(1f).Str("");
            Frame(w.ToArray(), Channels.Reliable);
        }

        static string ActName(byte a) => a == ActMount ? "MONTER" : a == ActUnmount ? "DÉMONTER" : a == ActBolt ? "BOULON" : "RELEASE";

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

                    case PacketType.PartAction:
                    {
                        byte act = r.Byte(); string key = r.Str(); int bolt = r.Int();
                        lock (_partLock) { _lastPartKey = key; _lastPartAction = act; _lastBolt = bolt; }
                        _gotPartAction = true;
                        if (act == ActUnmount) lock (_partLock) { _removedParts.Add(key); }
                        else if (act == ActMount) lock (_partLock) { _removedParts.Remove(key); }
                        if (act == 4 /*ActSwitch — openable open/close*/)
                            Console.WriteLine($"[fakepeer] <- OUVRANT '{key}' {(bolt != 0 ? "OUVERT" : "FERMÉ")}");
                        else
                            Console.WriteLine($"[fakepeer] <- PART TEMPS RÉEL: hôte a fait {ActName(act)} sur \"{key}\"" + (act == ActBolt ? $" (boulon #{bolt})" : ""));
                        break;
                    }

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

                    case PacketType.PartState:
                    {
                        byte type = r.Byte(); int key = r.Int(); int index = r.Int(); bool mounted = r.Bool();
                        float condition = 0f, dust = 0f; int quality = 0; byte flags = 0, paintType = 0;
                        float cr = 0f, cg = 0f, cb = 0f, ca = 0f; string tunedId = "";
                        if (mounted)
                        {
                            // part DATA, same field order the mod writes on a MOUNT
                            condition = r.Float(); dust = r.Float(); quality = r.Int(); flags = r.Byte(); paintType = r.Byte();
                            cr = r.Float(); cg = r.Float(); cb = r.Float(); ca = r.Float(); tunedId = r.Str();
                        }
                        string[] tn = { "engine", "suspension", "other", "driveshaft", "body", "flat" };
                        string typeName = type < tn.Length ? tn[type] : type.ToString();
                        // mechanical slot key for the canonical-hash reconstruction (matches the mod's "m:type:key:index")
                        string slot = $"m:{type}:{key}:{index}";
                        lock (_partLock) { if (mounted) _removedParts.Remove(slot); else _removedParts.Add(slot); }
                        _gotPartAction = true;
                        Console.WriteLine($"[fakepeer] <- PART MÉCA TEMPS RÉEL: {(mounted ? "MONTÉ" : "DÉMONTÉ")} {typeName} key={key} idx={index}" +
                                          (mounted ? $" cond={condition:0.00} dust={dust:0.00} q={quality} paint={(((flags & 2) != 0) ? paintType.ToString() : "-")} tuned={tunedId} rgba=({cr:0.0},{cg:0.0},{cb:0.0},{ca:0.0})" : ""));
                        break;
                    }

                    case PacketType.EntityHash:
                    {
                        // Rebuild the host's canonical state from the keys WE received, then compare hashes
                        // (§14.3). Same inputs → same deterministic hash if we're in sync.
                        var cfg = r.Str(); var place = r.Int(); var rev = r.Int(); var hostHash = r.ULong();
                        var st = new CarMecaSim.Shared.Coop.VehiclePartState(new CarMecaSim.Shared.Coop.VehicleKey(cfg, place));
                        lock (_partLock) { foreach (var k in _removedParts) st.SetRemoved(k); }
                        ulong mine = st.CanonicalHash();
                        _gotHashReport = true; _lastSyncOk = mine == hostHash;
                        Console.WriteLine($"[fakepeer] <- HASH hôte {cfg}@{place} rev={rev} {(mine == hostHash ? "=> SYNC OK" : $"=> DÉSYNC (moi {mine:X} vs hôte {hostHash:X})")}");
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
