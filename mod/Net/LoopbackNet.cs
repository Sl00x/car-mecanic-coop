using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Steamworks;

namespace CarMecaSim.Mp.Net
{
    /// <summary>
    /// Local TCP transport on 127.0.0.1:&lt;port&gt; standing in for Steam P2P so two players can co-op on ONE
    /// machine (and so tools/FakePeer can be a headless player 2). Auto-negotiates role by launch order:
    /// the FIRST process to bind the port becomes the HOST (listener); a second process fails to bind and
    /// becomes a CLIENT (connects to the host). Same mp_config on both — no per-instance config needed.
    /// Exactly one guest. TCP (not UDP) so the chunked save snapshot streams reliably.
    /// Frame = [i32 payloadLen][u8 channel][payload].
    /// </summary>
    public sealed class LoopbackNet : INet
    {
        // Synthetic but *valid* ids — a raw small integer fails CSteamID.IsValid() (no universe/account-type
        // bits), which would make IsLobbyOwner false. Build proper individual/chat ids.
        private static readonly CSteamID Host  = new CSteamID(new AccountID_t(1u), EUniverse.k_EUniversePublic, EAccountType.k_EAccountTypeIndividual);
        private static readonly CSteamID Guest = new CSteamID(new AccountID_t(2u), EUniverse.k_EUniversePublic, EAccountType.k_EAccountTypeIndividual);
        private static readonly CSteamID Lobby = new CSteamID(new AccountID_t(1000u), 0x40000u, EUniverse.k_EUniversePublic, EAccountType.k_EAccountTypeChat);

        public bool Ready { get; private set; }
        private bool _isHost = true;                 // decided in TryInit by whether the bind succeeds
        public CSteamID LocalId => _isHost ? Host : Guest;
        private CSteamID PeerId => _isHost ? Guest : Host;   // the OTHER party on this one connection
        public CSteamID LobbyId { get; private set; }
        public CSteamID LobbyOwner { get; private set; } = Host;   // the host is always the owner
        private bool _lobbyUp;
        public bool IsLobbyOwner => Ready && _lobbyUp && _isHost;   // a client is never the owner

        public Action<CSteamID> OnLobbyEntered { get; set; }
        public Action<CSteamID> OnPeerJoined { get; set; }
        public Action<CSteamID> OnPeerLeft { get; set; }
        public Action<CSteamID, byte[], int> OnData { get; set; }
        public Action<string> OnStatus { get; set; }

        private readonly int _port;
        private TcpListener _listener;
        private volatile TcpClient _client;     // volatile: written by the accept/connect thread, read by the
        private volatile NetworkStream _stream;  // Unity main (Pump) thread — else a stale null hides writes.
        private readonly object _writeLock = new object();
        private volatile bool _connected;
        private int _wrote;

        private enum EvKind { Joined, Data, Left }
        private readonly struct Ev
        {
            public readonly EvKind Kind; public readonly byte[] Data; public readonly int Channel;
            public Ev(EvKind k, byte[] d, int ch) { Kind = k; Data = d; Channel = ch; }
        }
        private readonly ConcurrentQueue<Ev> _inbox = new ConcurrentQueue<Ev>();

        public LoopbackNet(int port) { _port = port; }

        public bool TryInit()
        {
            if (Ready) return true;
            // 1) Try to be the host: bind the port.
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
                _isHost = true;
                var accept = new Thread(AcceptLoop) { IsBackground = true, Name = "CarMeca-Loopback-Accept" };
                accept.Start();
                Ready = true;
                OnStatus?.Invoke($"Loopback transport listening on 127.0.0.1:{_port} (HOST).");
                return true;
            }
            catch (SocketException)
            {
                // Port is taken → another instance is already hosting. Become the CLIENT and connect.
            }
            catch (Exception e) { OnStatus?.Invoke("Loopback bind failed: " + e.Message); return false; }

            // 2) Client mode: connect to the host. Retried each frame (via EnsureSteam) until the host is up.
            try
            {
                var c = new TcpClient();
                c.Connect(IPAddress.Loopback, _port);
                c.NoDelay = true;
                _isHost = false;
                _client = c; _stream = c.GetStream(); _connected = true;
                var read = new Thread(() => ReadLoop(c)) { IsBackground = true, Name = "CarMeca-Loopback-ReadC" };
                read.Start();
                _inbox.Enqueue(new Ev(EvKind.Joined, null, 0));   // Pump → OnPeerJoined(Host)
                Ready = true;
                OnStatus?.Invoke($"Loopback transport connected to 127.0.0.1:{_port} (CLIENT / joueur 2).");
                return true;
            }
            catch (Exception e) { OnStatus?.Invoke("Loopback connect pending (host not up yet): " + e.Message); return false; }
        }

