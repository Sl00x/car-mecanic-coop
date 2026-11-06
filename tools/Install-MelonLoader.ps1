<#
.SYNOPSIS
  Installs (or removes) MelonLoader into the Car Mechanic Simulator 2021 install.
  Reversible: -Uninstall removes every file MelonLoader adds.
.NOTES
  Manual-install method: copy `version.dll` + `MelonLoader/` from the staged payload
  into the game root. MelonLoader generates Il2CppInterop assemblies on first game launch.
#>
param(
  [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Car Mechanic Simulator 2021",
  [string]$Payload = "$PSScriptRoot\MelonLoader_x64",
  [switch]$Uninstall,
  [switch]$Force
)

$ErrorActionPreference = "Stop"
$exe = Join-Path $GameDir "Car Mechanic Simulator 2021.exe"
if (-not (Test-Path $exe)) { throw "Game not found at: $GameDir" }

# Files/folders MelonLoader manages (manual x64 install)
$managed = @("version.dll", "MelonLoader", "dobby.dll", "Il2CppAssemblyGenerator", "MelonLoader.NativeHost.dll")

if ($Uninstall) {
  Write-Host "Removing MelonLoader from: $GameDir"
  foreach ($m in $managed + @("Latest.log", "Logs")) {
    $p = Join-Path $GameDir $m
    if (Test-Path $p) { Remove-Item -Recurse -Force $p; Write-Host "  removed $m" }
  }
  Write-Host "Done. Game restored to vanilla."
  return
}

# Install
if (-not (Test-Path $Payload)) { throw "MelonLoader payload not found: $Payload (run the download step first)" }
$already = Test-Path (Join-Path $GameDir "version.dll")
if ($already -and -not $Force) {
  Write-Host "MelonLoader already present (version.dll exists). Use -Force to overwrite."
} else {
  Write-Host "Installing MelonLoader into: $GameDir"
  Copy-Item -Path (Join-Path $Payload "*") -Destination $GameDir -Recurse -Force
  Write-Host "  copied payload."
}

# Ensure steam_appid.txt so the mod's Steamworks binds to CMS21's AppId without Steam relaunch.
# AppId filled in by launcher/mod; 1190000 is CMS21's Steam AppId.
$appIdFile = Join-Path $GameDir "steam_appid.txt"
if (-not (Test-Path $appIdFile)) { "1190000" | Out-File -Encoding ascii -NoNewline $appIdFile; Write-Host "  wrote steam_appid.txt (1190000)" }

Write-Host "Installed. First launch will generate MelonLoader\Il2CppAssemblies\ (interop API)."
