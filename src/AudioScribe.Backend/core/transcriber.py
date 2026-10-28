"""Transkription: Silero-VAD + faster-whisper (CTranslate2) + Wav2vec2-Alignment.

Anforderungen:
- OOM-Schutz: torch.cuda.OutOfMemoryError -> Batch 16 -> 4 -> 1, erst dann Fehler.
- Atomare Checkpoints (.tmp -> rename) fuer VAD, Rohtext und Alignment.
"""

from __future__ import annotations

import json
import math
import os
from pathlib import Path
from typing import Callable

from . import protocol

ProgressFn = Callable[[str, float, str], None]

BATCH_ATTEMPTS = (16, 4, 1)

# Neutraler Universal-Prompt je Zielsprache: kalibriert den Whisper-Decoder auf
# Groß-/Kleinschreibung und Umlaute, um Fehler wie "Durken" statt "Drücken" zu mindern.
UNIVERSAL_PROMPTS = {
    "de": "Transkription auf Deutsch. Bitte auf korrekte Groß- und Kleinschreibung sowie Umlaute (ä, ö, ü, ß) achten.",
}


def _initial_prompt_for(language: str | None) -> str | None:
    if not language:
        return None
    return UNIVERSAL_PROMPTS.get(language.split("-")[0].lower())


class HfResourceError(RuntimeError):
    pass


# ---------------------------------------------------------------- devices

def resolve_device(requested: str) -> tuple[str, str]:
    """Geraet + Compute-Type. device: auto|cuda|cpu (Korr. 2: Backend entscheidet)."""
    import torch

    cuda_ok = torch.cuda.is_available()
    if requested == "cuda":
        if not cuda_ok:
            raise protocol.BackendError(
                "--device cuda verlangt, aber torch.cuda.is_available() ist False "
                "(PyTorch-Build ohne CUDA oder kein funktionierender Treiber).",
                "cuda_unavailable",
            )
        return "cuda", "float16"
    if requested == "cpu":
        return "cpu", "int8"
    return ("cuda", "float16") if cuda_ok else ("cpu", "int8")


# ---------------------------------------------------------------- checkpointing

def _atomic_write(path: Path, payload) -> None:
    tmp = path.with_name(path.name + ".tmp")
    with open(tmp, "w", encoding="utf-8") as fh:
        json.dump(payload, fh, ensure_ascii=False)
    os.replace(tmp, path)


def _read(path: Path):
    if not path.exists():
        return None
    try:
        with open(path, "r", encoding="utf-8") as fh:
            return json.load(fh)
    except (json.JSONDecodeError, OSError):
        return None


def _checkpoint_paths(session_dir: str) -> dict:
    d = Path(session_dir) / "checkpoints"
    d.mkdir(parents=True, exist_ok=True)
    return {
        "vad": d / "vad_segments.json",
        "raw": d / "raw_transcript.json",
        "aligned": d / "aligned_transcript.json",
    }


# ---------------------------------------------------------------- VAD (checkpointfaehig)

def _load_silero(chunk_size: int):
    """Silero VAD: bevorzugt torch.hub, Fallback auf das PyPI-Paket 'silero-vad'
    (gebündeltes ONNX, kein GitHub-Zugriff nötig)."""
    import numpy as np
    import torch
    import torchaudio

    # torchcodec (transitiv von pyannote) ist auf FFmpeg-8-Systemen often defekt.
    # Wir dekodieren nur WAV/FLAC -> soundfile-Backend vorziehen (torch >= 2.8:
    # load(..., backend="soundfile");aeltere: set_audio_backend).
    def read_audio_patched(path, sampling_rate=16000):
        try:
            return torchaudio.load(str(path), backend="soundfile")[0]
        except TypeError:
            try:
                torchaudio.utils.set_audio_backend("soundfile")
            except Exception:
                pass
            return torchaudio.load(str(path))[0]

    try:
        pipeline, utils = torch.hub.load(
            repo_or_dir="snakers4/silero-vad",
            model="silero_vad",
            force_reload=False,
            onnx=False,
            trust_repo=True,
        )
        (get_speech_timestamps, _, read_audio, _, _) = utils
        use_onnx = False
    except Exception as hub_exc:
        try:
            from silero_vad import get_speech_timestamps, load_silero_vad, read_audio
            pipeline = load_silero_vad(onnx=True)
            use_onnx = True
            _hub_exc = hub_exc
        except ImportError as exc:
            raise HfResourceError(
                "Silero-VAD konnte nicht geladen werden (weder torch.hub noch das PyPI-Paket "
                f"'silero-vad'). Details: {str(exc)[:200]}"
            ) from exc

    class _SileroHelper:
        def __init__(self):
            self.vad_pipeline = pipeline
            self.get_speech_timestamps = get_speech_timestamps
            self.read_audio = read_audio
            self.chunk_size = chunk_size

        def detect(self, audio_path: str) -> list[dict]:
            """Laeuft auf CPU, damit dafuer kein VRAM reserviert wird."""
            waveform = read_audio_patched(audio_path)  # Datei ist bereits 16 kHz Mono
            if waveform is None or len(waveform) == 0:
                return []
            ts = self.get_speech_timestamps(
                waveform, model=self.vad_pipeline, sampling_rate=16000,
                max_speech_duration_s=self.chunk_size, threshold=0.5,
                **(dict(return_seconds=True) if use_onnx else {}),
            )
            return [
                {"start": float(t["start"]) / 16000.0, "end": float(t["end"]) / 16000.0}
                for t in ts
            ]

    return _SileroHelper()


