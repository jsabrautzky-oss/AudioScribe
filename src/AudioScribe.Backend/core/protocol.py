"""Zeilenbasiertes JSON-Protokoll ueber STDOUT (eine Nachricht pro Zeile).

C# liest genau diese Struktur:
  {"type": "info"|"progress"|"completed"|"error", ...}
"""

from __future__ import annotations

import json
import sys


class BackendError(Exception):
    """Fachlicher Fehler mit maschinenlesbarem Code (z. B. "hf_auth")."""

    def __init__(self, message: str, code: str = "error"):
        super().__init__(message)
        self.code = code


def emit(obj: dict) -> None:
    line = json.dumps(obj, ensure_ascii=False, allow_nan=False)
    sys.stdout.write(line + "\n")
    sys.stdout.flush()


def info(message: str, **extra) -> None:
    emit({"type": "info", "message": message, **extra})


def progress(phase: str, percent: float, message: str) -> None:
    emit({
        "type": "progress",
        "phase": phase,
        "percent": round(max(0.0, min(100.0, float(percent))), 1),
        "message": message,
    })


def completed(data: dict) -> None:
    emit({"type": "completed", "data": data})


def error(message: str, code: str = "error") -> None:
    emit({"type": "error", "message": message, "code": code})
