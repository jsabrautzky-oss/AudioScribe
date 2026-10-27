@echo off
setlocal
chcp 65001 >nul

set "ROOT=%~dp0.."
cd /d "%ROOT%"

set "APP_DIST=%ROOT%\dist\AudioScribe"
set "OUT_DIR=%ROOT%\dist"
set "SETUP_SCRIPT=%ROOT%\installer\setup.iss"

if not exist "%APP_DIST%" (
  echo ERROR: "%APP_DIST%" fehlt.
  echo Bitte zuerst die App-Publikate in dist\AudioScribe erzeugen, z. B.:
  echo   dotnet publish src\AudioScribe.App\AudioScribe.App.csproj -c Release -r win-x64 --self-contained true -p:DebugType=none -o dist\AudioScribe
  exit /b 1
)

if not exist "%APP_DIST%\backend\audioscribe-backend.exe" (
  echo WARNUNG: dist\AudioScribe\backend\audioscribe-backend.exe fehlt.
  echo Das Setup wird ohne gebuendeltes Python-Backend erstellt.
)

set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ISCC%" goto compile

set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if exist "%ISCC%" goto compile

set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if exist "%ISCC%" goto compile

echo Inno Setup nicht gefunden. Versuche Installation per winget...
winget install JRSoftware.InnoSetup --silent --accept-package-agreements --accept-source-agreements

set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ISCC%" goto compile

set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if exist "%ISCC%" goto compile

echo ERROR: ISCC.exe konnte nicht gefunden oder installiert werden.
exit /b 1

:compile
if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

echo Compiling installer with: "%ISCC%"
"%ISCC%" /O"%OUT_DIR%" /F"AudioScribe-Setup-v0.1.0" "%SETUP_SCRIPT%"
if errorlevel 1 (
  echo ERROR: Inno Setup konnte nicht kompilieren.
  exit /b 1
)

echo Fertig: "%OUT_DIR%\AudioScribe-Setup-v0.1.0.exe"
