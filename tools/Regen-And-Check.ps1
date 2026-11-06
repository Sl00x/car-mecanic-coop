<#
  Launches the game with the freshly-installed MelonLoader, waits for interop
  (re)generation, then keeps running long enough to see whether the engine
  SUPPORT MODULE loads (the thing that was failing on 0.7.3). Scrapes the log
  and closes the game.
#>
param(
  [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Car Mechanic Simulator 2021",
  [int]$TimeoutMin = 9
)
$exe = Join-Path $GameDir "Car Mechanic Simulator 2021.exe"
$asm = Join-Path $GameDir "MelonLoader\Il2CppAssemblies"
$probe = Join-Path $asm "Assembly-CSharp.dll"
$log = Join-Path $GameDir "MelonLoader\Latest.log"

Write-Output "Launching game (0.6.6)..."
$proc = Start-Process -FilePath $exe -WorkingDirectory $GameDir -PassThru
Write-Output ("PID: {0}" -f $proc.Id)

$deadline = (Get-Date).AddMinutes($TimeoutMin)
$genDone = $false; $last = -1; $stable = 0
# Phase 1: wait for interop generation to finish
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Seconds 10
  if (Test-Path $probe) {
    $cnt = @(Get-ChildItem $asm -Filter *.dll -ErrorAction SilentlyContinue).Count
    if ($cnt -eq $last -and $cnt -gt 20) { $stable++ } else { $stable = 0; $last = $cnt }
    if ($stable -ge 2) { $genDone = $true; Write-Output ("Interop generated: {0} dlls" -f $cnt); break }
  }
}
# Phase 2: give the game ~40s to boot so the support module + first scene load
if ($genDone) { Start-Sleep -Seconds 40 }

# close
try { $g = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue; if ($g) { $g.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 3 } } catch {}
Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "Car Mechanic*" } | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Output "`n===== Support-module / error lines ====="
if (Test-Path $log) {
  Get-Content $log | Where-Object { $_ -match "Support Module|No Support|\[ERROR\]|Engine Module|Scene|Mods are loaded|loaded\.|MelonLoader v" } | Select-Object -First 60
} else { Write-Output "NO LOG." }

$bad = (Test-Path $log) -and (Select-String -Path $log -Pattern "No Support Module Loaded" -Quiet)
Write-Output "`n===== RESULT ====="
Write-Output ("interopGenerated: {0}" -f $genDone)
Write-Output ("supportModuleFailed: {0}" -f [bool]$bad)