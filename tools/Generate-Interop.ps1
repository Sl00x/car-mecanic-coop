<#
  Launches CMS2021 once so MelonLoader generates the Il2CppInterop assemblies,
  waits until MelonLoader\Il2CppAssemblies is populated and stable, then closes the game.
  Prints a final status line. Intended to be run in the background.
#>
param(
  [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Car Mechanic Simulator 2021",
  [int]$TimeoutMin = 15
)
$exe = Join-Path $GameDir "Car Mechanic Simulator 2021.exe"
$asm = Join-Path $GameDir "MelonLoader\Il2CppAssemblies"
$probe = Join-Path $asm "Assembly-CSharp.dll"

Write-Output "Launching game for interop generation..."
$proc = Start-Process -FilePath $exe -WorkingDirectory $GameDir -PassThru
Write-Output ("Game PID: {0}" -f $proc.Id)

$deadline = (Get-Date).AddMinutes($TimeoutMin)
$lastCount = -1; $stable = 0; $done = $false
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Seconds 10
  if (Test-Path $probe) {
    $cnt = @(Get-ChildItem $asm -Filter *.dll -ErrorAction SilentlyContinue).Count
    Write-Output ("  Il2CppAssemblies dll count: {0}" -f $cnt)
    if ($cnt -eq $lastCount -and $cnt -gt 20) { $stable++ } else { $stable = 0; $lastCount = $cnt }
    if ($stable -ge 2) { $done = $true; break }
  } else {
    # still generating or downloading deps
    $dep = Join-Path $GameDir "MelonLoader\Dependencies\Il2CppAssemblyGenerator"
    if (Test-Path $dep) { Write-Output "  (generator deps present, generating...)" } else { Write-Output "  (waiting for generator...)" }
  }
}

# Close the game — we only needed the generated assemblies.
try {
  $g = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
  if ($g) { $g.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 3 }
  $g = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
  if ($g) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
} catch {}
# Also catch any lingering game process by name
Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "Car Mechanic*" } | Stop-Process -Force -ErrorAction SilentlyContinue

if ($done) {
  Write-Output ("RESULT: SUCCESS - {0} interop assemblies generated." -f $lastCount)
} elseif (Test-Path $probe) {
  Write-Output ("RESULT: PARTIAL - Assembly-CSharp.dll exists, count={0} (did not confirm stable)." -f $lastCount)
} else {
  Write-Output "RESULT: FAILED - Il2CppAssemblies not generated within timeout. Check MelonLoader log."
}
Write-Output ("AssembliesDir: {0}" -f $asm)