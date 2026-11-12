using System;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;
using CarMecaSim.Shared;
using CarMecaSim.Mp.Net;
using CarMecaSim.Mp.Game;

namespace CarMecaSim.Mp.Core
{
    /// <summary>
    /// Orchestrates a co-op session: Steam lobby + peer presence + chat.
    /// Lobby owner == host (authoritative for economy/parts, layered on later).
    /// v0 keeps presence peer-to-peer for responsiveness.
    /// </summary>
    public sealed class MpSession
    {
        public INet Net { get; private set; } = new SteamNet();
        public MpConfig Config = new();
        public bool InLobby { get; private set; }
        public bool IsHost => Net.IsLobbyOwner;

        public readonly List<string> ChatLog = new();
        public int PresenceReceived;   // diagnostics: how many presence packets arrived
        private readonly Dictionary<ulong, RemotePlayer> _peers = new();
        private byte _nextPlayerId = 1;
        private float _lastPresence;
        private CSteamID _curLobby;
        private Action<string> _log;

        public IReadOnlyCollection<RemotePlayer> Peers => _peers.Values;

        public void Init(MpConfig cfg, Action<string> log)
        {
            Config = cfg; _log = log;
            // TEST-ONLY: swap in the local TCP loopback transport so tools/FakePeer can be player 2.
            if (cfg.LoopbackPort > 0) { Net = new LoopbackNet(cfg.LoopbackPort); log?.Invoke($"[net] LOOPBACK test transport on 127.0.0.1:{cfg.LoopbackPort}"); }
            Net.OnStatus = s => { _log?.Invoke("[net] " + s); };
            Net.OnLobbyEntered = OnLobbyEntered;
            Net.OnPeerJoined = OnPeerJoined;
            Net.OnPeerLeft = OnPeerLeft;
            Net.OnData = OnData;
        }

        public bool EnsureSteam() => Net.TryInit();

        public void StartFromConfig()
        {
            if (!EnsureSteam()) { _log?.Invoke("Steam not ready yet; will retry."); return; }
            switch (Config.Role)
            {
                case MpRole.Host:
                    Net.CreateLobby(MapLobbyType(Config.LobbyType), Math.Max(2, Config.MaxPlayers));
                    break;
                case MpRole.Join:
                    if (Config.LobbyId != 0) Net.JoinLobby(new CSteamID(Config.LobbyId));
                    else _log?.Invoke("Join role but no LobbyId; use Steam overlay invite or host.");
                    break;
            }
        }

        public void HostLobby() { if (EnsureSteam()) Net.CreateLobby(MapLobbyType(Config.LobbyType), Math.Max(2, Config.MaxPlayers)); }
        public void JoinLobby(ulong id) { if (EnsureSteam()) Net.JoinLobby(new CSteamID(id)); }
        public void Leave()
        {
            foreach (var p in _peers.Values) p.Despawn();
            _peers.Clear();
            Net.LeaveLobby();
            InLobby = false;
            AddChat("* left the session");
        }

        // ---- lifecycle ----
        private void OnLobbyEntered(CSteamID lobby)
        {
            // Switching lobbies (e.g. we were auto-hosting, then joined a friend): drop stale peers.
            if (_curLobby.IsValid() && _curLobby != lobby)
            {
                foreach (var p in _peers.Values) p.Despawn();
                _peers.Clear();
            }
            _curLobby = lobby;
            InLobby = true;
            _snapshotApplied = false; _lastSnapReq = -999f;   // request a fresh snapshot for this lobby
            AddChat($"* joined lobby {lobby.m_SteamID}  (host: {Net.NameOf(Net.LobbyOwner)})");
            // Greet everyone already present.
            Hello();
        }

        private void OnPeerJoined(CSteamID who)
        {
            AddChat($"* {Net.NameOf(who)} connected");
            GetOrAdd(who);
            Hello(); // re-announce so the newcomer learns our name
            if (IsHost) { BroadcastEcoAbsolute(); Sync.CarSync.OnPeerJoined(); Sync.OrderSync.OnPeerJoined(); }  // newcomer gets money + garage + jobs
        }

