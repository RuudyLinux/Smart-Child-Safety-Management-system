"""
Extracts a reproducible subset of Labeled Faces in the Wild (people with at least N images) from lfw.tgz.

  python eval/prepare_lfw.py --archive eval/datasets/lfw_download/lfw.tgz --out eval/datasets/lfw --persons 80 --per-person 5

The archive is untrusted input: only plain `lfw/<person>/<file>.jpg` members are read, anything else (absolute
paths, "..", links) is ignored, and files are written by this script under --out, never by tar itself.
"""
from __future__ import annotations

import argparse
import random
import re
import tarfile
from collections import defaultdict
from pathlib import Path

MEMBER = re.compile(r"^(?:\./)?lfw/([A-Za-z0-9_.\-]+)/([A-Za-z0-9_.\-]+\.jpg)$")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--archive", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--persons", type=int, default=80)
    ap.add_argument("--per-person", type=int, default=5)
    ap.add_argument("--seed", type=int, default=7)
    args = ap.parse_args()

    by_person = defaultdict(list)
    with tarfile.open(args.archive, "r:gz") as tar:
        for m in tar:
            hit = MEMBER.match(m.name)
            if m.isfile() and hit:
                by_person[hit.group(1)].append(hit.group(2))
    eligible = sorted(p for p, files in by_person.items() if len(files) >= args.per_person)
    random.Random(args.seed).shuffle(eligible)
    chosen = {p: sorted(by_person[p])[:args.per_person] for p in sorted(eligible[:args.persons])}
    print(f"{len(by_person)} people in the archive, {len(eligible)} with >= {args.per_person} images, taking {len(chosen)}")

    wanted = {f"lfw/{p}/{f}" for p, files in chosen.items() for f in files}
    out = Path(args.out)
    written = 0
    with tarfile.open(args.archive, "r:gz") as tar:
        for m in tar:
            name = m.name[2:] if m.name.startswith("./") else m.name
            if m.isfile() and name in wanted:
                person, file = name.split("/")[1:]
                target = out / person / file
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(tar.extractfile(m).read())
                written += 1
    print(f"wrote {written} images under {out}")


if __name__ == "__main__":
    main()
