<#
  Verifies the HOST path: writes a Host-role config, launches the game, and confirms the
  plugin creates a Steam lobby (from the menu), then closes the game.
#>
param(
  [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Car Mechanic Simulator 2021",
  [int]$WaitSec = 140
)
$exe = Join-Path $GameDir "Car Mechanic Simulator 2021.exe"
$log = Join-Path $GameDir "BepInEx\LogOutput.log"
$cfgDir = Join-Path $GameDir "BepInEx\config\CarMecaSim"
New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
@'
{ "SchemaVersion":1, "Role":"Host", "DisplayName":"HostTest", "LobbyId":0,
  "MaxPlayers":4, "LobbyType":"FriendsOnly", "AutoStart":true, "ModVersion":"0.1.0" }
'@ | Out-File -Encoding ascii (Join-Path $cfgDir "mp_config.json")

Write-Output "Launching game (Host role)..."
$proc = Start-Process -FilePath $exe -WorkingDirectory $GameDir -PassThru
Write-Output ("PID: {0}" -f $proc.Id)
$deadline = (Get-Date).AddSeconds($WaitSec)
$lobby = $false
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Seconds 5
  if (Test-Path $log) {
    $c = Get-Content $log -ErrorAction SilentlyContinue
    if ($c -match "Lobby created" -or $c -match "Lobby entered") { $lobby = $true; break }
  }
}
try { $g = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue; if ($g) { $g.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 3 } } catch {}
Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "Car Mechanic*" } | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Output "`n===== CarMecaSim net log lines ====="
if (Test-Path $log) {
  Get-Content $log | Where-Object { $_ -match "CarMecaSim|\[net\]|Lobby|Steam ready|Error|Exception" } | Select-Object -First 40
}
Write-Output "`n===== RESULT ====="
Write-Output ("lobbyCreated: {0}" -f $lobby)
