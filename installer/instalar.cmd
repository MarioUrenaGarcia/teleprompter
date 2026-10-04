@echo off
rem Doble clic para compilar e instalar Teleprompter sin cambiar la politica de ejecucion del sistema.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0instalar.ps1" %*
if errorlevel 1 (
    echo.
    echo La instalacion no se completo.
)
echo.
pause
