"""AudioScribe Backend-CLI.

Start (Dev):  python cli.py transcribe --input "audio.mp3" --device auto --out-dir "..."
Protokoll:    eine JSON-Zeile pro Ereignis auf STDOUT (utf-8, ensure_ascii=False).
"""

from __future__ import annotations

import argparse
import gc
import os
import sys


def _bootstrap_paths() -> None:
    # UTF-8 STDOUT: Umlaute duerfen das JSON-Protokoll nicht crashen
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="backslashreplace")
        sys.stderr.reconfigure(encoding="utf-8", errors="backslashreplace")
    except (AttributeError, OSError):
        os.environ.setdefault("PYTHONIOENCODING", "utf-8")

    # Modell-/Download-Caches zentral bundeln (ERST vor jedem torch/huggingface-Import setzen).
    # Der C#-BackendService setzt HF_HOME/TORCH_HOME/NLTK_DATA auf den Installer-Standard
    # ProgramData\AudioScribe\models; fuer manuelle CLI-Laeufe gibt es einen User-AppData-Fallback.
    appdata = os.environ.get("APPDATA") or os.path.expanduser("~")
    local_default = os.path.join(appdata, "AudioScribe", "models")
    models = (
        os.environ.get("AUDIOSCRIBE_MODEL_DIR")
        or os.environ.get("HF_HOME")
        or os.environ.get("TORCH_HOME")
        or local_default
    )
    models = os.path.abspath(models)
    os.makedirs(models, exist_ok=True)

    hub_cache = (
        os.environ.get("HUGGINGFACE_HUB_CACHE")
        or os.environ.get("HF_HUB_CACHE")
        or os.path.join(models, "hub")
    )
    hub_cache = os.path.abspath(hub_cache)
    os.makedirs(hub_cache, exist_ok=True)

    os.environ["HF_HOME"] = models
    os.environ.setdefault("TORCH_HOME", models)
    os.environ.setdefault("HUGGINGFACE_HUB_CACHE", hub_cache)
    os.environ.setdefault("HF_HUB_CACHE", hub_cache)
    os.environ.setdefault("XDG_CACHE_HOME", models)

    # NLTK-Ressourcen (punkt_tab fuer WhisperX-Alignment) ebenfalls zentral cachen.
    nltk_data = os.environ.get("NLTK_DATA") or os.path.join(models, "nltk_data")
    nltk_data = os.path.abspath(nltk_data)
    os.makedirs(nltk_data, exist_ok=True)
    os.environ["NLTK_DATA"] = nltk_data
    os.environ["NLTK_ALLOW_PROXIED_URLOPEN"] = "1"


if __package__ in (None, ""):
    _bootstrap_paths()

from core import protocol  # noqa: E402
from core.protocol import BackendError, progress  # noqa: E402


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="audioscribe-backend",
                                description="AudioScribe Transkriptions-Backend")
    sub = p.add_subparsers(dest="command", required=True)

    t = sub.add_parser("transcribe", help="Audio transkribieren + diarisisieren")
    t.add_argument("--input", required=True, help="Audiodatei (.mp3/.wav/.m4a/.aac/.flac ...)")
    t.add_argument("--device", default="auto", choices=["auto", "cuda", "cpu"],
                   help="auto: Backend entscheidet via torch.cuda.is_available()")
    t.add_argument("--model", default="large-v3",
                   help="large-v3 | large-v3-turbo | groesserere faster-whisper-Architekturen")
    t.add_argument("--out-dir", required=True, help="Session-Verzeichnis (Temp)")
    t.add_argument("--hf-token", default=None, help="Hugging-Face-Token fuer pyannote")
    t.add_argument("--language", default="de")
    t.add_argument("--min-speakers", type=int, default=None)
    t.add_argument("--max-speakers", type=int, default=None)
    t.add_argument("--batch-size", type=int, default=16)
    t.add_argument("--no-align", action="store_true", help="Wort-Timestamps ueberspringen")
    t.add_argument("--no-resume", action="store_true",
                   help="Checkpoints verwerfen und komplett neu rechnen")
    t.add_argument("--no-diarize", action="store_true",
                   help="Diarisierung ueberspringen (nur fuers Testen)")
    return p


def _free_cuda(*objs):
    import torch
    for o in objs:
        try:
            del o
        except Exception:
            pass
    gc.collect()
    if torch.cuda.is_available():
        torch.cuda.empty_cache()


