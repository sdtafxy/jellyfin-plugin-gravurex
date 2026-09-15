#!/usr/bin/env python3
"""Draws the plugin artwork.

Two pieces are produced:

* a square icon, for places that want one
* a 16:9 banner with a dark gradient, the mark and the wordmark centred, which is
  the shape the Jellyfin plugin catalogue renders

Everything is rendered at 2x and downsampled, which gives clean edges without
pulling in an SVG rasteriser.
"""

import math
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "assets")

BG_TOP = (46, 38, 68)
BG_BOTTOM = (17, 15, 26)
RING = (231, 104, 90)
RIM = (255, 209, 168)
WORDMARK = (243, 238, 248)
WORDMARK_DIM = (186, 174, 204)

NAME = "GravureX"

SUPER = 2


def polar(cx, cy, r, deg):
    a = math.radians(deg)
    return (cx + r * math.cos(a), cy + r * math.sin(a))


def draw_aperture(d, cx, cy, r_outer, opening_ratio=0.38, blades=6):
    """Draws a camera aperture centred on (cx, cy)."""
    step = 360 / blades
    r_open = r_outer * opening_ratio
    line = max(2, int(r_outer * 0.055))
    outline = max(2, int(r_outer * 0.05))

    d.ellipse([cx - r_outer, cy - r_outer, cx + r_outer, cy + r_outer], fill=RING)

    opening = [polar(cx, cy, r_open, -90 + i * step) for i in range(blades)]

    # Real aperture blades meet along the extensions of the opening's edges, so
    # each separator is one hexagon side continued out to the barrel.
    for i in range(blades):
        a = opening[i]
        b = opening[(i + 1) % blades]
        ux, uy = b[0] - a[0], b[1] - a[1]
        length = math.hypot(ux, uy)
        ux, uy = ux / length, uy / length

        px, py = b[0] - cx, b[1] - cy
        proj = px * ux + py * uy
        disc = proj * proj - (px * px + py * py) + r_outer * r_outer
        t = -proj + math.sqrt(max(disc, 0.0)) - r_outer * 0.05

        d.line([b, (b[0] + ux * t, b[1] + uy * t)], fill=BG_BOTTOM, width=line)

    d.polygon(opening, fill=BG_BOTTOM)

    # Rim and opening outlines go on last so they cover the separator ends.
    d.ellipse(
        [cx - r_outer, cy - r_outer, cx + r_outer, cy + r_outer],
        outline=RIM,
        width=outline,
    )
    d.line(opening + [opening[0]], fill=RIM, width=max(2, outline * 2 // 3), joint="curve")


def gradient(size, top, bottom):
    """Diagonal two stop gradient, built small and scaled up."""
    small = Image.new("RGB", (64, 36))
    px = small.load()
    for y in range(small.height):
        for x in range(small.width):
            t = (x / (small.width - 1) + y / (small.height - 1)) / 2
            px[x, y] = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
    return small.resize(size, Image.BICUBIC).convert("RGBA")


def glow(size, centre, radius, colour, strength=70):
    """A soft radial highlight, used to lift the mark off the background."""
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.ellipse(
        [centre[0] - radius, centre[1] - radius, centre[0] + radius, centre[1] + radius],
        fill=(*colour, strength),
    )
    return layer.filter(ImageFilter.GaussianBlur(radius * 0.55))


def load_font(size):
    for path, index in (
        ("/System/Library/Fonts/Avenir Next.ttc", 0),
        ("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 0),
    ):
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size, index=index)
            except OSError:
                continue
    return ImageFont.load_default()


def centred_text(d, text, font, centre_x, top, fill):
    box = d.textbbox((0, 0), text, font=font)
    d.text((centre_x - (box[2] - box[0]) / 2 - box[0], top), text, font=font, fill=fill)


def make_square_icon():
    canvas = 1024 * SUPER
    img = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    pad = 96 * SUPER
    d.rounded_rectangle(
        [pad, pad, canvas - pad, canvas - pad],
        radius=224 * SUPER,
        fill=BG_BOTTOM,
    )

    draw_aperture(d, canvas / 2, canvas / 2, 302 * SUPER)
    return img.resize((1024, 1024), Image.LANCZOS)


def make_banner():
    """The mark and the name sit side by side on one line, centred as a group."""
    width, height = 1920, 1080
    big = (width * SUPER, height * SUPER)
    img = gradient(big, BG_TOP, BG_BOTTOM)

    font = load_font(int(big[1] * 0.205))
    box = ImageDraw.Draw(img).textbbox((0, 0), NAME, font=font)
    text_width, text_height = box[2] - box[0], box[3] - box[1]

    mark_radius = big[1] * 0.145
    gap = big[1] * 0.085
    group_width = mark_radius * 2 + gap + text_width

    left = (big[0] - group_width) / 2
    centre_y = big[1] / 2
    mark_centre = (left + mark_radius, centre_y)

    img.alpha_composite(glow(big, mark_centre, mark_radius * 2.2, RING, 60))

    d = ImageDraw.Draw(img)
    draw_aperture(d, mark_centre[0], mark_centre[1], mark_radius)

    d.text(
        (left + mark_radius * 2 + gap - box[0], centre_y - text_height / 2 - box[1]),
        NAME,
        font=font,
        fill=WORDMARK,
    )

    return img.resize((width, height), Image.LANCZOS)


def main():
    os.makedirs(OUT, exist_ok=True)

    icon = make_square_icon()
    icon.save(os.path.join(OUT, "icon.png"))
    icon.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "icon-256.png"))
    icon.resize((128, 128), Image.LANCZOS).save(os.path.join(OUT, "icon-128.png"))

    banner = make_banner()
    banner.save(os.path.join(OUT, "logo.png"))
    banner.resize((1280, 720), Image.LANCZOS).save(os.path.join(OUT, "logo-1280.png"))

    print("artwork written to", OUT)


if __name__ == "__main__":
    main()
