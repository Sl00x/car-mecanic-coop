using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using CarMecaSim.Mp.Core;

namespace CarMecaSim.Mp.Game
{
    /// <summary>
    /// Data-driven car transport: turns the game's own <c>NewCarData</c> (a serializable value type) into a
    /// byte[] and back, using the game's native <c>NewCarData.Serialize(BinaryWriter,byte)</c> /
    /// <c>Deserialize(BinaryReader,byte)</c> with the current save version (<c>GlobalData.CurrentSaveVersion</c>).
    /// This is what lets a client reproduce the host's car WITHOUT deserializing the host's whole profile or
    /// touching its own save file — the fragile path that threw "assertion failed" on a guest. The reconstructed
    /// NewCarData is fed to <c>CarLoader.LoadCarFromFile(NewCarData)</c> (verified Public in the interop), the
    /// exact data-driven spawn primitive CMS21-Together (MIT) uses.
    /// </summary>
    public static class CarCodec
    {
        /// <summary>Current on-disk save format version — host and client run the same Steam build, so this is
        /// identical on both and makes Serialize/Deserialize round-trip exactly.</summary>
        public static byte Version()
        {
            try { return GlobalData.CurrentSaveVersion; } catch { return 0; }
        }

        /// <summary>Serialize one car (the game's NewCarData) to a managed byte[] via the game's own writer.</summary>
        public static byte[] Serialize(NewCarData cd)
        {
            if (cd == null) return null;
            try
            {
                var ms = new Il2CppSystem.IO.MemoryStream();
                var bw = new Il2CppSystem.IO.BinaryWriter(ms);
                cd.Serialize(bw, Version());
                bw.Flush();
                Il2CppStructArray<byte> raw = ms.ToArray();
                if (raw == null) return null;
                int n = raw.Length;
                var managed = new byte[n];
                for (int i = 0; i < n; i++) managed[i] = raw[i];
                return managed;
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[car] serialize failed: " + e.Message); return null; }
        }

        /// <summary>Rebuild a NewCarData from bytes produced by <see cref="Serialize"/>.</summary>
        public static NewCarData Deserialize(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            try
            {
                var arr = new Il2CppStructArray<byte>(data);
                var ms = new Il2CppSystem.IO.MemoryStream(arr);
                var br = new Il2CppSystem.IO.BinaryReader(ms);
                var cd = new NewCarData();
                cd.Deserialize(br, Version());
                return cd;
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[car] deserialize failed: " + e.Message); return null; }
        }

        /// <summary>The saved NewCarData for a given garage place (or null). After a live load the host should
        /// SaveCarToFile() first so this reflects the current car.</summary>
        public static NewCarData CarDataAt(ProfileData pd, int place)
        {
            try
            {
                var garage = pd != null ? pd.carsInGarage : null;
                if (garage == null || place < 0 || place >= garage.Length) return null;
                return garage[place];
            }
            catch { return null; }
        }

        /// <summary>Config name of the saved car at a place ("" if empty/none).</summary>
        public static string CarNameAt(ProfileData pd, int place)
        {
            try { var cd = CarDataAt(pd, place); var n = cd != null ? cd.carToLoad : null; return string.IsNullOrEmpty(n) ? "" : n; }
            catch { return ""; }
        }
    }
}
