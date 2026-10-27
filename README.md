# AudioScribe

AudioScribe ist eine lokale Windows-App für lange Audioaufnahmen: Transkription,
Sprechertrennung (Diarisierung), Sprecher-Vorschau und Exporte. Die Verarbeitung
läuft vollständig lokal, ohne Cloud-Zwang.

## Features

- WhisperX/faster-whisper Transkription mit Wort-Timestamps
- Pyannote Diarisierung für Sprechertrennung
- Optionale lokale CUDA-Beschleunigung mit automatischem CPU-Fallback
- CPU-Modus als schlanke Basisinstallation
- GPU-Add-on-Download in der App für NVIDIA-Systeme
- Sprecher-Kacheln mit Audio- und Text-Vorschau
- Live-Umbenennung von Sprechern im Transkript
- Exporte als Markdown, TXT, SRT und JSON
- Zentrale, deinstallierbare Modell-Caches unter `%ProgramData%\AudioScribe`
  bzw. `%APPDATA%\AudioScribe` im Dev-Modus

## Stack

- Frontend: C# / .NET 9 WPF, WPF-UI, CommunityToolkit.Mvvm
- Backend: Python 3.12, WhisperX, faster-whisper, pyannote.audio, PyTorch
- Audio-Decoding: FFmpeg im PATH
- Installer: Inno Setup

## Development

### Voraussetzungen

- Windows 10/11
- .NET SDK 9
- Python 3.12
- FFmpeg im PATH
- Optional NVIDIA CUDA/Treiber
- Hugging-Face-Token für Pyannote-Diarisierung:
  - https://huggingface.co/pyannote/speaker-diarization-community-1
  - https://huggingface.co/settings/tokens?type=read

### Python-Backend

```bat
cd src\AudioScribe.Backend
python -m venv .venv
.venv\Scripts\python -m pip install -U pip

:: GPU / CUDA 12.8
.venv\Scripts\pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu128

:: Optional CPU:
:: .venv\Scripts\pip install torch torchvision torchaudio

.venv\Scripts\pip install -r requirements.txt
```

Testlauf:

```bat
.venv\Scripts\python cli.py transcribe ^
  --input "Pfad\zur\audio.mp3" ^
  --device auto ^
  --model large-v3-turbo ^
  --out-dir "%TEMP%\AudioScribe\sessions\test1" ^
  --language de
```

### C#-App bauen

```bat
dotnet build AudioScribe.sln
```

Die App nutzt im Dev-Modus automatisch
`src\AudioScribe.Backend\.venv\Scripts\python.exe`.

### Installer bauen

```bat
build\create_installer.bat
```

Ergebnis: `dist\AudioScribe-Setup.exe`.

## GPU-CUDA-Addon

Das Basis-Setup enthält bewusst keine schweren CUDA-/cuDNN-DLLs. Für NVIDIA-Systeme
kann in der App ein GPU-Add-on heruntergeladen werden. Die App erkennt NVIDIA-GPUs
über WMI und zeigt bei fehlender CUDA-Runtime einen Download-Bereich an.

Für die Download-URL kann eine Datei `cuda_package_url.txt` neben der `AudioScribe.exe`
angelegt werden. Sie enthält die URL zu einem ZIP-Archiv mit den CUDA-/cuDNN-DLLs.

Beispiel:

```text
https://example.com/audioscribe-cuda-windows-x64.zip
```

## Modell-Caches

Im Installer-Modus werden Modell-Caches zentral unter folgendem Ordner abgelegt:

```text
C:\ProgramData\AudioScribe\models
```

Dieser Ordner wird bei der Deinstallation entfernt.
