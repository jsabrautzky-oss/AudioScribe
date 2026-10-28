@echo off
setlocal
chcp 65001 >nul

rem GPU-Engine (CUDA) fuer das optionale CUDA-Add-on.
rem Basiert auf dem Dev-Venv src\AudioScribe.Backend\.venv (torch mit cu128).
rem Ergebnis: dist\AudioScribe_cuda\backend\audioscribe-backend.exe
rem plus      dist\audioscribe-backend-cuda-windows-x64.zip  (Add-on-Paket, Layout: backend\...)

set "ROOT=%~dp0.."
cd /d "%ROOT%"

set "VENV_SCRIPTS=%ROOT%\src\AudioScribe.Backend\.venv\Scripts"
set "SITE=%ROOT%\src\AudioScribe.Backend\.venv\Lib\site-packages"
set "PYI=%VENV_SCRIPTS%\pyinstaller.exe"
set "PYTHON=%VENV_SCRIPTS%\python.exe"
set "ENTRY=%ROOT%\src\AudioScribe.Backend\cli.py"

set "STAGE=%ROOT%\dist\AudioScribe_cuda"
set "TEMP_DIST=%STAGE%\temp_dist"
set "WORK=%ROOT%\build\pyinstaller_work_cuda"
set "TARGET=%STAGE%\backend"
set "ZIP=%ROOT%\dist\audioscribe-backend-cuda-windows-x64.zip"

if not exist "%PYI%" (
  echo ERROR: "%PYI%" fehlt.
  echo Bitte im CUDA-Venv PyInstaller nachruesten:
  echo   src\AudioScribe.Backend\.venv\Scripts\pip install pyinstaller
  exit /b 1
)

"%PYTHON%" -c "import torch,sys; sys.exit(0 if torch.version.cuda else 1)" >nul 2>nul
if errorlevel 1 (
  echo ERROR: Das CUDA-Venv enthaelt ein torch ohne CUDA.
  echo Bitte CUDA-Build installieren:
  echo   src\AudioScribe.Backend\.venv\Scripts\pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu128
  exit /b 1
)

echo Baue GPU-Engine mit PyInstaller (das kann mehrere Minuten dauern)...
"%PYI%" --noconfirm --clean --onedir --console ^
  --name "audioscribe-backend" ^
  --distpath "%TEMP_DIST%" ^
  --workpath "%WORK%" ^
  --collect-all pyannote.audio ^
  --collect-all whisperx ^
  --add-data "%SITE%\pyannote;pyannote" ^
  --copy-metadata torchcodec ^
  --copy-metadata transformers ^
  --copy-metadata huggingface-hub ^
  --copy-metadata pyannote.audio ^
  --copy-metadata whisperx ^
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

echo Sicherheitsnetz: Kopiere pyannote-Daten in das Engine-Verzeichnis...
xcopy /E /I /Y "%SITE%\pyannote" "%TARGET%\_internal\pyannote" >nul

if not exist "%TARGET%\_internal\pyannote\audio\telemetry\config.yaml" (
  echo FEHLER: config.yaml fehlt weiterhin!
  exit /b 1
)

echo Reduziere Engine-Paket: Importbibliotheken (.lib), Header und PDBs entfernen...
del /Q /S "%TARGET%\_internal\*.lib" >nul 2>nul
del /Q /S "%TARGET%\_internal\*.pdb" >nul 2>nul
if exist "%TARGET%\_internal\torch\include" rmdir /s /q "%TARGET%\_internal\torch\include"
if exist "%TARGET%\_internal\torch\test" rmdir /s /q "%TARGET%\_internal\torch\test"
if exist "%TARGET%\_internal\torch\gen" rmdir /s /q "%TARGET%\_internal\torch\gen"

echo Reduziere Engine-Paket: CUDA-Laufzeit-DLLs bleiben im separaten Runtime-Add-on...
del /Q "%TARGET%\_internal\torch\lib\cublas*.dll" >nul 2>nul
del /Q "%TARGET%\_internal\torch\lib\cudart*.dll" >nul 2>nul
del /Q "%TARGET%\_internal\torch\lib\cudnn*.dll" >nul 2>nul
del /Q "%TARGET%\_internal\torch\lib\cusparse*.dll" >nul 2>nul
del /Q "%TARGET%\_internal\torch\lib\cusolver*.dll" >nul 2>nul
del /Q "%TARGET%\_internal\torch\lib\curand*.dll" >nul 2>nul
del /Q "%TARGET%\_internal\torch\lib\nvrtc*.dll" >nul 2>nul
del /Q "%TARGET%\_internal\torch\lib\cupti*.dll" >nul 2>nul
del /Q "%TARGET%\_internal\torch\lib\nvtx*.dll" >nul 2>nul
del /Q "%TARGET%\_internal\torch\lib\nvToolsExt*.dll" >nul 2>nul

if not exist "%TARGET%\_internal\torch\lib\torch_cuda.dll" (
  echo FEHLER: torch_cuda.dll fehlt - PyTorch wurde ohne CUDA gebaut.
  exit /b 1
)
if not exist "%TARGET%\_internal\ctranslate2\ctranslate2.dll" (
  echo FEHLER: ctranslate2.dll fehlt im Engine-Paket.
  exit /b 1
)

echo Pakete ZIP (Layout backend\...)...
if exist "%ZIP%" del /Q "%ZIP%"
tar -a -c -f "%ZIP%" -C "%STAGE%" "backend"
if errorlevel 1 (
  echo ERROR: ZIP-Erzeugung fehlgeschlagen.
  exit /b 1
)

echo Fertig.
echo Engine-Ordner: "%TARGET%\audioscribe-backend.exe"
echo Add-on-Paket:  "%ZIP%"
