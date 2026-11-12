"""
Presentation-attack detection (PAD) evaluation. It reports what it was given and says "not measured" otherwise.

Frame level (the passive MiniFASNet score on single images):
  python eval/pad_eval.py --live DIR_OF_LIVE_CAPTURES --attack print=DIR --attack screen=DIR --attack replay=DIR

Burst level (the whole liveness pipeline: head-turn challenge + passive model + face continuity):
  python eval/pad_eval.py --bursts DIR
where DIR/<class>/<burst>/ holds frame_000.jpg ... and meta.json {"steps": ["turn_left", "turn_right"], "t_ms": [...]}.
<class> is "live" for genuine presentations; every other folder name (print, screen, replay, mask, ...) is an attack type.

Attack samples must be captured with the real desk camera: a printed photo of the guardian, the guardian's photo or
video on a phone and a tablet screen, a replayed video. Public PAD corpora (CASIA-FASD, Replay-Attack, OULU-NPU) need
licence agreements and are not fetched here. See eval/README.md for the capture protocol. Nothing in this script
fabricates or simulates attacks.
"""
from __future__ import annotations

import argparse
import json
import sys
from datetime import date
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent))

import cv2  # noqa: E402

import metrics as M  # noqa: E402

IMAGE_EXT = {".jpg", ".jpeg", ".png", ".webp"}
# Mirrors the thresholds ChildGuard.Api applies (FaceVerification:PassiveMinRealFraction / PassiveMinMeanReal).
MIN_REAL_FRACTION, MIN_MEAN_REAL = 0.8, 0.6


def frame_scores(folder: Path):
    from app.services import passive_pad
    from app.services.face_detection import detect
    scores, no_face = [], 0
    for f in sorted(p for p in folder.rglob("*") if p.suffix.lower() in IMAGE_EXT):
        img = cv2.imread(str(f))
        faces = detect(img) if img is not None else []
        if not faces:
            no_face += 1
            continue
        p = passive_pad.real_probability(img, faces[0]["box"])
        if p is None:
            raise SystemExit("Passive anti-spoofing is unavailable (torch or weights missing).")
        scores.append(p)
    return scores, no_face


def liveness_passes(result: dict) -> bool:
    """Mirror of ChildGuard.Application LivenessPolicy, used only here to score evaluation bursts."""
    p = result["passive"]
    return (not result["issues"] and result["challenge_completed"] and p["available"]
            and p["real_frac"] is not None and p["real_frac"] >= MIN_REAL_FRACTION and p["mean"] >= MIN_MEAN_REAL
            and result["continuity"]["same_person"] is True)


def burst_results(root: Path):
    from app.services.liveness import analyze_liveness
    import base64
    out = {}
    for cls_dir in sorted(p for p in root.iterdir() if p.is_dir()):
        passed = total = 0
        for burst in sorted(p for p in cls_dir.iterdir() if p.is_dir()):
            meta = json.loads((burst / "meta.json").read_text())
            frames = []
            for i, f in enumerate(sorted(p for p in burst.iterdir() if p.suffix.lower() in IMAGE_EXT)):
                frames.append({"image_base64": base64.b64encode(f.read_bytes()).decode(), "t_ms": meta["t_ms"][i]})
            try:
                result = analyze_liveness(meta["steps"], frames)
            except ValueError as exc:
                print("skipped", burst, exc)
                continue
            total += 1
            passed += liveness_passes(result)
        out[cls_dir.name] = (passed, total)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--live", help="folder of bona fide (live) captures, frame level")
    ap.add_argument("--attack", action="append", default=[], metavar="TYPE=DIR")
    ap.add_argument("--bursts", help="folder of liveness bursts by class")
    ap.add_argument("--name", default="pad")
    ap.add_argument("--out", default=str(HERE.parent.parent / "docs" / "face-evaluation"))
    args = ap.parse_args()

    md = [f"# Presentation-attack evaluation: {args.name} ({date.today()})", ""]
    measured = False

    if args.live and not args.attack:
        live, live_nf = frame_scores(Path(args.live))
        md += ["## Frame level: bona fide samples only (no attack samples supplied)", "",
               f"- Live samples: {len(live)} ({live_nf} without a usable face).", ""]
        md += ["| real-score threshold | BPCER (live rejected) [95% CI] |", "|---|---|"]
        for t in (0.5, MIN_MEAN_REAL, 0.8, 0.95):
            r = M.apcer_bpcer(live, [], t)
            md.append(f"| {t:.2f} | {M.fmt_pct(r['bpcer'])} {M.fmt_ci(r['bpcer_ci'])} |")
        md += ["", "APCER (attacks accepted) cannot be computed without attack samples.", ""]
        measured = True

    if args.live and args.attack:
        live, live_nf = frame_scores(Path(args.live))
        md += ["## Frame level: passive anti-spoofing score (P(real))", "",
               f"- Live samples: {len(live)} ({live_nf} without a usable face).", ""]
        rows = []
        attacks = {}
        for spec in args.attack:
            kind, folder = spec.split("=", 1)
            scores, nf = frame_scores(Path(folder))
            attacks[kind] = scores
            md.append(f"- {kind}: {len(scores)} samples ({nf} without a usable face).")
        md.append("")
        for kind, scores in attacks.items():
            for t in (0.5, MIN_MEAN_REAL, 0.8, 0.95):
                r = M.apcer_bpcer(live, scores, t)
                rows.append([kind, f"{t:.2f}", f"{M.fmt_pct(r['apcer'])} {M.fmt_ci(r['apcer_ci'])}",
                             f"{M.fmt_pct(r['bpcer'])} {M.fmt_ci(r['bpcer_ci'])}", M.fmt_pct(r["acer"])])
        md += ["| attack | real-score threshold | APCER (attacks accepted) [95% CI] | BPCER (live rejected) [95% CI] | ACER |",
               "|---|---|---|---|---|"] + ["| " + " | ".join(r) + " |" for r in rows] + [""]
        measured = True

    if args.bursts:
        results = burst_results(Path(args.bursts))
        md += ["## Burst level: full liveness pipeline", "",
               "| class | bursts | passed liveness | pass rate [95% CI] |", "|---|---|---|---|"]
        for cls, (passed, total) in results.items():
            md.append(f"| {cls} | {total} | {passed} | {M.fmt_pct(passed / total if total else float('nan'))} {M.fmt_ci(M.wilson_interval(passed, total))} |")
        md += ["", "For `live`, the pass rate is 1 - BPCER; for every other class it is the APCER for that attack.", ""]
        measured = True

    if not measured:
        md += ["**Not measured.** No live captures and attack samples were supplied, so no APCER, BPCER or ACER can be reported. "
               "The checks that exist are covered by unit tests with synthetic inputs (challenge logic, fail-closed paths), which say "
               "nothing about how well real print, screen or replay attacks are caught.", "",
               "Supply samples as described in `eval/README.md` and run this script again."]

    md += ["", "## Limits", "",
           "- Results hold for the cameras, attack artefacts and people in the supplied samples only.",
           "- A determined attacker (a live video of the guardian that can be steered through the requested head turns, a 3D mask) is outside what this measures.",
           "- Passive scores are per frame; the production decision combines five frames with a head-turn challenge and a continuity check."]
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    path = out / f"pad-{args.name}-{date.today()}.md"
    path.write_text("\n".join(md) + "\n", encoding="utf-8")
    print("wrote", path)


if __name__ == "__main__":
    main()
