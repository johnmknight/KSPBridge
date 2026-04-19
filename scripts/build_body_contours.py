"""
Build vector contour layers for stock-body globe assets.

For each body in BODY_PRESETS:
  1. Load the equirectangular source image.
  2. Build a grayscale luminance map.
  3. Trace marching-squares contours at one or more brightness levels
     (terminator-style thin layers per level).
  4. Convert pixel coords to (lon, lat), simplify, drop tiny specks.
  5. Write a JS file: window.<BODY>_CONTOUR_LAYERS = [{level, rings},..]
     The FDO console reads this and renders each layer as a coloured
     ring set on the body sphere.

Body-specific tuning lives in BODY_PRESETS only - one entry per body.
The pipeline itself is uniform.

Special bodies:
  - Jool (gas giant): no contour tracing. Synthetic horizontal cloud
    bands are emitted directly as ring polygons.

Run:
  python scripts/build_body_contours.py            # all bodies
  python scripts/build_body_contours.py --body eve # one body
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from skimage import measure
from shapely.geometry import Polygon

REPO_ROOT = Path(__file__).resolve().parent.parent
SRC_DIR = REPO_ROOT / "consoles" / "hard-scifi" / "source" / "bodies"
OUT_DIR = REPO_ROOT / "consoles" / "hard-scifi" / "data"

WORK_WIDTH = 2048   # plenty for small source images; trace at this width
SIMPLIFY_TOLERANCE_DEG = 0.4
MIN_AREA_SQ_DEG = 0.6  # drop polygons smaller than ~70 km^2 on Kerbin scale

# Per-body min-area override. For nearly-uniform bodies (asteroids,
# minty Minmus, smooth Eeloo) we want only the 2-3 strongest features
# to survive; aggressive area culling kills the noise specks the
# classifier picks up from compression artefacts and shading.
DEFAULT_MIN_AREA = 0.6
SMALL_BODY_MIN_AREA = 8.0


# Per-body tuning. `levels` is the list of luma thresholds (0..1) at
# which to trace contours. `palette` is the matching list of RGB hex
# colours to render each contour layer in (passed through to the JS
# output so the console can apply them without further config).
#
# `mode` defaults to 'contour' (the standard pipeline). 'jool' is a
# special path that skips image processing and emits horizontal bands.
# 'outline' is a single-contour quick mode for nearly-uniform bodies
# where one threshold captures the only visible feature.
BODY_PRESETS: dict[str, dict] = {
    # ---- Tier 2: small / uniform bodies ----------------------------
    # Tight min_area filters out speckle noise - for small moons we
    # only want the handful of actually-visible dark spots.
    "gilly":  {"src": "gilly.jpg",  "levels": [0.35, 0.50],
               "palette": [0x6e5e44, 0xa49278],
               "min_area": SMALL_BODY_MIN_AREA},
    "bop":    {"src": "bop.jpg",    "levels": [0.32, 0.48],
               "palette": [0x5e4a36, 0x8a7560],
               "min_area": SMALL_BODY_MIN_AREA},
    "pol":    {"src": "pol.jpg",    "levels": [0.45, 0.62],
               "palette": [0xa08848, 0xd6c690],
               "min_area": SMALL_BODY_MIN_AREA},
    "minmus": {"src": "minmus.png", "levels": [0.25, 0.45, 0.65, 0.82],
               "palette": [0x4a8a78, 0x7ab8a0, 0xa8d8c0, 0xe0f2e8],
               "min_area": 2.5,
               "missing_ok": True},
    "eeloo":  {"src": "eeloo.jpg",  "levels": [0.55, 0.72],
               "palette": [0xa8b0b8, 0xd8dce0],
               "min_area": SMALL_BODY_MIN_AREA},
    "dres":   {"src": "dres.png",   "levels": [0.40, 0.55, 0.70],
               "palette": [0x6b6660, 0x8c8678, 0xb0a892],
               "min_area": 2.5},

    # ---- Tier 1: rich-feature bodies -------------------------------
    "eve":    {"src": "eve.png",    "levels": [0.30, 0.42, 0.55, 0.70],
               "palette": [0x4a1d6a, 0x6b2d85, 0x9046a8, 0xb96fcf],
               "min_area": 1.5},  # tighter min_area so it's not 400KB
    "moho":   {"src": "moho_alt.jpg", "levels": [0.25, 0.40, 0.55],
               "palette": [0x5a3220, 0x7a4a32, 0xa07050]},
    "ike":    {"src": "ike.png",    "levels": [0.35, 0.50, 0.65, 0.80],
               "palette": [0x3e3c3a, 0x5c5a58, 0x7c7a78, 0x9c9a98],
               "min_area": 1.5},
    "tylo":   {"src": "tylo.jpg",   "levels": [0.40, 0.55, 0.70],
               "palette": [0x6e615a, 0x988878, 0xb8a898],
               "min_area": 1.5},
    "vall":   {"src": "vall.jpg",   "levels": [0.55, 0.72, 0.85],
               "palette": [0x4a6878, 0x7a98a8, 0xb0c8d8]},
    "laythe": {"src": "laythe.png", "levels": [0.25, 0.45, 0.65, 0.82],
               "palette": [0x184260, 0x2e5f8a, 0x4a8ab0, 0xa8c8d8],
               "min_area": 2.0,
               "missing_ok": True},

    # Kerbin - reuses the existing 16k-wide surface texture and runs
    # it through the same multi-contour pipeline as Duna. This
    # replaces the old single-coastline + ice-ring pair with a
    # full topographic-style stack, matching Duna's look.
    #
    # Source lives at source/kerbin_surface.jpg (not source/bodies/)
    # so we override with src_path to reach it.
    "kerbin": {"src_path": "source/kerbin_surface.jpg",
               "levels": [0.20, 0.32, 0.44, 0.55, 0.68, 0.88],
               # Palette sweeps ocean (deep navy) -> shallow coast
               # -> low forest -> mid plains -> highland olive ->
               # polar ice. Matches the game's own colour scheme.
               "palette": [0x0a1a2e, 0x1a3a5e, 0x2b6a8a,
                           0x2e5a2e, 0x6a7a42, 0xdce8f5],
               "min_area": 1.0},

    # ---- Special: gas giant ---------------------------------------
    "jool":   {"src": "jool.jpg",   "mode": "jool",
               # Bands defined as (lat_lo, lat_hi, colour) triples.
               # Mimics Jool's banded olive-green appearance.
               "bands": [
                   (-90, -75, 0x4a6a3a),
                   (-75, -55, 0x5a7a44),
                   (-55, -35, 0x6a8a4a),
                   (-35, -15, 0x82a052),
                   (-15,  +5, 0x6a8a4a),
                   (+5,  +25, 0x82a052),
                   (+25, +45, 0x6a8a4a),
                   (+45, +65, 0x5a7a44),
                   (+65, +90, 0x4a6a3a),
               ]},
}


def _luma(rgb: np.ndarray) -> np.ndarray:
    """Rec. 709 luminance, normalised to [0, 1]."""
    r = rgb[:, :, 0].astype(np.float32) / 255.0
    g = rgb[:, :, 1].astype(np.float32) / 255.0
    b = rgb[:, :, 2].astype(np.float32) / 255.0
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def _pixel_to_lonlat(coord_xy: np.ndarray, width: int, height: int) -> np.ndarray:
    """Equirectangular pixel (col, row) -> (lon, lat) in degrees."""
    out = np.empty_like(coord_xy, dtype=np.float64)
    out[:, 0] = -180.0 + (coord_xy[:, 0] / width) * 360.0
    out[:, 1] = 90.0 - (coord_xy[:, 1] / height) * 180.0
    return out


def _trace_polygons_at(luma: np.ndarray, level: float,
                       min_area: float = DEFAULT_MIN_AREA) -> list[Polygon]:
    """Marching-squares trace of the >= level region. Returns Polygons in
    lon/lat with simple Douglas-Peucker simplification + small-speck
    rejection.
    """
    h, w = luma.shape
    # Pad top/bottom with the OPPOSITE class so polar contours close
    # cleanly. Pad value is a luma below `level` so the boundary
    # passes between the pad and any polar-region pixel above level.
    pad_val = max(0.0, level - 0.05)
    padded = np.full((h + 2, w), pad_val, dtype=np.float32)
    padded[1:-1] = luma

    contours = measure.find_contours(padded, level)
    polys: list[Polygon] = []
    for contour in contours:
        if len(contour) < 4:
            continue
        # contour is (row, col); swap to (col, row) for geographic.
        # Subtract 1 from row to undo the top padding.
        xy = np.column_stack([contour[:, 1], contour[:, 0] - 1.0])
        # Clip rows back into [0, h-1] so the lat conversion stays sane.
        xy[:, 1] = np.clip(xy[:, 1], 0.0, h - 1.0)
        lonlat = _pixel_to_lonlat(xy, w, h)
        if not np.allclose(lonlat[0], lonlat[-1]):
            lonlat = np.vstack([lonlat, lonlat[:1]])
        try:
            poly = Polygon(lonlat)
        except Exception:
            continue
        if not poly.is_valid:
            poly = poly.buffer(0)
            if not poly.is_valid or poly.is_empty:
                continue
        polys.append(poly)

    # Simplify + drop specks.
    out = []
    for p in polys:
        s = p.simplify(SIMPLIFY_TOLERANCE_DEG, preserve_topology=False)
        if s.is_empty or s.area < min_area:
            continue
        if s.geom_type == "Polygon":
            out.append(s)
        elif s.geom_type == "MultiPolygon":
            for sub in s.geoms:
                if sub.area >= min_area:
                    out.append(sub)
    return out


def _polys_to_rings(polys: list[Polygon]) -> list[list[list[float]]]:
    """Turn list of Polygons into the ring-of-points format the
    console expects. Inner rings (holes) are kept as separate rings;
    the renderer treats them additively (not as holes) which is fine
    at this zoom level.
    """
    out = []
    for p in polys:
        if p.is_empty:
            continue
        for ring in [p.exterior] + list(p.interiors):
            xs, ys = ring.coords.xy
            pts = [[round(x, 4), round(y, 4)]
                   for x, y in zip(xs, ys)]
            if len(pts) >= 4:
                out.append(pts)
    return out


def _emit_js(name: str, layers: list[dict], out_path: Path) -> None:
    """Write the JS file with the body's contour layer array."""
    js_var = f"{name.upper()}_CONTOUR_LAYERS"
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with out_path.open("w", encoding="utf-8") as f:
        f.write(f"// Auto-generated by scripts/build_body_contours.py --body {name}\n")
        f.write(f"window.{js_var} = ")
        # Compact JSON keeps file sizes reasonable; the layer count is
        # small so readability isn't a concern here.
        json.dump(layers, f, separators=(",", ":"))
        f.write(";\n")