def _seg_bounds(seg: dict) -> tuple[float, float]:
    start = seg.get("start", seg.get("start_time"))
    end = seg.get("end", seg.get("end_time"))
    return float(start or 0.0), float(end or 0.0)


def _vad_objs_from(segments: list[dict]):
    from whisperx.diarize import Segment as SegmentX
    return [SegmentX(s["start"], s["end"], "UNKNOWN") for s in segments]


def _make_cached_vad(segments: list[dict]):
    """WhisperX-Vad-Adapter, der vorberechnete Silero-Segmente durchreicht.

    Verhindert doppelte VAD-Compute und umgeht den torch.hub-Download im
    Silero-Konstruktor von whisperx (Offline-/Netzwerk-Robustheit).
    """
    from whisperx.vads.vad import Vad as _VadBase

    class _Impl(_VadBase):
        def __init__(self):  # absichtlich kein super().__init__()
            self._objs = _vad_objs_from(segments)

        @staticmethod
        def preprocess_audio(audio):
            return audio

        def __call__(self, _audio, **_kw):
            return self._objs

    return _Impl()


def _bump(p: ProgressFn, state: dict, key: str, phase: str, pct: float, msg: str,
          min_delta: float = 1.0) -> None:
    if pct - state.get(key, -1.0) >= min_delta or pct >= 100.0:
        state[key] = pct
        p(phase, pct, msg)


# ---------------------------------------------------------------- Hauptfunktion

