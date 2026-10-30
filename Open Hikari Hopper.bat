@echo off
setlocal EnableExtensions

cd /d "%~dp0"
set "APP_DIR=%~dp0src\HikariHopper\bin\Release\net10.0-windows"
set "APP_EXE=%APP_DIR%\HikariHopper.exe"

where dotnet >nul 2>&1
if errorlevel 1 goto dotnet_missing

rem Incremental: a no-op when nothing changed since the last build.
echo Building HikariHopper...
dotnet build "src\HikariHopper\HikariHopper.csproj" -c Release -v quiet -nologo
if errorlevel 1 goto build_failed

start "" "%APP_EXE%"
exit /b 0

:dotnet_missing
echo The .NET 10 SDK could not be found.
echo Install it from https://dotnet.microsoft.com/download and run this file again.
pause
exit /b 1

:build_failed
echo.
echo HikariHopper failed to build. Check the error above and try again.
pause
exit /b 1
