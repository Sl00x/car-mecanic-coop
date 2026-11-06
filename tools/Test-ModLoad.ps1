<#
  In-game load test: writes a Menu-role config, launches the game, waits for it to
  initialize, scrapes the MelonLoader log for our mod + Steam lines and any errors,
  then closes the game. Designed to run in the background.
#>
param(
  [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Car Mechanic Simulator 2021",
  [int]$WaitSec = 85
)
$exe = Join-Path $GameDir "Car Mechanic Simulator 2021.exe"
$log = Join-Path $GameDir "MelonLoader\Latest.log"

# Menu-role config: mod initializes + brings up Steam, but takes no lobby action.
$cfgDir = Join-Path $GameDir "UserData\CarMecaSim"
New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
@'
{
  "SchemaVersion": 1,
  "Role": "Menu",
  "DisplayName": "LoadTest",
  "LobbyId": 0,
  "MaxPlayers": 4,
  "LobbyType": "FriendsOnly",
  "AutoStart": true,
  "ModVersion": "0.1.0"
}
'@ | Out-File -Encoding ascii (Join-Path $cfgDir "mp_config.json")

if (Test-Path $log) { Remove-Item $log -Force -ErrorAction SilentlyContinue }

Write-Output "Launching game for load test..."
$proc = Start-Process -FilePath $exe -WorkingDirectory $GameDir -PassThru
Write-Output ("PID: {0}; waiting {1}s..." -f $proc.Id, $WaitSec)
$deadline = (Get-Date).AddSeconds($WaitSec)
$seen = $false
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Seconds 5
  if (Test-Path $log) {
    $c = Get-Content $log -ErrorAction SilentlyContinue
    if ($c -match "Steam ready" -or $c -match "CarMecaSim co-op initializing") { $seen = $true }
    if ($c -match "Steam ready") { break }  # got the key signal, stop early
  }
}

# Close the game
try {
  $g = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
  if ($g) { $g.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 3 }
} catch {}
Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "Car Mechanic*" } | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Output "`n===== Mod / Steam / error lines from MelonLoader log ====="
if (Test-Path $log) {
  Get-Content $log | Where-Object {
    $_ -match "CarMecaSim|\[net\]|Steam ready|MelonLoader\] Loading|Mods are loaded|Error|Exception|SteamAPI|\bMp\b"
  } | Select-Object -First 80
} else {
  Write-Output "NO LOG FOUND (MelonLoader may not have run)."
}
Write-Output "`n===== RESULT ====="
Write-Output ("mod init/steam seen: {0}" -f $seen)
