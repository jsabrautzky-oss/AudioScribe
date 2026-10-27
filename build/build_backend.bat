@echo off
setlocal
chcp 65001 >nul

set "ROOT=%~dp0.."
cd /d "%ROOT%"

set "VENV_SCRIPTS=%ROOT%\src\AudioScribe.Backend\.venv-build\Scripts"
set "PYI=%VENV_SCRIPTS%\pyinstaller.exe"
set "PYTHON=%VENV_SCRIPTS%\python.exe"
set "ENTRY=%ROOT%\src\AudioScribe.Backend\cli.py"

set "TEMP_DIST=%ROOT%\dist\AudioScribe\temp_dist"
set "WORK=%ROOT%\build\pyinstaller_work"
set "TARGET=%ROOT%\dist\AudioScribe\backend"

if not exist "%PYI%" (
  echo ERROR: "%PYI%" fehlt.
  echo Bitte Build-venv anlegen:
  echo   cd src\AudioScribe.Backend
  echo   python -m venv .venv-build
  echo   .venv-build\Scripts\pip install -r requirements.txt pyinstaller
  exit /b 1
)

echo Building backend with PyInstaller (das kann mehrere Minuten dauern)...
"%PYI%" --noconfirm --clean --onedir --console ^
  --name "audioscribe-backend" ^
  --distpath "%TEMP_DIST%" ^
  --workpath "%WORK%" ^
  "%ENTRY%"
if errorlevel 1 (
  echo ERROR: PyInstaller-Fehlgeschlagen.
  exit /b 1
)

if not exist "%TEMP_DIST%\audioscribe-backend\audioscribe-backend.exe" (
  echo ERROR: "%TEMP_DIST%\audioscribe-backend\audioscribe-backend.exe" wurde nicht erzeugt.
  exit /b 1
)

if exist "%TARGET%" rmdir /s /q "%TARGET%"
move "%TEMP_DIST%\audioscribe-backend" "%TARGET%" >nul
if errorlevel 1 (
  echo ERROR: Verschieben nach "%TARGET%" fehlgeschlagen.
  exit /b 1
)
if exist "%TEMP_DIST%" rmdir /s /q "%TEMP_DIST%"

echo Fertig: "%TARGET%\audioscribe-backend.exe"
