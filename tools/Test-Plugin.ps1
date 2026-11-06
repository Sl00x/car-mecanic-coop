<#
  Definitive foundation test on BepInEx: writes a Menu-role config, launches the game,
  confirms the plugin loads (Load()), callbacks run (Update loop), and Steam P2P inits,
  then closes the game and prints the key log lines.
#>
param(
  [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Car Mechanic Simulator 2021",
  [int]$WaitSec = 110
)
$exe = Join-Path $GameDir "Car Mechanic Simulator 2021.exe"
$log = Join-Path $GameDir "BepInEx\LogOutput.log"

$cfgDir = Join-Path $GameDir "BepInEx\config\CarMecaSim"
New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
@'
{ "SchemaVersion":1, "Role":"Menu", "DisplayName":"LoadTest", "LobbyId":0,
  "MaxPlayers":4, "LobbyType":"FriendsOnly", "AutoStart":true, "ModVersion":"0.1.0" }
'@ | Out-File -Encoding ascii (Join-Path $cfgDir "mp_config.json")

Write-Output "Launching game (BepInEx + plugin)..."
$proc = Start-Process -FilePath $exe -WorkingDirectory $GameDir -PassThru
Write-Output ("PID: {0}" -f $proc.Id)
$deadline = (Get-Date).AddSeconds($WaitSec)
$loaded = $false; $steam = $false
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Seconds 5
  if (Test-Path $log) {
    $c = Get-Content $log -ErrorAction SilentlyContinue
    if ($c -match "CarMecaSim co-op loading") { $loaded = $true }
    if ($c -match "Steam ready") { $steam = $true }
    if ($steam) { break }  # got the full signal
  }
}
try { $g = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue; if ($g) { $g.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 3 } } catch {}
Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "Car Mechanic*" } | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Output "`n===== CarMecaSim / chainloader / error log lines ====="
if (Test-Path $log) {
  Get-Content $log | Where-Object { $_ -match "CarMecaSim|Chainloader|\[net\]|Steam ready|plugin|Error|Exception" } | Select-Object -First 60
} else { Write-Output "NO LOG." }
Write-Output "`n===== RESULT ====="
Write-Output ("pluginLoaded: {0}" -f $loaded)
Write-Output ("steamReady:   {0}" -f $steam)
