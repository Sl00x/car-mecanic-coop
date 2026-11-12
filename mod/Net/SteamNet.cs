using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
// SteamManager is the game's global-namespace type (readiness gate), referenced unqualified.

namespace CarMecaSim.Mp.Net
{
    /// <summary>
    /// Steam P2P transport: a lobby for discovery + SteamNetworkingMessages for data.
    /// Reuses the game's already-initialized Steam client; never blocks the game.
    /// </summary>
    public sealed class SteamNet
    {
        public bool Ready { get; private set; }
        public CSteamID LocalId { get; private set; }
        public CSteamID LobbyId { get; private set; }
        public CSteamID LobbyOwner { get; private set; }
        public bool IsLobbyOwner => Ready && LobbyId.IsValid() && LobbyOwner == LocalId;

        // events
        public Action<CSteamID> OnLobbyEntered;     // we joined/created a lobby
        public Action<CSteamID> OnPeerJoined;
        public Action<CSteamID> OnPeerLeft;
        public Action<CSteamID, byte[], int> OnData; // sender, payload, channel
        public Action<string> OnStatus;

        private Callback<LobbyChatUpdate_t> _cbChat;
        private Callback<LobbyDataUpdate_t> _cbData;
        private Callback<GameLobbyJoinRequested_t> _cbJoinReq; // Steam overlay "Join game"
        private Callback<SteamNetworkingMessagesSessionRequest_t> _cbSession;
        private CallResult<LobbyCreated_t> _crCreate;
        private CallResult<LobbyEnter_t> _crEnter;

        private readonly IntPtr[] _recvBuf = new IntPtr[64];
        private bool _initTried;
        private bool _wantGuest;   // true once we've chosen to join a friend's lobby (stand down auto-host)

        public bool TryInit()
        {
            if (Ready) return true;
            if (_initTried && !Ready) { /* retry allowed */ }
            _initTried = true;

            // Wait until the game has brought Steam up.
            if (!SteamManager.Initialized) return false;

            try
            {
                // Bring up our own Steamworks.NET context over the already-loaded steam_api64.dll.
                if (!SteamAPI.Init())
                    OnStatus?.Invoke("SteamAPI.Init() returned false (continuing; game context may be shared).");

                LocalId = SteamUser.GetSteamID();
                SteamNetworkingUtils.InitRelayNetworkAccess();

                _cbChat = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
                _cbData = Callback<LobbyDataUpdate_t>.Create(_ => { });
                _cbJoinReq = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
                // Accept incoming P2P message sessions, else the first messages are dropped.
                _cbSession = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSessionRequest);
                _crCreate = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
                _crEnter = CallResult<LobbyEnter_t>.Create(OnLobbyEnter);

                Ready = true;
                OnStatus?.Invoke($"Steam ready as {LocalId} ({SteamFriends.GetPersonaName()}).");
                return true;
            }
            catch (Exception e)
            {
                OnStatus?.Invoke("Steam init failed: " + e.Message);
                return false;
            }
        }

        public void CreateLobby(ELobbyType type, int maxMembers)
        {
            if (!Ready) return;
            OnStatus?.Invoke("Creating lobby...");
            var call = SteamMatchmaking.CreateLobby(type, maxMembers);
            _crCreate.Set(call);
        }

        public void JoinLobby(CSteamID lobby)
        {
            if (!Ready) return;
            _wantGuest = true;   // we chose to join someone — a still-pending auto-host must stand down
            // If we were auto-hosting (or in another lobby), leave it before joining the target.
            if (LobbyId.IsValid() && LobbyId != lobby)
            {
                SteamMatchmaking.LeaveLobby(LobbyId);
                LobbyId = CSteamID.Nil;
            }
            OnStatus?.Invoke($"Joining lobby {lobby}...");
            var call = SteamMatchmaking.JoinLobby(lobby);
            _crEnter.Set(call);
        }

