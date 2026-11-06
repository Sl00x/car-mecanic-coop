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
        public readonly SteamNet Net = new();
        public MpConfig Config = new();
        public bool InLobby { get; private set; }
        public bool IsHost => Net.IsLobbyOwner;

        public readonly List<string> ChatLog = new();
        private readonly Dictionary<ulong, RemotePlayer> _peers = new();
        private byte _nextPlayerId = 1;
        private float _lastPresence;
        private Action<string> _log;

        public IReadOnlyCollection<RemotePlayer> Peers => _peers.Values;

        public void Init(MpConfig cfg, Action<string> log)
        {
            Config = cfg; _log = log;
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
            InLobby = true;
            AddChat($"* joined lobby {lobby.m_SteamID}  (host: {Net.NameOf(Net.LobbyOwner)})");
            // Greet everyone already present.
            Hello();
        }

        private void OnPeerJoined(CSteamID who)
        {
            AddChat($"* {Net.NameOf(who)} connected");
            GetOrAdd(who);
            Hello(); // re-announce so the newcomer learns our name
        }

        private void OnPeerLeft(CSteamID who)
        {
            if (_peers.TryGetValue(who.m_SteamID, out var p)) { p.Despawn(); _peers.Remove(who.m_SteamID); }
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
                        p.LastUpdate = Time.time;
                        p.EnsureSpawned();
                        break;
                    }
                    case PacketType.Chat:
                    {
                        var name = r.Str(); var text = r.Str();
                        AddChat($"{name}: {text}");
                        break;
                    }
                    case PacketType.Bye:
                        OnPeerLeft(sender);
                        break;
                }
            }
            catch (Exception e) { _log?.Invoke("packet error: " + e.Message); }
        }

        // ---- per-frame ----
        public void Update(float dt)
        {
            Net.Pump();
            bool show = GameRefs.InGarage;
            foreach (var p in _peers.Values)
            {
                p.SetVisible(show);
                p.Update(dt);
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