        private void OnPeerLeft(CSteamID who)
        {
            if (_peers.TryGetValue(who.m_SteamID, out var p)) { p.Despawn(); _peers.Remove(who.m_SteamID); }
            Sync.PartSync.OnPeerLeft(PlayerIdOf(who));   // free any parts they were holding (§10.3)
            AddChat($"* {Net.NameOf(who)} disconnected");
        }

        private RemotePlayer GetOrAdd(CSteamID who)
        {
            if (!_peers.TryGetValue(who.m_SteamID, out var p))
            {
                p = new RemotePlayer { Id = who, PlayerId = _nextPlayerId++, Name = Net.NameOf(who) };
                _peers[who.m_SteamID] = p;
            }
            return p;
        }

        // ---- sending ----
        private void Hello()
        {
            using var w = new PacketWriter(PacketType.Hello);
            w.Str(Config.DisplayName).Int(Protocol.Version);
            Net.Broadcast(w.ToArray(), SendFlags.Reliable, Channels.Reliable);
        }

        public void SendPresenceTick(float time)
        {
            if (!InLobby || !GameRefs.InGarage) return;
            if (time - _lastPresence < 0.05f) return; // ~20 Hz
            _lastPresence = time;
            var eye = GameRefs.Eye;
            if (eye == null) return;
            var pos = eye.position;
            var euler = eye.eulerAngles;
            using var w = new PacketWriter(PacketType.Presence);
            w.Vec3(pos.x, pos.y, pos.z).Float(euler.y).Float(euler.x)
             .Byte((byte)0).Byte((byte)GameRefs.CurrentMode);
            Net.Broadcast(w.ToArray(), SendFlags.UnreliableNoDelay, Channels.Unreliable);
        }

        public void SendChat(string text)
        {
            if (!InLobby || string.IsNullOrWhiteSpace(text)) return;
            using var w = new PacketWriter(PacketType.Chat);
            w.Str(Config.DisplayName).Str(text);
            Net.Broadcast(w.ToArray(), SendFlags.Reliable, Channels.Reliable);
            AddChat($"{Config.DisplayName}: {text}");
        }

        /// <summary>Add a line to the local chat log only (no network) — for system notices.</summary>
        public void LocalChat(string line) => AddChat(line);

        // Stable per-player id derived from SteamID (unique enough for a small lobby).
        public byte LocalPlayerId => (byte)(Net.LocalId.m_SteamID % 250 + 1);
        public byte PlayerIdOf(CSteamID id) => (byte)(id.m_SteamID % 250 + 1);

        public void SendPartAction(byte action, string key, int boltIdx)
        {
            if (!InLobby) return;
            using var w = new PacketWriter(PacketType.PartAction);
            w.Byte(action).Str(key).Int(boltIdx);
            Net.Broadcast(w.ToArray(), SendFlags.Reliable, Channels.Reliable);
        }

        /// <summary>Broadcast a mechanical part mount/unmount by positional identity (type,key,index).</summary>
        public void SendPartState(byte type, int key, int index, bool mounted)
        {
            if (!InLobby) return;
            using var w = new PacketWriter(PacketType.PartState);
            w.Byte(type).Int(key).Int(index).Bool(mounted);
            Net.Broadcast(w.ToArray(), SendFlags.Reliable, Channels.Reliable);
        }

        /// <summary>Send one part action to a SINGLE peer (used for late-join mechanical catch-up).</summary>
        public void SendPartActionTo(CSteamID target, byte action, string key, int boltIdx)
        {
            if (!InLobby) return;
            using var w = new PacketWriter(PacketType.PartAction);
            w.Byte(action).Str(key).Int(boltIdx);
            Net.Send(target, w.ToArray(), SendFlags.Reliable, Channels.Reliable);
        }

        public void SendGarage(System.Collections.Generic.List<(int, string)> entries)
        {
            if (!InLobby) return;
            using var w = new PacketWriter(PacketType.CarOp);
            w.Byte(Sync.CarSync.OpGarage).Int(entries.Count);
            foreach (var (i, n) in entries) w.Int(i).Str(n);
            Net.Broadcast(w.ToArray(), SendFlags.Reliable, Channels.Reliable);
        }

