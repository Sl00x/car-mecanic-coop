using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using CarMecaSim.Shared;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;
using CarMecaSim.Mp.UI;

namespace CarMecaSim.Mp
{
    [BepInPlugin("com.alexu.carmecasim.mp", Constants.ModName, Constants.ModVersion)]
    public sealed class CarMecaPlugin : BasePlugin
    {
        public static ManualLogSource Logger;
        public static MpSession Session;
        public static Overlay Overlay;
        public static MpConfig Cfg = new();

        public override void Load()
        {
            Logger = Log;
            Log.LogInfo("CarMecaSim co-op loading...");
            LoadConfig();

            Session = new MpSession();
            Session.Init(Cfg, s => Log.LogInfo(s));
            Overlay = new Overlay(Session);

            // Drive Update/OnGUI via an injected Il2Cpp MonoBehaviour (loader-independent game loop).
            ClassInjector.RegisterTypeInIl2Cpp<MpBehaviour>();
            var go = new GameObject("CarMecaSim_Mp");
            go.hideFlags = HideFlags.HideAndDontSave;
            GameObject.DontDestroyOnLoad(go);
            go.AddComponent<MpBehaviour>();

            // Harmony patches for car/part sync + per-part locking. Patch each class INDIVIDUALLY so one
            // bad patch (e.g. an ambiguous method match) can't drop every other patch like PatchAll would.
            try
            {
                var harmony = new HarmonyLib.Harmony("com.alexu.carmecasim.mp");
                int ok = 0, fail = 0;
                foreach (var type in System.Reflection.Assembly.GetExecutingAssembly().GetTypes())
                {
                    if (type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), true).Length == 0) continue;
                    try { new HarmonyLib.PatchClassProcessor(harmony, type).Patch(); ok++; }
                    catch (Exception pe) { fail++; Log.LogError($"Harmony patch '{type.Name}' failed: {pe.Message}"); }
                }
                Log.LogInfo($"Harmony patches applied: {ok} ok, {fail} failed.");
                SkipTutorials(harmony);
            }
            catch (Exception e) { Log.LogError("Harmony patch failed: " + e); }

