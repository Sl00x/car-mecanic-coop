<#
  Builds a ready-to-share distributable of the co-op mod + launcher.
  Produces dist/CarMecaSim-Coop-v<ver>.zip that a friend can unzip and run.
  The launcher is published self-contained (no .NET install needed on their PC).
#>
param([string]$Root = "C:\Users\alexu\Documents\Projects\CarMecaSim")
$ErrorActionPreference = "Stop"
Set-Location $Root

$ver  = "0.6.1"
$dist = Join-Path $Root "dist"
$app  = Join-Path $dist "CarMecaSim-Coop"
if (Test-Path $app) { Remove-Item -Recurse -Force $app }
New-Item -ItemType Directory -Force -Path $app | Out-Null

Write-Host "Building mod (Release)..."
dotnet build "$Root\mod\CarMecaSim.Mp.csproj" -c Release -v quiet -nologo | Out-Null

Write-Host "Publishing launcher (self-contained, single-file)..."
dotnet publish "$Root\launcher\CarMecaSim.Launcher.csproj" -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $app -v quiet -nologo | Out-Null
Get-ChildItem $app -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "Assembling payload..."
$payload = Join-Path $app "payload"
New-Item -ItemType Directory -Force -Path (Join-Path $payload "BepInEx_x64") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $payload "plugins") | Out-Null
Copy-Item -Recurse -Force (Join-Path $Root "tools\BepInEx_x64\*") (Join-Path $payload "BepInEx_x64")
foreach ($d in @("CarMecaSim.Mp.dll", "CarMecaSim.Shared.dll", "Steamworks.NET.dll")) {
  Copy-Item -Force (Join-Path $Root "mod\bin\Release\net6.0\$d") (Join-Path $payload "plugins")
}
# Avatar bundle (animated mechanic) so a guest sees a real body instead of primitives.
$avatar = Join-Path $Root "mod\assets\player.assets"
if (Test-Path $avatar) { Copy-Item -Force $avatar (Join-Path $payload "plugins\player.assets") }

# Friend-facing readme
@"
CarMecaSim — Co-op pour Car Mechanic Simulator 2021
===================================================

PREREQUIS
 - Posseder Car Mechanic Simulator 2021 sur Steam (installe).
 - Steam lance et connecte.

INSTALLATION (une seule fois)
 1. Dezippe ce dossier ou tu veux.
 2. Lance  CarMecaSim.Launcher.exe
 3. Clique  RÉPARER / INSTALLER  puis attends  « SYSTÈME PRÊT ».
 4. Clique  JOUER.  (Le 1er lancement genere des fichiers, 2-5 min, c'est normal.)

METTRE A JOUR (quand une nouvelle version sort)
 - Lance le launcher -> clique  METTRE A JOUR.
   (Telecharge et remplace le plugin automatiquement depuis GitHub. Pas besoin de re-telecharger ce zip.)

JOUER EN CO-OP
 - Charge / continue une partie, puis appuie sur  F6  (menu co-op).
   * Hote    : « HÉBERGER UNE PARTIE » -> partage le code Lobby affiche,
               ou invite via l'overlay Steam (Maj+Tab -> Inviter).
   * Invite  : colle le code Lobby -> « OK », ou accepte l'invitation Steam.

DESINSTALLER
 - Relance le launcher -> « DÉSINSTALLER » (retour au jeu normal, reversible).

NOTES
 - Les DEUX joueurs doivent installer ce mod et posseder le jeu (meme version Steam).
 - Windows SmartScreen peut avertir (exe non signe) :
   « Informations complementaires » -> « Executer quand meme ».
 - v$ver
"@ | Out-File -Encoding utf8 (Join-Path $app "LISEZ-MOI.txt")

Write-Host "Zipping..."
$zip = Join-Path $dist "CarMecaSim-Coop-v$ver.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$app\*" -DestinationPath $zip
$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ("DONE -> {0}  ({1} MB)" -f $zip, $mb)
