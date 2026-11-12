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
            }
            catch (Exception e) { Log.LogError("Harmony patch failed: " + e); }

            Log.LogInfo($"Ready. role={Cfg.Role} name={Cfg.DisplayName} lobby={Cfg.LobbyId}");
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

        private void Update()
        {
            var s = CarMecaPlugin.Session;
            if (s == null) return;

            // F6 opens/closes the full co-op panel — only during an active session.
            if (GameRefs.InGameSession && Input.GetKeyDown(KeyCode.F6))
            {
                var ov = CarMecaPlugin.Overlay;
                if (ov != null) { ov.Visible = !ov.Visible; SetFrozen(ov.Visible); }
            }
            // Safety: never stay frozen if we lose the session or the panel closed another way.
            if (_frozen && (!GameRefs.InGameSession || CarMecaPlugin.Overlay == null || !CarMecaPlugin.Overlay.Visible))
                SetFrozen(false);
            // Keep the cursor free each frame while frozen, so the game's FPS controller can't grab it back.
            if (_frozen)
            {
                try { Time.timeScale = 0f; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } catch { }
            }

            s.EnsureSteam();
            // Use UNSCALED time so networking/chat keep running even while the game is frozen (timeScale 0).
            s.Update(Time.unscaledDeltaTime);
            s.SendPresenceTick(Time.unscaledTime);
            UI.NativeCoopMenu.Tick(s);   // native "MULTIJOUEUR" button on the main menu

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

        /// <summary>Freeze/unfreeze the local game while the co-op panel is open (F6), freeing the cursor.</summary>
        private void SetFrozen(bool on)
        {
            if (on == _frozen) return;
            _frozen = on;
            try
            {
                if (on)
                {
                    _savedTime = Time.timeScale; _savedLock = Cursor.lockState; _savedCursor = Cursor.visible;
                    Time.timeScale = 0f;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else
                {
                    Time.timeScale = _savedTime <= 0f ? 1f : _savedTime;
                    Cursor.lockState = _savedLock;
                    Cursor.visible = _savedCursor;
                }
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("freeze toggle failed: " + e.Message); }
        }

        private void OnGUI()
        {
            CarMecaPlugin.Overlay?.Draw();        // in-game co-op panel (F6)
        }
    }
}
