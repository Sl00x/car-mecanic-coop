using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Steamworks;

namespace CarMecaSim.Mp.Net
{
    /// <summary>
    /// TEST-ONLY transport: a local TCP server on 127.0.0.1:&lt;port&gt; standing in for Steam P2P so a
    /// headless peer (tools/FakePeer) can act as a second player on the same machine. The mod is always
    /// the host/owner here; exactly one guest is supported. TCP (not UDP) so the chunked save snapshot
    /// streams reliably with no datagram-size limit. Frame = [i32 payloadLen][u8 channel][payload].
    /// </summary>
    public sealed class LoopbackNet : INet
    {
        // Synthetic, stable ids (CSteamIDs just used as opaque handles here).
        private static readonly CSteamID Host  = new CSteamID(1UL);
        private static readonly CSteamID Guest = new CSteamID(2UL);
        private static readonly CSteamID Lobby = new CSteamID(1000UL);

        public bool Ready { get; private set; }
        public CSteamID LocalId => Host;
        public CSteamID LobbyId { get; private set; }
        public CSteamID LobbyOwner { get; private set; }
        public bool IsLobbyOwner => Ready && LobbyId.IsValid() && LobbyOwner == LocalId;

        public Action<CSteamID> OnLobbyEntered { get; set; }
        public Action<CSteamID> OnPeerJoined { get; set; }
        public Action<CSteamID> OnPeerLeft { get; set; }
        public Action<CSteamID, byte[], int> OnData { get; set; }
        public Action<string> OnStatus { get; set; }

        private readonly int _port;
        private TcpListener _listener;
        private TcpClient _client;
        private NetworkStream _stream;
        private readonly object _writeLock = new object();
        private volatile bool _guestConnected;

        // Background threads enqueue; Pump() drains on the Unity main thread (OnData touches game state).
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
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
                var accept = new Thread(AcceptLoop) { IsBackground = true, Name = "CarMeca-Loopback-Accept" };
                accept.Start();
                Ready = true;
                OnStatus?.Invoke($"Loopback transport listening on 127.0.0.1:{_port} (host).");
                return true;
            }
            catch (Exception e) { OnStatus?.Invoke("Loopback bind failed: " + e.Message); return false; }
        }

        private void AcceptLoop()
        {
            while (true)
            {
                TcpClient c;
                try { c = _listener.AcceptTcpClient(); }
                catch { return; }   // listener stopped
                if (_guestConnected) { try { c.Close(); } catch { } continue; }   // only one guest
                try { c.NoDelay = true; } catch { }
                _client = c; _stream = c.GetStream(); _guestConnected = true;
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
                _guestConnected = false;
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

        public void CreateLobby(ELobbyType type, int maxMembers)
        {
            LobbyId = Lobby; LobbyOwner = Host;
            OnStatus?.Invoke("Loopback lobby ready (host).");
            OnLobbyEntered?.Invoke(LobbyId);
        }

        public void JoinLobby(CSteamID lobby) { /* loopback host never joins */ }

        public void LeaveLobby() { LobbyId = CSteamID.Nil; }

        public IEnumerable<CSteamID> LobbyMembers()
        {
            yield return Host;
            if (_guestConnected) yield return Guest;
        }

        public string NameOf(CSteamID id) => id == Guest ? "FakePeer" : id == Host ? "Host" : id.ToString();

        public EResult Send(CSteamID target, byte[] data, int sendFlags, int channel)
        {
            if (!_guestConnected || target == Host || data == null || data.Length == 0)
                return EResult.k_EResultOK;
            return WriteFrame(data, channel);
        }

        public void Broadcast(byte[] data, int sendFlags, int channel, CSteamID except = default)
        {
            if (_guestConnected && Guest != except) WriteFrame(data, channel);
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
                    case EvKind.Joined: OnStatus?.Invoke("FakePeer connected (loopback)."); OnPeerJoined?.Invoke(Guest); break;
                    case EvKind.Data:   OnData?.Invoke(Guest, ev.Data, ev.Channel); break;
                    case EvKind.Left:   OnStatus?.Invoke("FakePeer disconnected (loopback)."); OnPeerLeft?.Invoke(Guest); break;
                }
            }
        }

        public IEnumerable<SteamNet.JoinableFriend> JoinableFriends() { yield break; }
    }
}
