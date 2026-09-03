@echo off
setlocal EnableExtensions

cd /d "%~dp0"
set "VENV_DIR=%~dp0.venv"
set "VENV_PYTHON=%VENV_DIR%\Scripts\python.exe"
set "VENV_PYTHONW=%VENV_DIR%\Scripts\pythonw.exe"
set "INSTALL_MARKER=%VENV_DIR%\.hikari-pyproject.toml"

if exist "%VENV_PYTHON%" goto ensure_install

call :find_python
if not defined PYTHON_EXE goto python_missing

echo Creating the local Python environment...
if "%PYTHON_EXE%"=="py" (
    py -3 -m venv "%VENV_DIR%"
) else (
    "%PYTHON_EXE%" -m venv "%VENV_DIR%"
)
if errorlevel 1 goto setup_failed

:ensure_install
if not exist "%INSTALL_MARKER%" goto install
fc /b "pyproject.toml" "%INSTALL_MARKER%" >nul 2>&1
if errorlevel 1 goto install
"%VENV_PYTHON%" -c "import rpf_explorer, PySide6, fivefury, texfury" >nul 2>&1
if not errorlevel 1 goto launch

:install
echo Installing HikariHopper and its dependencies...
"%VENV_PYTHON%" -m pip install --editable .
if errorlevel 1 goto setup_failed
copy /y "pyproject.toml" "%INSTALL_MARKER%" >nul

:launch
start "" "%VENV_PYTHONW%" -m rpf_explorer
exit /b 0

:find_python
py -3 -c "import sys; raise SystemExit(sys.version_info < (3, 11))" >nul 2>&1
if not errorlevel 1 (
    set "PYTHON_EXE=py"
    exit /b 0
)

for %%I in (python.exe python3.exe) do if not "%%~$PATH:I"=="" call :try_python "%%~$PATH:I"
if defined PYTHON_EXE exit /b 0

for /d %%D in (
    "%LOCALAPPDATA%\Programs\Python\Python*"
    "%LOCALAPPDATA%\Python\pythoncore-*"
    "%ProgramFiles%\Python*"
    "%ProgramFiles%\Python\Python*"
) do if exist "%%~fD\python.exe" call :try_python "%%~fD\python.exe"
exit /b 0

:try_python
if defined PYTHON_EXE exit /b 0
"%~1" -c "import sys; raise SystemExit(sys.version_info < (3, 11))" >nul 2>&1
if not errorlevel 1 set "PYTHON_EXE=%~1"
exit /b 0

:python_missing
echo Python 3.11 or newer could not be found.
echo Install Python from https://www.python.org/downloads/ and run this file again.
pause
exit /b 1

:setup_failed
echo.
echo HikariHopper setup failed. Check the error above and try again.
pause
exit /b 1
