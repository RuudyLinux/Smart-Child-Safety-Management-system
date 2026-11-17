"""Generates the initials avatars used by the demo data seeder.

Reads src/ChildGuard.Infrastructure/Persistence/DemoData/demo_people.json and writes one 256x256 PNG
per driver, guardian and student into DemoData/avatars/<slug>.png. No real photographs are used.
Run from the repository root:  python scripts/generate-demo-avatars.py
"""
import hashlib
import json
import pathlib
import re

from PIL import Image, ImageDraw, ImageFont

ROOT = pathlib.Path(__file__).resolve().parent.parent
DATA = ROOT / "src" / "ChildGuard.Infrastructure" / "Persistence" / "DemoData"
OUT = DATA / "avatars"
SIZE = 256

# Calm two-tone palettes (top, bottom) per role so drivers, guardians and students read differently.
PALETTES = {
    "driver": [("#1e3a8a", "#2563eb"), ("#0f766e", "#14b8a6"), ("#334155", "#64748b")],
    "guardian": [("#7c2d12", "#ea580c"), ("#4c1d95", "#8b5cf6"), ("#831843", "#db2777"), ("#14532d", "#16a34a")],
    "student": [("#0369a1", "#38bdf8"), ("#a16207", "#facc15"), ("#9d174d", "#f472b6"), ("#166534", "#4ade80"), ("#5b21b6", "#a78bfa")],
}


def slug(name: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")


def font(size: int) -> ImageFont.FreeTypeFont:
    for candidate in ("C:/Windows/Fonts/segoeuib.ttf", "C:/Windows/Fonts/arialbd.ttf", "DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(candidate, size)
        except OSError:
            continue
    return ImageFont.load_default()


def hex_rgb(h: str):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))


def avatar(name: str, role: str, path: pathlib.Path) -> None:
    palette = PALETTES[role]
    top, bottom = (hex_rgb(c) for c in palette[int(hashlib.md5(name.encode()).hexdigest(), 16) % len(palette)])
    img = Image.new("RGB", (SIZE, SIZE))
    draw = ImageDraw.Draw(img)
    for y in range(SIZE):  # vertical gradient
        t = y / (SIZE - 1)
        draw.line([(0, y), (SIZE, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    # A soft head-and-shoulders silhouette behind the initials.
    shade = (255, 255, 255, 38)
    overlay = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    od = ImageDraw.Draw(overlay)
    od.ellipse([78, 40, 178, 140], fill=shade)
    od.ellipse([28, 150, 228, 330], fill=shade)
    img = Image.alpha_composite(img.convert("RGBA"), overlay)
    draw = ImageDraw.Draw(img)
    initials = "".join(p[0] for p in name.split()[:2]).upper()
    f = font(96)
    box = draw.textbbox((0, 0), initials, font=f)
    w, h = box[2] - box[0], box[3] - box[1]
    draw.text(((SIZE - w) / 2 - box[0], (SIZE - h) / 2 - box[1]), initials, font=f, fill=(255, 255, 255))
    img.convert("RGB").save(path, "PNG", optimize=True)


def main() -> None:
    people = json.loads((DATA / "demo_people.json").read_text(encoding="utf-8"))
    OUT.mkdir(exist_ok=True)
    count = 0
    for d in people["drivers"]:
        avatar(d["name"], "driver", OUT / f"{slug(d['name'])}.png"); count += 1
    for fam in people["families"]:
        for g in fam["guardians"]:
            avatar(g["name"], "guardian", OUT / f"{slug(g['name'])}.png"); count += 1
        for c in fam["children"]:
            avatar(c["name"], "student", OUT / f"{slug(c['name'])}.png"); count += 1
    print(f"{count} avatars written to {OUT}")


if __name__ == "__main__":
    main()