def _load_hf_token_from_appconfig() -> str | None:
    """DPAPI-verschluesselten HF-Token aus %APPDATA%\\AudioScribe\\appconfig.json lesen.

    Wird nur fuer CLI-Tests genutzt, wenn kein --hf-token/HF-Token-Env gesetzt ist.
    """
    import base64
    import ctypes
    import json

    appdata = os.environ.get("APPDATA") or os.path.expanduser("~")
    cfg = os.path.join(appdata, "AudioScribe", "appconfig.json")
    if not os.path.exists(cfg):
        return None

    try:
        with open(cfg, "r", encoding="utf-8") as fh:
            raw = json.load(fh)
        token_b64 = raw.get("hf_token_dpapi")
        if not token_b64:
            return None

        blob = base64.b64decode(token_b64)
        entropy = b"AudioScribe.v1"

        class DATA_BLOB(ctypes.Structure):
            _fields_ = [("cbData", ctypes.c_uint32),
                        ("pbData", ctypes.POINTER(ctypes.c_ubyte))]

        in_buf = ctypes.create_string_buffer(blob, len(blob))
        ent_buf = ctypes.create_string_buffer(entropy, len(entropy))
        blob_in = DATA_BLOB(len(blob), ctypes.cast(in_buf, ctypes.POINTER(ctypes.c_ubyte)))
        blob_ent = DATA_BLOB(len(entropy), ctypes.cast(ent_buf, ctypes.POINTER(ctypes.c_ubyte)))
        blob_out = DATA_BLOB()

        crypt = ctypes.windll.crypt32.CryptUnprotectData
        crypt.argtypes = [
            ctypes.POINTER(DATA_BLOB),
            ctypes.c_void_p,
            ctypes.POINTER(DATA_BLOB),
            ctypes.c_void_p,
            ctypes.c_void_p,
            ctypes.c_uint32,
            ctypes.POINTER(DATA_BLOB),
        ]
        crypt.restype = ctypes.c_int

        if not crypt(ctypes.byref(blob_in), None, ctypes.byref(blob_ent), None, None, 0,
                     ctypes.byref(blob_out)):
            return None

        out_bytes = ctypes.string_at(blob_out.pbData, blob_out.cbData)
        ctypes.windll.kernel32.LocalFree(blob_out.pbData)
        return out_bytes.decode("utf-8", errors="strict").strip()
    except Exception:
        return None


def _resolve_hf_token(args) -> str | None:
    if getattr(args, "hf_token", None):
        return args.hf_token
    env_token = os.environ.get("HF_TOKEN") or os.environ.get("HUGGINGFACE_TOKEN") or os.environ.get("AUDIOSCRIBE_HF_TOKEN")
    if env_token:
        return env_token.strip() or None
    return _load_hf_token_from_appconfig()