        private void AcceptLoop()
        {
            while (true)
            {
                TcpClient c;
                try { c = _listener.AcceptTcpClient(); }
                catch { return; }   // listener stopped
                if (_connected) { try { c.Close(); } catch { } continue; }   // only one guest
                try { c.NoDelay = true; } catch { }
                _client = c; _stream = c.GetStream(); _connected = true;
                _inbox.Enqueue(new Ev(EvKind.Joined, null, 0));
                var read = new Thread(() => ReadLoop(c)) { IsBackground = true, Name = "CarMeca-Loopback-Read" };
                read.Start();
            }
        }

        private void ReadLoop(TcpClient c)
        {
            var s = c.GetStream();
            var head = new byte[5];
            try
            {
                while (true)
                {
                    if (!ReadExact(s, head, 5)) break;
                    int len = head[0] | (head[1] << 8) | (head[2] << 16) | (head[3] << 24);
                    int channel = head[4];
                    if (len < 0 || len > 64 * 1024 * 1024) break;   // sanity
                    var payload = new byte[len];
                    if (len > 0 && !ReadExact(s, payload, len)) break;
                    _inbox.Enqueue(new Ev(EvKind.Data, payload, channel));
                }
            }
            catch { }
            finally
            {
                if (_client == c) { _connected = false; _stream = null; _client = null; }
                try { c.Close(); } catch { }
                _inbox.Enqueue(new Ev(EvKind.Left, null, 0));
            }
        }

        private static bool ReadExact(NetworkStream s, byte[] buf, int n)
        {
            int off = 0;
            while (off < n)
            {
                int r = s.Read(buf, off, n - off);
                if (r <= 0) return false;
                off += r;
            }
            return true;
        }

        // Host: create the lobby. Client: "enter" the host's lobby (the connection is already up). Both route
        // through MpSession's auto-host call, so a client just joins instead of hosting.
        public void CreateLobby(ELobbyType type, int maxMembers)
        {
            LobbyId = Lobby; LobbyOwner = Host; _lobbyUp = true;
            OnStatus?.Invoke(_isHost ? "Loopback lobby ready (HOST)." : "Loopback lobby joined (CLIENT).");
            OnLobbyEntered?.Invoke(LobbyId);
        }

        public void JoinLobby(CSteamID lobby) => CreateLobby(default, 0);

        public void LeaveLobby() { LobbyId = CSteamID.Nil; _lobbyUp = false; }

        public IEnumerable<CSteamID> LobbyMembers()
        {
            yield return Host;
            if (_connected || !_isHost) yield return Guest;
        }

        public string NameOf(CSteamID id) => id == Guest ? "Joueur 2" : id == Host ? "Hôte" : id.ToString();

        public EResult Send(CSteamID target, byte[] data, int sendFlags, int channel)
        {
            if (data == null || data.Length == 0) return EResult.k_EResultOK;
            if (_isHost && target == Host) return EResult.k_EResultOK;   // host never sends to itself
            if (_stream == null) return EResult.k_EResultOK;             // no connection yet
            return WriteFrame(data, channel);
        }

        public void Broadcast(byte[] data, int sendFlags, int channel, CSteamID except = default)
        {
            if (_stream != null && PeerId != except) WriteFrame(data, channel);
        }

        private EResult WriteFrame(byte[] payload, int channel)
        {
            var s = _stream;
            if (s == null) return EResult.k_EResultOK;
            var head = new byte[5];
            int len = payload.Length;
            head[0] = (byte)(len & 0xFF); head[1] = (byte)((len >> 8) & 0xFF);
            head[2] = (byte)((len >> 16) & 0xFF); head[3] = (byte)((len >> 24) & 0xFF);
            head[4] = (byte)channel;
            try
            {
                lock (_writeLock) { s.Write(head, 0, 5); s.Write(payload, 0, len); s.Flush(); }
                if (_wrote++ < 3) OnStatus?.Invoke($"wrote frame ch{channel} {len}B");
                return EResult.k_EResultOK;
            }
            catch (Exception e) { OnStatus?.Invoke("Loopback send failed: " + e.Message); return EResult.k_EResultFail; }
        }

        public void Pump()
        {
            while (_inbox.TryDequeue(out var ev))
            {
                switch (ev.Kind)
                {
                    case EvKind.Joined: OnStatus?.Invoke($"{NameOf(PeerId)} connected (loopback)."); OnPeerJoined?.Invoke(PeerId); break;
                    case EvKind.Data:   OnData?.Invoke(PeerId, ev.Data, ev.Channel); break;
                    case EvKind.Left:   OnStatus?.Invoke($"{NameOf(PeerId)} disconnected (loopback)."); OnPeerLeft?.Invoke(PeerId); break;
                }
            }
        }

        public IEnumerable<SteamNet.JoinableFriend> JoinableFriends() { yield break; }
    }
}