        public void SendJobs(System.Collections.Generic.List<Sync.OrderSync.JobInfo> jobs)
        {
            if (!InLobby) return;
            using var w = new PacketWriter(PacketType.Order);
            w.Byte(Sync.OrderSync.OpList).Int(jobs.Count);
            foreach (var j in jobs) w.Int(j.Id).Str(j.Car).Int(j.Pay).Int(j.Xp).Bool(j.Mission).Int(j.MissionId);
            Net.Broadcast(w.ToArray(), SendFlags.Reliable, Channels.Reliable);
        }

        public void SendTakeJob(int id, bool mission, int missionId)
        {
            if (!InLobby) return;
            using var w = new PacketWriter(PacketType.Order);
            w.Byte(Sync.OrderSync.OpTake).Int(id).Bool(mission).Int(missionId);
            Net.Send(Net.LobbyOwner, w.ToArray(), SendFlags.Reliable, Channels.Reliable);
        }

        // ---- receiving ----
        private void OnData(CSteamID sender, byte[] data, int channel)
        {
            try
            {
                var r = new PacketReader(data);
                switch (r.Type)
                {
                    case PacketType.Hello:
                    {
                        var name = r.Str();
                        var p = GetOrAdd(sender); p.Name = name;
                        break;
                    }
                    case PacketType.Presence:
                    {
                        r.Vec3(out var x, out var y, out var z);
                        var yaw = r.Float(); var pitch = r.Float();
                        var tool = r.Byte(); var mode = r.Byte();
                        var p = GetOrAdd(sender);
                        p.TargetPos = new Vector3(x, y, z);
                        p.TargetYaw = yaw; p.TargetPitch = pitch; p.Tool = tool; p.Mode = mode;
                        p.LastUpdate = Time.unscaledTime;
                        p.EnsureSpawned();
                        if (PresenceReceived++ == 0) CarMecaPlugin.Logger?.LogInfo($"[diag] first presence from {Net.NameOf(sender)} at {p.TargetPos}");
                        break;
                    }
                    case PacketType.Chat:
                    {
                        var name = r.Str(); var text = r.Str();
                        AddChat($"{name}: {text}");
                        break;
                    }
                    case PacketType.Economy:
                    {
                        var kind = r.Byte();
                        if (kind == 0) // absolute (host -> all): mirror it, only touch what changed
                        {
                            int m = r.Int(), x = r.Int(), s = r.Int(), lvl = r.Int();
                            if (GameState.Money != m) GameState.Money = m;      // avoid redundant gain animation
                            if (GameState.Scraps != s) GameState.Scraps = s;
                            if (GameState.Exp != x) GameState.Exp = x;
                            if (GameState.Level != lvl) GameState.Level = lvl;
                            _lastMoney = m; _lastExp = x; _lastScraps = s; _ecoInit = true;
                        }
                        else if (kind == 1 && IsHost) // delta (client -> host): apply to the pool via game methods
                        {
                            int dm = r.Int(), dx = r.Int(), ds = r.Int();
                            GameState.AddMoney(dm);
                            GameState.AddExp(dx);
                            GameState.AddScraps(ds);
                            // host's next EconomyTick detects the change and broadcasts the new absolute.
                        }
                        break;
                    }
                    case PacketType.PartAction:
                    {
                        var action = r.Byte(); var key = r.Str(); var idx = r.Int();
                        Sync.PartSync.ApplyRemote(PlayerIdOf(sender), action, key, idx);
                        break;
                    }
                    case PacketType.CarOp:
                    {
                        var op = r.Byte();
                        if (op == Sync.CarSync.OpGarage)
                        {
                            int n = r.Int();
                            var entries = new System.Collections.Generic.List<(int, string)>(n);
                            for (int k = 0; k < n; k++) { int idx = r.Int(); string nm = r.Str(); entries.Add((idx, nm)); }
                            Sync.CarSync.ApplyGarage(entries);
                        }
                        break;
                    }
                    case PacketType.Order:
                    {
                        var op = r.Byte();
                        if (op == Sync.OrderSync.OpList)
                        {
                            int n = r.Int();
                            var jobs = new System.Collections.Generic.List<Sync.OrderSync.JobInfo>(n);
                            for (int k = 0; k < n; k++)
                            {
                                int id = r.Int(); string car = r.Str(); int pay = r.Int(); int xp = r.Int();
                                bool mi = r.Bool(); int mid = r.Int();
                                jobs.Add(new Sync.OrderSync.JobInfo(id, car, pay, xp, mi, mid));
                            }
                            Sync.OrderSync.ApplyJobs(jobs);
                        }
                        else if (op == Sync.OrderSync.OpTake && IsHost)
                        {
                            int id = r.Int(); bool mi = r.Bool(); int mid = r.Int();
                            Sync.OrderSync.HostTake(id, mi, mid);
                        }
                        break;
                    }
                    case PacketType.FullState:
                    {
                        // Host's save snapshot, chunked. flags bit0 = delta (garage-only, mid-session).
                        bool delta = (r.Flags & 1) != 0;
                        int total = r.Int(); int ci = r.Int(); int chunks = r.Int(); int len = r.Int();
                        var chunk = r.BytesRaw(len);
                        if (_saveBuf == null || _saveBuf.Length != total || _saveChunksExpected != chunks)
                        { _saveBuf = new byte[total]; _saveChunksExpected = chunks; _saveChunksGot = 0; }
                        int dst = ci * SaveChunk;
                        if (dst >= 0 && dst + len <= _saveBuf.Length) Array.Copy(chunk, 0, _saveBuf, dst, len);
                        if (++_saveChunksGot >= _saveChunksExpected)
                        {
                            var full = _saveBuf; _saveBuf = null; _saveChunksExpected = 0; _saveChunksGot = 0;
                            _log?.Invoke($"[save] received {(delta ? "delta" : "initial")} snapshot ({full.Length} bytes); applying");
                            Sync.SaveSync.ApplyHost(full, delta);
                        }
                        break;
                    }
                    case PacketType.Ping:
                        // A client reached the garage and wants the current state.
                        if (IsHost)
                        {
                            SendSaveSnapshot(sender);          // client adopts host's full garage/jobs/money (native)
                            BroadcastEcoAbsolute();            // then set live money on top (ordered after the save)
                            Sync.CarSync.OnPeerJoined();       // keep incremental garage/job sync going afterwards
                            Sync.OrderSync.OnPeerJoined();
                            Sync.PartSync.SendCatchUp(sender); // replay this-session part removals (live mechanical state)
                        }
                        break;
                    case PacketType.Latency:
                    {
                        var kind = r.Byte(); var stamp = r.UInt();
                        if (kind == 0) // probe -> echo it straight back
                        {
                            using var w = new PacketWriter(PacketType.Latency);
                            w.Byte(1).UInt(stamp);
                            Net.Send(sender, w.ToArray(), SendFlags.UnreliableNoDelay, Channels.Unreliable);
                        }
                        else // echo of our probe -> compute RTT against our own clock
                        {
                            uint now = (uint)(Time.unscaledTime * 1000f);
                            int rtt = (int)(now - stamp);
                            var p = GetOrAdd(sender);
                            if (rtt >= 0 && rtt < 10000) p.PingMs = rtt;
                            p.LastUpdate = Time.unscaledTime;
                        }
                        break;
                    }
                    case PacketType.PartState:
                    {
                        byte type = r.Byte(); int key = r.Int(); int index = r.Int(); bool mounted = r.Bool();
                        Sync.CarPartSync.ApplyRemote(type, key, index, mounted);
                        break;
                    }
                    case PacketType.EntityHash:
                    {
                        var cfg = r.Str(); var place = r.Int(); var rev = r.Int(); var hash = r.ULong();
                        if (!IsHost)
                        {
                            ulong local = Sync.PartSync.Hash;
                            if (local != hash)
                            {
                                _log?.Invoke($"[sync] DESYNC {cfg}@{place}: host rev={rev} hash={hash:X} vs local {local:X} — re-syncing");
                                _snapshotApplied = false; _lastSnapReq = -999f;   // re-request the host snapshot + catch-up
                            }
                        }
                        break;
                    }
                    case PacketType.Bye:
                        OnPeerLeft(sender);
                        break;
                }
            }
            catch (Exception e) { _log?.Invoke("packet error: " + e.Message); }
        }