def cmd_transcribe(args) -> int:
    from pathlib import Path

    from core import audio_prep, diarizer, preview_generator, transcriber

    out_dir = Path(args.out_dir).resolve()
    out_dir.mkdir(parents=True, exist_ok=True)
    if args.no_resume:
        for f in (out_dir / "checkpoints").glob("*.json"):
            f.unlink(missing_ok=True)

    warnings: list[str] = []

    # ---------------- 1. Audioanalyse
    progress("audio", 0.0, "Audiodatei analysieren...")
    duration = audio_prep.probe_duration(args.input)
    progress("audio", 3.0, f"Dauer {duration / 60:.1f} min - auf 16 kHz Mono FLAC herunterrechnen...")
    audio16 = audio_prep.ensure_16k_mono(args.input, str(out_dir))

    device, compute_type = transcriber.resolve_device(args.device)
    protocol.info(f"Rechne auf {device} ({compute_type})", device=device, compute_type=compute_type)

    # ---------------- 2. Transkription + Alignment
    try:
        tr = transcriber.transcribe(
            audio16, str(out_dir), device, compute_type, args.model,
            language=args.language, batch_size=args.batch_size,
            align=not args.no_align, resume=not args.no_resume,
        )
    except transcriber.HfResourceError as exc:
        raise BackendError(str(exc), "resource_download") from exc
    segments = tr["segments"]

    # VRAM fuer Diarisierung freigeben
    _free_cuda()

    # ---------------- 3. Diarisierung
    if args.no_diarize:
        for seg in segments:
            seg["speaker"] = "SPEAKER_00"
        speakers = [{
            "id": "SPEAKER_00", "original_label": "SPEAKER_00",
            "total_time": round(sum(s["end"] - s["start"] for s in segments), 2),
            "percent": 100.0,
        }]
        warnings.append("Diarisierung uebersprungen (--no-diarize).")
    else:
        hf_token = _resolve_hf_token(args)
        if not hf_token:
            raise BackendError(
                "Fuer die Diarisierung ist ein Hugging-Face-Token erforderlich. Bitte Token in der "
                "App hinterlegen, --hf-token uebergeben oder HF_TOKEN setzen.",
                "hf_token_missing",
            )
        df, stats = diarizer.run(
            audio16, hf_token, device,
            min_speakers=args.min_speakers, max_speakers=args.max_speakers,
        )
        # Diarisierungs-Labels auf kanonische SPEAKER_XX normalisieren
        label_map = {s["original_label"]: s["id"] for s in stats}
        df = diarizer.rename_df_speakers(df, label_map)
        segments = diarizer.merge_speakers_into_segments(segments, df)
        speakers = stats
        progress("diarization", 80.0, "Sprecher den Segmenten zugeordnet.")

    # ---------------- 4. Vorbereitung / Previews
    progress("preparing", 0.0, "Erzeuge Sprechergesamtzeiten...")
    per_speaker_time: dict[str, float] = {}
    for seg in segments:
        per_speaker_time[seg.get("speaker", "SPEAKER_00")] = (
            per_speaker_time.get(seg.get("speaker", "SPEAKER_00"), 0.0)
            + (seg["end"] - seg["start"])
        )
    ids = sorted(set(list(per_speaker_time) + [s["id"] for s in speakers]))
    known = {s["id"]: s for s in speakers}
    speakers = []
    for sid in ids:
        t = per_speaker_time.get(sid, 0.0)
        meta = known.get(sid, {})
        speakers.append({
            "id": sid,
            "total_time": round(t, 2),
            "percent": round(100.0 * t / max(duration, 1e-6), 2),
            "preview_file": None,
            "preview_snippet": None,
        })

    progress("preparing", 30.0, "Schneide beste 4-7s-Hoerproben pro Sprecher...")
    previews = preview_generator.generate(str(out_dir), audio16, segments,
                                          [{"id": s["id"], "original_label": s["id"]} for s in speakers])
    for s in speakers:
        pv = previews.get(s["id"])
        if pv:
            s["preview_file"] = pv["file"]
            s["preview_snippet"] = pv.get("snippet")

    # Segmente in backend-JSON-Form bringen
    out_segments = []
    for i, seg in enumerate(sorted(segments, key=lambda s: s["start"])):
        words = [
            {"start": w.get("start"), "end": w.get("end"),
             "word": (w.get("word") or "").strip(), "score": w.get("score", 0.0)}
            for w in (seg.get("words") or [])
            if (w.get("word") or "").strip()
        ]
        out_segments.append({
            "id": seg.get("id", f"seg_{i:05d}"),
            "speaker_id": seg.get("speaker", "SPEAKER_00"),
            "start": round(float(seg["start"]), 3),
            "end": round(float(seg["end"]), 3),
            "text": (seg.get("text") or "").strip(),
            "avg_confidence": round(float(seg.get("avg_logprob") or 0.0), 4),
            "words": words,
        })

    progress("preparing", 100.0, "Session abgeschlossen.")
    protocol.completed({
        "duration_seconds": round(duration, 2),
        "device": device,
        "compute_type": compute_type,
        "model": args.model,
        "language": tr.get("language", args.language),
        "session_dir": str(out_dir),
        "speakers": speakers,
        "segments": out_segments,
        "warnings": warnings,
    })
    return 0


def main(argv=None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    if args.command == "transcribe":
        try:
            return cmd_transcribe(args)
        except BackendError as exc:
            protocol.error(str(exc), exc.code)
            return 2
        except KeyboardInterrupt:
            protocol.error("Verarbeitung abgebrochen.", "cancelled")
            return 130
        except Exception as exc:  # letzte Sicherheitsnetzebene
            import traceback

            tb = traceback.format_exc(limit=6)
            protocol.error(f"Unerwarteter Fehler: {type(exc).__name__}: {str(exc)[:300]}\n{tb}",
                           "unexpected")
            return 1
    parser.error("Unbekanntes Kommando")
    return 2


if __name__ == "__main__":
    sys.exit(main())
