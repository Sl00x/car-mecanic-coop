# CarMecaSim — Co-op Multiplayer for Car Mechanic Simulator 2021

A reverse-engineering + modding project that adds **shared-garage co-op multiplayer** to
Car Mechanic Simulator 2021 (Unity 2020.3.49f1, IL2CPP), plus a **launcher** to host/join games.

> You must own Car Mechanic Simulator 2021 on Steam. This project modifies your local
> install via a mod loader (MelonLoader). All changes are reversible.

## Target game facts (from reverse engineering)

| | |
|---|---|
| Engine | Unity 2020.3.49f1, **IL2CPP** |
| Metadata | `global-metadata.dat` v27 |
| Game logic | `Assembly-CSharp.dll` + `Assembly-CSharp-firstpass.dll` |
| Networking available | **Steamworks.NET** (bundled) → Steam P2P / relay transport |
| Mod loader | **MelonLoader** (IL2CPP, Il2CppInterop) |
| Anti-cheat | none |

## Layout

```
tools/      Il2CppDumper, MelonLoader (downloaded tooling)
re/         Reverse-engineering output + subsystem analysis docs
  output/     Il2CppDumper raw dump (dump.cs, DummyDll/, il2cpp.h, script.json)
  *.md        per-subsystem analysis (game-state, car-parts, player, steam-net...)
libs/       Il2CppInterop assemblies to compile the mod against
mod/        MelonLoader multiplayer mod (C#)
launcher/   Host/Join launcher app (C#)
docs/       Architecture, protocol spec, build/run instructions
```

## Multiplayer model

**Co-op shared garage**: 2+ players in the same garage work on the same car(s) together.
Host-authoritative: the host owns the ground-truth game state; clients send intents and
render replicated state. Transport = Steam P2P (SteamNetworkingSockets) via a Steam lobby.

## Status

See the task list / `docs/`. Work in progress.