        private float _lastSnapReq = -999f;
        private bool _snapshotApplied;
        private bool _autoHostTried;
        private float _lastLatencyTick;
        private float _lastHashTick;

        /// <summary>Host: broadcast the current vehicle's canonical state hash so peers can detect a
        /// divergence (§14.3). Cheap (one small reliable packet); peers compare against their local hash.</summary>
        public void SendHashReport()
        {
            if (!InLobby || !IsHost) return;
            var st = Sync.PartSync.State;
            if (st == null) return;
            using var w = new PacketWriter(PacketType.EntityHash);
            w.Str(st.Vehicle.Config).Int(st.Vehicle.Place).Int(st.Revision).ULong(st.CanonicalHash());
            Net.Broadcast(w.ToArray(), SendFlags.Reliable, Channels.Reliable);
        }

        /// <summary>Friends currently hosting a joinable CMS co-op lobby (for the main-menu join list).</summary>
        public System.Collections.Generic.IEnumerable<SteamNet.JoinableFriend> JoinableFriends() => Net.JoinableFriends();

        /// <summary>Called by SaveSync once the host's snapshot has actually been applied, so the
        /// client stops re-requesting it (and a scene-transition dip won't re-tear-down the garage).</summary>
        public void MarkSnapshotApplied() { _snapshotApplied = true; }

