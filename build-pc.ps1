# Compila la consola PC (WPF).
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "Limpiando bin/obj del proyecto PC..."
Remove-Item -Recurse -Force "src\GestionAlmacenVehiculos.PC\bin","src\GestionAlmacenVehiculos.PC\obj" -ErrorAction SilentlyContinue

Write-Host "Build PC Release -> dist\pc ..."
Remove-Item -Recurse -Force dist\pc -ErrorAction SilentlyContinue
dotnet build "src\GestionAlmacenVehiculos.PC\GestionAlmacenVehiculos.PC.csproj" -c Release -o dist\pc --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "OK. EXE:"
Write-Host "  dist\pc\GestionAlmacenVehiculos.PC.exe"
Write-Host "Login: usuarios de Logi Compass (API :5088)"
