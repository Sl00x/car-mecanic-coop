namespace CarMecaSim.Mp.Net
{
    /// <summary>Wire packet identifiers. See docs/protocol.md.</summary>
    public enum PacketType : byte
    {
        Hello = 0,
        Welcome = 1,
        Reject = 2,
        FullState = 3,
        PlayerJoin = 4,
        PlayerLeave = 5,
        Presence = 6,
        PartAction = 7,
        CarOp = 8,
        Economy = 9,
        Order = 10,
        Inventory = 11,
        Chat = 12,
        Ping = 13,
        Bye = 14,
        Latency = 15,   // RTT probe/echo for the player-list ping display
        EntityHash = 16, // host broadcasts the current vehicle's canonical state hash → peers detect desync (§14.3)
        PartState = 17,  // mechanical part mount/unmount by positional identity (type,key,index,mounted)
    }

    public static class Channels
    {
        public const int Unreliable = 0;   // presence
        public const int Reliable = 1;      // control / state / economy / chat
    }

    /// <summary>Steam send flags (mirror of ESteamNetworkingSend_* values).</summary>
    public static class SendFlags
    {
        public const int Unreliable = 0;
        public const int NoDelay = 4;
        public const int UnreliableNoDelay = Unreliable | NoDelay; // 4
        public const int Reliable = 8;
    }

    public static class Protocol
    {
        /// <summary>Bumped when the wire format changes incompatibly.</summary>
        public const int Version = 1;
    }
}
