using System.Collections.Generic;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;

namespace CarMecaSim.Mp.Sync
{
    /// <summary>
    /// Shared garage, host-authoritative. The garage is replicated through the full SAVE SNAPSHOT
    /// (SaveSync): the whole profile at join, and a throttled delta whenever the host loads/removes a
    /// car. This module just forwards the host's "garage changed" signal — the old name-only broadcast
    /// is gone (it couldn't rebuild a car's saved config: "Cannot load config 1 for car ...").
    /// </summary>
    public static class CarSync
    {
        public const byte OpGarage = 2;   // legacy wire op; no longer emitted, handler is a no-op

        public static bool Applying;      // true while SaveSync is applying the host's garage

        private static MpSession S => CarMecaPlugin.Session;
        private static bool Active => S != null && S.InLobby && GameRefs.InGameSession;

        /// <summary>Harmony postfix on CarLoader.LoadCar: host's garage changed → flag a delta snapshot.</summary>
        public static void OnLocalLoadCar(CarLoader cl, string name)
        {
            if (Applying || !Active || !S.IsHost) return;
            S.MarkGarageChanged();
        }

        /// <summary>Initial garage now arrives via the full save snapshot; nothing to do here.</summary>
        public static void OnPeerJoined() { }

        /// <summary>Legacy name-only garage op — superseded by the save snapshot. No-op.</summary>
        public static void ApplyGarage(List<(int, string)> entries) { }
    }
}
