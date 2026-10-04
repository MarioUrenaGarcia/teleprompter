#Requires -Version 5.1
<#
.SYNOPSIS
    Genera el paquete .zip listo para publicar como Release.

.DESCRIPTION
    Compila la aplicacion autocontenida y arma un .zip con la aplicacion ya compilada, el instalador,
    el desinstalador, el README y la licencia. Quien lo descarga no necesita el SDK de .NET.
    El resultado queda en la carpeta artifacts del repositorio.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$AppName = 'Teleprompter'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\Teleprompter\Teleprompter.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = $projectXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
if (-not $version) {
    throw "No se encontró la versión en '$project'."
}

$packageName = "$AppName-$version-win-x64"
$outputRoot = Join-Path $repoRoot 'artifacts'
$stagingDir = Join-Path $outputRoot "package\$packageName"
$zipPath = Join-Path $outputRoot "$packageName.zip"

if (Test-Path -LiteralPath $stagingDir) {
    Remove-Item -LiteralPath $stagingDir -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

Write-Host "==> Compilando $AppName $version" -ForegroundColor Cyan
# ContinuousIntegrationBuild sustituye las rutas locales de compilacion por rutas neutras dentro de los binarios.
& dotnet publish $project -c Release -r win-x64 --self-contained true -o (Join-Path $stagingDir 'app') `
    -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'La compilación falló. Revisa los mensajes anteriores.'
}

Write-Host '==> Armando el paquete' -ForegroundColor Cyan
foreach ($script in 'instalar.cmd', 'instalar.ps1', 'desinstalar.ps1') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $script) -Destination $stagingDir
}
foreach ($document in 'README.md', 'LICENSE') {
    Copy-Item -LiteralPath (Join-Path $repoRoot $document) -Destination $stagingDir
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stagingDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $true)

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
$sizeMb = (Get-Item -LiteralPath $zipPath).Length / 1MB
Write-Host ''
Write-Host ("Paquete: {0} ({1:N1} MB)" -f $zipPath, $sizeMb) -ForegroundColor Green
Write-Host "SHA256:  $hash"
