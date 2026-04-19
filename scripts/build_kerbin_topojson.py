"""
Build a Kerbin coastline TopoJSON from an equirectangular surface map.

Pipeline
========
  1. Load the surface JPG (any resolution; 8K works fine).
  2. Downsample to a manageable working resolution. The final TopoJSON
     gets simplified by Douglas-Peucker anyway, so tracing at the
     native 16384x8192 just burns CPU for output that's
     indistinguishable from a 4096x2048 trace.
  3. Build a binary land-vs-water mask. Water is identified as
     dark + blue-dominant; everything else (including bright polar
     ice) is treated as land.
  4. Trace the land/water boundary via marching squares
     (skimage.measure.find_contours).
  5. Convert pixel (col, row) to (longitude, latitude) using the
     standard equirectangular mapping:
         lon = -180 + (col / W) * 360
         lat =  +90 - (row / H) * 180
  6. Build shapely polygons, simplify (Douglas-Peucker), drop
     specks smaller than the minimum area threshold.
  7. Wrap into a single MultiPolygon, dump as GeoJSON, then convert
     to TopoJSON via the `topojson` package (much smaller wire
     format and matches the existing land-110m.json structure used
     by the hard-scifi console).

Usage
=====
    python scripts/build_kerbin_topojson.py \\
        --input  consoles/hard-scifi/source/kerbin_surface.jpg \\
        --output consoles/hard-scifi/data/kerbin_land.topojson

All defaults match the repo layout, so plain
    python scripts/build_kerbin_topojson.py
also works.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from skimage import measure
from shapely.geometry import Polygon, MultiPolygon, mapping
import topojson

# ----------------------------------------------------------------------
# Defaults — paths are relative to repo root.
# ----------------------------------------------------------------------
REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_INPUT = REPO_ROOT / "consoles" / "hard-scifi" / "source" / "kerbin_surface.jpg"
DEFAULT_OUTPUT = REPO_ROOT / "consoles" / "hard-scifi" / "data" / "kerbin_land.topojson"

# Working resolution. 4096x2048 keeps coastline detail crisp at globe
# zoom and runs in ~10s. Drop to 2048x1024 for a faster iteration loop.
WORK_WIDTH = 4096
WORK_HEIGHT = 2048

# Douglas-Peucker tolerance, in degrees of lon/lat. 0.05 ~= 5.5 km on
# Kerbin (which has 600 km radius), small enough to keep the ragged
# coastline character but large enough to cut polygon point counts by
# 10-20x vs raw trace.
SIMPLIFY_TOLERANCE_DEG = 0.05

# Minimum polygon area (square degrees) to keep. Below this we drop the
# polygon as a "speck" that would just add noise. 0.01 sq.deg ~= 12 km²
# of coastline area, roughly a single-pixel artifact at our resolution.
MIN_AREA_SQ_DEG = 0.01

# Pixel classification: a pixel is "water" if it is darker than this
# luminance AND blue-dominant. Tuned empirically for the standard
# Kerbin surface render — adjust if your input has different colour
# grading.
WATER_LUMA_MAX = 0.42
WATER_BLUE_DOM_MIN = 1.05  # blue >= 1.05 * max(red, green)

# Ice classification: high luminance pixels that aren't water. Polar
# ice caps on Kerbin render as pale-bluish-white (~0.85 luma), well
# above any vegetation or desert pixel. 0.70 catches the full cap
# including the fade-to-ocean edges without pulling in glaciated
# mountain peaks (which sit around 0.55-0.65).
ICE_LUMA_MIN = 0.70


def _rgb_channels(rgb: np.ndarray):
    """Return r, g, b float arrays normalised to [0, 1]."""
    r = rgb[:, :, 0].astype(np.float32) / 255.0
    g = rgb[:, :, 1].astype(np.float32) / 255.0
    b = rgb[:, :, 2].astype(np.float32) / 255.0
    return r, g, b


def classify_water(rgb: np.ndarray) -> np.ndarray:
    """Return a boolean mask (H, W): True where pixel is water.

    Water is detected by the conjunction of two simple criteria:
      - luminance below WATER_LUMA_MAX (water in this map renders
        darker than any vegetation, desert, or ice);
      - the blue channel exceeds the brightest of red/green by at
        least WATER_BLUE_DOM_MIN (water is blue-dominant; ice is
        bright so it's already excluded by the luminance test).

    Both criteria fire together to avoid misclassifying dark mountain
    shadows (low luma but green/grey, not blue) as water.
    """
    r, g, b = _rgb_channels(rgb)
    # Rec. 709 luma. Good enough for a binary classifier.
    luma = 0.2126 * r + 0.7152 * g + 0.0722 * b
    # Avoid divide-by-zero where r=g=0 by clamping the denominator.
    rg_max = np.maximum(np.maximum(r, g), 1e-6)
    blue_dom = b / rg_max
    return (luma < WATER_LUMA_MAX) & (blue_dom >= WATER_BLUE_DOM_MIN)


def classify_ice(rgb: np.ndarray, water: np.ndarray) -> np.ndarray:
    """Return a boolean mask (H, W): True where pixel is polar ice / snow.

    Ice is everything bright enough to exceed ICE_LUMA_MIN that isn't
    already classified as water. On Kerbin this cleanly captures the
    two polar caps and leaves vegetation / mountain peaks / desert
    untouched.
    """
    r, g, b = _rgb_channels(rgb)
    luma = 0.2126 * r + 0.7152 * g + 0.0722 * b
    return (luma >= ICE_LUMA_MIN) & (~water)


def classify_mun_basin(rgb: np.ndarray) -> np.ndarray:
    """Return a boolean mask (H, W): True where the Mun height map is
    tinted blue (low-elevation basins / craters).

    The source is an elevation colormap going blue (0 m) → grey (mid)
    → dark (7 000 m). Basins are detected as pixels that are clearly
    bluer than red or green. The thresholds below are intentionally
    permissive to catch even pale pool edges that fade toward grey at
    the basin perimeter.
    """
    r, g, b = _rgb_channels(rgb)
    # Blue dominance: how much bluer than the warmer channels this
    # pixel is. Grey pixels give ~0; pure blue gives ~1.
    blue_above_red = b - r
    blue_above_green = b - g
    # Saturation-ish metric to exclude near-white pixels that happen
    # to have marginal blue bias from anti-aliasing.
    max_c = np.maximum(np.maximum(r, g), b)
    min_c = np.minimum(np.minimum(r, g), b)
    chroma = max_c - min_c
    return (blue_above_red > 0.04) & (blue_above_green > 0.04) & (chroma > 0.05)


def pixel_to_lonlat(coord_xy: np.ndarray, width: int, height: int) -> np.ndarray:
    """Convert (col, row) array to (lon, lat) array, equirectangular.

    skimage.measure.find_contours returns coordinates as (row, col)
    floats in image space. We swap to (col, row) before this call.
    Output is degrees: lon in [-180, 180], lat in [-90, 90].
    """
    out = np.empty_like(coord_xy, dtype=np.float64)
    out[:, 0] = -180.0 + (coord_xy[:, 0] / width) * 360.0
    out[:, 1] = 90.0 - (coord_xy[:, 1] / height) * 180.0
    return out


def trace_polygons(mask: np.ndarray) -> list[Polygon]:
    """Trace contours on the binary mask, return list of shapely Polygons.

    find_contours uses marching squares at threshold 0.5, producing
    sub-pixel coordinates of the land/water boundary. Contours that
    touch the image edge are returned open; we close them by
    appending the start point so they form valid polygons.
    """
    h, w = mask.shape
    # Cast to float for the threshold test. Values are 0.0 (water)
    # and 1.0 (land); we trace the 0.5 isocontour.
    contours = measure.find_contours(mask.astype(np.float32), 0.5)

    polygons: list[Polygon] = []
    for contour in contours:
        if len(contour) < 4:
            continue  # need at least 3 distinct points + closure

        # contour is (row, col); swap to (col, row) = (x, y) for
        # geographic convention.
        xy = contour[:, [1, 0]]
        lonlat = pixel_to_lonlat(xy, w, h)

        # Close the ring if open. A polygon ring must have first
        # point == last point.
        if not np.allclose(lonlat[0], lonlat[-1]):
            lonlat = np.vstack([lonlat, lonlat[:1]])

        try:
            poly = Polygon(lonlat)
        except Exception:
            continue
        if not poly.is_valid:
            poly = poly.buffer(0)  # standard self-intersection fix
            if not poly.is_valid or poly.is_empty:
                continue
        polygons.append(poly)

    return polygons


def _process_duna(rgb: np.ndarray, args) -> int:
    """Duna pipeline: classify the bright polar ice caps on top of the
    rust-red terrain, emit a two-colour mask PNG (red land + white
    ice) and a JS file defining window.DUNA_ICE_RINGS.

    Duna has no oceans, so there's no coastline layer — the entire
    surface is land, and ice is a distinct overlay class the same
    way Kerbin's polar caps are.
    """
    print("Classifying ice + multi-level topographic contours (Duna) ...")
    # Duna pipeline now traces topographic isocontours at multiple
    # elevation levels, like a topo map. Each contour level becomes
    # a separate ring layer rendered in a graded warm tone in the
    # console. Ice (polar caps) is still its own class on top.
    #
    # Contour levels are normalised luma 0..1 (the source is an
    # elevation-ish height map shaded grey-to-light). Tighter or
    # wider sets give more or less topographic detail.
    # No polar latitude gate — classifier relies entirely on
    # brightness + colour neutrality. Polar ice on Duna is bright
    # AND near-neutral (the source caps are ~RGB(218,208,204),
    # chroma ~0.055), while warm tan/red terrain always carries a
    # noticeable red bias (chroma > 0.18 even at high luma). This
    # lets the ice fill match the natural bright boundary in the
    # source map, including ice "islands" that extend below the
    # main cap, and avoids the hard latitude clip that cut them off.
    DUNA_ICE_LUMA = 0.70
    DUNA_ICE_CHROMA_MAX = 0.15
    DUNA_CONTOUR_LEVELS = (0.25, 0.32, 0.40, 0.48, 0.55, 0.62)

    r, g, b = _rgb_channels(rgb)
    luma = 0.2126 * r + 0.7152 * g + 0.0722 * b
    max_c = np.maximum(np.maximum(r, g), b)
    min_c = np.minimum(np.minimum(r, g), b)
    chroma = max_c - min_c

    # Two-way AND: bright AND near-neutral.
    ice = (luma > DUNA_ICE_LUMA) & (chroma < DUNA_ICE_CHROMA_MAX)
    print(f"  ice: {100.0 * ice.mean():.2f}%  (polar-constrained)")

    # Build one mask per contour level: pixel is in mask if luma >= level.
    # Tracing the boundary of each mask gives the isocontour at that
    # level. Levels are walked outermost-first so visual layering
    # rendered later places higher-elevation lines above lower ones.
    contour_masks = []
    for lvl in DUNA_CONTOUR_LEVELS:
        m = luma >= lvl
        contour_masks.append((lvl, m))
        print(f"  contour >={lvl:.2f}: {100.0 * m.mean():.2f}%")

    def _pad_and_trace(mask, label):
        # Pad top/bottom with whatever's on the adjacent row, so
        # features that touch the polar edge close into proper
        # polygons (same trick Kerbin's coastline trace uses).
        padded = np.zeros((mask.shape[0] + 2, mask.shape[1]), dtype=mask.dtype)
        padded[1:-1] = mask
        padded[0] = mask[0]
        padded[-1] = mask[-1]
        polys = trace_polygons(padded)
        print(f"  raw {label} polygons: {len(polys)}")
        return polys

    print("Tracing feature outlines ...")
    ice_polys = _pad_and_trace(ice, "ice")
    contour_polys_per_level = []
    for lvl, m in contour_masks:
        polys = _pad_and_trace(m, f"contour >={lvl:.2f}")
        contour_polys_per_level.append((lvl, polys))

    print(f"Simplifying (tol={args.simplify}°) and filtering specks (<{args.min_area} sq.deg) ...")
    def _simplify_filter(plist):
        out = []
        for p in plist:
            s = p.simplify(args.simplify, preserve_topology=True)
            if s.is_empty or not s.is_valid:
                continue
            geoms = list(s.geoms) if s.geom_type == "MultiPolygon" else [s]
            for g in geoms:
                if g.is_empty or not g.is_valid or g.geom_type != "Polygon":
                    continue
                if g.area < args.min_area:
                    continue
                out.append(g)
        return out

    ice_kept = _simplify_filter(ice_polys)
    print(f"  kept ice: {len(ice_kept)}")
    contour_kept_per_level = []
    for lvl, polys in contour_polys_per_level:
        kept = _simplify_filter(polys)
        contour_kept_per_level.append((lvl, kept))
        print(f"  kept contour >={lvl:.2f}: {len(kept)}")

    args.output.parent.mkdir(parents=True, exist_ok=True)

    if args.mask_png is not None:
        mw = args.mask_width
        mh = mw // 2
        # Duna-specific defaults. --ocean-rgb is reused as the land
        # base colour since Duna has no ocean; --ice-rgb is ice as
        # usual.
        land_rgb = (150, 65, 40)      # rusty red (#962d28)
        ice_rgb_d = (230, 235, 240)   # cool white
        try:
            land_rgb = tuple(int(x) for x in args.ocean_rgb.split(","))
            ice_rgb_d = tuple(int(x) for x in args.ice_rgb.split(","))
        except Exception:
            pass

        # Flat two-colour mask: rusty red land + white polar ice.
        # The vector ring layers (highland/lowland) carry the
        # terrain detail — keeping the fill flat preserves the
        # same visual hierarchy Kerbin uses (fill = biome class,
        # lines = topography).
        ice_small = np.asarray(
            Image.fromarray(ice.astype(np.uint8) * 255, mode="L")
                 .resize((mw, mh), Image.NEAREST)
        ) > 127
        out = np.empty((mh, mw, 3), dtype=np.uint8)
        out[:] = land_rgb
        out[ice_small] = ice_rgb_d
        args.mask_png.parent.mkdir(parents=True, exist_ok=True)
        Image.fromarray(out, mode="RGB").save(args.mask_png)
        print(f"Mask PNG written: {args.mask_png} ({args.mask_png.stat().st_size/1024:.1f} KB, {mw}x{mh})")

    if args.js_output is not None:
        def _polys_to_rings(polys):
            out = []
            for poly in polys:
                out.append([list(coord) for coord in poly.exterior.coords])
                for interior in poly.interiors:
                    out.append([list(coord) for coord in interior.coords])
            return out

        def _round_ring(ring):
            return [[round(c, 4) for c in pt] for pt in ring]

        ice_rings = [_round_ring(r) for r in _polys_to_rings(ice_kept)]

        # Each contour level is emitted as its own object inside an
        # array, with the level value (for downstream colour mapping)
        # and the rings. Console picks colours along a gradient to
        # render the topographic stack.
        contour_layers = []
        total_contour_rings = 0
        for lvl, kept in contour_kept_per_level:
            rings = [_round_ring(r) for r in _polys_to_rings(kept)]
            contour_layers.append({"level": round(lvl, 3), "rings": rings})
            total_contour_rings += len(rings)

        args.js_output.parent.mkdir(parents=True, exist_ok=True)
        args.js_output.write_text(
            "// Auto-generated by scripts/build_kerbin_topojson.py --body duna\n"
            "window.DUNA_ICE_RINGS = " + json.dumps(ice_rings, separators=(",", ":")) + ";\n"
            "window.DUNA_CONTOUR_LAYERS = " + json.dumps(contour_layers, separators=(",", ":")) + ";\n",
            encoding="utf-8")
        print(f"JS written: {args.js_output} ({args.js_output.stat().st_size/1024:.1f} KB, "
              f"{len(ice_rings)} ice, {total_contour_rings} contour rings across "
              f"{len(contour_layers)} levels)")

    return 0


def _process_mun(rgb: np.ndarray, args) -> int:
    """Mun pipeline: identify low-elevation basins from the height-map
    blue tint, emit a two-colour mask PNG (highland grey + basin
    blue-grey) and a JS file defining window.MUN_BASIN_RINGS.

    Mun has no oceans and no polar ice, so the output is intentionally
    simpler than Kerbin's: a single class of "feature region" rather
    than three. The HTML body registry treats a missing ice-rings
    field as "no ice layer" automatically.
    """
    print("Classifying highland/basin ...")
    basin = classify_mun_basin(rgb)
    print(f"  basin: {100.0 * basin.mean():.1f}% of pixels")

    # Pad top/bottom with non-basin so basins that touch the polar
    # edge close as proper polygons. Same trick as Kerbin's land trace.
    padded = np.zeros((basin.shape[0] + 2, basin.shape[1]), dtype=basin.dtype)
    padded[1:-1] = basin

    print("Tracing basin outlines ...")
    basin_polys = trace_polygons(padded)
    print(f"  raw basin polygons: {len(basin_polys)}")

    print(f"Simplifying (tol={args.simplify}°) and filtering specks (<{args.min_area} sq.deg) ...")
    basin_kept = []
    for p in basin_polys:
        s = p.simplify(args.simplify, preserve_topology=True)
        if s.is_empty or not s.is_valid:
            continue
        geoms = list(s.geoms) if s.geom_type == "MultiPolygon" else [s]
        for g in geoms:
            if g.is_empty or not g.is_valid or g.geom_type != "Polygon":
                continue
            if g.area < args.min_area:
                continue
            basin_kept.append(g)
    print(f"  kept basin polygons: {len(basin_kept)}")

    if not basin_kept:
        print("ERROR: no basin polygons survived filtering. Loosen --min-area "
              "or check the basin classifier thresholds.", file=sys.stderr)
        return 2

    # Mun has no notion of "topology" the way Kerbin's coastlines do,
    # so we skip the TopoJSON output for Mun. The mask PNG plus the
    # JS rings are everything the console needs.
    args.output.parent.mkdir(parents=True, exist_ok=True)

    if args.mask_png is not None:
        mw = args.mask_width
        mh = mw // 2
        # Mun-specific defaults: ocean/land args reused as
        # highland/basin colours since Mun has no actual ocean.
        # Highland = #1a1e25 dark cool grey, basin = #2d3d4a cool blue-grey.
        highland_rgb = (26, 30, 37)   # the "sea floor" of the FUI globe
        basin_rgb = (45, 61, 74)      # subtly lighter cool blue-grey

        # Override via --land-rgb / --ocean-rgb so users can retune.
        # (We map ocean-rgb → highland, land-rgb → basin: in Mun mode
        # "ocean" is the sphere base, "land" is the highlighted region.)
        try:
            highland_rgb = tuple(int(x) for x in args.ocean_rgb.split(","))
            basin_rgb = tuple(int(x) for x in args.land_rgb.split(","))
        except Exception:
            pass

        basin_small = np.asarray(
            Image.fromarray(basin.astype(np.uint8) * 255, mode="L")
                 .resize((mw, mh), Image.NEAREST)
        ) > 127

        out = np.empty((mh, mw, 3), dtype=np.uint8)
        out[:] = highland_rgb
        out[basin_small] = basin_rgb
        args.mask_png.parent.mkdir(parents=True, exist_ok=True)
        Image.fromarray(out, mode="RGB").save(args.mask_png)
        print(f"Mask PNG written: {args.mask_png} ({args.mask_png.stat().st_size/1024:.1f} KB, {mw}x{mh})")

    if args.js_output is not None:
        def _polys_to_rings(polys):
            out = []
            for poly in polys:
                out.append([list(coord) for coord in poly.exterior.coords])
                for interior in poly.interiors:
                    out.append([list(coord) for coord in interior.coords])
            return out

        def _round_ring(ring):
            return [[round(c, 4) for c in pt] for pt in ring]

        rings = [_round_ring(r) for r in _polys_to_rings(basin_kept)]
        args.js_output.parent.mkdir(parents=True, exist_ok=True)
        body_json = json.dumps(rings, separators=(",", ":"))
        args.js_output.write_text(
            "// Auto-generated by scripts/build_kerbin_topojson.py --body mun\n"
            "// Regenerate with: python scripts/build_kerbin_topojson.py --body mun --js-output ...\n"
            "window.MUN_BASIN_RINGS = " + body_json + ";\n",
            encoding="utf-8")
        print(f"JS written: {args.js_output} ({args.js_output.stat().st_size/1024:.1f} KB, {len(rings)} basin rings)")

    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, default=DEFAULT_INPUT,
                        help=f"Source equirectangular image (default: {DEFAULT_INPUT})")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT,
                        help=f"Output TopoJSON path (default: {DEFAULT_OUTPUT})")
    parser.add_argument("--work-width", type=int, default=WORK_WIDTH,
                        help=f"Trace at this width, height = width/2 (default: {WORK_WIDTH})")
    parser.add_argument("--simplify", type=float, default=SIMPLIFY_TOLERANCE_DEG,
                        help=f"Douglas-Peucker tolerance in degrees (default: {SIMPLIFY_TOLERANCE_DEG})")
    parser.add_argument("--min-area", type=float, default=MIN_AREA_SQ_DEG,
                        help=f"Drop polygons smaller than this (sq.deg) (default: {MIN_AREA_SQ_DEG})")
    parser.add_argument("--also-geojson", action="store_true",
                        help="Also write a sibling .geojson file alongside the TopoJSON")
    parser.add_argument("--js-output", type=Path, default=None,
                        help="Also write a JS file defining window.KERBIN_COASTLINE_RINGS "
                             "(matches the inline const format the hard-scifi console expects). "
                             "Pass the target path, e.g. consoles/hard-scifi/data/kerbin_coastline.js")
    parser.add_argument("--mask-png", type=Path, default=None,
                        help="Also write a two-colour land/ocean PNG mask suitable for use "
                             "as a sphere texture (equirectangular UVs). Pass the target path.")
    parser.add_argument("--mask-width", type=int, default=2048,
                        help="Width of the mask PNG (height = width / 2). Default 2048.")
    parser.add_argument("--ocean-rgb", type=str, default="2,6,22",
                        help="Ocean colour as r,g,b (0-255 decimal). Default matches the "
                             "hard-scifi console's core sphere colour.")
    parser.add_argument("--land-rgb", type=str, default="13,34,68",
                        help="Land colour as r,g,b. Default a couple of stops lighter than ocean.")
    parser.add_argument("--ice-rgb", type=str, default="220,235,245",
                        help="Polar ice colour as r,g,b. Default a cold bluish-white.")
    parser.add_argument("--body", type=str, default="kerbin",
                        choices=("kerbin", "mun", "duna"),
                        help="Which celestial body the source image is. Picks the classifier "
                             "used: kerbin uses water/land/ice, mun uses highland/basin, "
                             "duna uses land/ice (no water, Mars-like).")
    args = parser.parse_args()

    if not args.input.exists():
        print(f"ERROR: input not found: {args.input}", file=sys.stderr)
        return 1

    args.output.parent.mkdir(parents=True, exist_ok=True)

    print(f"Loading {args.input} ...")
    img = Image.open(args.input).convert("RGB")
    print(f"  source: {img.width} x {img.height}")

    work_h = args.work_width // 2
    if (img.width, img.height) != (args.work_width, work_h):
        print(f"  resampling to {args.work_width} x {work_h} (Lanczos)")
        img = img.resize((args.work_width, work_h), Image.LANCZOS)

    rgb = np.asarray(img)

    # Branch on body. Each non-Kerbin body has its own subroutine
    # because the classification semantics differ materially:
    #   mun  — highland vs basin (from height-map tinting)
    #   duna — land vs polar ice  (no water anywhere)
    # Kerbin is the original water/land/ice flow inline below.
    if args.body == "mun":
        return _process_mun(rgb, args)
    if args.body == "duna":
        return _process_duna(rgb, args)

    print("Classifying land/water/ice ...")
    water = classify_water(rgb)
    ice = classify_ice(rgb, water)
    land = ~water  # land includes the ice caps
    print(f"  water: {100.0 * water.mean():.1f}%, "
          f"land (incl. ice): {100.0 * land.mean():.1f}%, "
          f"ice: {100.0 * ice.mean():.1f}%")

    # Pad the mask top and bottom with water so contours that touch
    # the polar edges close cleanly. Without this, polar landmasses
    # leave open contours that can't form valid polygons.
    padded = np.zeros((land.shape[0] + 2, land.shape[1]), dtype=land.dtype)
    padded[1:-1] = land

    print("Tracing coastline ...")
    polys = trace_polygons(padded)
    print(f"  raw land polygons: {len(polys)}")

    # Trace ice the same way. Ice that touches the polar edge of the
    # image needs the contour to close over the pole — we pad the top
    # and bottom rows with ice so any cap that reaches the edge wraps
    # cleanly into a closed polygon.
    ice_padded = np.zeros((ice.shape[0] + 2, ice.shape[1]), dtype=ice.dtype)
    ice_padded[1:-1] = ice
    # Carry ice into the polar pad rows wherever the original edge row
    # is itself ice — this closes polar caps without introducing fake
    # ice elsewhere.
    ice_padded[0] = ice[0]
    ice_padded[-1] = ice[-1]

    print("Tracing ice ...")
    ice_polys = trace_polygons(ice_padded)
    print(f"  raw ice polygons: {len(ice_polys)}")

    # Account for the 1-row pad we added above when converting back
    # to lat/lat. The pad shifts row indices by +1, which after
    # pixel_to_lonlat shifts latitudes south by ~0.09° at our
    # resolution. Negligible visually but easy to fix: simply do
    # the trace on the padded image then discard the pad. Since
    # find_contours returned sub-pixel float coords, the simplest
    # correction is to project assuming the original (unpadded) height
    # — pixel_to_lonlat above already used `padded.shape` via h/w from
    # the array, so the pad rows mapped to the polar caps. That's
    # exactly what we want for closure; no further fix needed.

    # Simplify and area-filter both polygon sets the same way.
    # simplify(preserve_topology=True) can split a concave polygon
    # into a MultiPolygon when the tolerance cuts a thin isthmus —
    # we unpack those so the downstream code always sees plain
    # Polygons.
    def _simplify_filter(plist):
        out = []
        for p in plist:
            s = p.simplify(args.simplify, preserve_topology=True)
            if s.is_empty or not s.is_valid:
                continue
            geoms = list(s.geoms) if s.geom_type == "MultiPolygon" else [s]
            for g in geoms:
                if g.is_empty or not g.is_valid:
                    continue
                if g.geom_type != "Polygon":
                    continue
                if g.area < args.min_area:
                    continue
                out.append(g)
        return out

    print(f"Simplifying (tol={args.simplify}°) and filtering specks (<{args.min_area} sq.deg) ...")
    kept = _simplify_filter(polys)
    ice_kept = _simplify_filter(ice_polys)
    print(f"  kept land polygons: {len(kept)}")
    print(f"  kept ice polygons:  {len(ice_kept)}")

    if not kept:
        print("ERROR: no land polygons survived filtering. Loosen --min-area "
              "or check the water classifier.", file=sys.stderr)
        return 2

    multi = MultiPolygon(kept)
    feature = {
        "type": "Feature",
        "properties": {"name": "Kerbin Land"},
        "geometry": mapping(multi),
    }
    fc = {"type": "FeatureCollection", "features": [feature]}

    if args.also_geojson:
        geojson_path = args.output.with_suffix(".geojson")
        geojson_path.write_text(json.dumps(fc), encoding="utf-8")
        print(f"GeoJSON written: {geojson_path} ({geojson_path.stat().st_size/1024:.1f} KB)")

    print("Converting to TopoJSON ...")
    topo = topojson.Topology(fc, prequantize=False)
    args.output.write_text(topo.to_json(), encoding="utf-8")
    print(f"TopoJSON written: {args.output} ({args.output.stat().st_size/1024:.1f} KB)")

    # Optional: emit a two-colour land/ocean mask PNG for use as a
    # sphere texture in the console. Sampled from the working-resolution
    # mask we already built (the `water` array), downscaled to
    # --mask-width. SphereGeometry in Three.js uses equirectangular
    # UVs by default, so this image just works as a diffuse map.
    if args.mask_png is not None:
        mw = args.mask_width
        mh = mw // 2
        ocean_rgb = tuple(int(x) for x in args.ocean_rgb.split(","))
        land_rgb = tuple(int(x) for x in args.land_rgb.split(","))
        ice_rgb = tuple(int(x) for x in args.ice_rgb.split(","))
        if any(len(c) != 3 for c in (ocean_rgb, land_rgb, ice_rgb)):
            print("ERROR: --ocean-rgb, --land-rgb, and --ice-rgb each need three values",
                  file=sys.stderr)
            return 3

        # Resize each mask independently using nearest-neighbour so
        # the colour boundaries stay sharp on the sphere texture.
        def _resize_mask(mask):
            return np.asarray(
                Image.fromarray(mask.astype(np.uint8) * 255, mode="L")
                     .resize((mw, mh), Image.NEAREST)
            ) > 127

        water_small = _resize_mask(water)
        ice_small = _resize_mask(ice)
        # Land is "not water"; ice is a subset of land that we paint
        # with its own colour, so apply colours in this order:
        #   1. start ocean, 2. paint land, 3. paint ice on top.
        out = np.empty((mh, mw, 3), dtype=np.uint8)
        out[:] = ocean_rgb
        out[~water_small] = land_rgb
        out[ice_small] = ice_rgb
        args.mask_png.parent.mkdir(parents=True, exist_ok=True)
        Image.fromarray(out, mode="RGB").save(args.mask_png)
        print(f"Mask PNG written: {args.mask_png} ({args.mask_png.stat().st_size/1024:.1f} KB, {mw}x{mh})")

    if args.js_output is not None:
        # Emit a JS file with two globals:
        #   window.KERBIN_COASTLINE_RINGS  — land/water boundaries
        #   window.KERBIN_ICE_RINGS        — polar ice / snow boundaries
        # Both are arrays of rings (each ring an array of [lon, lat]
        # pairs), matching the inline COASTLINE_RINGS const format.
        # Polygons with holes get flattened — exterior + interior
        # rings both rendered as lines, same as the existing pattern.
        def _polys_to_rings(polys):
            out = []
            for poly in polys:
                out.append([list(coord) for coord in poly.exterior.coords])
                for interior in poly.interiors:
                    out.append([list(coord) for coord in interior.coords])
            return out

        # Round to 4 decimal places (~11m at Kerbin radius) to keep
        # the file size reasonable. Console renders to ~1px accuracy
        # on a globe view anyway.
        def _round_ring(ring):
            return [[round(c, 4) for c in pt] for pt in ring]

        coast_rings = [_round_ring(r) for r in _polys_to_rings(kept)]
        ice_rings = [_round_ring(r) for r in _polys_to_rings(ice_kept)]

        args.js_output.parent.mkdir(parents=True, exist_ok=True)
        coast_body = json.dumps(coast_rings, separators=(",", ":"))
        ice_body = json.dumps(ice_rings, separators=(",", ":"))
        args.js_output.write_text(
            "// Auto-generated by scripts/build_kerbin_topojson.py — do not hand-edit.\n"
            "// Regenerate with: python scripts/build_kerbin_topojson.py --js-output ...\n"
            "window.KERBIN_COASTLINE_RINGS = " + coast_body + ";\n"
            "window.KERBIN_ICE_RINGS = " + ice_body + ";\n",
            encoding="utf-8")
        print(f"JS written: {args.js_output} ({args.js_output.stat().st_size/1024:.1f} KB, "
              f"{len(coast_rings)} land rings, {len(ice_rings)} ice rings)")

    return 0


if __name__ == "__main__":
    sys.exit(main())
