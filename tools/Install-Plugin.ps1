<#
  Builds the co-op mod (Release) and deploys EVERY plugin DLL to the live game.

  WHY THIS SCRIPT EXISTS: the mod is split across two managed assemblies -
    CarMecaSim.Mp.dll      (the plugin)
    CarMecaSim.Shared.dll  (canonical Coop model: VehiclePartState, InteractionLockManager, ...)
  The IL2CPP runtime loads Shared.dll at runtime. If you copy only Mp.dll after adding/changing a
  type in Shared, the deployed Shared.dll goes stale and the game throws TypeLoadException on load
  (the "stuck at 80%" hang). ALWAYS deploy via this script so both DLLs move together.

  Usage:
    pwsh tools/Install-Plugin.ps1                 # Release build + deploy
    pwsh tools/Install-Plugin.ps1 -Config Debug
#>
param(
  [string]$Root    = "C:\Users\alexu\Documents\Projects\CarMecaSim",
  [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Car Mechanic Simulator 2021",
  [string]$Config  = "Release"
)
$ErrorActionPreference = "Stop"
Set-Location $Root

$plugins = Join-Path $GameDir "BepInEx\plugins\CarMecaSim"
$bin     = Join-Path $Root "mod\bin\$Config\net6.0"
# Every file the plugin needs at runtime. Shared.dll is NOT optional.
$dlls    = @("CarMecaSim.Mp.dll", "CarMecaSim.Shared.dll", "Steamworks.NET.dll")

Write-Host "Building mod ($Config)..." -ForegroundColor Cyan
dotnet build "$Root\mod\CarMecaSim.Mp.csproj" -c $Config -v quiet -nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# The game locks the DLLs while running. Wait for it to exit rather than failing the copy.
$proc = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'CarMechanicSimulator|mechanic' }
if ($proc) {
  Write-Host "Game is running (pid $($proc.Id -join ',')). Close it to finish the install..." -ForegroundColor Yellow
  while (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }
  Write-Host "Game closed." -ForegroundColor Green
}

New-Item -ItemType Directory -Force -Path $plugins | Out-Null
foreach ($d in $dlls) {
  $src = Join-Path $bin $d
  if (-not (Test-Path $src)) { throw "Missing build output: $src" }
  Copy-Item -Force $src (Join-Path $plugins $d)
  $len = (Get-Item (Join-Path $plugins $d)).Length
  Write-Host ("  deployed {0,-26} {1,7} bytes" -f $d, $len) -ForegroundColor Green
}

# Optional runtime assets (avatar bundle). Deployed next to the DLL so BundleAvatar can load it.
$asset = Join-Path $Root "mod\assets\player.assets"
if (Test-Path $asset) {
  Copy-Item -Force $asset (Join-Path $plugins "player.assets")
  Write-Host ("  deployed {0,-26} {1,7} bytes" -f "player.assets", (Get-Item $asset).Length) -ForegroundColor Green
}

# Sanity: the canonical types the plugin references must be present in the deployed Shared.dll.
$shared = Join-Path $plugins "CarMecaSim.Shared.dll"
$bytes  = [System.IO.File]::ReadAllBytes($shared)
$text   = [System.Text.Encoding]::ASCII.GetString($bytes)
foreach ($t in @("VehiclePartState", "InteractionLockManager")) {
  if ($text.Contains($t)) { Write-Host "  verify  $t present in Shared.dll" -ForegroundColor Green }
  else { throw "Deployed Shared.dll is missing type '$t' - plugin would TypeLoadException on load." }
}

Write-Host "Install OK -> $plugins" -ForegroundColor Cyan