def transcribe(
    audio_path: str,
    session_dir: str,
    device: str,
    compute_type: str,
    model_name: str,
    language: str = "de",
    batch_size: int = 16,
    align: bool = True,
    resume: bool = True,
) -> dict:
    """VAD -> ASR -> Alignment. Liefert {"segments", "language", "vad_segments"}; Segmente
    enthalten start/end/text und (nach Alignment) words[] mit Wort-Timestamps."""
    import gc

    language = (language or "de").strip() or "de"

    import torch
    from whisperx.audio import load_audio

    ck = _checkpoint_paths(session_dir)
    state: dict = {}

    def p(phase: str, pct: float, msg: str):
        protocol.progress(phase, pct, msg)

    # ---------------- 1. VAD (CPU, checkpointed)
    vad_segments = _read(Path(ck["vad"])) if resume else None
    if vad_segments is not None:
        p("audio", 100.0, f"VAD-Fortschritt geladen ({len(vad_segments)} Segmente).")
    else:
        p("audio", 5.0, "Silero-VAD initialisieren...")
        helper = _load_silero(30)
        p("audio", 15.0, "Sprachabschnitte erkennen (VAD, CPU)...")
        vad_segments = helper.detect(audio_path)
        _atomic_write(Path(ck["vad"]), vad_segments)
        p("audio", 100.0, f"VAD abgeschlossen: {len(vad_segments)} Sprachabschnitte.")
    if not vad_segments:
        raise protocol.BackendError(
            "Kein Spracheanteil erkannt - die Datei enthaelt keine verwertbare Sprache.",
            "no_speech",
        )

    # ---------------- 2. ASR (VAD-Batching, OOM-Downgrade 16 -> 4 -> 1)
    raw = _read(Path(ck["raw"])) if resume else None
    if raw is not None:
        p("transcription", 100.0, f"Rohtext geladen ({len(raw['segments'])} Segmente).")
    else:
        audio = load_audio(audio_path)  # 16 kHz float32 ndarray

        def _run_once(bs: int) -> dict:
            """Ein Transkriptionslauf mit fester Batch-Size; VAD wird aus Step 1 wiederverwendet."""
            from whisperx.asr import load_model

            vad_model = _make_cached_vad(vad_segments)
            asr_options: dict = {}
            prompt = _initial_prompt_for(language)
            if prompt is not None:
                asr_options["initial_prompt"] = prompt
            model = load_model(
                model_name, device, compute_type=compute_type,
                vad_model=vad_model, language=language, threads=4,
                vad_options={"vad_onset": 0.5, "vad_offset": 0.363, "chunk_size": 30},
                asr_options=asr_options or None,
            )

            def cb(pct: float):
                _bump(p, state, "asr", "transcription", pct,
                      f"Transkribiere Abschnitt-Cluster ({pct:0.0f} %)...")

            try:
                return model.transcribe(audio, batch_size=bs, language=language,
                                        progress_callback=cb)
            finally:
                del model, vad_model
                gc.collect()
                torch.cuda.empty_cache()

        p("transcription", 0.0, f"Lade Whisper-Modell {model_name} ({device}, {compute_type})...")
        attempts = [batch_size] + [b for b in BATCH_ATTEMPTS if b < batch_size]
        last_err = None
        for idx, bs in enumerate(attempts):
            try:
                asr_result = _run_once(bs)
                if idx > 0:
                    protocol.info(
                        f"VRAM-Engpass umgangen: Batch-Size auf {bs} reduziert.",
                        code="oom_downgrade",
                    )
                break
            except (torch.cuda.OutOfMemoryError, RuntimeError) as exc:
                oom = isinstance(exc, torch.cuda.OutOfMemoryError) or "out of memory" in str(exc).lower()
                if not oom:
                    raise
                last_err = exc
                gc.collect()
                torch.cuda.empty_cache()
                p("transcription", state.get("asr", 0.0),
                  f"VRAM zu klein - neuer Versuch mit Batch-Size {attempts[idx + 1] if idx + 1 < len(attempts) else 1}...")
        else:
            raise protocol.BackendError(
                f"Auch mit Batch-Size 1 reicht der Speicher nicht: {str(last_err)[:200]}", "oom"
            )

        segments = []
        for seg in asr_result["segments"]:
            start, end = _seg_bounds(seg)
            segments.append({
                "start": start,
                "end": end,
                "text": (seg.get("text") or "").strip(),
            })
        raw = _jsonify({"segments": segments, "language": asr_result.get("language", language)})
        _atomic_write(Path(ck["raw"]), raw)
        p("transcription", 100.0, f"Transkription fertig: {len(segments)} Segmente.")

        try:
            del asr_result, audio
        except NameError:
            pass
        gc.collect()
        torch.cuda.empty_cache()

    language_code = raw.get("language") or language

    # ---------------- 3. Alignment (Wav2vec2, checkpointed)
    if not align:
        gc.collect()
        torch.cuda.empty_cache()
        return {"segments": raw["segments"], "language": language_code, "vad_segments": vad_segments}

    aligned_cached = _read(Path(ck["aligned"])) if resume else None
    if aligned_cached is not None:
        p("alignment", 100.0, f"Alignment geladen ({len(aligned_cached['segments'])} Segmente).")
        return {"segments": aligned_cached["segments"],
                "language": aligned_cached.get("language", language_code),
                "vad_segments": vad_segments}

    audio_np = load_audio(audio_path)
    align_model = None
    align_meta = None
    aligned = None
    try:
        from whisperx.alignment import align as wx_align, load_align_model

        p("alignment", 0.0, "Lade deutsches Alignment-Modell (VoxPopuli Wav2vec2)...")
        align_model, align_meta = load_align_model(language_code, device)
    except Exception as exc:
        protocol.info(
            f"Alignment-Modell nicht ladbar ({str(exc)[:150]}) - ohne Wort-Timestamps fortgefahren.",
            code="align_skipped",
        )
        return {"segments": raw["segments"], "language": language_code, "vad_segments": vad_segments}

    def cb(pct: float):
        _bump(p, state, "al", "alignment", pct, f"Wort-Timestamps berechnen ({pct:0.0f} %)...")

    try:
        _ensure_alignment_nltk()
        aligned = wx_align(
            _with_text_aligned_input(raw["segments"]), align_model, align_meta,
            audio_np, device, interpolate_method="nearest", progress_callback=cb,
        )
    except protocol.BackendError:
        raise
    except (torch.cuda.OutOfMemoryError, RuntimeError) as exc:
        if not (isinstance(exc, torch.cuda.OutOfMemoryError) or "out of memory" in str(exc).lower()):
            raise
        protocol.info("Alignment wegen VRAM-Engpass uebersprungen - Segmente ohne Wortzeiten.",
                      code="oom_downgrade")
        return {"segments": raw["segments"], "language": language_code, "vad_segments": vad_segments}
    finally:
        try:
            del align_model
        except NameError:
            pass
        try:
            del align_meta
        except NameError:
            pass
        gc.collect()
        torch.cuda.empty_cache()

    segments = _jsonify(aligned["segments"])
    _normalize_words(segments)
    _atomic_write(Path(ck["aligned"]),
                  {"segments": segments, "language": aligned.get("language", language_code)})
    p("alignment", 100.0, "Alignment abgeschlossen.")

    out = {"segments": segments, "language": aligned.get("language", language_code),
           "vad_segments": vad_segments}
    try:
        del aligned, audio_np, raw
    except NameError:
        pass
    gc.collect()
    torch.cuda.empty_cache()
    return out


