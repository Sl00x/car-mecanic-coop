using System;
using Il2CppInterop.Runtime;

namespace CarMecaSim.Mp.Game
{
    /// <summary>
    /// Reads the live economy statics on GameSettings. Il2CppInterop failed to restore these
    /// fields on this (IL2CPP Master) build, so we access them by name via the il2cpp runtime
    /// API instead of generated wrappers. Read-only for now; setters land with Milestone D.
    /// </summary>
    public static unsafe class GameState
    {
        private static IntPtr _cls, _money, _exp, _scraps, _level, _jobs;
        private static bool _tried;

        private static void Resolve()
        {
            if (_tried && _cls != IntPtr.Zero) return;
            _tried = true;
            try
            {
                // GameSettings is a static class in the interop, so it can't be a generic type arg;
                // resolve the il2cpp class by name instead. The image-name key format varies, so try a few.
                foreach (var asmName in new[] { "Assembly-CSharp-firstpass.dll", "Assembly-CSharp-firstpass",
                                                "Assembly-CSharp.dll", "Assembly-CSharp" })
                {
                    _cls = IL2CPP.GetIl2CppClass(asmName, "", "GameSettings");
                    if (_cls != IntPtr.Zero) break;
                }
                if (_cls == IntPtr.Zero) return;
                _money  = IL2CPP.GetIl2CppField(_cls, "PlayerMoney");
                _exp    = IL2CPP.GetIl2CppField(_cls, "PlayerExp");
                _scraps = IL2CPP.GetIl2CppField(_cls, "PlayerScraps");
                _level  = IL2CPP.GetIl2CppField(_cls, "PlayerLevel");
                _jobs   = IL2CPP.GetIl2CppField(_cls, "Jobs");
            }
            catch { _cls = IntPtr.Zero; }
        }

        private static int ReadInt(IntPtr field)
        {
            if (field == IntPtr.Zero) return 0;
            int v = 0;
            try { IL2CPP.il2cpp_field_static_get_value(field, &v); } catch { }
            return v;
        }

        private static void WriteInt(IntPtr field, int v)
        {
            if (field == IntPtr.Zero) return;
            try { IL2CPP.il2cpp_field_static_set_value(field, &v); } catch { }
        }

        public static bool Available { get { Resolve(); return _cls != IntPtr.Zero && _money != IntPtr.Zero; } }
        public static int Money  { get { Resolve(); return ReadInt(_money); } set { Resolve(); WriteInt(_money, value); } }
        public static int Exp    { get { Resolve(); return ReadInt(_exp); }   set { Resolve(); WriteInt(_exp, value); } }
        public static int Scraps { get { Resolve(); return ReadInt(_scraps); } set { Resolve(); WriteInt(_scraps, value); } }
        public static int Level  { get { Resolve(); return ReadInt(_level); } set { Resolve(); WriteInt(_level, value); } }
        public static int Jobs   { get { Resolve(); return ReadInt(_jobs); } }
    }
}
