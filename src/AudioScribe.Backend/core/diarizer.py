"""Diarisierung (pyannote) mit Versionsweiche + manuellem Timestamp-Merge.

WhisperX >= 3.8 bringt eine pyannote-4-kompatible DiarizationPipeline
("pyannote/speaker-diarization-community-1"). Bricht der whisperx-Pfad
(Import/API-Drift), wird pyannote separat ausgefuehrt und die Timestamps
manuell auf die WhisperX-Segmente gemerged (Korr. 1).

Versionsmatrix:
  pyannote.audio >= 4  -> Pipeline "pyannote/speaker-diarization-community-1"
  pyannote.audio 3.x   -> Pipeline "pyannote/speaker-diarization-3.1"
"""

from __future__ import annotations

from typing import Optional

from . import protocol

HF_MODEL_V4 = "pyannote/speaker-diarization-community-1"
HF_MODEL_V3 = "pyannote/speaker-diarization-3.1"


def _parse_version(v: str) -> tuple:
    parts = []
    for chunk in str(v).split(".")[:3]:
        digits = "".join(c for c in chunk if c.isdigit())
        parts.append(int(digits) if digits else 0)
    return tuple(parts + [0] * (3 - len(parts)))


def _diar_device(requested: str) -> str:
    """Pyannote/Diarisierung bewusst auf CPU erzwingen.

    Auf Blackwell/Ryzen-Systemen kann das Laden von pyannote-Community-1 auf CUDA
    TDR-/Treiberabstuerze ausloesen, obwohl Whisper-ASR dort stabil laeuft.
    """
    return "cpu"


def _is_gpu_runtime_error(exc: Exception) -> bool:
    msg = str(exc).lower()
    return any(x in msg for x in ("cuda", "driver", "out of memory", "vram", "tdr", "device-side"))


def _hf_auth_message(model_id: str) -> str:
    return (
        "Hugging-Face-Zugriff auf das Diarisierungsmodell fehlgeschlagen (HTTP 401/403 oder "
        f"Zustimmung erforderlich). Bitte (1) gueltigen Token unter https://huggingface.co/settings/tokens "
        f"hinterlegen und (2) die Nutzungsbedingungen auf https://huggingface.co/{model_id} akzeptieren."
    )


class DiarizationUnavailable(RuntimeError):
    pass


def _wrap_hf_errors(exc: Exception, model_id: str):
    from . import protocol as _p

    status = getattr(exc, "response", None)
    status = getattr(status, "status_code", None)
    cls = type(exc).__name__
    if status in (401, 403) or cls in ("GatedRepoError", "RepositoryNotFoundError",
                                       "HfHubHTTPError", "LocalEntryNotFoundError"):
        return _p.BackendError(_hf_auth_message(model_id), "hf_auth")
    return _p.BackendError(f"Diarisierungsmodell nicht ladbar: {str(exc)[:250]}", "diar_model")


# ---------------------------------------------------------------- Pfad A: whisperx

def _run_whisperx_pipeline(audio_path: str, hf_token: Optional[str], device: str,
                           min_speakers: Optional[int], max_speakers: Optional[int],
                           progress_cb):
    import pandas as pd
    from whisperx.diarize import DiarizationPipeline

    model_id = HF_MODEL_V4
    pipe = DiarizationPipeline(model_name=None, token=hf_token, device=device)
    df = pipe(audio_path, min_speakers=min_speakers, max_speakers=max_speakers,
              progress_callback=progress_cb)
    if not isinstance(df, pd.DataFrame):  # API drift safety net
        raise AttributeError("whisperx DiarizationPipeline lieferte kein DataFrame")
    return df


# ---------------------------------------------------------------- Pfad B: pyannote pur

def _run_pyannote_standalone(audio_path: str, hf_token: Optional[str], device: str,
                             min_speakers: Optional[int], max_speakers: Optional[int],
                             progress_cb):
    import pandas as pd
    import torch
    import pyannote.audio as pa
    from packaging.version import parse as _vparse  # noqa: F401  (verif)

    major, minor, _ = _parse_version(pa.__version__)
    if major >= 4:
        from pyannote.audio import Pipeline
        model_id = HF_MODEL_V4
        try:
            pipe = Pipeline.from_pretrained(model_id, token=hf_token)
        except Exception as exc:
            raise _wrap_hf_errors(exc, model_id) from exc
        if pipe is None:
            raise _wrap_hf_errors(RuntimeError("Pipeline.from_pretrained lieferte None"), model_id)
        try:
            pipe = pipe.to(torch.device(device))
        except Exception:
            pass
        kwargs = {}
        if min_speakers is not None:
            kwargs["min_speakers"] = min_speakers
        if max_speakers is not None:
            kwargs["max_speakers"] = max_speakers
        try:
            out = pipe({"waveform": torch.from_numpy(_load16k(audio_path))[None, :],
                        "sample_rate": 16000}, **kwargs)
        except TypeError:
            out = pipe({"waveform": torch.from_numpy(_load16k(audio_path))[None, :],
                        "sample_rate": 16000})
        ann = out if not isinstance(out, tuple) else out[0]
    else:
        model_id = HF_MODEL_V3
        from pyannote.audio.pipelines import SpeakerDiarization
        try:
            pipe = SpeakerDiarization.from_pretrained(model_id, token=hf_token)
        except Exception as exc:
            raise _wrap_hf_errors(exc, model_id) from exc
        try:
            pipe = pipe.to(torch.device(_diar_device(device)))
        except Exception:
            pass
        # 3.1-Pipeline erwartet Annotation-/Datei-Eingang
        out = pipe(audio_path, num_speakers=None,
                   **({"min_speakers": min_speakers} if min_speakers else {}),
                   **({"max_speakers": max_speakers} if max_speakers else {}))
        ann = out

    progress_cb(99.0)
    rows = []
    for seg, _, speaker in ann.itertracks(yield_label=True):
        rows.append({"segment": seg, "start": float(seg.start),
                     "end": float(seg.end), "speaker": str(speaker)})
    if not rows:
        return pd.DataFrame(columns=["segment", "start", "end", "speaker"])
    return pd.DataFrame(rows)


