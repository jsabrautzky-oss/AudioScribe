# 🎙️ AudioScribe

> Lokale KI-Transkription & Multi-Sprecher-Erkennung für Windows. Optimiert für Deutsch, 1-Klick-Installation und direkter Multi-Format-Export (LLM-Prompt, Markdown, TXT, SRT, JSON).

---

## Über AudioScribe

**AudioScribe** verwandelt Audioaufnahmen vollautomatisch in präzisen, strukturierten Text mit sekundengenauer Sprecherzuordnung (*Diarisierung*) – maßgeschneidert für die **deutsche Sprache** und vorbereitet für die direkte Weiterverarbeitung in modernen KI-Workflows.

### Highlights & Features

* **Kinderleichte 1-Klick-Installation:** Schlüsselfertiges Windows-Setup. Kein manuelles Einrichten von Python, kein Terminal und keine externen Abhängigkeiten.
* **Multi-Sprecher-Erkennung:** Automatische Unterscheidung einzelner Stimmen mit integrierter Audio- und Text-Hörprobe zur schnellen Zuordnung.
* **Optimiert für LLMs & KI-Agenten:** Die Schaltfläche *„Kopieren für LLM-Agent“* formatiert das Transkript samt Zeitstempeln und Sprechernamen so, dass es ohne Nachbearbeitung direkt in ChatGPT, Claude oder lokale Modelle für Protokolle, Zusammenfassungen oder To-do-Listen übergeben werden kann.
* **Flexible Exportformate:** Ein Klick genügt für den Export in gängige Formate:
  * **Markdown (`.md`)** – lesbar strukturiert mit Sprecher-Headern
  * **Reiner Text (`.txt`)** – kompakt und übersichtlich
  * **Untertitel (`.srt`)** – sekundengenau synchronisiert für Videoschnitt und Player
  * **Session (`.json`)** – maschinenlesbare Rohdaten mit allen Timestamps für Entwickler
* **100 % lokal & datenschutzfreundlich:** Alle Berechnungen laufen vollständig auf der eigenen Hardware (NVIDIA CUDA-Beschleunigung oder CPU-Fallback). Keine Cloud-Dienste, keine Abo-Modelle, vollständige Datensouveränität.

---

## 💡 Hinweise für Tester & Erste Schritte

Da sich AudioScribe aktuell im **Alpha-Stadium (v0.1.0)** befindet, beachte bitte folgende Hinweise bei der ersten Nutzung:


### 1. Windows SmartScreen-Meldung
* Beim ersten Start des Installers meldet Windows möglicherweise: *„Der Computer wurde durch Windows geschützt“*.
* **Hintergrund:** Die Vorabversion besitzt noch kein kostenpflichtiges Code-Signing-Zertifikat.
* **Lösung:** Klicke einfach auf **„Weitere Informationen“** und anschließend auf **„Trotzdem ausführen“**.


### 2. Performance: CPU vs. GPU (NVIDIA CUDA)
* **Mit NVIDIA-Grafikkarte (RTX / GTX):** Über den Button in der App kann das optionale CUDA-Paket geladen werden. Die Transkription läuft dann mit bis zu 10- bis 20-facher Echtzeit.
* **Reiner CPU-Modus (z. B. Büro-PCs / Laptops):** Die App läuft vollkommen stabil auch ohne Grafikkarte. Die Verarbeitung dauert auf Standard-CPUs jedoch spürbar länger (in etwa 1:1 zur Länge der Audiodatei). 
* *Tipp für den ersten Funktionstest:* Nutze am besten eine kurze Testdatei von 30 bis 60 Sekunden.


### 3. Hugging-Face-Token beim ersten Start
* Für die Sprechertrennung (*Pyannote Diarization*) ist beim ersten Ausführen ein kostenloser Hugging-Face-Token erforderlich, um die Modellgewichte in den lokalen Cache zu laden.
* Nach dem einmaligen Download arbeitet die Anwendung zu 100 % offline.

---

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
