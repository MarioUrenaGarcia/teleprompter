#Requires -Version 5.1
<#
.SYNOPSIS
    Compila e instala Teleprompter para el usuario actual.

.DESCRIPTION
    Publica la aplicacion autocontenida (no requiere instalar .NET aparte), la copia a
    %LOCALAPPDATA%\Programs\Teleprompter, crea los accesos directos en el menu Inicio y en el
    escritorio, y la registra en Configuracion > Aplicaciones para poder desinstalarla.
    No necesita permisos de administrador.

.PARAMETER SinEscritorio
    No crea el acceso directo en el escritorio.

.PARAMETER NoAbrir
    No abre la aplicacion al terminar.
#>
[CmdletBinding()]
param(
    [switch]$SinEscritorio,
    [switch]$NoAbrir
)

$ErrorActionPreference = 'Stop'
$AppName = 'Teleprompter'
$Description = 'Teleprompter flotante invisible al compartir pantalla'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\Teleprompter\Teleprompter.csproj'
$publishDir = Join-Path $repoRoot 'artifacts\publish'
$installDir = Join-Path $env:LOCALAPPDATA "Programs\$AppName"
$exe = Join-Path $installDir "$AppName.exe"

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

if (-not (Test-Path $project)) {
    throw "No se encontró el proyecto en '$project'. Ejecuta este script desde la carpeta installer del repositorio."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Se necesita el SDK de .NET 10 para compilar. Descárgalo desde https://dotnet.microsoft.com/download'
}

Write-Step 'Compilando la aplicación (puede tardar un par de minutos la primera vez)'
if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

& dotnet publish $project -c Release -r win-x64 --self-contained true -o $publishDir -p:DebugType=None -p:DebugSymbols=false --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'La compilación falló. Revisa los mensajes anteriores.'
}

Write-Step 'Cerrando Teleprompter si está abierto'
Get-Process -Name $AppName -ErrorAction SilentlyContinue | ForEach-Object {
    $null = $_.CloseMainWindow()
    if (-not $_.WaitForExit(5000)) {
        $_.Kill()
        $_.WaitForExit()
    }
}

Write-Step "Copiando archivos a $installDir"
if (Test-Path $installDir) {
    Remove-Item -LiteralPath $installDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $installDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'desinstalar.ps1') -Destination $installDir -Force

Write-Step 'Creando accesos directos'
$shell = New-Object -ComObject WScript.Shell
function New-AppShortcut([string]$Path) {
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $installDir
    $shortcut.IconLocation = "$exe,0"
    $shortcut.Description = $Description
    $shortcut.Save()
    Write-Host "    $Path"
}

# La carpeta Programas del menu Inicio es la que indexa la busqueda de Windows.
New-AppShortcut (Join-Path ([Environment]::GetFolderPath('Programs')) "$AppName.lnk")
if (-not $SinEscritorio) {
    # GetFolderPath respeta el escritorio redirigido a OneDrive.
    New-AppShortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) "$AppName.lnk")
}

Write-Step 'Registrando en Configuración > Aplicaciones'
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"
$uninstaller = Join-Path $installDir 'desinstalar.ps1'
$sizeKb = [int]((Get-ChildItem -LiteralPath $installDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1KB)
$version = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion

New-Item -Path $uninstallKey -Force | Out-Null
Set-ItemProperty -Path $uninstallKey -Name DisplayName -Value $AppName
Set-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value "$exe,0"
Set-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value $version
Set-ItemProperty -Path $uninstallKey -Name Publisher -Value $AppName
Set-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installDir
Set-ItemProperty -Path $uninstallKey -Name InstallDate -Value (Get-Date -Format 'yyyyMMdd')
Set-ItemProperty -Path $uninstallKey -Name UninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstaller`""
Set-ItemProperty -Path $uninstallKey -Name QuietUninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstaller`" -Silencioso"
New-ItemProperty -Path $uninstallKey -Name EstimatedSize -Value $sizeKb -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null

Write-Host ''
Write-Host "Teleprompter $version instalado correctamente." -ForegroundColor Green
Write-Host 'Búscalo como "Teleprompter" en el menú Inicio o usa el acceso directo del escritorio.'

if (-not $NoAbrir) {
    Start-Process -FilePath $exe
}