def process_contour_body(name: str, preset: dict) -> int:
    # `src_path` overrides the default "in source/bodies/" location -
    # used by Kerbin which lives one directory up at
    # source/kerbin_surface.jpg.
    if "src_path" in preset:
        src_path = REPO_ROOT / "consoles" / "hard-scifi" / preset["src_path"]
    else:
        src_path = SRC_DIR / preset["src"]
    if not src_path.exists():
        if preset.get("missing_ok"):
            print(f"  [{name}] SKIP: source missing ({src_path.name})")
            return 0
        print(f"  [{name}] ERROR: source missing ({src_path.name})", file=sys.stderr)
        return 1

    try:
        img = Image.open(src_path).convert("RGB")
    except Exception as e:
        if preset.get("missing_ok"):
            print(f"  [{name}] SKIP: {src_path.name} not a valid image ({e})")
            return 0
        print(f"  [{name}] ERROR opening {src_path.name}: {e}", file=sys.stderr)
        return 1

    # Resample to a uniform working resolution. Most source images are
    # 600-2000 wide; we standardise to WORK_WIDTH so simplify
    # tolerances behave consistently across bodies.
    w, h = img.size
    work_h = WORK_WIDTH // 2
    if (w, h) != (WORK_WIDTH, work_h):
        img = img.resize((WORK_WIDTH, work_h), Image.LANCZOS)
    rgb = np.asarray(img)
    luma = _luma(rgb)

    levels = preset["levels"]
    palette = preset["palette"]
    if len(palette) != len(levels):
        print(f"  [{name}] WARN: palette/levels length mismatch")

    min_area = preset.get("min_area", DEFAULT_MIN_AREA)

    layers = []
    for i, lvl in enumerate(levels):
        polys = _trace_polygons_at(luma, lvl, min_area=min_area)
        rings = _polys_to_rings(polys)
        color = palette[i] if i < len(palette) else palette[-1]
        print(f"  [{name}] level {lvl:.2f}: "
              f"{len(polys):3d} polys -> {len(rings):3d} rings  color=#{color:06x}")
        # Drop empty layers - no point bloating the output JS with
        # an entry that renders nothing.
        if not rings:
            continue
        layers.append({
            "level": round(lvl, 3),
            "color": color,
            "rings": rings,
        })

    if not layers:
        print(f"  [{name}] WARN: no layers produced; writing stub")
    

    out_path = OUT_DIR / f"{name}_contours.js"
    _emit_js(name, layers, out_path)
    size_kb = out_path.stat().st_size / 1024
    print(f"  [{name}] wrote {out_path.name} ({size_kb:.1f} KB)")
    return 0