        // ---- per-frame ----
        public void Update(float dt)
        {
            Net.Pump();
            bool show = GameRefs.InGameSession;

            // A client (guest, in a game session) asks the host for its full save snapshot, RETRYING
            // every few seconds until one is actually applied — the host may not be ready the instant we
            // join (ProfileData/GameManager still spinning up). We stop once applied, so a scene-transition
            // dip in `show` does NOT re-tear-down the garage. Entering a new lobby re-arms it.
            if (InLobby && !IsHost && show && !_snapshotApplied
                && Time.unscaledTime - _lastSnapReq > 3f)
            {
                using var w = new PacketWriter(PacketType.Ping);
                Net.Send(Net.LobbyOwner, w.ToArray(), SendFlags.Reliable, Channels.Reliable);
                _lastSnapReq = Time.unscaledTime;
                _log?.Invoke("[save] requested host snapshot");
            }

            // Native hosting: load your save through the game's own menu and you're automatically
            // hosting a lobby friends can drop into via Steam's "Join Game" — no custom menu UI.
            // Joining a friend (Steam overlay) replaces this auto-hosted lobby with theirs.
            if (show && Net.Ready && !InLobby && !_autoHostTried)
            {
                _autoHostTried = true;
                _log?.Invoke("[net] auto-hosting on session entry");
                HostLobby();
            }
            else if (!show) _autoHostTried = false;   // re-arm for the next session
            foreach (var p in _peers.Values)
            {
                p.SetVisible(show);
                p.Update(dt);
            }

            // economy sync, ~3 Hz
            _ecoAccum += dt;
            if (_ecoAccum >= 0.33f) { _ecoAccum = 0f; EconomyTick(); }

            // latency probes (~1 Hz) so the player list can show ping
            if (InLobby && Time.unscaledTime - _lastLatencyTick > 1f)
            {
                _lastLatencyTick = Time.unscaledTime;
                uint stamp = (uint)(Time.unscaledTime * 1000f);
                foreach (var p in _peers.Values)
                {
                    using var w = new PacketWriter(PacketType.Latency);
                    w.Byte(0).UInt(stamp);                 // 0 = probe
                    Net.Send(p.Id, w.ToArray(), SendFlags.UnreliableNoDelay, Channels.Unreliable);
                    p.LastPingSent = Time.unscaledTime;
                }
            }

            // Host: periodically publish the current vehicle's canonical state hash so clients detect desync.
            if (IsHost && InLobby && GameRefs.InGameSession && Time.unscaledTime - _lastHashTick > 5f)
            {
                _lastHashTick = Time.unscaledTime;
                SendHashReport();
            }

            // Host: when its garage changed (a car loaded/removed), push a delta snapshot to all guests,
            // coalesced — a single job-take can fire several LoadCar calls; wait for them to settle.
            if (IsHost && _garageDirty && Time.unscaledTime - _lastGarageSnap > 1.5f)
            {
                _garageDirty = false; _lastGarageSnap = Time.unscaledTime;
                SendSaveSnapshot(CSteamID.Nil, delta: true);
            }

            Sync.PartSync.Tick();   // release stale part locks
            Sync.OrderSync.Tick();  // host: periodic job-board broadcast
        }

        // ---- save snapshot (host -> joining client; makes the client's garage a native copy) ----
        private const int SaveChunk = 256 * 1024;   // < Steam's 512KB message limit, with header room
        private byte[] _saveBuf;
        private int _saveChunksExpected, _saveChunksGot;