        public void LeaveLobby()
        {
            if (Ready && LobbyId.IsValid())
            {
                SteamMatchmaking.LeaveLobby(LobbyId);
                LobbyId = CSteamID.Nil;
            }
            _wantGuest = false;
            try { if (Ready) SteamFriends.ClearRichPresence(); } catch { }
        }

        /// <summary>Publish "connect" rich presence so friends get a "Join game" button in Steam.</summary>
        private void PublishJoinable()
        {
            try { if (Ready && LobbyId.IsValid()) SteamFriends.SetRichPresence("connect", "+connect_lobby " + LobbyId.m_SteamID); }
            catch { }
        }

        public IEnumerable<CSteamID> LobbyMembers()
        {
            if (!Ready || !LobbyId.IsValid()) yield break;
            int n = SteamMatchmaking.GetNumLobbyMembers(LobbyId);
            for (int i = 0; i < n; i++)
                yield return SteamMatchmaking.GetLobbyMemberByIndex(LobbyId, i);
        }

        public string NameOf(CSteamID id) => Ready ? SteamFriends.GetFriendPersonaName(id) : id.ToString();

        /// <summary>A friend currently hosting a joinable CMS co-op lobby (from their "connect" rich presence).</summary>
        public readonly struct JoinableFriend
        {
            public readonly CSteamID Id; public readonly string Name; public readonly ulong Lobby;
            public JoinableFriend(CSteamID id, string name, ulong lobby) { Id = id; Name = name; Lobby = lobby; }
        }

        /// <summary>Enumerate friends who are in THIS game and advertising a joinable lobby via rich presence.</summary>
        public IEnumerable<JoinableFriend> JoinableFriends()
        {
            if (!Ready) yield break;
            uint myApp = SteamUtils.GetAppID().m_AppId;
            int n = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < n; i++)
            {
                CSteamID fid;
                string connect;
                try
                {
                    fid = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                    // must be playing the same game
                    if (!SteamFriends.GetFriendGamePlayed(fid, out var fgi) || fgi.m_gameID.AppID().m_AppId != myApp) continue;
                    connect = SteamFriends.GetFriendRichPresence(fid, "connect");
                }
                catch { continue; }
                if (string.IsNullOrEmpty(connect)) continue;
                // format: "+connect_lobby <id>"
                var idx = connect.IndexOf("+connect_lobby", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                var rest = connect.Substring(idx + "+connect_lobby".Length).Trim();
                var tok = rest.Split(' ');
                if (tok.Length == 0 || !ulong.TryParse(tok[0], out var lobby) || lobby == 0) continue;
                yield return new JoinableFriend(fid, SteamFriends.GetFriendPersonaName(fid), lobby);
            }
        }

        public void SetLobbyData(string key, string val)
        {
            if (Ready && LobbyId.IsValid()) SteamMatchmaking.SetLobbyData(LobbyId, key, val);
        }
        public string GetLobbyData(string key) =>
            (Ready && LobbyId.IsValid()) ? SteamMatchmaking.GetLobbyData(LobbyId, key) : "";

        public EResult Send(CSteamID target, byte[] data, int sendFlags, int channel)
        {
            if (!Ready || data == null || data.Length == 0) return EResult.k_EResultFail;
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID(target);
            IntPtr p = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, p, data.Length);
                return SteamNetworkingMessages.SendMessageToUser(ref identity, p, (uint)data.Length, sendFlags, channel);
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        public void Broadcast(byte[] data, int sendFlags, int channel, CSteamID except = default)
        {
            foreach (var m in LobbyMembers())
                if (m != LocalId && m != except) Send(m, data, sendFlags, channel);
        }

        /// <summary>Call every frame: dispatch Steam callbacks and drain inbound messages.</summary>
        public void Pump()
        {
            if (!Ready) return;
            try { SteamAPI.RunCallbacks(); } catch { /* game also pumps; ignore */ }
            Receive(Channels.Unreliable);
            Receive(Channels.Reliable);
        }

