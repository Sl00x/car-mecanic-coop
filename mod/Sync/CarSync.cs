using System;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;

namespace CarMecaSim.Mp.Sync
{
    /// <summary>
    /// Shared garage: when a player loads a car, the others load the same car, so you both see
    /// the vehicle on the lift. Loading the host's car also replaces the joiner's private car.
    /// (v1 — model-level; exact part/damage state then syncs via PartSync.)
    /// </summary>
    public static class CarSync
    {
        public const byte OpLoad = 0;

        public static bool Applying;   // true while applying a remote car load (don't re-broadcast)

        private static MpSession S => CarMecaPlugin.Session;
        private static bool Active => S != null && S.InLobby && GameRefs.InGameSession;
        private static string _lastSent;

        /// <summary>Harmony postfix on CarLoader.LoadCar: broadcast the car we just loaded.</summary>
        public static void OnLocalLoadCar(CarLoader cl, string name)
        {
            if (Applying || !Active || string.IsNullOrEmpty(name)) return;
            if (name == _lastSent) return;             // avoid spamming the same load
            _lastSent = name;
            CarMecaPlugin.Logger?.LogInfo("[car] local load -> broadcast " + name);
            S.SendCarOp(OpLoad, name);
        }

        /// <summary>Host sends its current car to a newcomer so they load into the same garage.</summary>
        public static void OnPeerJoined()
        {
            if (!Active || !S.IsHost) return;
            try
            {
                var cl = CarRefs.CurrentCar();
                var name = cl != null ? cl.carToLoad : null;
                if (!string.IsNullOrEmpty(name)) { _lastSent = null; S.SendCarOp(OpLoad, name); }
            }
            catch { }
        }

        public static void ApplyRemote(byte op, string name)
        {
            if (!Active || op != OpLoad || string.IsNullOrEmpty(name)) return;
            var cl = CarRefs.CurrentCar();
            if (cl == null) { CarMecaPlugin.Logger?.LogWarning("[car] no CarLoader to load " + name); return; }
            if (name == _lastSent) return;             // we already have this car
            Applying = true;
            try
            {
                CarMecaPlugin.Logger?.LogInfo("[car] loading remote car: " + name);
                _lastSent = name;
                cl.StartCoroutine(cl.LoadCar(name));   // replaces the current car with the shared one
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[car] load failed: " + e.Message); }
            Applying = false;
        }
    }
}
