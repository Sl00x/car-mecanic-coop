using System;
using System.Collections.Generic;
using Steamworks;

namespace CarMecaSim.Mp.Net
{
    /// <summary>
    /// Transport abstraction the co-op session talks to. The real transport is <see cref="SteamNet"/>
    /// (Steam lobby + P2P); <see cref="LoopbackNet"/> is a local TCP stand-in used only for testing a
    /// second player on the same machine. Keeps the Steam-shaped surface (CSteamID ids) so
    /// <see cref="CarMecaSim.Mp.Core.MpSession"/> stays identical regardless of transport.
    /// </summary>
    public interface INet
    {
        bool Ready { get; }
        CSteamID LocalId { get; }
        CSteamID LobbyId { get; }
        CSteamID LobbyOwner { get; }
        bool IsLobbyOwner { get; }

        Action<CSteamID> OnLobbyEntered { get; set; }
        Action<CSteamID> OnPeerJoined { get; set; }
        Action<CSteamID> OnPeerLeft { get; set; }
        Action<CSteamID, byte[], int> OnData { get; set; }   // sender, payload, channel
        Action<string> OnStatus { get; set; }

        bool TryInit();
        void CreateLobby(ELobbyType type, int maxMembers);
        void JoinLobby(CSteamID lobby);
        void LeaveLobby();
        IEnumerable<CSteamID> LobbyMembers();
        string NameOf(CSteamID id);
        EResult Send(CSteamID target, byte[] data, int sendFlags, int channel);
        void Broadcast(byte[] data, int sendFlags, int channel, CSteamID except = default);
        void Pump();
        IEnumerable<SteamNet.JoinableFriend> JoinableFriends();
    }
}
