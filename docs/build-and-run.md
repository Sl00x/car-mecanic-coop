# Build & Run

## Prerequisites
- Car Mechanic Simulator 2021 on Steam (AppId 1190000), owned.
- .NET SDK 10 (builds the net6 plugin + net8 launcher). Steam running and logged in.
- Windows x64.

## Mod loader
This project uses **BepInEx 6 (IL2CPP), bleeding-edge build**. MelonLoader (0.6.6 and 0.7.3) was
tried first but its engine support module aborts on this title (`SceneHandler.Init: Sequence
contains no elements`). The deeper cause is Il2CppInterop class-injection setup on this specific
`GameAssembly.dll`; a recent BepInEx BE build ships a newer Il2CppInterop that handles it, and
BepInEx bundles its own .NET 6 runtime (so the machine's newer .NET doesn't interfere).

BepInEx generates its Il2CppInterop assemblies into `BepInEx/interop/` on first launch — the API
the plugin compiles against (referenced directly from the game folder via the `GameDir` MSBuild
property; see `Directory.Build.props`).

## Build everything
```powershell
dotnet build CarMecaSim.slnx -c Release
```
Outputs:
- `mod/bin/Release/net6.0/CarMecaSim.Mp.dll` (+ `Steamworks.NET.dll`, `CarMecaSim.Shared.dll`)
- `launcher/bin/Release/net8.0-windows/CarMecaSim.Launcher.exe`

Game installed elsewhere? Pass the path:
```powershell
dotnet build CarMecaSim.slnx -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Car Mechanic Simulator 2021"
```

## Install
Via the launcher GUI: run `CarMecaSim.Launcher.exe` → **Setup / Repair**.
Via CLI:
```powershell
CarMecaSim.Launcher.exe --setup     # installs BepInEx (if needed) + the co-op plugin
CarMecaSim.Launcher.exe --status    # shows install state (READY when good)
```
Setup places BepInEx in the game root and the plugin in `BepInEx/plugins/CarMecaSim/`, and writes
`steam_appid.txt` (1190000). The **first** game launch generates `BepInEx/interop/` (a few minutes).

## Play
- **Host**: launcher GUI → *Host game* → Launch, or `--host --name "Alexis" --max 4 --type friends`.
  In-game press **F6** for the overlay; it shows the Lobby ID. Invite via the Steam overlay
  (Shift+Tab → Invite) or share the Lobby ID.
- **Join**: `--join <lobbyId>`, accept a Steam invite, or paste the Lobby ID in the overlay → *Join*.
- The plugin auto-hosts/joins (per the launcher config at `BepInEx/config/CarMecaSim/mp_config.json`)
  the first time you reach the **garage**.

## Uninstall (reversible)
```powershell
CarMecaSim.Launcher.exe --uninstall        # removes the co-op plugin only
CarMecaSim.Launcher.exe --uninstall --all  # also removes BepInEx (full vanilla restore)
```

## In-game controls
- **F6** — toggle the co-op overlay (status, host/join/leave, player list, chat).
- **Enter** (chat field focused) — send a chat message.

## Troubleshooting
- Nothing loads: open `BepInEx/LogOutput.log`. Look for `Chainloader started` and
  `CarMecaSim co-op loading...`. `--status` should say READY.
- "Steam not ready": start Steam first; the plugin waits for the game's own Steam init.
- After a game update: delete `BepInEx/interop/`, relaunch once to regenerate, then rebuild.

## Tooling (tools/)
- `InteropDump/` — regenerates `re/interop-*.txt` (exact Il2CppInterop member signatures).
- `Install-MelonLoader.ps1` / `Generate-Interop.ps1` — legacy MelonLoader path (kept for reference).
- `Test-BepInEx.ps1` — launches, confirms BepInEx generates interop + chainloads.
- `Test-Plugin.ps1` — smoke test: confirms the plugin loads, callbacks run, Steam inits; closes.
