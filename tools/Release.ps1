<#
  Publishes a new version of the co-op plugin as a GitHub Release.
  The in-launcher "METTRE À JOUR" button then downloads it automatically.

  Usage:
    tools\Release.ps1 -Repo "<owner>/<name>" -Token "<github_token>" -Version "0.1.1" [-CreateRepo]

  The token needs "Contents: write" (fine-grained, scoped to this repo) or classic "repo" scope.
  -CreateRepo creates the repo under your account first (needs "Administration: write" / repo scope).
  The token stays on your machine; it is never written to disk.
#>
param(
  [Parameter(Mandatory)] [string]$Repo,
  [Parameter(Mandatory)] [string]$Token,
  [Parameter(Mandatory)] [string]$Version,
  [switch]$CreateRepo,
  [string]$Root = "C:\Users\alexu\Documents\Projects\CarMecaSim"
)
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$H = @{ Authorization = "Bearer $Token"; "User-Agent" = "CarMecaSim-Release"; Accept = "application/vnd.github+json" }
$owner, $name = $Repo.Split('/')

if ($CreateRepo) {
  Write-Host "Creating repo $Repo..."
  try { Invoke-RestMethod -Method Post -Uri "https://api.github.com/user/repos" -Headers $H -Body (@{ name = $name; private = $false; description = "Car Mechanic Simulator 2021 co-op mod" } | ConvertTo-Json) | Out-Null }
  catch { Write-Host "  (repo may already exist: $($_.Exception.Message))" }
}

# 1. Stamp the version into the mod and build the plugins
$csproj = Join-Path $Root "mod\CarMecaSim.Mp.csproj"
(Get-Content $csproj -Raw) -replace '<Version>.*?</Version>', "<Version>$Version</Version>" | Set-Content $csproj -Encoding utf8
Write-Host "Building plugin v$Version..."
dotnet build $csproj -c Release -v quiet -nologo | Out-Null
$bin = Join-Path $Root "mod\bin\Release\net6.0"
$dlls = @("CarMecaSim.Mp.dll", "CarMecaSim.Shared.dll", "Steamworks.NET.dll") | ForEach-Object { Join-Path $bin $_ }

# 2. Replace any existing release/tag with this version, then create a fresh one
$tag = "v$Version"
try {
  $existing = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/tags/$tag" -Headers $H
  if ($existing.id) { Invoke-RestMethod -Method Delete -Uri "https://api.github.com/repos/$Repo/releases/$($existing.id)" -Headers $H | Out-Null }
  Invoke-RestMethod -Method Delete -Uri "https://api.github.com/repos/$Repo/git/refs/tags/$tag" -Headers $H -ErrorAction SilentlyContinue | Out-Null
} catch {}

Write-Host "Creating release $tag..."
Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$Repo/releases" -Headers $H -Body (@{
  tag_name = $tag; name = "CarMecaSim Co-op $tag"; body = "Plugin v$Version - clique METTRE A JOUR dans le launcher."; draft = $false; prerelease = $false
} | ConvertTo-Json) | Out-Null

# 3. Re-fetch the newest release to get a reliable id, then upload the plugin DLLs as assets.
Start-Sleep -Milliseconds 800
$rel = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers $H
$relId = [string]$rel.id
if (-not $relId) { throw "release id introuvable apres creation" }
$uploadBase = "https://uploads.github.com/repos/$Repo/releases/" + $relId + "/assets"
Write-Host "  (upload -> $uploadBase)"
foreach ($dll in $dlls) {
  $fn = Split-Path $dll -Leaf
  Write-Host "  uploading $fn..."
  Invoke-RestMethod -Method Post -Uri ($uploadBase + "?name=$fn") -Headers ($H + @{ "Content-Type" = "application/octet-stream" }) -InFile $dll | Out-Null
}

Write-Host ""
Write-Host "DONE -> $($rel.html_url)"
Write-Host "Joueurs: cliquez METTRE A JOUR dans le launcher."
