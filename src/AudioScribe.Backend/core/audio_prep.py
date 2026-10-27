"""Audio-Vorverarbeitung: FFmpeg -> 16 kHz Mono FLAC, atomar (.tmp -> rename)."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
from pathlib import Path

from . import protocol

SUPPORTED_EXTENSIONS = {".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wma"}

INTERMEDIATE_NAME = "audio_16k_mono.flac"
PROBE_NAME = "probe.json"


class ToolMissing(RuntimeError):
    pass


def _tool(name: str) -> str:
    exe = shutil.which(name)
    if not exe:
        raise ToolMissing(
            f"{name} wurde nicht im PATH gefunden. Bitte FFmpeg installieren "
            "(https://ffmpeg.org/download.html) und den PATH aktualisieren."
        )
    return exe


def probe_duration(input_path: str) -> float:
    ffprobe = _tool("ffprobe")
    out = subprocess.run(
        [ffprobe, "-v", "error", "-show_entries", "format=duration",
         "-of", "json", input_path],
        capture_output=True, text=True, timeout=120,
    )
    if out.returncode != 0:
        raise protocol.BackendError(
            f"Datei kann nicht gelesen werden: {out.stderr.strip()[:300]}", "audio_probe"
        )
    try:
        return float(json.loads(out.stdout)["format"]["duration"])
    except (KeyError, ValueError, json.JSONDecodeError) as exc:
        raise protocol.BackendError("Audio-Metadaten unvollstaendig (Duration fehlt).", "audio_probe") from exc


def ensure_16k_mono(input_path: str, session_dir: str) -> str:
    """Rechnet die Eingabe auf 16 kHz Mono FLAC herunter; atomar im Session-Ordner."""
    src = Path(input_path)
    if not src.exists():
        raise protocol.BackendError(f"Eingabedatei nicht gefunden: {input_path}", "file_missing")
    if src.suffix.lower() not in SUPPORTED_EXTENSIONS:
        raise protocol.BackendError(
            f"Nicht unterstuetztes Format: {src.suffix}. Erlaubt: "
            + ", ".join(sorted(SUPPORTED_EXTENSIONS)),
            "unsupported_format",
        )

    ffmpeg = _tool("ffmpeg")
    session = Path(session_dir)
    session.mkdir(parents=True, exist_ok=True)
    target = session / INTERMEDIATE_NAME
    # Endung .flac behalten (ffmpeg-Formaterkennung), atomar via tmp -> rename
    tmp = session / (INTERMEDIATE_NAME + ".writing.flac")

    cmd = [
        ffmpeg, "-y", "-hide_banner", "-loglevel", "error",
        "-i", str(src),
        "-ac", "1", "-ar", "16000",
        "-c:a", "flac", "-compression_level", "8",
        str(tmp),
    ]
    res = subprocess.run(cmd, capture_output=True, text=True, timeout=7200)
    if res.returncode != 0 or not tmp.exists():
        if tmp.exists():
            tmp.unlink(missing_ok=True)
        raise protocol.BackendError(
            f"FFmpeg-Konvertierung fehlgeschlagen: {res.stderr.strip()[:300]}", "ffmpeg"
        )

    os.replace(tmp, target)
    return str(target)
