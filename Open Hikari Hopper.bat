@echo off
setlocal

cd /d "%~dp0"
set "PYTHONPATH=%~dp0src;%PYTHONPATH%"

set "PYTHONW=%LOCALAPPDATA%\Python\pythoncore-3.14-64\pythonw.exe"
if not exist "%PYTHONW%" set "PYTHONW="
if not defined PYTHONW for %%I in (pythonw.exe) do set "PYTHONW=%%~$PATH:I"

if not defined PYTHONW (
    echo Python could not be found. Install Python with PySide6 and FiveFury first.
    pause
    exit /b 1
)

start "Hikari Hopper" "%PYTHONW%" -m rpf_explorer
