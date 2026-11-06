# Build & Run

## Prerequisites
- Car Mechanic Simulator 2021 on Steam (AppId 1190000), owned.
- .NET SDK 10 (builds net6/net8 targets). Steam running and logged in.

## One-time: generate the Il2CppInterop API (already done on this machine)
MelonLoader must run once so it generates `MelonLoader/Il2CppAssemblies/` — the API the mod
compiles against. `tools/Install-MelonLoader.ps1` installs MelonLoader; the first game launch
generates the assemblies (see `tools/Generate-Interop.ps1`). The mod csproj references them
directly from the game folder via the `GameDir` MSBuild property (see `Directory.Build.props`).

## Build everything
```powershell
dotnet build CarMecaSim.slnx -c Release
```
Outputs:
- `mod/bin/Release/net6.0/CarMecaSim.Mp.dll` (+ `Steamworks.NET.dll`, `CarMecaSim.Shared.dll`)
- `launcher/bin/Debug|Release/net8.0-windows/CarMecaSim.Launcher.exe`

If the game is installed elsewhere:
```powershell
dotnet build CarMecaSim.slnx -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Car Mechanic Simulator 2021"
```

## Install the mod
Via the launcher (GUI): run `CarMecaSim.Launcher.exe` → **Setup / Repair**.
Via CLI:
```powershell
CarMecaSim.Launcher.exe --setup     # installs MelonLoader (if needed) + the co-op mod
CarMecaSim.Launcher.exe --status    # shows install state
```

## Play
- **Host**: launcher GUI → *Host game* → Launch, or `--host --name "Alexis" --max 4 --type friends`.
  In-game, open the overlay (**F6**) to see the Lobby ID; invite friends via the Steam overlay
  (Shift+Tab → Invite) or share the Lobby ID.
- **Join**: `--join <lobbyId>`, or accept a Steam invite (the mod reads the overlay join request),
  or paste the Lobby ID into the overlay and click *Join*.
- The mod auto-hosts/joins (per the launcher config) the first time you reach the **garage**.

## Uninstall (reversible)
```powershell
CarMecaSim.Launcher.exe --uninstall        # removes the co-op mod only
CarMecaSim.Launcher.exe --uninstall --all  # also removes MelonLoader (full vanilla restore)
```

## In-game controls
- **F6** — toggle the co-op overlay (status, host/join/leave, player list, chat).
- **Enter** (with chat field focused) — send a chat message.

## Troubleshooting
- No overlay / mod not loading: check `<game>/MelonLoader/Latest.log`. Confirm `--status` is READY.
- "Steam not ready": start Steam first; the mod waits for the game's own Steam init.
- Interop assemblies missing: launch the game once (MelonLoader regenerates them), then rebuild.
- Reset the generated API after a game update: delete `MelonLoader/Il2CppAssemblies`, relaunch once.

## Project tests / tooling
- `tools/InteropDump` — regenerates `re/interop-signatures.txt` (exact interop member signatures).
- `tools/Generate-Interop.ps1` — launches the game once to (re)generate interop assemblies.
- `tools/Test-ModLoad.ps1` — smoke test: launches, confirms the mod + Steam initialize, closes.