def _load16k(audio_path: str):
    import whisperx
    import numpy as np
    audio = whisperx.load_audio(audio_path)
    return np.asarray(audio, dtype="float32")


# ---------------------------------------------------------------- public API

def run(audio_path: str, hf_token: Optional[str], device: str,
        min_speakers: Optional[int] = None, max_speakers: Optional[int] = None) -> tuple:
    """Fuhrt Diarisierung aus; immer mit Fortschritt (phase=diarization).

    Rueckgabe: (pandas.DataFrame mit start/end/speaker, speaker_stats-Liste).
    """
    state = {"last": -1.0}

    def progress_cb(pct: float, _total=None):
        pct = float(pct)
        if pct - state["last"] >= 2.0 or pct >= 100.0:
            state["last"] = pct
            protocol.progress("diarization", pct * 0.6, "Sprecher-Clustering laeuft...")

    protocol.progress("diarization", 0.0, "Lade Diarisierungsmodell (pyannote)...")
    diar_device = _diar_device(device)
    if device and device != diar_device:
        protocol.info(
            "Diarisierung laeuft auf CPU; GPU-Pfad deaktiviert (TDR-/Treiber-Schutz).",
            code="diar_cpu_fallback",
        )

    try:
        df = _run_whisperx_pipeline(audio_path, hf_token, diar_device,
                                    min_speakers, max_speakers,
                                    lambda pct: progress_cb(pct))
        protocol.info("Diarisierung ueber whisperx.DiarizationPipeline (pyannote 4 API).",
                      code="diar_backend")
    except protocol.BackendError:
        raise  # hf_auth o. a. klarer Fehler weiterreichen
    except Exception as exc:
        protocol.info(
            f"whisperx-Diarisierung nicht nutzbar ({str(exc)[:140]}) - "
            "Fallback: pyannote separat + manueller Merge.",
            code="diar_fallback",
        )
        try:
            df = _run_pyannote_standalone(audio_path, hf_token, diar_device,
                                          min_speakers, max_speakers, progress_cb)
        except Exception as fallback_exc:
            if _is_gpu_runtime_error(fallback_exc):
                protocol.info(
                    "GPU-Fehler bei der Diarisierung abgefangen; CPU-Rueckfall wurde verwendet.",
                    code="diar_cpu_fallback",
                )
            raise
        protocol.info("Diarisierung ueber pyannote-Standalone-Pfad.", code="diar_backend")

    if df is None or len(df) == 0:
        raise protocol.BackendError(
            "Diarisierung lieferte keine Sprecher - ggf. min/max-Sprecher-Optionen anpassen.",
            "diar_empty",
        )
    protocol.progress("diarization", 70.0, f"{df['speaker'].nunique()} Sprecher erkannt.")
    total = float(df["end"].max()) if len(df) else 0.0
    return df, speaker_stats(df, total)


def merge_speakers_into_segments(segments: list[dict], df) -> list[dict]:
    """Manueller Timestamp-Merge: jeder WhisperX-Segment bekommt den Sprecher mit
    groesster Zeitueberlappung (dominanter Sprecher). Kein Wort-Merge noetig."""
    di = [
        (float(row.start), float(row.end), str(row.speaker))
        for row in df.itertuples(index=False)
    ]
    for seg in segments:
        s0, s1 = float(seg["start"]), float(seg["end"])
        best_spk, best_ov = None, 0.0
        for d0, d1, spk in di:
            ov = min(s1, d1) - max(s0, d0)
            if ov > best_ov:
                best_ov, best_spk = ov, spk
        if best_spk is None:
            # keine Ueberlappung: naechsten Diarisierungs-Sprecher nehmen
            mid = (s0 + s1) / 2.0
            near = min(di, key=lambda t: min(abs(mid - t[0]), abs(mid - t[1]),
                                             max(0.0, min(t[1], mid) - max(t[0], mid))))
            best_spk = near[2]
        seg["speaker"] = best_spk
    return segments


def speaker_stats(df, total_duration: float) -> list[dict]:
    """Sprecher-Metadaten: Gesamt-Sprechzeit + Anteil; sortiert nach Erstauftreten
    und in SPEAKER_XX-Namen ueberfuehrt (Community-Model nutzt u. U. andere Labels)."""
    # Reihenfolge des Erstauftretens bestimmen
    order: list[str] = []
    for row in df.itertuples(index=False):
        spk = str(row.speaker)
        if spk not in order:
            order.append(spk)

    totals = {spk: 0.0 for spk in order}
    for row in df.itertuples(index=False):
        totals[str(row.speaker)] += float(row.end) - float(row.start)

    out = []
    for idx, spk in enumerate(order):
        out.append({
            "original_label": spk,
            "id": f"SPEAKER_{idx:02d}",
            "total_time": round(totals[spk], 2),
            "percent": round(100.0 * totals[spk] / max(total_duration, 1e-6), 2),
        })
    return out


def rename_df_speakers(df, mapping: dict[str, str]):
    """Diarisierungs-DataFrame auf kanonische SPEAKER_XX-IDs normalisieren."""
    df = df.copy()
    df["speaker"] = df["speaker"].astype(str).map(lambda s: mapping.get(s, s))
    return df