        private void Receive(int channel)
        {
            int n;
            try { n = SteamNetworkingMessages.ReceiveMessagesOnChannel(channel, _recvBuf, _recvBuf.Length); }
            catch { return; }
            for (int i = 0; i < n; i++)
            {
                IntPtr ptr = _recvBuf[i];
                if (ptr == IntPtr.Zero) continue;
                try
                {
                    var msg = SteamNetworkingMessage_t.FromIntPtr(ptr);
                    int size = msg.m_cbSize;
                    var data = new byte[size];
                    Marshal.Copy(msg.m_pData, data, 0, size);
                    var sender = msg.m_identityPeer.GetSteamID();
                    OnData?.Invoke(sender, data, channel);
                }
                catch (Exception e) { OnStatus?.Invoke("recv error: " + e.Message); }
                finally { SteamNetworkingMessage_t.Release(ptr); }
            }
        }

        // --- callbacks ---
        private void OnLobbyCreated(LobbyCreated_t cb, bool ioFail)
        {
            if (ioFail || cb.m_eResult != EResult.k_EResultOK)
            { OnStatus?.Invoke("Lobby create failed: " + cb.m_eResult); return; }
            var created = new CSteamID(cb.m_ulSteamIDLobby);
            if (_wantGuest)
            {
                // We decided to join a friend while this auto-host create was still in flight — abandon it,
                // so we don't end up owner of an orphan lobby (and never request the host's snapshot).
                SteamMatchmaking.LeaveLobby(created);
                OnStatus?.Invoke("Auto-host lobby abandoned (joining a friend instead).");
                return;
            }
            LobbyId = created;
            LobbyOwner = LocalId;
            SetLobbyData("cms_mp", Protocol.Version.ToString());
            PublishJoinable();
            OnStatus?.Invoke($"Lobby created: {LobbyId}");
            OnLobbyEntered?.Invoke(LobbyId);
        }

        private void OnLobbyEnter(LobbyEnter_t cb, bool ioFail)
        {
            if (ioFail) { OnStatus?.Invoke("Lobby enter IO failure."); return; }
            LobbyId = new CSteamID(cb.m_ulSteamIDLobby);
            LobbyOwner = SteamMatchmaking.GetLobbyOwner(LobbyId);
            PublishJoinable();
            OnStatus?.Invoke($"Entered lobby {LobbyId}, owner {LobbyOwner}");
            OnLobbyEntered?.Invoke(LobbyId);
            // Open a P2P session to the host by sending a probe handled upstream (Hello).
        }

        private void OnLobbyChatUpdate(LobbyChatUpdate_t cb)
        {
            var who = new CSteamID(cb.m_ulSteamIDUserChanged);
            const uint entered = (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered;
            const uint gone = (uint)(EChatMemberStateChange.k_EChatMemberStateChangeLeft
                                   | EChatMemberStateChange.k_EChatMemberStateChangeDisconnected
                                   | EChatMemberStateChange.k_EChatMemberStateChangeKicked
                                   | EChatMemberStateChange.k_EChatMemberStateChangeBanned);
            if ((cb.m_rgfChatMemberStateChange & entered) != 0 && who != LocalId) OnPeerJoined?.Invoke(who);
            else if ((cb.m_rgfChatMemberStateChange & gone) != 0) OnPeerLeft?.Invoke(who);
        }

        private void OnJoinRequested(GameLobbyJoinRequested_t cb)
        {
            OnStatus?.Invoke("Steam overlay join requested.");
            JoinLobby(cb.m_steamIDLobby);
        }

        private void OnSessionRequest(SteamNetworkingMessagesSessionRequest_t cb)
        {
            var id = cb.m_identityRemote;
            try { SteamNetworkingMessages.AcceptSessionWithUser(ref id); } catch { }
            OnStatus?.Invoke("P2P session accepted: " + id.GetSteamID());
        }
    }
}
