#!/usr/bin/env python3
"""Render the FenCalc2 logo ("Sunrise pane") into Assets/.

Outputs
-------
Assets/FenCalc2.ico  multi-size Windows icon (16..256) for the exe + window/taskbar icon.
                     16-128 are classic BMP frames (max compatibility, works with
                     CreateIconFromResourceEx / Avalonia's ICO parser); 256 is a PNG frame
                     as is normal for jumbo icons.
Assets/FenCalc2.png  256 px RGBA artwork, used by the About dialog via avares://.

Everything is drawn once on an 8x supersampled canvas (2048 px) with exact geometry
(rectangles/rounded-rect masks, not stroke conventions) and LANCZOS-downsampled, so the
16 px frame stays crisp.

Re-run after any design tweak:  python tools/make_logo.py
Requires Pillow (python -m pip install --user Pillow).
"""

from __future__ import annotations

import io
import math
import os
import struct

from PIL import Image, ImageDraw

# --- design constants, all in the 256 px master coordinate space -------------------
BASE = 256          # master artwork size
SS = 8              # supersample factor -> 2048 px canvas

TILE_TOP = (78, 126, 184)       # #4E7EB8  (lighter end of accent #3D6DA8)
TILE_BOTTOM = (44, 81, 121)     # #2C5179  (accent pressed #2C5179)
SUN = (247, 185, 76)            # #F7B94C
WHITE = (255, 255, 255)
GLASS_ALPHA = 46                # pane tint over the tile gradient

TILE_BOX = (16, 16, 240, 240)   # rounded tile
TILE_RADIUS = 52

SUN_C = (180.0, 76.0)           # sun centre, top-right
SUN_R = 30.0
RAY_WIDTH = 13.0
RAY_INNER, RAY_OUTER = 40.0, 50.0
# rays fan away from the window: right, up-right, up, up-left, left, down-right
RAY_DEGREES = (0.0, -45.0, -90.0, -135.0, 180.0, 45.0)

# window: stroke centred on (56,98)-(174,224), width 16, corner radius 10
FRAME_OUTER = (48, 90, 182, 232)    # centreline +/- 8
FRAME_OUTER_R = 18
FRAME_INNER = (64, 106, 166, 216)   # centreline +/- 8 (pane opening)
FRAME_INNER_R = 2
MULLION_V = (109, 106, 121, 216)    # width 12, splits the opening 2 x 2
MULLION_H = (64, 155, 166, 167)     # centred vertically in the opening (y = 161)

ICO_SIZES_BMP = (16, 24, 32, 48, 64, 128)
ICO_SIZE_PNG = 256


def _sc(v: float, ss: int) -> int:
    return int(round(v * ss))


def _box(box, ss: int):
    return [_sc(v, ss) for v in box]


def _ray_polygon(cx: float, cy: float, deg: float, ss: int):
    """Rectangle from RAY_INNER to RAY_OUTER along deg, half-width RAY_WIDTH/2."""
    a = math.radians(deg)
    ux, uy = math.cos(a), math.sin(a)      # along the ray
    px, py = -uy, ux                       # perpendicular
    hw = RAY_WIDTH / 2.0
    inner, outer = RAY_INNER, RAY_OUTER
    pts = [
        (cx + ux * inner + px * hw, cy + uy * inner + py * hw),
        (cx + ux * outer + px * hw, cy + uy * outer + py * hw),
        (cx + ux * outer - px * hw, cy + uy * outer - py * hw),
        (cx + ux * inner - px * hw, cy + uy * inner - py * hw),
    ]
    return [(_sc(x, ss), _sc(y, ss)) for x, y in pts]


def _caps(cx: float, cy: float, deg: float, ss: int):
    """Round cap circles at both ends of a ray."""
    a = math.radians(deg)
    ux, uy = math.cos(a), math.sin(a)
    r = _sc(RAY_WIDTH / 2.0, ss)
    out = []
    for d in (RAY_INNER, RAY_OUTER):
        x, y = _sc(cx + ux * d, ss), _sc(cy + uy * d, ss)
        out.append([x - r, y - r, x + r, y + r])
    return out