def process_jool(name: str, preset: dict) -> int:
    """Synthetic horizontal cloud bands. Each band becomes a single
    rectangular polygon spanning the full longitude range at that
    latitude band. The console renders these as line strips on the
    sphere - same rendering path as the contour layers.
    """
    layers = []
    for (lat_lo, lat_hi, color) in preset["bands"]:
        # Subdivide the longitude axis so the great-circle path on
        # the sphere stays close to a parallel of latitude. Without
        # this, rendering the rectangle as a 4-vertex polygon would
        # bow into the centre of the sphere.
        n = 36  # 10-degree subdivisions
        bottom = [[-180 + i * 360 / n, lat_lo] for i in range(n + 1)]
        top = [[180 - i * 360 / n, lat_hi] for i in range(n + 1)]
        ring = bottom + top + [bottom[0]]
        layers.append({
            "level": round((lat_hi + lat_lo) / 180.0, 3),  # informational
            "color": color,
            "rings": [ring],
        })

    out_path = OUT_DIR / f"{name}_contours.js"
    _emit_js(name, layers, out_path)
    print(f"  [{name}] wrote {out_path.name} (jool bands)")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--body", default="all",
                        help="Body name to process, or 'all' (default)")
    args = parser.parse_args()

    targets = (
        list(BODY_PRESETS.keys())
        if args.body == "all"
        else [args.body]
    )

    if args.body != "all" and args.body not in BODY_PRESETS:
        print(f"unknown body: {args.body}", file=sys.stderr)
        print(f"available: {', '.join(BODY_PRESETS.keys())}", file=sys.stderr)
        return 2

    rc = 0
    for name in targets:
        preset = BODY_PRESETS[name]
        mode = preset.get("mode", "contour")
        print(f"--- {name} ({mode}) ---")
        if mode == "jool":
            rc |= process_jool(name, preset)
        else:
            rc |= process_contour_body(name, preset)
    return rc


if __name__ == "__main__":
    sys.exit(main())
