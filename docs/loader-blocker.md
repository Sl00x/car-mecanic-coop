# Runtime blocker: Il2CppInterop class-injection fails on CMS2021

## ✅ RESOLVED
Fixed by patching Il2CppInterop's finder. `GetTypeInfoFromTypeDefinitionIndex` lives at
**RVA 0x2AE3A0** in this game's `GameAssembly.dll` (found by following the call chain from the
`il2cpp_image_get_class` export; see `tools/find_func.py`). The launcher's `InteropPatcher`
verifies the function prologue at that RVA, then rewrites `MetadataCache_GetTypeInfoFromType-
DefinitionIndex_Hook.FindTargetMethod` (Mono.Cecil) in `BepInEx/core/Il2CppInterop.Runtime.dll`
to return `Il2CppModule.BaseAddress + 0x2AE3A0`. Applied automatically by `--setup` (keeps a
`.bak`). Verified in-game: plugin loads, `MpBehaviour` injects, Steam P2P initializes.
If the game updates and the RVA changes, `--setup` detects the prologue mismatch and warns;
re-run `tools/find_func.py` and update `InteropPatcher.DefaultRva`.

The original diagnosis is kept below for reference.

## Summary
The co-op **plugin and launcher are complete and compile**, and the reverse engineering is done.
But the **mod loader cannot initialize on this specific game build**, so plugin callbacks never
run. This is an unresolved upstream incompatibility, independent of our code.

## Exact failure
`Il2CppInterop.Runtime.Injection.InjectorHelpers.Setup()` cannot locate the il2cpp runtime
function `MetadataCache::GetTypeInfoFromTypeDefinitionIndex` inside `GameAssembly.dll` via its
signature/xref scan:

```
System.InvalidOperationException: Sequence contains no elements
  at ...MetadataCache_GetTypeInfoFromTypeDefinitionIndex_Hook.FindGetTypeInfoFromTypeDefinitionIndex
  at ...InjectorHelpers.Setup()
  at ...ClassInjector.RegisterTypeInIl2Cpp(...)
  at ...DelegateSupport.ConvertDelegate(...)            ← BepInEx SetupUnityLogging / any delegate
```

That setup underpins **class injection, delegate conversion, and native→managed Harmony
trampolines** — so it blocks essentially all managed-interop modding, not just one feature.
It is tripped during the loader's own startup, before any plugin loads.

## What was tried (all fail the same way)
| Loader | Il2CppInterop | Result |
|---|---|---|
| MelonLoader 0.7.3 | 1.5.1 | `No Support Module Loaded` (SceneHandler.Init: Sequence contains no elements) |
| MelonLoader 0.6.6 | 1.5.1 | same |
| BepInEx 6.0.0-pre.2 / be.697 | 1.5.1 | `Unable to execute IL2CPP chainloader` (same root: InjectorHelpers.Setup) |
| BepInEx 6.0.0 be.793 (latest BE) | 1.5.3 | same |

Il2CppInterop **1.5.3 is the latest release** (June 2024) and already contains the "IL2CPP Master"
finder fixes (added in 1.4.2). CMS2021's `GameAssembly.dll` still defeats the scan.

## Upstream
Open, unresolved: BepInEx issue #820 ("Unable to execute IL2CPP chainloader", same stack),
affecting multiple Unity 2020.3.x IL2CPP titles. No documented fix as of now.

## Why this game is hard
CMS2021 appears to be an IL2CPP **Master** (heavily optimized/inlined) build. The scanner that
finds the metadata function relies on recognizable code/xref patterns that Master builds erase.

## Options to actually run the mod here
1. **Patch Il2CppInterop to hard-provide the function address** (highest effort, best chance):
   reverse `GetTypeInfoFromTypeDefinitionIndex` in `GameAssembly.dll` (we have the tooling — REA /
   Il2CppDumper `script.json` with RVAs), fork Il2CppInterop so `InjectorHelpers` uses that address
   instead of scanning, rebuild `Il2CppInterop.Runtime.dll`, and ship it inside BepInEx. This makes
   injection work and the existing plugin runs unchanged.
2. **Wait for / track upstream** (BepInEx #820) and retry when a newer Il2CppInterop lands.
3. **Injection-free native approach**: a custom C/C++ (or Frida-script) layer that hooks the game
   natively and bridges to our Steam netcode — large rewrite, abandons the managed plugin.

## State of the install
BepInEx be.793 + the co-op plugin are currently installed in the game folder (reversible via
`CarMecaSim.Launcher.exe --uninstall --all`). The game still launches; the plugin just doesn't
activate because the loader aborts at the injection step above.