def _ensure_alignment_nltk() -> None:
    """WhisperX-Alignment braucht NLTK 'punkt_tab'; einmal lokal nachladen."""
    try:
        import nltk
    except ImportError as exc:
        raise protocol.BackendError(
            "WhisperX-Alignment benötigt NLTK, aber das Paket ist nicht installiert.",
            "resource_download",
        ) from exc

    env_path = os.environ.get("NLTK_DATA")
    if env_path and env_path not in nltk.data.path:
        nltk.data.path.insert(0, env_path)

    errors = []
    try:
        ok = nltk.download("punkt_tab", quiet=True)
        if not ok:
            errors.append("punkt_tab")
    except Exception as exc:
        errors.append(f"punkt_tab: {str(exc)[:200]}")

    if errors:
        raise protocol.BackendError(
            "WhisperX-Alignment konnte die NLTK-Ressource 'punkt_tab' nicht laden. "
            "Bitte Netzwerk/Proxy prüfen oder die App einmal mit Internetverbindung starten. "
            f"Details: {'; '.join(errors)[:500]}",
            "resource_download",
        )


# ---------------------------------------------------------------- JSON-Helfer

def _with_text_aligned_input(segments: list[dict]) -> list[dict]:
    """whisperx.align erwartet Segment-Dicts; bestehende words-Keys entfernen."""
    out = []
    for s in segments:
        s2 = {k: v for k, v in s.items() if k not in ("words", "word_segments", "chars")}
        out.append(s2)
    return out


def _jsonify(obj):
    """numpy/NaN/Tuple-Faelle fuer JSON serialisierbar machen."""
    if isinstance(obj, dict):
        return {k: _jsonify(v) for k, v in obj.items()}
    if isinstance(obj, (list, tuple)):
        return [_jsonify(v) for v in obj]
    if isinstance(obj, set):
        return [_jsonify(v) for v in obj]
    if isinstance(obj, float):
        return obj if math.isfinite(obj) else None
    if hasattr(obj, "item"):
        try:
            v = obj.item()
            return v if not isinstance(v, float) or math.isfinite(v) else None
        except Exception:
            pass
    if isinstance(obj, (str, int, bool)) or obj is None:
        return obj
    return str(obj)


def _normalize_words(segments: list[dict]) -> None:
    """word_segments -> words normalisieren; None-Zeitstempel mit Segmentgrenzen fuellen."""
    for seg in segments:
        words = seg.pop("word_segments", None) or seg.get("words") or []
        cleaned = []
        prev_end = seg.get("start")
        for w in words:
            start = w.get("start") if isinstance(w, dict) else None
            end = w.get("end") if isinstance(w, dict) else None
            word = w.get("word", "") if isinstance(w, dict) else str(w)
            cleaned.append({
                "word": (word or "").strip(),
                "start": start if start is not None else prev_end,
                "end": end if end is not None else seg.get("end"),
                "score": w.get("score", 0.0) if isinstance(w, dict) else 0.0,
            })
            prev_end = cleaned[-1]["end"] or prev_end
        seg["words"] = cleaned