        /// <summary>Host: serialize the current profile and stream it (chunked). target==Nil broadcasts
        /// to every guest. delta=true marks a mid-session garage change (client applies garage only).</summary>
        public void SendSaveSnapshot(CSteamID target, bool delta = false)
        {
            if (!InLobby || !IsHost) return;
            var data = Sync.SaveSync.SerializeHost();
            if (data == null || data.Length == 0) { _log?.Invoke("[save] nothing to send"); return; }
            byte flags = (byte)(delta ? 1 : 0);
            int total = data.Length;
            int chunks = (total + SaveChunk - 1) / SaveChunk;
            for (int c = 0; c < chunks; c++)
            {
                int off = c * SaveChunk;
                int len = Math.Min(SaveChunk, total - off);
                using var w = new PacketWriter(PacketType.FullState, flags);
                w.Int(total).Int(c).Int(chunks).Int(len).BytesRaw(data, off, len);
                if (target.IsValid()) Net.Send(target, w.ToArray(), SendFlags.Reliable, Channels.Reliable);
                else Net.Broadcast(w.ToArray(), SendFlags.Reliable, Channels.Reliable);
            }
            _log?.Invoke($"[save] sent {(delta ? "delta" : "initial")} snapshot {total}B/{chunks}ch to {(target.IsValid() ? Net.NameOf(target) : "all")}");
        }

        /// <summary>Host: the garage changed (a car was loaded/removed) — flag a throttled delta broadcast.</summary>
        public void MarkGarageChanged() { if (IsHost) _garageDirty = true; }
        private bool _garageDirty;
        private float _lastGarageSnap = -999f;

        // ---- economy (shared pool, host-authoritative) ----
        private int _lastMoney = -1, _lastExp = -1, _lastScraps = -1;
        private bool _ecoInit;
        private float _ecoAccum, _ecoHeartbeat;

        /// <summary>Host pushes its absolute economy to everyone (money becomes the host's money).</summary>
        public void BroadcastEcoAbsolute()
        {
            if (!IsHost || !GameState.Available) return;
            using var w = new PacketWriter(PacketType.Economy);
            w.Byte(0).Int(GameState.Money).Int(GameState.Exp).Int(GameState.Scraps).Int(GameState.Level);
            Net.Broadcast(w.ToArray(), SendFlags.Reliable, Channels.Reliable);
            _lastMoney = GameState.Money; _lastExp = GameState.Exp; _lastScraps = GameState.Scraps;
            _ecoHeartbeat = Time.unscaledTime;
        }

        private void EconomyTick()
        {
            if (!InLobby || !GameRefs.InGameSession || !GameState.Available) return;
            int m = GameState.Money, x = GameState.Exp, s = GameState.Scraps, lvl = GameState.Level;
            if (!_ecoInit) { _lastMoney = m; _lastExp = x; _lastScraps = s; _ecoInit = true; return; }
            bool changed = (m != _lastMoney || x != _lastExp || s != _lastScraps);

            if (IsHost)
            {
                // Broadcast ONLY on a real change (no periodic heartbeat — it retriggered the
                // money-gain animation on clients). Initial sync is handled on-join + on ping.
                if (changed) BroadcastEcoAbsolute();
            }
            else if (changed)
            {
                using var w = new PacketWriter(PacketType.Economy);
                w.Byte(1).Int(m - _lastMoney).Int(x - _lastExp).Int(s - _lastScraps); // 1 = delta -> host applies
                Net.Send(Net.LobbyOwner, w.ToArray(), SendFlags.Reliable, Channels.Reliable);
                _lastMoney = m; _lastExp = x; _lastScraps = s;
            }
        }

        private void AddChat(string line)
        {
            ChatLog.Add(line);
            if (ChatLog.Count > 80) ChatLog.RemoveAt(0);
            _log?.Invoke(line);
        }

        private static ELobbyType MapLobbyType(MpLobbyType t) => t switch
        {
            MpLobbyType.Public => ELobbyType.k_ELobbyTypePublic,
            MpLobbyType.Private => ELobbyType.k_ELobbyTypePrivate,
            _ => ELobbyType.k_ELobbyTypeFriendsOnly,
        };
    }
}
