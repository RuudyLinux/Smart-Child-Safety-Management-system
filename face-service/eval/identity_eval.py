"""
Identity evaluation for the ArcFace matcher at the production threshold.

  python eval/identity_eval.py --dataset eval/datasets/lfw --name lfw-subset --persons 60 --per-person 4 --variants

Dataset layout: <dataset>/<person>/<image>.jpg. For a school set add --meta meta.csv with columns
file,camera,lighting (file is the path relative to the dataset, e.g. p001/img1.jpg) to get a per-condition
breakdown. Reads images only; nothing is uploaded, and the embedding cache lives under eval/cache (git-ignored).

Measures: FAR, FRR, genuine/impostor acceptance rate at the configured threshold with 95% Wilson intervals, the
equal error rate, a threshold sweep, several reference photos per person against one, and (optional) synthetic
lighting/camera degradations. It never changes the threshold; it reports what the threshold does.
"""
from __future__ import annotations

import argparse
import csv
import json
import os
import random
import sys
import time
from datetime import date
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent))

import cv2  # noqa: E402
import numpy as np  # noqa: E402

import metrics as M  # noqa: E402

IMAGE_EXT = {".jpg", ".jpeg", ".png", ".webp"}


# ---------------------------------------------------------------- dataset
def list_dataset(root: Path, persons: int, per_person: int, seed: int, min_images: int):
    rng = random.Random(seed)
    candidates = []
    for d in sorted(p for p in root.iterdir() if p.is_dir()):
        files = sorted(f for f in d.iterdir() if f.suffix.lower() in IMAGE_EXT)
        if len(files) >= min_images:
            candidates.append((d.name, files))
    rng.shuffle(candidates)
    chosen = sorted(candidates[:persons], key=lambda c: c[0])
    items = []
    for name, files in chosen:
        for f in files[:per_person]:
            items.append((name, f))
    return items, len(candidates)


# -------------------------------------------------------------- embeddings
class Embedder:
    def __init__(self, cache_path: Path):
        from app.services import face_engine
        self.fe = face_engine
        self.cache_path = cache_path
        self.cache = json.loads(cache_path.read_text()) if cache_path.exists() else {}
        self.failures = 0
        self.computed = 0

    def save(self):
        self.cache_path.parent.mkdir(parents=True, exist_ok=True)
        self.cache_path.write_text(json.dumps(self.cache))

    def embed(self, key: str, loader):
        if key in self.cache:
            return None if self.cache[key] is None else np.array(self.cache[key], dtype=np.float64)
        try:
            vec = self.fe.embed_array(loader())
        except Exception:  # no usable face: counted, not hidden
            vec = None
            self.failures += 1
        self.computed += 1
        self.cache[key] = None if vec is None else [round(float(v), 6) for v in vec]
        if self.computed % 25 == 0:
            self.save()
        return None if vec is None else np.array(vec, dtype=np.float64)


def cos(a: np.ndarray, b: np.ndarray) -> float:
    denom = np.linalg.norm(a) * np.linalg.norm(b)
    return 0.0 if denom == 0 else float(np.dot(a, b) / denom)


# ------------------------------------------------------ synthetic variants
def v_dark(img):
    return np.clip(((img / 255.0) ** 2.2) * 255 * 0.7, 0, 255).astype(np.uint8)


def v_bright(img):
    return np.clip(img.astype(np.int16) + 70, 0, 255).astype(np.uint8)


def v_low_contrast(img):
    return np.clip(128 + (img.astype(np.float32) - 128) * 0.35, 0, 255).astype(np.uint8)


def v_blur(img):
    return cv2.GaussianBlur(img, (0, 0), 2.5)


def v_noise(img):
    rng = np.random.default_rng(0)
    return np.clip(img.astype(np.float32) + rng.normal(0, 18, img.shape), 0, 255).astype(np.uint8)


def v_jpeg(img):
    ok, buf = cv2.imencode(".jpg", img, [cv2.IMWRITE_JPEG_QUALITY, 25])
    return cv2.imdecode(buf, cv2.IMREAD_COLOR)


def v_lowres(img):
    h, w = img.shape[:2]
    small = cv2.resize(img, (max(16, int(w * 0.35)), max(16, int(h * 0.35))), interpolation=cv2.INTER_AREA)
    return cv2.resize(small, (w, h), interpolation=cv2.INTER_LINEAR)


def v_side_shadow(img):
    h, w = img.shape[:2]
    ramp = np.linspace(0.3, 1.0, w, dtype=np.float32)[None, :, None]
    return np.clip(img.astype(np.float32) * ramp, 0, 255).astype(np.uint8)


VARIANTS = {
    "dark (gamma 2.2, x0.7)": v_dark, "bright (+70)": v_bright, "low contrast (x0.35)": v_low_contrast,
    "blur (sigma 2.5)": v_blur, "sensor noise (sigma 18)": v_noise, "JPEG quality 25": v_jpeg,
    "low resolution (x0.35)": v_lowres, "side shadow": v_side_shadow,
}


