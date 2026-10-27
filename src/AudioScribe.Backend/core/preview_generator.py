"""Speaker-Previews: bester 4-7-Sekunden-Abschnitt pro Sprecher -> MP3 im Session-Ordner.

Bewertung: mittlere Wort-/Segment-Confidence; Laenge moeglichst nahe 6 s.
Kurz-Abschnitte (< 4 s) werden akzeptiert, wenn kein besserer Kandidat existiert.
"""

from __future__ import annotations

import json
import os
import shutil
import subprocess
from pathlib import Path

from . import protocol

MIN_LEN = 4.0
MAX_LEN = 7.0
TARGET_LEN = 6.0
MERGE_GAP = 2.0  # Pausen unterhalb davon gelten noch als durchgehender Redebeitrag


def _segment_confidence(seg: dict) -> float:
    scores = [w.get("score") for w in (seg.get("words") or []) if isinstance(w.get("score"), (int, float))]
    if scores:
        return sum(scores) / len(scores)
    return 0.5


def _runs_for_speaker(segments: list[dict]) -> list[dict]:
    """Zusammenhaengende Laeufe eines Sprechers (Luecke < MERGE_GAP) bilden."""
    runs = []
    cur = None
    for seg in segments:
        if cur and seg["start"] - cur["end"] <= MERGE_GAP:
            cur["end"] = max(cur["end"], seg["end"])
            cur["segments"].append(seg)
        else:
            if cur:
                runs.append(cur)
            cur = {"start": seg["start"], "end": seg["end"], "segments": [seg]}
    if cur:
        runs.append(cur)
    return [r for r in runs if r["end"] - r["start"] > 0.8]


def _score(window_len: float, conf: float) -> float:
    len_penalty = abs(window_len - TARGET_LEN) / TARGET_LEN  # 0 bei 6 s
    if window_len < MIN_LEN:
        len_penalty += (MIN_LEN - window_len) / MIN_LEN * 2.0
    return conf * (1.0 - min(0.6, len_penalty * 0.5))


def _snippet_for(segments: list[dict]) -> str:
    parts = []
    for seg in sorted(segments, key=lambda s: float(s.get("start") or 0.0)):
        text = (seg.get("text") or "").strip()
        if text:
            parts.append(text)
    return " ".join(parts).strip()


def _best_window(run: dict) -> tuple[float, float, float, str]:
    """(start, end, score, snippet) des besten Fensters in einem Lauf."""
    length = run["end"] - run["start"]
    confs = [_segment_confidence(s) for s in run["segments"]]
    avg_conf = sum(confs) / len(confs) if confs else 0.5

    if length <= MAX_LEN:
        return run["start"], run["end"], _score(length, avg_conf), _snippet_for(run["segments"])

    # Fenster der Laenge MAX_LEN ueber den Lauf schieben, besten Abschnitt waehlen
    best = (run["start"], run["start"] + MAX_LEN, -1.0, _snippet_for(run["segments"][:1]))
    step = 1.0
    t = run["start"]
    while t + MAX_LEN <= run["end"] + 1e-6:
        segs_in = [s for s in run["segments"] if s["end"] > t and s["start"] < t + MAX_LEN]
        local = (sum(_segment_confidence(s) for s in segs_in) / len(segs_in)) if segs_in else avg_conf
        sc = _score(MAX_LEN, local)
        if sc > best[2]:
            best = (t, t + MAX_LEN, sc, _snippet_for(segs_in))
        t += step
    return best


def _cut_mp3(source_flac: str, start: float, end: float, out_path: Path, label: str,
             speaker_name: str) -> bool:
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        protocol.info("ffmpeg fehlt - Speaker-Preview konnte nicht erstellt werden.", code="ffmpeg_missing")
        return False
    tmp = out_path.with_name(out_path.stem + ".writing.mp3")
    duration = max(1.0, end - start)
    cmd = [
        ffmpeg, "-y", "-hide_banner", "-loglevel", "error",
        "-ss", f"{start:.3f}", "-t", f"{duration:.3f}",
        "-i", source_flac,
        "-vn", "-c:a", "libmp3lame", "-q:a", "4",
        "-metadata", f"title={label}",
        "-metadata", f"artist={speaker_name}",
        str(tmp),
    ]
    res = subprocess.run(cmd, capture_output=True, text=True, timeout=300)
    if res.returncode != 0 or not tmp.exists():
        tmp.unlink(missing_ok=True)
        protocol.info(f"Preview-Schnitt fehlergeschlagen ({label}): {res.stderr.strip()[:150]}",
                      code="preview_cut")
        return False
    os.replace(tmp, out_path)
    return True


def generate(session_dir: str, source_flac: str, segments: list[dict],
             speakers: list[dict]) -> dict:
    """segments: bereits sprecher-zugeordnete WhisperX-Segmente (speaker=Fallback-ID).
    speakers: speaker_stats-Liste mit id/original_label. Liefert {id: {"file","start","end"}}."""
    out_dir = Path(session_dir)
    by_speaker: dict[str, list[dict]] = {}
    for seg in segments:
        by_speaker.setdefault(seg.get("speaker", "SPEAKER_00"), []).append(seg)

    result: dict = {}
    total = len(speakers) or 1
    for n, meta in enumerate(sorted(speakers, key=lambda s: s["id"])):
        spk = meta["id"]
        original = meta.get("original_label", spk)
        runs = _runs_for_speaker(sorted(by_speaker.get(spk, []), key=lambda s: s["start"]))
        if not runs:
            continue
        candidates = [_best_window(r) for r in runs]
        start, end, _score_v, snippet = max(candidates, key=lambda c: c[2])
        fname = f"preview_{spk}.mp3"
        ok = _cut_mp3(source_flac, start, end, out_dir / fname,
                      label=f"Preview {spk}", speaker_name=spk)
        if ok:
            result[spk] = {
                "file": fname,
                "start": round(start, 3),
                "end": round(end, 3),
                "original_label": original,
                "snippet": snippet,
            }
        protocol.progress("preparing", 30.0 + 70.0 * (n + 1) / total,
                          f"Hoerprobe fuer {spk} erstellt ({n + 1}/{total}).")
    return result