            Log.LogInfo($"Ready. role={Cfg.Role} name={Cfg.DisplayName} lobby={Cfg.LobbyId}");
        }

        // Skip the game's tutorial (annoying, esp. on a new garage). The declaring type lives in the
        // firstpass interop (beyond our RE tooling budget), so find it by reflection at runtime — scan the
        // game assemblies for a Tutorial-named type exposing InstantShowTutorial(), then no-op it (+ force
        // ShouldShow() false if present). Each patch is guarded so a miss never breaks startup.
        private void SkipTutorials(HarmonyLib.Harmony harmony)
        {
            try
            {
                const System.Reflection.BindingFlags BF = System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
                System.Reflection.MethodInfo instantShow = null, shouldShow = null, loadNext = null;
                string typeName = null;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    var an = asm.GetName().Name;
                    if (an != "Assembly-CSharp" && an != "Assembly-CSharp-firstpass") continue;
                    System.Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (System.Reflection.ReflectionTypeLoadException rtle) { types = rtle.Types; }
                    catch { continue; }
                    foreach (var t in types)
                    {
                        if (t == null || t.Name.IndexOf("Tutorial", StringComparison.Ordinal) < 0) continue;
                        System.Reflection.MethodInfo m;
                        try { m = t.GetMethod("InstantShowTutorial", BF, null, System.Type.EmptyTypes, null); }
                        catch { m = null; }
                        if (m == null) continue;
                        typeName = t.FullName; instantShow = m;
                        try { shouldShow = t.GetMethod("ShouldShow", BF, null, System.Type.EmptyTypes, null); } catch { }
                        try { loadNext = t.GetMethod("LoadNextTutorial", BF, null, System.Type.EmptyTypes, null); } catch { }
                        break;
                    }
                    if (instantShow != null) break;
                }
                if (instantShow == null) { Log.LogWarning("[tuto] tutorial type not found — skip not applied"); return; }
                Log.LogInfo($"[tuto] tutorial type: {typeName} (ShouldShow={(shouldShow != null)}, LoadNext={(loadNext != null)})");

                var skipVoid = new HarmonyLib.HarmonyMethod(typeof(Sync.TutorialSkip).GetMethod(nameof(Sync.TutorialSkip.SkipVoid)));
                try { harmony.Patch(instantShow, prefix: skipVoid); Log.LogInfo("[tuto] InstantShowTutorial no-op'd"); }
                catch (Exception e) { Log.LogWarning("[tuto] InstantShowTutorial patch: " + e.Message); }
                if (loadNext != null)
                    try { harmony.Patch(loadNext, prefix: skipVoid); Log.LogInfo("[tuto] LoadNextTutorial no-op'd"); } catch { }
                if (shouldShow != null && shouldShow.ReturnType == typeof(bool))
                {
                    var shouldFalse = new HarmonyLib.HarmonyMethod(typeof(Sync.TutorialSkip).GetMethod(nameof(Sync.TutorialSkip.ShouldShowFalse)));
                    try { harmony.Patch(shouldShow, prefix: shouldFalse); Log.LogInfo("[tuto] ShouldShow forced false"); }
                    catch (Exception e) { Log.LogWarning("[tuto] ShouldShow patch: " + e.Message); }
                }
            }
            catch (Exception e) { Log.LogWarning("[tuto] skip setup failed: " + e.Message); }
        }

        private void LoadConfig()
        {
            try
            {
                var path = Path.Combine(Paths.ConfigPath, "CarMecaSim", "mp_config.json");
                if (File.Exists(path)) { Cfg = ConfigIo.Read(path); Log.LogInfo("Loaded mp_config.json"); }
                else Log.LogWarning("No mp_config.json; use the in-game overlay (F6) to host/join.");
            }
            catch (Exception e) { Log.LogError("Config load failed: " + e); }
        }
    }

    /// <summary>Il2Cpp-injected MonoBehaviour that forwards the Unity loop to the mod.</summary>
    public sealed class MpBehaviour : MonoBehaviour
    {
        public MpBehaviour(IntPtr ptr) : base(ptr) { }

        private bool _autoStarted;
        private bool _frozen;
        private float _savedTime = 1f;
        private CursorLockMode _savedLock;
        private bool _savedCursor;
        private bool _savedReadInput = true;

        private void Update()
        {
            var s = CarMecaPlugin.Session;
            if (s == null) return;

            // F6 opens/closes the full co-op panel — only during an active session.
            if (GameRefs.InGameSession && Input.GetKeyDown(KeyCode.F6))
            {
                var ov = CarMecaPlugin.Overlay;
                if (ov != null) { ov.Visible = !ov.Visible; SetFrozen(AnyPanelOpen()); }
            }
            // F8 opens/closes the host mega-trainer (freezes + frees the cursor, same as F6).
            if (GameRefs.InGameSession && Input.GetKeyDown(KeyCode.F8))
            {
                UI.TrainerOverlay.Visible = !UI.TrainerOverlay.Visible;
                SetFrozen(AnyPanelOpen());
            }
            // F9 toggles the read-only network/state debug overlay (any time).
            if (Input.GetKeyDown(KeyCode.F9)) UI.NetDebugOverlay.Visible = !UI.NetDebugOverlay.Visible;
            // Safety: never stay frozen if we lose the session or every panel closed another way.
            if (_frozen && (!GameRefs.InGameSession || !AnyPanelOpen()))
                SetFrozen(false);
            // Keep the game frozen + its FPS input OFF each frame while the panel is open, so the camera can't
            // grab the mouse back and typing in chat can't leak into gameplay keybinds. The cursor itself is
            // re-asserted in OnGUI (which runs AFTER the game's Update each frame, so we get the last word and
            // the game can't re-hide it — the old "I had to press Échap" bug).
            if (_frozen)
            {
                try { Time.timeScale = 0f; GameRefs.SetPlayerInputEnabled(false); } catch { }
            }

            s.EnsureSteam();
            // Use UNSCALED time so networking/chat keep running even while the game is frozen (timeScale 0).
            // Guarded: a per-frame throw is swallowed by the IL2CPP trampoline anyway, but without this it
            // spams thousands of identical stack traces (as seen when the stale Shared.dll threw TypeLoadException).
            SafeFrame("session.Update", () => s.Update(Time.unscaledDeltaTime));
            SafeFrame("presence", () => s.SendPresenceTick(Time.unscaledTime));
            SafeFrame("coopMenu", () => UI.NativeCoopMenu.Tick(s));   // native "MULTIJOUEUR" button on the base main menu

            var cfg = CarMecaPlugin.Cfg;
            if (!_autoStarted && cfg.AutoStart && cfg.Role != MpRole.Menu && s.EnsureSteam())
            {
                s.StartFromConfig();
                _autoStarted = true;
            }

            // --- character model: headless build-validation (once) + F7 visual test spawn ---
            _charClock += Time.unscaledDeltaTime;
            if (!_charValidated && _charClock > 3f)
            {
                _charValidated = true;
                try
                {
                    var test = CarMecaSim.Mp.Core.CharacterModel.Instantiate(1, out _);
                    if (test != null)
                    {
                        CarMecaPlugin.Logger?.LogInfo("[char] build-validation OK (mesh/skeleton/material constructed).");
                        GameObject.Destroy(test);
                    }
                    else CarMecaPlugin.Logger?.LogInfo("[char] build-validation: no model (mechanic.cmch absent → primitives).");
                }
                catch (Exception e) { CarMecaPlugin.Logger?.LogError("[char] build-validation FAILED: " + e); }
            }
            // F7 (in-session): spawn/despawn a mechanic in front of you to eyeball the model.
            if (GameRefs.InGameSession && Input.GetKeyDown(KeyCode.F7)) ToggleTestMechanic();
            if (_testMech != null && _testRig != null) { try { _testRig.Animate(0f, true, Time.unscaledTime); } catch { } }
        }

        private bool _charValidated;
        private float _charClock;
        private GameObject _testMech;
        private CarMecaSim.Mp.Core.CharacterRig _testRig;

        /// <summary>Debug: spawn/despawn a mechanic 2.5 m in front of the player to eyeball the model.</summary>
        private void ToggleTestMechanic()
        {
            if (_testMech != null) { GameObject.Destroy(_testMech); _testMech = null; _testRig = null; return; }
            var eye = GameRefs.Eye;
            _testMech = CarMecaSim.Mp.Core.CharacterModel.Instantiate(1, out _testRig);
            if (_testMech == null) { CarMecaPlugin.Logger?.LogWarning("[char] F7: no model to spawn."); return; }
            if (eye != null)
            {
                var fwd = eye.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
                _testMech.transform.position = eye.position + fwd * 2.5f + Vector3.down * 1.65f;
                _testMech.transform.rotation = Quaternion.LookRotation(-fwd);
            }
            CarMecaPlugin.Logger?.LogInfo($"[char] F7 test mechanic spawned at {_testMech.transform.position}.");
        }

        /// <summary>True if any mod panel that needs the cursor is open (F6 co-op or F8 trainer).</summary>
        private static bool AnyPanelOpen()
        {
            var ov = CarMecaPlugin.Overlay;
            return (ov != null && ov.Visible) || UI.TrainerOverlay.Visible;
        }

        /// <summary>Freeze/unfreeze the local game while a mod panel is open (F6/F8), freeing the cursor.</summary>
        private void SetFrozen(bool on)
        {
            if (on == _frozen) return;
            _frozen = on;
            try
            {
                if (on)
                {
                    _savedTime = Time.timeScale; _savedLock = Cursor.lockState; _savedCursor = Cursor.visible;
                    _savedReadInput = GameRefs.PlayerInputEnabled;
                    Time.timeScale = 0f;
                    GameRefs.SetPlayerInputEnabled(false);   // stop the FPS camera eating the mouse + keybinds
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else
                {
                    Time.timeScale = _savedTime <= 0f ? 1f : _savedTime;
                    GameRefs.SetPlayerInputEnabled(_savedReadInput);
                    Cursor.lockState = _savedLock;
                    Cursor.visible = _savedCursor;
                }
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("freeze toggle failed: " + e.Message); }
        }

        private void OnGUI()
        {
            // Re-assert the free cursor HERE (OnGUI runs after the game's Update each frame) so the game's
            // FPS controller can't re-hide/re-lock it — this is what fixes "I had to press Échap to use the menu".
            if (_frozen)
            {
                try { if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None; if (!Cursor.visible) Cursor.visible = true; } catch { }
            }
            SafeFrame("overlay", () => CarMecaPlugin.Overlay?.Draw());        // in-game co-op panel (F6)
            SafeFrame("trainer", () => UI.TrainerOverlay.Draw(CarMecaPlugin.Session));     // host mega-trainer (F8)
            SafeFrame("netdebug", () => UI.NetDebugOverlay.Draw(CarMecaPlugin.Session));   // network/state debug (F9)
        }

        // Rate-limited guard for per-frame hooks: keeps one bad frame from flooding the log (and makes any
        // such regression visible without the game appearing to freeze).
        private static int _frameErr;
        private static void SafeFrame(string tag, Action body)
        {
            try { body(); }
            catch (Exception e)
            {
                if (_frameErr++ < 30)
                    CarMecaPlugin.Logger?.LogWarning($"[frame] {tag} threw (swallowed): {e.Message}");
            }
        }
    }
}
