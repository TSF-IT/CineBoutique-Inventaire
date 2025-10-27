param(
  [switch]$ScanDocker   # use: .\scripts\audit.ps1 -ScanDocker
)
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path .\logs | Out-Null
$ts = Get-Date -Format "yyyyMMdd_HHmmss"
$md = ".\logs\audit_$ts.md"

function Save-Section($title, $contentPath) {
  Add-Content $md "`n## $title`n"
  if (Test-Path $contentPath) {
    Add-Content $md "Fichier : `$($contentPath)`"
  } else {
    Add-Content $md "_Aucun résultat_"
  }
}

# .NET
$sl = Get-ChildItem -Path . -Recurse -Filter *.sln | Select-Object -First 1
if (-not $sl) { Write-Host "No .sln found, skipping .NET audit"; }
else {
  "### .NET ($($sl.Name))" | Out-File $md
  dotnet restore $sl.FullName
  dotnet list $sl.FullName package --outdated --include-transitive --format json > ".\logs\dotnet_outdated_$ts.json" 2>&1
  dotnet list $sl.FullName package --vulnerable --include-transitive --format json > ".\logs\dotnet_vuln_$ts.json" 2>&1
  Save-Section "NuGet outdated (json)" ".\logs\dotnet_outdated_$ts.json"
  Save-Section "NuGet vulnerabilities (json)" ".\logs\dotnet_vuln_$ts.json"
}

# npm (web/)
if (Test-Path ".\src\inventory-web\package.json") {
  Push-Location .\src\inventory-web
  npm ci --no-audit --no-fund | Out-Null
  npm outdated > "..\..\logs\npm_outdated_$ts.txt" 2>&1
  npm audit --json > "..\..\logs\npm_audit_$ts.json" 2>&1
  Pop-Location
  Save-Section "npm outdated (txt)" ".\logs\npm_outdated_$ts.txt"
  Save-Section "npm audit (json)" ".\logs\npm_audit_$ts.json"
}

# Docker (optionnel, nécessite trivy dans le PATH)
if ($ScanDocker) {
  $trivy = Get-Command trivy -ErrorAction SilentlyContinue
  if ($trivy) {
    trivy fs --scanners vuln,secret --severity HIGH,CRITICAL --format json --output ".\logs\trivy_fs_$ts.json" .
    Save-Section "Trivy FS (json)" ".\logs\trivy_fs_$ts.json"
  } else {
    Add-Content $md "`n_Trivy non trouvé dans le PATH — section Docker ignorée_`n"
  }
}

Add-Content $md "`n### Rappels utiles`n- .NET upgrades sûrs : mettre à jour en **patch/minor** d’abord, lancer tests, puis major si nécessaire.`n- npm : privilégier `npx npm-check-updates -u -t minor` puis `npm ci`."
Write-Host "Audit terminé → $md"