def render_master(ss: int = SS) -> Image.Image:
    """Draw the whole logo once at BASE*ss and return it as RGBA."""
    n = BASE * ss

    # 1. tile: vertical gradient, cut out with a rounded-rect mask
    col = Image.new("RGBA", (1, n))
    col.putdata(
        [
            (
                round(TILE_TOP[0] + (TILE_BOTTOM[0] - TILE_TOP[0]) * y / (n - 1)),
                round(TILE_TOP[1] + (TILE_BOTTOM[1] - TILE_TOP[1]) * y / (n - 1)),
                round(TILE_TOP[2] + (TILE_BOTTOM[2] - TILE_TOP[2]) * y / (n - 1)),
                255,
            )
            for y in range(n)
        ]
    )
    tile = col.resize((n, n), Image.NEAREST)
    tile_mask = Image.new("L", (n, n), 0)
    ImageDraw.Draw(tile_mask).rounded_rectangle(
        _box(TILE_BOX, ss), radius=_sc(TILE_RADIUS, ss), fill=255
    )
    base = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    base.paste(tile, (0, 0), tile_mask)

    # 2. amber layer: rays then disc
    amber = Image.new("L", (n, n), 0)
    da = ImageDraw.Draw(amber)
    for deg in RAY_DEGREES:
        da.polygon(_ray_polygon(SUN_C[0], SUN_C[1], deg, ss), fill=255)
        for box in _caps(SUN_C[0], SUN_C[1], deg, ss):
            da.ellipse(box, fill=255)
    da.ellipse(
        [
            _sc(SUN_C[0] - SUN_R, ss),
            _sc(SUN_C[1] - SUN_R, ss),
            _sc(SUN_C[0] + SUN_R, ss),
            _sc(SUN_C[1] + SUN_R, ss),
        ],
        fill=255,
    )
    base.paste((*SUN, 255), (0, 0), amber)

    # 3. glass tint inside the pane opening
    glass = Image.new("L", (n, n), 0)
    ImageDraw.Draw(glass).rounded_rectangle(
        _box(FRAME_INNER, ss), radius=_sc(FRAME_INNER_R, ss), fill=GLASS_ALPHA
    )
    base.paste((255, 255, 255, 255), (0, 0), glass)

    # 4. white frame (outer rounded rect minus the pane opening) + mullions
    outer = Image.new("L", (n, n), 0)
    ImageDraw.Draw(outer).rounded_rectangle(
        _box(FRAME_OUTER, ss), radius=_sc(FRAME_OUTER_R, ss), fill=255
    )
    inner = Image.new("L", (n, n), 0)
    ImageDraw.Draw(inner).rounded_rectangle(
        _box(FRAME_INNER, ss), radius=_sc(FRAME_INNER_R, ss), fill=255
    )
    # composite takes image1 where the mask is 255 -> zeros where the opening is
    white = Image.composite(Image.new("L", (n, n), 0), outer, inner)
    dw = ImageDraw.Draw(white)
    dw.rectangle(_box(MULLION_V, ss), fill=255)
    dw.rectangle(_box(MULLION_H, ss), fill=255)
    base.paste((*WHITE, 255), (0, 0), white)

    return base


def save_png(master: Image.Image, path: str, size: int = BASE) -> Image.Image:
    img = master.resize((size, size), Image.LANCZOS)
    img.save(path, format="PNG")
    return img


# --- ICO writer (BMP frames <=128, PNG frame for 256) -----------------------------

def _bmp_frame(img: Image.Image) -> bytes:
    w, h = img.size
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, w * h * 4, 2835, 2835, 0, 0)
    px = img.load()
    rows = []
    for y in range(h - 1, -1, -1):          # bottom-up BGRA
        row = bytearray()
        for x in range(w):
            r, g, b, a = px[x, y]
            row += bytes((b, g, r, a))
        rows.append(bytes(row))
    mask_stride = ((w + 31) // 32) * 4
    return header + b"".join(rows) + bytes(mask_stride * h)


def _png_frame(img: Image.Image) -> bytes:
    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def save_ico(master: Image.Image, path: str) -> None:
    frames = []
    for size in ICO_SIZES_BMP:
        frames.append((size, _bmp_frame(master.resize((size, size), Image.LANCZOS))))
    big = master.resize((ICO_SIZE_PNG, ICO_SIZE_PNG), Image.LANCZOS)
    frames.append((ICO_SIZE_PNG, _png_frame(big)))

    offset = 6 + 16 * len(frames)
    directory = b""
    blobs = b""
    for size, data in frames:
        dim = 0 if size >= 256 else size
        directory += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
        blobs += data
    with open(path, "wb") as fp:
        fp.write(struct.pack("<HHH", 0, 1, len(frames)) + directory + blobs)


def save_preview(master: Image.Image, path: str) -> None:
    """Contact sheet: 256 / 48 / 16 px on light and dark backgrounds."""
    sheet = Image.new("RGBA", (760, 320), (238, 241, 245, 255))
    d = ImageDraw.Draw(sheet)
    sheet.alpha_composite(master.resize((256, 256), Image.LANCZOS), (20, 40))
    sheet.alpha_composite(master.resize((48, 48), Image.LANCZOS), (300, 60))
    sheet.alpha_composite(master.resize((16, 16), Image.LANCZOS), (300, 140))

    sheet.paste(Image.new("RGBA", (380, 320), (18, 22, 28, 255)), (380, 0))
    sheet.alpha_composite(master.resize((256, 256), Image.LANCZOS), (400, 40))
    sheet.alpha_composite(master.resize((48, 48), Image.LANCZOS), (680, 60))
    sheet.alpha_composite(master.resize((16, 16), Image.LANCZOS), (680, 140))

    d.text((20, 14), "FenCalc2 logo - 256 / 48 / 16 px, light + dark", fill=(17, 24, 39, 255))
    sheet.save(path, format="PNG")


def main() -> None:
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    assets = os.path.join(root, "Assets")
    os.makedirs(assets, exist_ok=True)

    master = render_master()
    save_png(master, os.path.join(assets, "FenCalc2.png"))
    save_ico(master, os.path.join(assets, "FenCalc2.ico"))

    preview = os.path.join(os.environ.get("TEMP", root), "fencalc2-logo-preview.png")
    save_preview(master, preview)
    print("wrote", os.path.join(assets, "FenCalc2.png"))
    print("wrote", os.path.join(assets, "FenCalc2.ico"))
    print("wrote", preview)


if __name__ == "__main__":
    main()
