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

## 🎙️ Tipps für optimale Audioaufnahmen & beste Erkennung

Die Erkennungsgenauigkeit von WhisperX und die Präzision der Sprechertrennung (Pyannote) hängen stark von der Qualität der Audio-Quelldatei ab. 

### ⚙️ Die idealen Aufnahme-Einstellungen

* **Abtastrate (Sample Rate): 16 kHz (16.000 Hz)**  
  * **Warum?** Die zugrundeliegenden KI-Modelle (Whisper und Wav2Vec2) arbeiten intern nativ mit **16 kHz Mono**. Höhere Raten (wie 44,1 kHz oder 48 kHz) bringen für Sprache keinen Genauigkeitsvorteil. Liegt das Audio bereits in 16 kHz vor, entfällt das softwareseitige Resampling beim Einlesen – das beschleunigt den Startvorgang messbar.
* **Kanäle: Mono**  
  * Ein Kanal reicht für Sprachaufnahmen vollkommen aus. Stereo verdoppelt lediglich das Datenvolumen, ohne der Spracherkennung Mehrwert zu bieten.
* **Dateiformat: MP3 (oder M4A) mit 64 bis 128 kbps**  
  * **Warum?** MP3 bietet die perfekte Balance aus Sprachklarheit und minimaler Dateigröße. Ein einstündiges Meeting belegt bei 64 kbps MP3 lediglich rund **25 bis 30 MB** Speicherplatz, lässt sich blitzschnell laden und schont den Arbeitsspeicher.

---

### 📱 Getestete Empfehlung: Easy Voice Recorder Pro

Für Smartphone-Aufnahmen hat sich **Easy Voice Recorder Pro** (Android / iOS) in der Praxis bewährt.

**Empfohlenes Setup in der App:**
* **Format:** MP3
* **Abtastrate (Sample Rate):** `16 kHz` (in den Audio-Einstellungen)
* **Kanäle:** `Mono`
* **Kodierungsqualität:** `Standard` (ca. 64–96 kbps)
* **Mikrofon:** *Standard* oder *Hauptmikrofon (unbearbeitet)*

---

### 💡 Praxistipps für perfekte Sprecherzuordnung (Diarisierung)

* **Smartphone zentrieren:** Das Gerät flach und mittig auf dem Besprechungstisch platzieren, damit alle Teilnehmenden mit ähnlichem Pegel erfasst werden.
* **Wortmeldungen ausreden lassen:** Pyannote trennt Stimmen am zuverlässigsten, wenn die Sprecher sich nicht ständig gegenseitig ins Wort fallen.
* **Tisch-Vibrationen meiden:** Das Handy nicht direkt neben eine klappernde Laptoptastatur oder auf ein vibrierendes Notebook legen.

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

### Backend-EXE für den Installer bauen

```bat
build\build_backend.bat
```

Ergebnis: `dist\AudioScribe\backend\audioscribe-backend.exe` (PyInstaller-Bundle, CPU-only).

### Installer bauen

```bat
build\create_installer.bat
```

Ergebnis: `dist\AudioScribe-Setup-v<Version>.exe` (Name wird über `OutputBaseFilename`
in `installer/setup.iss` gesteuert). Das Backend aus `dist\AudioScribe\backend` wird
automatisch mitgepackt.

## GPU-CUDA-Addon

Das Basis-Setup enthält bewusst keine schweren CUDA-/cuDNN-DLLs, und das gebündelte
Backend läuft standardmäßig auf der CPU. Für NVIDIA-Systeme kann in der App ein
GPU-Add-on heruntergeladen werden. Die App erkennt NVIDIA-GPUs über WMI und zeigt
bei fehlender CUDA-Runtime einen Download-Bereich an.

Die Download-URL ist fest im Client hinterlegt und zeigt auf das öffentliche
Prerelease `cuda-runtime` dieses Repositories:

```text
https://github.com/jsabrautzky-oss/AudioScribe/releases/download/cuda-runtime/audioscribe-cuda-windows-x64.zip
```

Diese URL kann ohne Neon-Neubau überschrieben werden, z. B. für Tests. Die App prüft
in dieser Reihenfolge:

1. Umgebungsvariable `AUDIOSCRIBE_CUDA_PACKAGE_URL`
2. Datei `cuda_package_url.txt` neben der `AudioScribe.exe`
3. Datei `%APPDATA%\AudioScribe\cuda_package_url.txt`

Eine Beispielvorlage liegt im Repo unter `cuda_package_url.example.txt`. Die Override-Datei
enthält eine einzelne URL pro Zeile, z. B.:

```text
https://github.com/jsabrautzky-oss/AudioScribe/releases/download/cuda-runtime/audioscribe-cuda-windows-x64.zip
```

## Modell-Caches

Im Installer-Modus werden Modell-Caches zentral unter folgendem Ordner abgelegt:

```text
C:\ProgramData\AudioScribe\models
```

Dieser Ordner wird bei der Deinstallation entfernt.