# ------------------------------------------------------------------ report
def table(rows, header):
    out = ["| " + " | ".join(header) + " |", "|" + "|".join("---" for _ in header) + "|"]
    out += ["| " + " | ".join(str(c) for c in r) + " |" for r in rows]
    return "\n".join(out)


def rate_row(label, r):
    return [label, f"{r['genuine_pairs']}", f"{M.fmt_pct(r['frr'])} {M.fmt_ci(r['frr_ci'])}",
            f"{r['impostor_pairs']}", f"{M.fmt_pct(r['far'])} {M.fmt_ci(r['far_ci'])}"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dataset", required=True)
    ap.add_argument("--name", required=True)
    ap.add_argument("--persons", type=int, default=60)
    ap.add_argument("--per-person", type=int, default=4)
    ap.add_argument("--min-images", type=int, default=4)
    ap.add_argument("--refs", type=int, default=3, help="reference photos per person for the multi-reference experiment")
    ap.add_argument("--impostor-pairs", type=int, default=5000)
    ap.add_argument("--genuine-cap", type=int, default=6, help="max genuine pairs per person")
    ap.add_argument("--variants", action="store_true", help="also measure synthetic lighting/camera degradations")
    ap.add_argument("--variant-persons", type=int, default=30)
    ap.add_argument("--meta", help="CSV with file,camera,lighting for a per-condition breakdown")
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--out", default=str(HERE.parent.parent / "docs" / "face-evaluation"))
    args = ap.parse_args()

    from app.services import face_engine as fe
    threshold = fe.MATCH_THRESHOLD
    root = Path(args.dataset)
    rng = random.Random(args.seed)
    started = time.time()

    items, eligible = list_dataset(root, args.persons, args.per_person, args.seed, args.min_images)
    print(f"{len(items)} images, {len({n for n, _ in items})} people (of {eligible} eligible)", flush=True)
    embedder = Embedder(HERE / "cache" / f"embeddings-{args.name}.json")
    vecs, labels, paths = [], [], []
    for name, path in items:
        v = embedder.embed(f"{name}/{path.name}", lambda p=path: cv2.imread(str(p)))
        if v is not None:
            vecs.append(v); labels.append(name); paths.append(f"{name}/{path.name}")
    embedder.save()
    excluded = len(items) - len(vecs)
    print(f"embedded {len(vecs)}; no usable face in {excluded}", flush=True)

    # ---- single reference: every pair compared once
    gp = M.genuine_pairs(labels, args.genuine_cap, rng)
    ip = M.impostor_pairs(labels, args.impostor_pairs, rng)
    genuine = [cos(vecs[a], vecs[b]) for a, b in gp]
    impostor = [cos(vecs[a], vecs[b]) for a, b in ip]
    at = M.rates_at(genuine, impostor, threshold)
    eer_t, eer = M.equal_error_rate(genuine, impostor)
    thresholds = sorted({round(t / 100, 2) for t in range(0, 101, 5)} | {round(t / 100, 2) for t in range(30, 51, 2)} | {threshold})
    sweep_rows = [M.rates_at(genuine, impostor, t) for t in thresholds]

    # ---- several reference photos: enrol k, probe the rest, best score over the references
    by_person = {}
    for i, label in enumerate(labels):
        by_person.setdefault(label, []).append(i)
    multi = {"single": ([], []), "multi": ([], [])}
    enrolled = {p: idx[:args.refs] for p, idx in by_person.items() if len(idx) >= args.refs + 1}
    people = list(enrolled)
    for p in people:
        refs = enrolled[p]
        for probe in by_person[p][args.refs:]:
            multi["single"][0].append(cos(vecs[refs[0]], vecs[probe]))
            multi["multi"][0].append(max(cos(vecs[r], vecs[probe]) for r in refs))
            for q in rng.sample([x for x in people if x != p], min(5, len(people) - 1)):
                qrefs = enrolled[q]
                multi["single"][1].append(cos(vecs[qrefs[0]], vecs[probe]))
                multi["multi"][1].append(max(cos(vecs[r], vecs[probe]) for r in qrefs))
    multi_rows = []
    for label, key in (("1 reference photo", "single"), (f"{args.refs} reference photos (best score)", "multi")):
        g, i = multi[key]
        multi_rows.append(rate_row(label, M.rates_at(g, i, threshold)))

    # ---- synthetic degradations
    variant_rows = []
    if args.variants:
        sub = people[:args.variant_persons]
        base_g, base_i, per_variant = [], [], {}
        for p in sub:
            ref = vecs[by_person[p][0]]
            for probe in by_person[p][1:]:
                base_g.append(cos(ref, vecs[probe]))
                for q in rng.sample([x for x in sub if x != p], min(3, len(sub) - 1)):
                    base_i.append(cos(vecs[by_person[q][0]], vecs[probe]))
        variant_rows.append(rate_row("none (original photo)", M.rates_at(base_g, base_i, threshold)))
        for vname, fn in VARIANTS.items():
            g, i = [], []
            for p in sub:
                ref = vecs[by_person[p][0]]
                for idx in by_person[p][1:]:
                    path = root / paths[idx]
                    vec = embedder.embed(f"{paths[idx]}::{vname}", lambda path=path, fn=fn: fn(cv2.imread(str(path))))
                    if vec is None:
                        g.append(0.0)  # a degraded photo the engine cannot use is a rejected genuine attempt
                        continue
                    g.append(cos(ref, vec))
                    for q in rng.sample([x for x in sub if x != p], min(3, len(sub) - 1)):
                        i.append(cos(vecs[by_person[q][0]], vec))
            variant_rows.append(rate_row(vname, M.rates_at(g, i, threshold)))
            embedder.save()
            print("  variant done:", vname, flush=True)

    # ---- per-condition (school data)
    condition_rows = []
    if args.meta:
        meta = {r["file"]: r for r in csv.DictReader(open(args.meta, newline=""))}
        groups = {}
        for (a, b), s in zip(gp, genuine):
            ma, mb = meta.get(paths[a], {}), meta.get(paths[b], {})
            key = (" / ".join(sorted({ma.get("lighting", "?"), mb.get("lighting", "?")})),
                   " / ".join(sorted({ma.get("camera", "?"), mb.get("camera", "?")})))
            groups.setdefault(key, []).append(s)
        for (light, cam), scores in sorted(groups.items()):
            r = M.rates_at(scores, impostor, threshold)
            condition_rows.append([light, cam, len(scores), f"{M.fmt_pct(r['frr'])} {M.fmt_ci(r['frr_ci'])}"])

    # ---- report
    result = {
        "name": args.name, "date": str(date.today()), "seed": args.seed, "threshold": threshold,
        "images": len(items), "people": len(set(labels)), "no_usable_face": excluded,
        "genuine_pairs": len(genuine), "impostor_pairs": len(impostor),
        "at_threshold": {k: v for k, v in at.items()}, "eer": {"threshold": eer_t, "rate": eer},
        "sweep": sweep_rows, "seconds": round(time.time() - started),
    }
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    stem = f"identity-{args.name}-{date.today()}"
    (out / f"{stem}.json").write_text(json.dumps(result, indent=2, default=float))

    md = [f"# Identity evaluation: {args.name} ({date.today()})", "",
          f"Threshold under test: **{threshold}** cosine similarity (unchanged; `FACE_MATCH_THRESHOLD`). Accept when score >= threshold.", "",
          "## Data", "",
          f"- {len(items)} images of {len({n for n, _ in items})} people (of {eligible} people with >= {args.min_images} images), seed {args.seed}.",
          f"- {excluded} images had no usable face and were excluded (they would be rejections in production).",
          f"- {len(genuine)} genuine pairs (same person, at most {args.genuine_cap} per person) and {len(impostor)} impostor pairs (different people).", "",
          "## At the production threshold", "",
          table([rate_row(f"{threshold}", at)], ["threshold", "genuine pairs", "FRR (false rejection) [95% CI]", "impostor pairs", "FAR (false acceptance) [95% CI]"]), "",
          f"Genuine acceptance rate: {M.fmt_pct(at['gar'])}. Impostor acceptance rate: {M.fmt_pct(at['far'])}. Equal error rate: {M.fmt_pct(eer)} at threshold {eer_t:.2f}.", "",
          "## Threshold sweep", "",
          table([[f"{r['threshold']:.2f}", M.fmt_pct(r["frr"]), M.fmt_pct(r["far"])] for r in sweep_rows], ["threshold", "FRR", "FAR"]), "",
          f"## One versus {args.refs} reference photos per person", "",
          f"Enrol the first {args.refs} photos of each person with at least {args.refs + 1}; probe with the rest; with several references the best score counts.", "",
          table(multi_rows, ["setup", "genuine probes", "FRR [95% CI]", "impostor probes", "FAR [95% CI]"]), ""]
    if variant_rows:
        md += ["## Synthetic lighting / camera degradations", "",
               "The probe photo is degraded after the fact (the reference stays original). **Synthetic: this is not a measurement of real cameras or real lighting.** "
               "An unusable degraded photo counts as a rejection.", "",
               table(variant_rows, ["probe photo", "genuine probes", "FRR [95% CI]", "impostor probes", "FAR [95% CI]"]), ""]
    if condition_rows:
        md += ["## By capture condition (school data)", "", table(condition_rows, ["lighting", "camera", "genuine pairs", "FRR [95% CI]"]), ""]
    md += ["## Reading this", "",
           "- Intervals are 95% Wilson intervals: a rate of 0 on a small sample still has a non-zero upper bound.",
           "- Public photographs of public figures taken with many cameras are not guardians at a school desk; treat this as a measure of the model, then repeat on the school set (`eval/README.md`).",
           "- The pair counts are limited by the subset size; larger subsets narrow the intervals.",
           f"- Run took {round(time.time() - started)} s. Embedding failures: {embedder.failures}."]
    (out / f"{stem}.md").write_text("\n".join(md) + "\n", encoding="utf-8")
    print("wrote", out / f"{stem}.md")


if __name__ == "__main__":
    main()
