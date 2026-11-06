<#
  Launches the game with BepInEx, waits for interop generation + chainloader,
  scrapes BepInEx/LogOutput.log to confirm the loader actually runs plugins,
  then closes the game.
#>
param(
  [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Car Mechanic Simulator 2021",
  [int]$TimeoutMin = 9
)
$exe = Join-Path $GameDir "Car Mechanic Simulator 2021.exe"
$interop = Join-Path $GameDir "BepInEx\interop"
$probe = Join-Path $interop "Assembly-CSharp.dll"
$log = Join-Path $GameDir "BepInEx\LogOutput.log"

Write-Output "Launching game (BepInEx)..."
$proc = Start-Process -FilePath $exe -WorkingDirectory $GameDir -PassThru
Write-Output ("PID: {0}" -f $proc.Id)

$deadline = (Get-Date).AddMinutes($TimeoutMin)
$gen = $false; $last = -1; $stable = 0
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Seconds 10
  if (Test-Path $probe) {
    $cnt = @(Get-ChildItem $interop -Filter *.dll -ErrorAction SilentlyContinue).Count
    if ($cnt -eq $last -and $cnt -gt 20) { $stable++ } else { $stable = 0; $last = $cnt }
    if ($stable -ge 2) { $gen = $true; Write-Output ("Interop generated: {0} dlls" -f $cnt); break }
  } elseif (Test-Path $log) {
    if (Select-String -Path $log -Pattern "Chainloader started" -Quiet) { $gen = $true; break }
  }
}
if ($gen) { Start-Sleep -Seconds 35 }  # let chainloader + game boot

try { $g = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue; if ($g) { $g.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 3 } } catch {}
Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "Car Mechanic*" } | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Output "`n===== BepInEx log (key lines) ====="
if (Test-Path $log) {
  Get-Content $log | Where-Object { $_ -match "BepInEx|Chainloader|plugin|Il2CppInterop|Generat|Error|Exception|Detour|Runtime" } | Select-Object -First 70
} else { Write-Output "NO LOG at $log" }
Write-Output "`n===== RESULT ====="
$chain = (Test-Path $log) -and (Select-String -Path $log -Pattern "Chainloader started" -Quiet)
Write-Output ("interopGenerated: {0}" -f $gen)
Write-Output ("chainloaderStarted: {0}" -f [bool]$chain)