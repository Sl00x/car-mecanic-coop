using System;
using Il2CppInterop.Runtime;

namespace CarMecaSim.Mp.Game
{
    /// <summary>
    /// Reads/writes the live economy statics on GameSettings. Il2CppInterop dropped these fields,
    /// and il2cpp_class_get_field_from_name doesn't resolve them, so we ENUMERATE the class fields
    /// and match by name (after initializing the class). Robust across the generation gaps.
    /// </summary>
    public static unsafe class GameState
    {
        private static IntPtr _cls, _money, _exp, _scraps, _level, _jobs;
        private static bool _scanned;
        public static string Status { get; private set; } = "pas encore résolu";

        private static void Resolve()
        {
            if (_money != IntPtr.Zero) return;   // resolved
            if (_scanned) return;                // already scanned the class; fields absent -> stop retrying
            try
            {
                if (_cls == IntPtr.Zero)
                    foreach (var asm in new[] { "Assembly-CSharp-firstpass.dll", "Assembly-CSharp-firstpass",
                                                "Assembly-CSharp.dll", "Assembly-CSharp" })
                    {
                        _cls = IL2CPP.GetIl2CppClass(asm, "", "GameSettings");
                        if (_cls != IntPtr.Zero) break;
                    }
                if (_cls == IntPtr.Zero) { Status = "classe GameSettings introuvable (réessai)"; return; }

                IL2CPP.il2cpp_runtime_class_init(_cls);

                int count = 0;
                IntPtr iter = IntPtr.Zero, f;
                while ((f = IL2CPP.il2cpp_class_get_fields(_cls, ref iter)) != IntPtr.Zero)
                {
                    count++;
                    switch (IL2CPP.il2cpp_field_get_name_(f))
                    {
                        case "PlayerMoney":  _money = f;  break;
                        case "PlayerExp":    _exp = f;    break;
                        case "PlayerScraps": _scraps = f; break;
                        case "PlayerLevel":  _level = f;  break;
                        case "Jobs":         _jobs = f;   break;
                    }
                }
                _scanned = true;
                Status = $"champs={count} money={(_money != IntPtr.Zero ? "ok" : "X")}";
            }
            catch (Exception e) { Status = "ex: " + e.Message; _scanned = true; }
            try { CarMecaSim.Mp.CarMecaPlugin.Logger?.LogInfo("[GameState] " + Status); } catch { }
        }

        private static int ReadInt(IntPtr field)
        {
            if (field == IntPtr.Zero) return 0;
            int v = 0; try { IL2CPP.il2cpp_field_static_get_value(field, &v); } catch { } return v;
        }
        private static void WriteInt(IntPtr field, int v)
        {
            if (field == IntPtr.Zero) return;
            try { IL2CPP.il2cpp_field_static_set_value(field, &v); } catch { }
        }

        public static bool Available { get { Resolve(); return _money != IntPtr.Zero; } }
        public static int Money  { get { Resolve(); return ReadInt(_money); }  set { Resolve(); WriteInt(_money, value); } }
        public static int Exp    { get { Resolve(); return ReadInt(_exp); }    set { Resolve(); WriteInt(_exp, value); } }
        public static int Scraps { get { Resolve(); return ReadInt(_scraps); } set { Resolve(); WriteInt(_scraps, value); } }
        public static int Level  { get { Resolve(); return ReadInt(_level); }  set { Resolve(); WriteInt(_level, value); } }
        public static int Jobs   { get { Resolve(); return ReadInt(_jobs); } }
    }
}
