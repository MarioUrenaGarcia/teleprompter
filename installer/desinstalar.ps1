#Requires -Version 5.1
<#
.SYNOPSIS
    Desinstala Teleprompter del usuario actual.

.PARAMETER Silencioso
    No pide confirmación y conserva los ajustes y el último guion.
#>
[CmdletBinding()]
param(
    [switch]$Silencioso
)

$ErrorActionPreference = 'Stop'
$AppName = 'Teleprompter'
$installDir = Join-Path $env:LOCALAPPDATA "Programs\$AppName"
$dataDir = Join-Path $env:LOCALAPPDATA $AppName
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"

$popup = New-Object -ComObject WScript.Shell
$yesNoQuestion = 4 + 32
$yes = 6

if (-not $Silencioso) {
    if ($popup.Popup('¿Quieres desinstalar Teleprompter?', 0, $AppName, $yesNoQuestion) -ne $yes) {
        exit 0
    }
}

Get-Process -Name $AppName -ErrorAction SilentlyContinue | ForEach-Object {
    $null = $_.CloseMainWindow()
    if (-not $_.WaitForExit(5000)) {
        $_.Kill()
        $_.WaitForExit()
    }
}

$shortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) "$AppName.lnk"),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) "$AppName.lnk")
)
foreach ($shortcut in $shortcuts) {
    if (Test-Path -LiteralPath $shortcut) {
        Remove-Item -LiteralPath $shortcut -Force
    }
}

if (Test-Path $uninstallKey) {
    Remove-Item -Path $uninstallKey -Recurse -Force
}

# El script se ejecuta desde la carpeta que va a borrar; se sale de ella antes de eliminarla.
Set-Location -LiteralPath $env:TEMP
if (Test-Path -LiteralPath $installDir) {
    Remove-Item -LiteralPath $installDir -Recurse -Force
}

if (-not $Silencioso -and (Test-Path -LiteralPath $dataDir)) {
    $answer = $popup.Popup('¿Borrar también tus ajustes y el último guion guardado?', 0, $AppName, $yesNoQuestion)
    if ($answer -eq $yes) {
        Remove-Item -LiteralPath $dataDir -Recurse -Force
    }
}

if (-not $Silencioso) {
    $null = $popup.Popup('Teleprompter se desinstaló correctamente.', 0, $AppName, 64)
}
