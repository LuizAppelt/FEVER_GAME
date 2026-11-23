import math
import os
import sys
import numpy as np

TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ASSETS = os.path.abspath(os.path.join(TOOLS_DIR, ".."))
TEX_DIR = os.path.join(PROJECT_ASSETS, "Textures")
PREVIEW_DIR = os.path.join(PROJECT_ASSETS, "Preview")
GLB_OUT = os.path.join(PROJECT_ASSETS, "Lamina_Ardente.glb")

sys.path.insert(0, TOOLS_DIR)
from glb_exporter import MeshBuilder, export_glb
from retro_textures import fbm_periodic, voronoi_periodic, apply_bayer_palette
from preview_render import load_glb, render_view


# ---------------------------------------------------------------------------
# 1. PS1 128x128 4x4 Bayer-Dithered Textures
# ---------------------------------------------------------------------------

def make_lamina_ardente_hilt_tex(path, size=128):
    """Deep visceral crimson-scarlet metallic/biological steel with dark blood-vein striations and edge highlights."""
    yy, xx = np.meshgrid(
        np.linspace(0.0, 1.0, size, endpoint=False, dtype=np.float32),
        np.linspace(0.0, 1.0, size, endpoint=False, dtype=np.float32),
        indexing="ij",
    )
    n_coarse = fbm_periodic(size, base_period=4, octaves=4, seed=7101)
    n_fine = fbm_periodic(size, base_period=10, octaves=3, seed=7102)
    wx = fbm_periodic(size, base_period=6, octaves=3, seed=7103) - 0.5

    # Forged biological-steel striations + brushed sheen
    striation = 0.5 + 0.5 * np.sin((xx * 14.0 + yy * 4.0 + wx * 2.2) * 2.0 * math.pi)
    micro_brush = 0.5 + 0.5 * np.sin((yy * 28.0 + n_fine * 2.0) * 2.0 * math.pi)

    # Dark clotted blood-vein fissures via warped Voronoi edges
    _, v_edge1 = voronoi_periodic(size, num_points=18, seed=7104)
    _, v_edge2 = voronoi_periodic(size, num_points=32, seed=7105)
    vein1 = np.clip(1.0 - (v_edge1 + wx * 0.025) * 26.0, 0.0, 1.0) ** 1.8
    vein2 = (np.clip(1.0 - (v_edge2 + (n_fine - 0.5) * 0.020) * 30.0, 0.0, 1.0) ** 2.0) * 0.65
    veins = np.clip(np.maximum(vein1, vein2), 0.0, 1.0)

    # Subtle sharp metallic edge catchlights near UV edges (u near 0 or 1)
    edge_dist = np.minimum(xx, 1.0 - xx)
    edge_hi = np.clip(1.0 - edge_dist / 0.16, 0.0, 1.0) ** 1.5

    val = np.clip(0.38 * striation + 0.34 * n_coarse + 0.16 * n_fine + 0.12 * micro_brush - veins * 0.48, 0.0, 1.0)
    spec = np.clip((val - 0.60) / 0.40, 0.0, 1.0) * 0.60 + edge_hi * 0.72

    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = 30.0 + val * 144.0 + spec * 66.0 - veins * 18.0
    rgb[:, :, 1] = 4.0 + val * 25.0 + spec * 48.0 - veins * 2.0
    rgb[:, :, 2] = 7.0 + val * 23.0 + spec * 38.0

    palette = [
        (22, 4, 7),      # deep coagulated vein shadow
        (44, 6, 10),     # dark blood-iron core
        (74, 10, 15),    # visceral maroon steel
        (108, 15, 20),   # mid crimson-steel
        (144, 23, 27),   # forged scarlet body
        (180, 36, 35),   # bright crimson ridge
        (212, 62, 50),   # sharp highlight edge
        (236, 104, 80),  # crisp specular rim
    ]
    img = apply_bayer_palette(rgb, palette, dither_strength=15.0)
    img.save(path)


def make_lamina_ardente_blade_tex(path, size=128):
    """
    Crimson blood-steel blade core:
    - U = 0.5 is dark crimson spine/fuller
    - U near 0.0 and 1.0 are glowing scarlet bevel edges
    - Crisp hairline hot orange-amber micro-fissures that ignite when _EmissionColor rises
    """
    yy, xx = np.meshgrid(
        np.linspace(0.0, 1.0, size, endpoint=False, dtype=np.float32),
        np.linspace(0.0, 1.0, size, endpoint=False, dtype=np.float32),
        indexing="ij",
    )
    n_coarse = fbm_periodic(size, base_period=4, octaves=4, seed=7201)
    n_fine = fbm_periodic(size, base_period=12, octaves=3, seed=7202)
    wx = fbm_periodic(size, base_period=6, octaves=3, seed=7203) - 0.5
    wy = fbm_periodic(size, base_period=8, octaves=2, seed=7206) - 0.5

    # Distance from spine (U=0.5) to bevel edges (U=0 or U=1)
    edge_dist = np.minimum(xx, 1.0 - xx)  # 0 at cutting edge, 0.5 at spine
    bevel = np.clip(1.0 - edge_dist / 0.21, 0.0, 1.0) ** 1.25
    spine_dark = np.clip(1.0 - np.abs(xx - 0.5) / 0.23, 0.0, 1.0) ** 1.2

    # Longitudinal brushed steel streaks along V
    brush = 0.5 + 0.5 * np.sin((yy * 46.0 + n_fine * 2.2) * 2.0 * math.pi)
    forged = np.clip(0.45 * n_coarse + 0.30 * brush + 0.25 * n_fine, 0.0, 1.0)

    # Crisp hairline hot orange-amber micro-fissures (narrow threshold avoids junction blobs)
    _, v_crack1 = voronoi_periodic(size, num_points=22, seed=7211)
    _, v_crack2 = voronoi_periodic(size, num_points=36, seed=7212)
    d1 = np.abs(v_crack1 + wx * 0.018 + wy * 0.012)
    d2 = np.abs(v_crack2 + (n_fine - 0.5) * 0.016)
    c1 = np.clip(1.0 - d1 * 62.0, 0.0, 1.0) ** 1.8
    c2 = (np.clip(1.0 - d2 * 68.0, 0.0, 1.0) ** 2.0) * 0.75
    fissure_mask = np.clip(np.maximum(c1, c2) * np.clip(0.40 + n_coarse * 0.75, 0.0, 1.0), 0.0, 1.0)
    fissure_hot = np.clip((fissure_mask - 0.36) / 0.64, 0.0, 1.0)

    # Base blood-steel color
    r = 62.0 + forged * 88.0 - spine_dark * 34.0 + bevel * 96.0
    g = 8.0 + forged * 16.0 - spine_dark * 4.0 + bevel * 28.0
    b = 11.0 + forged * 15.0 - spine_dark * 3.0 + bevel * 16.0

    # Hot orange-amber hairline micro-fissures
    r = r * (1.0 - fissure_hot) + (238.0 + fissure_hot * 16.0) * fissure_hot
    g = g * (1.0 - fissure_hot) + (104.0 + fissure_hot * 72.0) * fissure_hot
    b = b * (1.0 - fissure_hot) + (20.0 + fissure_hot * 28.0) * fissure_hot

    rgb = np.stack([r, g, b], axis=-1)
    palette = [
        (32, 5, 8),      # deep dark crimson spine
        (58, 8, 12),     # dark blood-steel core
        (96, 13, 18),    # mid crimson steel
        (138, 20, 22),   # rich arterial blade flat
        (182, 30, 26),   # glowing scarlet transition
        (222, 50, 30),   # bright scarlet bevel edge
        (242, 112, 24),  # hot orange micro-fissure
        (254, 178, 50),  # incandescent amber fissure spark
    ]
    img = apply_bayer_palette(rgb, palette, dither_strength=13.0)
    img.save(path)


def make_lamina_ardente_ember_tex(path, size=128):
    """Bright incandescent molten gold/orange-crimson magma vein texture for Ardente_Ember_Core."""
    yy, xx = np.meshgrid(
        np.linspace(0.0, 1.0, size, endpoint=False, dtype=np.float32),
        np.linspace(0.0, 1.0, size, endpoint=False, dtype=np.float32),
        indexing="ij",
    )
    n1 = fbm_periodic(size, base_period=5, octaves=4, seed=7301)
    n2 = fbm_periodic(size, base_period=12, octaves=3, seed=7302)
    f1, edge = voronoi_periodic(size, num_points=26, seed=7303)

    center_heat = np.clip(1.0 - np.abs(xx - 0.5) * 1.45, 0.0, 1.0) ** 0.85
    magma_cells = np.clip(1.0 - f1 * 3.2, 0.0, 1.0)
    vein_crust = np.clip(1.0 - edge * 18.0, 0.0, 1.0) ** 1.6

    heat = np.clip(0.50 * center_heat + 0.34 * n1 + 0.16 * n2 + 0.18 * magma_cells - vein_crust * 0.24, 0.0, 1.0)

    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = 168.0 + heat * 87.0
    rgb[:, :, 1] = 22.0 + (heat ** 1.35) * 218.0
    rgb[:, :, 2] = 8.0 + (heat ** 2.4) * 145.0

    palette = [
        (146, 20, 10),   # molten crimson rim
        (196, 48, 12),   # glowing red-orange magma
        (236, 94, 18),   # blazing orange magma
        (252, 148, 28),  # molten amber
        (255, 198, 62),  # incandescent gold
        (255, 238, 148), # white-gold magma core
    ]
    img = apply_bayer_palette(rgb, palette, dither_strength=14.0)
    img.save(path)


# ---------------------------------------------------------------------------
# 2. Geometry Helpers (Clean Winding, Faceted & Smooth Primitives)
# ---------------------------------------------------------------------------

def _norm(v):
    v = np.asarray(v, dtype=np.float32)
    n = float(np.linalg.norm(v))
    return v / n if n > 1e-8 else np.array([0.0, 1.0, 0.0], dtype=np.float32)


def add_oriented_quad(mb, p0, p1, p2, p3, uv0, uv1, uv2, uv3, c0, c1, c2, c3, outward_hint):
    a = np.asarray(p0, dtype=np.float32)
    b = np.asarray(p1, dtype=np.float32)
    c = np.asarray(p2, dtype=np.float32)
    n = np.cross(b - a, c - a)
    if float(np.dot(n, np.asarray(outward_hint, dtype=np.float32))) >= 0.0:
        mb.add_flat_quad(p0, p1, p2, p3, uv0, uv1, uv2, uv3, c0, c1, c2, c3)
    else:
        mb.add_flat_quad(p0, p3, p2, p1, uv0, uv3, uv2, uv1, c0, c3, c2, c1)


def add_oriented_tri(mb, p0, p1, p2, uv0, uv1, uv2, c0, c1, c2, outward_hint):
    a = np.asarray(p0, dtype=np.float32)
    b = np.asarray(p1, dtype=np.float32)
    c = np.asarray(p2, dtype=np.float32)
    n = np.cross(b - a, c - a)
    if float(np.dot(n, np.asarray(outward_hint, dtype=np.float32))) >= 0.0:
        mb.add_flat_triangle(p0, p1, p2, uv0, uv1, uv2, c0, c1, c2)
    else:
        mb.add_flat_triangle(p0, p2, p1, uv0, uv2, uv1, c0, c2, c1)


def compute_frames(centers):
    """Computes smooth twist-free (u_axis, v_axis) frames along a polyline of 3D points."""
    n = len(centers)
    pts = np.asarray(centers, dtype=np.float32)
    tangents = np.zeros((n, 3), dtype=np.float32)
    for i in range(n):
        if i == 0:
            tangents[i] = _norm(pts[1] - pts[0])
        elif i == n - 1:
            tangents[i] = _norm(pts[-1] - pts[-2])
        else:
            tangents[i] = _norm(pts[i + 1] - pts[i - 1])

    us = np.zeros((n, 3), dtype=np.float32)
    vs = np.zeros((n, 3), dtype=np.float32)
    t0 = tangents[0]
    ref = np.array([0.0, 0.0, 1.0], dtype=np.float32)
    if abs(float(np.dot(t0, ref))) > 0.92:
        ref = np.array([1.0, 0.0, 0.0], dtype=np.float32)
    u0 = _norm(np.cross(ref, t0))
    v0 = _norm(np.cross(t0, u0))
    us[0] = u0
    vs[0] = v0

    for i in range(1, n):
        tp = tangents[i - 1]
        tc = tangents[i]
        axis = np.cross(tp, tc)
        sin_a = float(np.linalg.norm(axis))
        if sin_a > 1e-6:
            axis /= sin_a
            cos_a = float(np.clip(np.dot(tp, tc), -1.0, 1.0))
            ang = math.atan2(sin_a, cos_a)
            up = us[i - 1]
            u_rot = (
                up * math.cos(ang)
                + np.cross(axis, up) * math.sin(ang)
                + axis * float(np.dot(axis, up)) * (1.0 - math.cos(ang))
            )
            uc = _norm(u_rot - tc * float(np.dot(u_rot, tc)))
        else:
            uc = _norm(us[i - 1] - tc * float(np.dot(us[i - 1], tc)))
        vc = _norm(np.cross(tc, uc))
        us[i] = uc
        vs[i] = vc
    return tangents, us, vs


def add_faceted_diamond_limb(mb, centers, rx_list, ry_list, colors, u_span=1.0, v_repeat=1.0,
                             cap_start=True, cap_end=True, sharp_tip=False):
    """
    Lofts a crisp 4-sided diamond prism along a 3D spine using flat faceted quads
    so edges catch razor-sharp PS1 highlights (ideal for thorns, ribs, and crossguard arms).
    """
    pts = np.asarray(centers, dtype=np.float32)
    n = len(pts)
    tangents, us, vs = compute_frames(pts)

    rings = []
    for i in range(n):
        c = pts[i]
        u_ax = us[i]
        v_ax = vs[i]
        rx = float(rx_list[i])
        ry = float(ry_list[i])
        ring = [
            (c + u_ax * rx).tolist(),
            (c + v_ax * ry).tolist(),
            (c - u_ax * rx).tolist(),
            (c - v_ax * ry).tolist(),
        ]
        rings.append(ring)

    last_ring_idx = n - 2 if sharp_tip else n - 1
    for i in range(last_ring_idx):
        v0 = (i / float(max(1, n - 1))) * v_repeat
        v1 = ((i + 1) / float(max(1, n - 1))) * v_repeat
        c0 = colors[i]
        c1 = colors[i + 1]
        for k in range(4):
            kn = (k + 1) % 4
            p00, p01 = rings[i][k], rings[i][kn]
            p11, p10 = rings[i + 1][kn], rings[i + 1][k]
            mid = 0.25 * (np.array(p00) + np.array(p01) + np.array(p11) + np.array(p10))
            mid_c = 0.5 * (pts[i] + pts[i + 1])
            out_dir = _norm(mid - mid_c)
            u_a = (0.04 if k == 0 else (0.50 if k == 1 else (0.96 if k == 2 else 0.50))) * u_span
            u_b = (0.50 if k == 0 else (0.96 if k == 1 else (0.50 if k == 2 else 0.04))) * u_span
            add_oriented_quad(
                mb, p00, p01, p11, p10,
                (u_a, v0), (u_b, v0), (u_b, v1), (u_a, v1),
                c0, c0, c1, c1, out_dir
            )

    if sharp_tip and n >= 2:
        tip = pts[-1].tolist()
        r_prev = rings[-2]
        v0 = ((n - 2) / float(max(1, n - 1))) * v_repeat
        c_prev, c_tip = colors[-2], colors[-1]
        for k in range(4):
            kn = (k + 1) % 4
            mid = (np.array(r_prev[k]) + np.array(r_prev[kn]) + pts[-1]) / 3.0
            out_dir = _norm(mid - pts[-2] + tangents[-1] * 0.25)
            add_oriented_tri(
                mb, r_prev[k], r_prev[kn], tip,
                (0.1, v0), (0.9, v0), (0.5, v_repeat),
                c_prev, c_prev, c_tip, out_dir
            )
    elif cap_end:
        c_end = pts[-1].tolist()
        r_end = rings[-1]
        for k in range(4):
            kn = (k + 1) % 4
            add_oriented_tri(
                mb, r_end[k], r_end[kn], c_end,
                (0.1, 1.0), (0.9, 1.0), (0.5, 1.0),
                colors[-1], colors[-1], colors[-1], tangents[-1]
            )

    if cap_start:
        c_start = pts[0].tolist()
        r_start = rings[0]
        for k in range(4):
            kn = (k + 1) % 4
            add_oriented_tri(
                mb, r_start[kn], r_start[k], c_start,
                (0.9, 0.0), (0.1, 0.0), (0.5, 0.0),
                colors[0], colors[0], colors[0], -tangents[0]
            )


def add_curved_rib_fin(mb, spine_pts, wy_list, wz_list, colors, u_span=1.0, v_repeat=1.0):
    """
    Lofts a flat-faceted curved anatomical rib/blade fin along `spine_pts` in the XY plane,
    using explicit XY normal (perp to tangent) for width `wy_list` and Z thickness `wz_list`.
    Creates broad, hooked rib blades matching the concept art silhouette cleanly.
    """
    pts = np.asarray(spine_pts, dtype=np.float32)
    n = len(pts)
    rings = []
    for i in range(n):
        if i == 0:
            tan = _norm(pts[1] - pts[0])
        elif i == n - 1:
            tan = _norm(pts[-1] - pts[-2])
        else:
            tan = _norm(pts[i + 1] - pts[i - 1])
        # XY perpendicular pointing mostly toward blade/guard
        perp_xy = _norm(np.array([-tan[1], tan[0], 0.0], dtype=np.float32))
        z_ax = np.array([0.0, 0.0, 1.0], dtype=np.float32)
        c = pts[i]
        wy = float(wy_list[i])
        wz = float(wz_list[i])
        ring = [
            (c + perp_xy * wy).tolist(),
            (c + z_ax * wz).tolist(),
            (c - perp_xy * wy).tolist(),
            (c - z_ax * wz).tolist(),
        ]
        rings.append(ring)

    for i in range(n - 2):
        v0 = (i / float(n - 1)) * v_repeat
        v1 = ((i + 1) / float(n - 1)) * v_repeat
        c0, c1 = colors[i], colors[i + 1]
        for k in range(4):
            kn = (k + 1) % 4
            p00, p01 = rings[i][k], rings[i][kn]
            p11, p10 = rings[i + 1][kn], rings[i + 1][k]
            mid = 0.25 * (np.array(p00) + np.array(p01) + np.array(p11) + np.array(p10))
            mid_c = 0.5 * (pts[i] + pts[i + 1])
            out_dir = _norm(mid - mid_c)
            u_a = (0.04 if k == 0 else (0.50 if k == 1 else (0.96 if k == 2 else 0.50))) * u_span
            u_b = (0.50 if k == 0 else (0.96 if k == 1 else (0.50 if k == 2 else 0.04))) * u_span
            add_oriented_quad(
                mb, p00, p01, p11, p10,
                (u_a, v0), (u_b, v0), (u_b, v1), (u_a, v1),
                c0, c0, c1, c1, out_dir
            )

    tip = pts[-1].tolist()
    r_prev = rings[-2]
    v0 = ((n - 2) / float(n - 1)) * v_repeat
    tan_end = _norm(pts[-1] - pts[-2])
    for k in range(4):
        kn = (k + 1) % 4
        mid = (np.array(r_prev[k]) + np.array(r_prev[kn]) + pts[-1]) / 3.0
        out_dir = _norm(mid - pts[-2] + tan_end * 0.25)
        add_oriented_tri(
            mb, r_prev[k], r_prev[kn], tip,
            (0.1, v0), (0.9, v0), (0.5, v_repeat),
            colors[-2], colors[-2], colors[-1], out_dir
        )

    c_start = pts[0].tolist()
    r_start = rings[0]
    tan_start = _norm(pts[1] - pts[0])
    for k in range(4):
        kn = (k + 1) % 4
        add_oriented_tri(
            mb, r_start[kn], r_start[k], c_start,
            (0.9, 0.0), (0.1, 0.0), (0.5, 0.0),
            colors[0], colors[0], colors[0], -tan_start
        )


def add_smooth_loft(mb, centers, sides, profile_fn, colors, v_coords=None,
                    u_repeat=1.0, cap_start=True, cap_end=True):
    pts = np.asarray(centers, dtype=np.float32)
    n = len(pts)
    tangents, us, vs = compute_frames(pts)
    if v_coords is None:
        v_coords = np.linspace(0.0, 1.0, n, dtype=np.float32)

    ring_indices = []
    for i in range(n):
        c = pts[i]
        u_ax = us[i]
        v_ax = vs[i]
        vc = float(v_coords[i])
        col = colors[i]
        ring = []
        for s in range(sides + 1):
            theta = (s % sides) * (2.0 * math.pi / sides)
            rx, ry = profile_fn(i, theta)
            pos = c + u_ax * (math.cos(theta) * rx) + v_ax * (math.sin(theta) * ry)
            uc = (s / float(sides)) * u_repeat
            ring.append(mb.add_vertex(pos, (0.0, 1.0, 0.0), (uc, vc), col))
        ring_indices.append(ring)

    for i in range(n - 1):
        r0 = ring_indices[i]
        r1 = ring_indices[i + 1]
        for s in range(sides):
            mb.add_tri_indices(r0[s], r0[s + 1], r1[s + 1])
            mb.add_tri_indices(r0[s], r1[s + 1], r1[s])

    if cap_start:
        c0 = mb.add_vertex(pts[0], -tangents[0], (0.5, float(v_coords[0])), colors[0])
        r0 = ring_indices[0]
        for s in range(sides):
            mb.add_tri_indices(c0, r0[s + 1], r0[s])

    if cap_end:
        c1 = mb.add_vertex(pts[-1], tangents[-1], (0.5, float(v_coords[-1])), colors[-1])
        r1 = ring_indices[-1]
        for s in range(sides):
            mb.add_tri_indices(c1, r1[s], r1[s + 1])


# ---------------------------------------------------------------------------
# 3. Blade Curve & Cross-Section Math (Shared between Blade and Ember Core)
# ---------------------------------------------------------------------------

BLADE_Y_START = 0.20
BLADE_Y_END = 2.15

# 4 serrated barbs on outer (+X) curved edge: (y_peak, extra_out, ramp_len, notch_drop_len)
BARB_SPECS = [
    (0.65, 0.040, 0.24, 0.022),  # forte shoulder wing barb
    (1.08, 0.032, 0.20, 0.020),  # mid-lower barb
    (1.48, 0.024, 0.18, 0.018),  # mid-upper barb
    (1.80, 0.017, 0.14, 0.015),  # distal barb
]


def blade_s(y):
    return float(np.clip((y - BLADE_Y_START) / (BLADE_Y_END - BLADE_Y_START), 0.0, 1.0))


def blade_spine_x(y):
    """Graceful curved sabre centerline: slight forte counter-curve, sweeping smoothly to x=-0.22 at tip."""
    s = blade_s(y)
    return 0.15 * s * ((1.0 - s) ** 1.35) - 0.22 * (s ** 3.45)


def blade_inner_x(y):
    """Inner (-X) razor-sharp concave cutting edge."""
    s = blade_s(y)
    half_in = 0.056 * ((1.0 - s) ** 2.6) + 0.024 * (1.0 - s) * (s ** 0.40)
    if s > 0.88:
        t_tip = (s - 0.88) / 0.12
        half_in *= (1.0 - t_tip)
    return blade_spine_x(y) - half_in


def blade_outer_base_x(y):
    """Base outer (+X) edge without serrated barb projections."""
    s = blade_s(y)
    half_out = 0.078 * ((1.0 - s) ** 0.62)
    if s > 0.90:
        t_tip = (s - 0.90) / 0.10
        half_out *= (1.0 - 0.80 * (t_tip ** 1.2))
    return blade_spine_x(y) + half_out


def blade_barb_offset(y):
    """Smooth ramp up to each barb apex, then sharp beveled drop into the notch."""
    extra = 0.0
    for y_pk, amp, r_len, n_len in BARB_SPECS:
        y_start = y_pk - r_len
        y_end = y_pk + n_len
        if y_start <= y <= y_pk:
            u = (y - y_start) / r_len
            extra = max(extra, amp * (u ** 1.65))
        elif y_pk < y <= y_end:
            u = (y - y_pk) / n_len
            extra = max(extra, amp * (1.0 - u))
    return extra


def blade_outer_x(y):
    return blade_outer_base_x(y) + blade_barb_offset(y)


def blade_ridge_x(y):
    """Central fuller/spine ridge slightly biased toward the cutting edge (~42% across)."""
    xin = blade_inner_x(y)
    xob = blade_outer_base_x(y)
    return xin + (xob - xin) * 0.42


def blade_ridge_z(y):
    """Blade half-thickness at central spine ridge."""
    s = blade_s(y)
    return 0.023 * (1.0 - 0.74 * (s ** 0.85))


def grip_base_radius(t):
    return 0.036 - 0.006 * math.sin(t * math.pi) + 0.005 * (t ** 2.2)


# ---------------------------------------------------------------------------
# 4. Node 1: Ardente_Hilt_Guard (Pommel, Twisted Grip, Crossguard, Rib-Cage)
# ---------------------------------------------------------------------------

def build_hilt_and_guard(mb):
    c_dark = (0.62, 0.48, 0.50, 1.0)
    c_mid = (0.88, 0.76, 0.76, 1.0)
    c_hi = (1.00, 0.94, 0.92, 1.0)

    # ------------------------------------------------------------------
    # A. Slender Twisted Crimson Grip (y = -0.34 .. +0.20)
    # ------------------------------------------------------------------
    n_grip = 28
    y_grip = np.linspace(-0.34, 0.20, n_grip, dtype=np.float32)
    centers_grip = np.column_stack([np.zeros(n_grip), y_grip, np.zeros(n_grip)]).astype(np.float32)
    cols_grip = []
    for i, yv in enumerate(y_grip):
        t = i / float(n_grip - 1)
        shade = 0.84 + 0.14 * math.sin(t * math.pi)
        cols_grip.append((shade, shade * 0.84, shade * 0.84, 1.0))

    def grip_profile(i, theta):
        t = i / float(n_grip - 1)
        base_r = grip_base_radius(t)
        twist = t * 2.1 * 2.0 * math.pi
        lobe = 1.0 - (0.5 - 0.5 * math.cos(3.0 * (theta - twist))) ** 1.9
        r = base_r * (0.90 + 0.18 * lobe)
        return r * 1.08, r * 0.86

    add_smooth_loft(
        mb, centers_grip, sides=18, profile_fn=grip_profile,
        colors=cols_grip, v_coords=np.linspace(0.0, 2.2, n_grip),
        u_repeat=1.5, cap_start=True, cap_end=True
    )

    # ------------------------------------------------------------------
    # B. Ringed Pommel Collar & Flame-Thorn Pommel (y = -0.525 .. -0.315)
    # ------------------------------------------------------------------
    collar_ys = [-0.314, -0.324, -0.334, -0.344, -0.354, -0.365, -0.376, -0.390]
    collar_rs = [0.035, 0.047, 0.027, 0.048, 0.026, 0.043, 0.033, 0.024]
    collar_pts = np.column_stack([np.zeros(len(collar_ys)), collar_ys, np.zeros(len(collar_ys))]).astype(np.float32)
    collar_cols = [(0.94, 0.82, 0.82, 1.0) if r > 0.04 else c_dark for r in collar_rs]
    add_smooth_loft(
        mb, collar_pts, sides=10,
        profile_fn=lambda i, th: (collar_rs[i] * 1.08, collar_rs[i] * 0.90),
        colors=collar_cols, v_coords=np.linspace(0.0, 0.8, len(collar_ys)),
        u_repeat=1.0, cap_start=True, cap_end=True
    )

    # Clustered sharp flame-thorns curving backward (-Y) and outward like a crown of thorns
    pommel_thorns = [
        (
            [(-0.002, -0.365, 0.0), (-0.014, -0.430, 0.002), (-0.024, -0.485, 0.002), (-0.014, -0.530, 0.0)],
            [0.024, 0.018, 0.011, 0.002], [0.018, 0.013, 0.008, 0.002],
        ),
        (
            [(0.010, -0.370, 0.003), (0.032, -0.418, 0.005), (0.056, -0.460, 0.004), (0.070, -0.495, 0.0)],
            [0.018, 0.014, 0.009, 0.002], [0.014, 0.010, 0.006, 0.002],
        ),
        (
            [(-0.010, -0.372, -0.003), (-0.034, -0.415, -0.005), (-0.056, -0.452, -0.004), (-0.066, -0.482, 0.0)],
            [0.017, 0.013, 0.009, 0.002], [0.013, 0.010, 0.006, 0.002],
        ),
        (
            [(0.002, -0.375, 0.012), (0.012, -0.418, 0.030), (0.008, -0.455, 0.042)],
            [0.013, 0.009, 0.002], [0.013, 0.009, 0.002],
        ),
        (
            [(-0.002, -0.375, -0.012), (-0.012, -0.418, -0.030), (-0.008, -0.455, -0.042)],
            [0.013, 0.009, 0.002], [0.013, 0.009, 0.002],
        ),
        (
            [(0.016, -0.362, 0.0), (0.038, -0.386, 0.002), (0.054, -0.416, 0.0)],
            [0.012, 0.008, 0.002], [0.010, 0.006, 0.002],
        ),
    ]
    for pts, rxs, rzs in pommel_thorns:
        cols = [c_dark] + [c_mid] * max(0, len(pts) - 2) + [c_hi]
        add_faceted_diamond_limb(mb, pts, rxs, rzs, cols, u_span=0.9, v_repeat=0.8, sharp_tip=True)

    # ------------------------------------------------------------------
    # C. Crossguard Hub & Slender Thorny Crossguard (y = +0.20, x = -0.46 .. +0.46)
    # ------------------------------------------------------------------
    hub_pts = [
        (0.000, 0.130, 0.0),
        (0.002, 0.175, 0.0),
        (0.004, 0.215, 0.0),
        (0.004, 0.265, 0.0),
    ]
    add_faceted_diamond_limb(
        mb, hub_pts,
        rx_list=[0.046, 0.068, 0.072, 0.056],
        ry_list=[0.036, 0.044, 0.044, 0.034],
        colors=[c_dark, c_mid, c_hi, c_mid],
        u_span=1.0, v_repeat=0.6, cap_start=True, cap_end=True, sharp_tip=False
    )

    for sx in (-1.0, 1.0):
        arm_pts = [
            (sx * 0.040, 0.204, 0.0),
            (sx * 0.145, 0.200, 0.0),
            (sx * 0.275, 0.205, 0.0),
            (sx * 0.340, 0.209, 0.0),  # subtle dorsal thorn shoulder
            (sx * 0.405, 0.198, 0.0),
            (sx * 0.460, 0.178, 0.0),  # sharp swept tip
        ]
        arm_ry = [0.032, 0.023, 0.021, 0.028, 0.017, 0.002]
        arm_rz = [0.030, 0.022, 0.018, 0.020, 0.014, 0.002]
        arm_cols = [c_dark, c_mid, c_mid, c_hi, c_hi, c_hi]
        add_faceted_diamond_limb(
            mb, arm_pts, arm_ry, arm_rz, arm_cols,
            u_span=1.0, v_repeat=1.2, sharp_tip=True
        )

        # Small sharp dorsal spur on each cross-arm
        spur_pts = [
            (sx * 0.285, 0.196, 0.0),
            (sx * 0.308, 0.176, 0.0),
            (sx * 0.324, 0.158, 0.0),
        ]
        add_faceted_diamond_limb(
            mb, spur_pts, [0.017, 0.010, 0.002], [0.014, 0.008, 0.002],
            [c_mid, c_hi, c_hi], u_span=0.8, v_repeat=0.5, sharp_tip=True
        )

    # ------------------------------------------------------------------
    # D. Asymmetric Ribbed Thorn-Cage (Signature Detail on -X Side)
    #    Curved sickle-like costal ribs arching out from the hub and hooking
    #    blade-ward (+Y) along the upper forte (matching concept art).
    # ------------------------------------------------------------------
    sternum_pts = [
        ( 0.040, 0.165, 0.0),
        ( 0.018, 0.215, 0.0),
        (-0.012, 0.275, 0.0),
        (-0.028, 0.355, 0.0),
    ]
    add_faceted_diamond_limb(
        mb, sternum_pts,
        rx_list=[0.044, 0.056, 0.052, 0.030],
        ry_list=[0.038, 0.046, 0.042, 0.028],
        colors=[c_mid, c_hi, c_mid, c_dark],
        u_span=1.0, v_repeat=0.8, sharp_tip=True
    )

    # 5 Curved Costal Rib-Spines: wrap across +Z/-Z forte collar and sweep OUT then HOOK blade-ward (+Y)
    # Notice: each spine arches outward horizontally/slightly guard-ward first, then curves into +Y!
    rib_specs = [
        # (y_right, y_center, [(x, y), ...], [wy...], [wz...], zw_arch)
        (
            0.220, 0.235,
            [(-0.035, 0.238, 0.0), (-0.155, 0.244, 0.0), (-0.255, 0.272, 0.0), (-0.315, 0.322, 0.0)],
            [0.032, 0.028, 0.017, 0.002], [0.024, 0.018, 0.011, 0.002], 0.040
        ),
        (
            0.255, 0.275,
            [(-0.030, 0.275, 0.0), (-0.165, 0.285, 0.0), (-0.280, 0.325, 0.0), (-0.345, 0.388, 0.0)],
            [0.028, 0.024, 0.015, 0.002], [0.022, 0.016, 0.010, 0.002], 0.039
        ),
        (
            0.290, 0.315,
            [(-0.022, 0.315, 0.0), (-0.135, 0.335, 0.0), (-0.225, 0.390, 0.0), (-0.275, 0.468, 0.0)],
            [0.026, 0.021, 0.013, 0.002], [0.020, 0.015, 0.009, 0.002], 0.037
        ),
        (
            0.325, 0.355,
            [(-0.012, 0.355, 0.0), (-0.088, 0.388, 0.0), (-0.142, 0.448, 0.0), (-0.172, 0.520, 0.0)],
            [0.023, 0.018, 0.011, 0.002], [0.018, 0.013, 0.008, 0.002], 0.035
        ),
        (
            0.350, 0.385,
            [( 0.006, 0.378, 0.0), (-0.026, 0.442, 0.0), (-0.046, 0.525, 0.0), (-0.054, 0.610, 0.0)],
            [0.019, 0.015, 0.010, 0.002], [0.016, 0.012, 0.007, 0.002], 0.032
        ),
    ]

    for yr, yc, spine_pts, wy_l, wz_l, zw in rib_specs:
        # Costal rib arches wrapping across +Z and -Z faces from +X forte edge to -X side
        for sz in (1.0, -1.0):
            arch_pts = [
                ( 0.088, yr,        sz * 0.016),
                ( 0.052, yr + 0.01, sz * zw),
                (-0.005, yc,        sz * (zw + 0.005)),
                (-0.052, yc + 0.02, sz * (zw * 0.72)),
                (-0.085, yc + 0.04, sz * 0.014),
            ]
            arch_r = [0.012, 0.019, 0.021, 0.018, 0.010]
            add_faceted_diamond_limb(
                mb, arch_pts, arch_r, [r * 0.72 for r in arch_r],
                [c_dark, c_mid, c_hi, c_mid, c_dark],
                u_span=0.9, v_repeat=0.9, sharp_tip=False
            )

        # Curved sickle rib blade projecting to -X and curving down/forward (+Y)
        add_curved_rib_fin(
            mb, spine_pts, wy_l, wz_l,
            [c_dark, c_mid, c_hi, c_hi],
            u_span=1.0, v_repeat=1.0
        )

    # Striated inner gill-flute ridges tucked inside the rib-cage pocket
    for g_idx in range(4):
        gx0 = -0.012 - g_idx * 0.012
        gy0 = 0.358 + g_idx * 0.018
        gx1 = -0.030 - g_idx * 0.016
        gy1 = 0.535 - g_idx * 0.024
        for sz in (1.0, -1.0):
            g_pts = [
                (gx0, gy0, sz * 0.024),
                (0.5 * (gx0 + gx1), 0.5 * (gy0 + gy1), sz * 0.025),
                (gx1, gy1, sz * 0.012),
            ]
            add_faceted_diamond_limb(
                mb, g_pts, [0.006, 0.007, 0.002], [0.005, 0.006, 0.002],
                [c_dark, c_mid, c_hi], u_span=0.6, v_repeat=0.5, sharp_tip=True
            )


# ---------------------------------------------------------------------------
# 5. Node 2: Ardente_Blade (Curved Beveled Crimson Sabre + 4 Serrated Barbs)
# ---------------------------------------------------------------------------

def build_ardente_blade(mb):
    c_edge = (1.00, 0.96, 0.94, 1.0)
    c_bev = (0.95, 0.85, 0.85, 1.0)
    c_spine = (0.68, 0.56, 0.58, 1.0)

    ys = set(np.round(np.linspace(BLADE_Y_START, BLADE_Y_END, 46), 4).tolist())
    for y_pk, _, r_len, n_len in BARB_SPECS:
        ys.add(round(y_pk - r_len, 4))
        ys.add(round(y_pk - r_len * 0.5, 4))
        ys.add(round(y_pk - 0.03, 4))
        ys.add(round(y_pk, 4))
        ys.add(round(y_pk + n_len * 0.45, 4))
        ys.add(round(y_pk + n_len, 4))
    y_list = sorted(ys)

    stations = []
    for y in y_list:
        s = blade_s(y)
        xin = blade_inner_x(y)
        xob = blade_outer_base_x(y)
        xout = blade_outer_x(y)
        xr = blade_ridge_x(y)

        xin_bev = xin + (xr - xin) * 0.44
        xout_bev = xob - (xob - xr) * 0.28

        zr = blade_ridge_z(y)
        zin_bev = zr * 0.62
        zout_bev = zr * 0.76
        z_edge = 0.0016 * (1.0 - 0.45 * s)

        stations.append({
            "y": y, "s": s,
            "xin": xin, "xin_bev": xin_bev, "xr": xr, "xout_bev": xout_bev, "xout": xout,
            "zr": zr, "zin_bev": zin_bev, "zout_bev": zout_bev, "z_edge": z_edge,
        })

    v_scale = 3.2

    for idx in range(len(stations) - 1):
        a = stations[idx]
        b = stations[idx + 1]
        y0, y1 = a["y"], b["y"]
        v0 = a["s"] * v_scale
        v1 = b["s"] * v_scale
        is_tip_seg = (idx == len(stations) - 2)

        u_cols = [0.02, 0.16, 0.50, 0.80, 0.98]
        col_list = [c_edge, c_bev, c_spine, c_bev, c_edge]

        if not is_tip_seg:
            for sz in (1.0, -1.0):
                pts_a = [
                    (a["xin"],      y0, sz * a["z_edge"]),
                    (a["xin_bev"],  y0, sz * a["zin_bev"]),
                    (a["xr"],       y0, sz * a["zr"]),
                    (a["xout_bev"], y0, sz * a["zout_bev"]),
                    (a["xout"],     y0, sz * a["z_edge"]),
                ]
                pts_b = [
                    (b["xin"],      y1, sz * b["z_edge"]),
                    (b["xin_bev"],  y1, sz * b["zin_bev"]),
                    (b["xr"],       y1, sz * b["zr"]),
                    (b["xout_bev"], y1, sz * b["zout_bev"]),
                    (b["xout"],     y1, sz * b["z_edge"]),
                ]
                for c_idx in range(4):
                    p00, p01 = pts_a[c_idx], pts_a[c_idx + 1]
                    p11, p10 = pts_b[c_idx + 1], pts_b[c_idx]
                    u0, u1 = u_cols[c_idx], u_cols[c_idx + 1]
                    cl0, cl1 = col_list[c_idx], col_list[c_idx + 1]
                    add_oriented_quad(
                        mb, p00, p01, p11, p10,
                        (u0, v0), (u1, v0), (u1, v1), (u0, v1),
                        cl0, cl1, cl1, cl0,
                        (0.0, 0.0, sz)
                    )

            add_oriented_quad(
                mb,
                (a["xin"], y0,  a["z_edge"]), (a["xin"], y0, -a["z_edge"]),
                (b["xin"], y1, -b["z_edge"]), (b["xin"], y1,  b["z_edge"]),
                (0.01, v0), (0.04, v0), (0.04, v1), (0.01, v1),
                c_edge, c_edge, c_edge, c_edge,
                (-1.0, 0.0, 0.0)
            )
            add_oriented_quad(
                mb,
                (a["xout"], y0, -a["z_edge"]), (a["xout"], y0,  a["z_edge"]),
                (b["xout"], y1,  b["z_edge"]), (b["xout"], y1, -b["z_edge"]),
                (0.96, v0), (0.99, v0), (0.99, v1), (0.96, v1),
                c_edge, c_edge, c_edge, c_edge,
                (1.0, 0.0, 0.0)
            )
        else:
            tip_pt = (blade_spine_x(BLADE_Y_END), BLADE_Y_END, 0.0)
            for sz in (1.0, -1.0):
                pts_a = [
                    (a["xin"],      y0, sz * a["z_edge"]),
                    (a["xin_bev"],  y0, sz * a["zin_bev"]),
                    (a["xr"],       y0, sz * a["zr"]),
                    (a["xout_bev"], y0, sz * a["zout_bev"]),
                    (a["xout"],     y0, sz * a["z_edge"]),
                ]
                for c_idx in range(4):
                    add_oriented_tri(
                        mb, pts_a[c_idx], pts_a[c_idx + 1], tip_pt,
                        (u_cols[c_idx], v0), (u_cols[c_idx + 1], v0), (0.50, v1),
                        col_list[c_idx], col_list[c_idx + 1], c_edge,
                        (0.0, 0.0, sz)
                    )

    s0 = stations[0]
    for c_idx in range(4):
        xs_a = [s0["xin"], s0["xin_bev"], s0["xr"], s0["xout_bev"], s0["xout"]]
        zs_a = [s0["z_edge"], s0["zin_bev"], s0["zr"], s0["zout_bev"], s0["z_edge"]]
        add_oriented_quad(
            mb,
            (xs_a[c_idx], BLADE_Y_START,  zs_a[c_idx]),
            (xs_a[c_idx + 1], BLADE_Y_START,  zs_a[c_idx + 1]),
            (xs_a[c_idx + 1], BLADE_Y_START, -zs_a[c_idx + 1]),
            (xs_a[c_idx], BLADE_Y_START, -zs_a[c_idx]),
            (0.2, 0.0), (0.8, 0.0), (0.8, 0.2), (0.2, 0.2),
            c_spine, c_spine, c_spine, c_spine,
            (0.0, -1.0, 0.0)
        )


# ---------------------------------------------------------------------------
# 6. Node 3: Ardente_Ember_Core (Inlaid Fiery Fissure Ridge + Guard/Grip Veins)
# ---------------------------------------------------------------------------

def _add_inlaid_surface_ribbon(mb, center_pts, half_widths, z_base_off, z_peak_off, sides=(1.0, -1.0), v_scale=2.0):
    """
    Creates a raised triangular-bevel glowing fissure ribbon embedded in a +/-Z surface
    with base skirts slightly inside the surface and crest proud by z_peak_off (zero Z-fighting).
    """
    n = len(center_pts)
    c_rim = (0.92, 0.68, 0.45, 1.0)
    c_core = (1.00, 0.98, 0.92, 1.0)
    for sz in sides:
        for i in range(n - 1):
            x0, y0, zb0 = center_pts[i]
            x1, y1, zb1 = center_pts[i + 1]
            hw0 = half_widths[i]
            hw1 = half_widths[i + 1]
            v0 = (i / float(max(1, n - 1))) * v_scale
            v1 = ((i + 1) / float(max(1, n - 1))) * v_scale

            z_skirt0 = sz * (zb0 + z_base_off)
            z_skirt1 = sz * (zb1 + z_base_off)
            z_crest0 = sz * (zb0 + z_peak_off)
            z_crest1 = sz * (zb1 + z_peak_off)

            add_oriented_quad(
                mb,
                (x0 - hw0, y0, z_skirt0), (x0, y0, z_crest0),
                (x1, y1, z_crest1), (x1 - hw1, y1, z_skirt1),
                (0.05, v0), (0.50, v0), (0.50, v1), (0.05, v1),
                c_rim, c_core, c_core, c_rim,
                (-0.4, 0.0, sz)
            )
            add_oriented_quad(
                mb,
                (x0, y0, z_crest0), (x0 + hw0, y0, z_skirt0),
                (x1 + hw1, y1, z_skirt1), (x1, y1, z_crest1),
                (0.50, v0), (0.95, v0), (0.95, v1), (0.50, v1),
                c_core, c_rim, c_rim, c_core,
                (0.4, 0.0, sz)
            )


def build_ember_core(mb):
    c_core = (1.00, 0.98, 0.92, 1.0)
    c_rim = (0.95, 0.72, 0.45, 1.0)

    # 1) Central jagged blade spine fissure (y = 0.24 .. 1.98)
    n_fiss = 42
    y_fiss = np.linspace(0.24, 1.98, n_fiss, dtype=np.float32)
    fiss_pts = []
    fiss_hw = []
    for i, y in enumerate(y_fiss):
        t = i / float(n_fiss - 1)
        xr = blade_ridge_x(y)
        zr = blade_ridge_z(y)
        jag = 0.0032 * math.sin(i * 2.45) + 0.0020 * math.cos(i * 4.3)
        pocket = 0.5 + 0.5 * math.sin(i * 1.45 + 0.4)
        w = (0.0030 + 0.0058 * pocket) * (1.0 - 0.68 * t)
        fiss_pts.append((xr + jag, float(y), zr))
        fiss_hw.append(max(0.0018, w))

    _add_inlaid_surface_ribbon(mb, fiss_pts, fiss_hw, z_base_off=-0.0010, z_peak_off=0.0040, v_scale=3.0)

    # 2) 4 Diagonal molten fissure branches angling from spine toward the serrated barbs
    for b_idx, (y_pk, amp, _, _) in enumerate(BARB_SPECS):
        y_start = y_pk - 0.13
        x_start = blade_ridge_x(y_start)
        z_start = blade_ridge_z(y_start)
        x_end = blade_outer_x(y_pk - 0.012) - 0.012
        y_end = y_pk - 0.015
        z_mid = blade_ridge_z(0.5 * (y_start + y_end)) * 0.78
        z_end = blade_ridge_z(y_end) * 0.34
        branch_pts = [
            (x_start, y_start, z_start),
            (0.5 * (x_start + x_end) + 0.004, 0.5 * (y_start + y_end) - 0.01, z_mid),
            (x_end, y_end, z_end),
        ]
        branch_hw = [0.0052 - b_idx * 0.0007, 0.0040 - b_idx * 0.0005, 0.0014]
        _add_inlaid_surface_ribbon(mb, branch_pts, branch_hw, z_base_off=-0.0005, z_peak_off=0.0038, v_scale=1.0)

    # 3) Rib-guard & crossguard glowing ember seams (proud of surface with zero clipping!)
    # Diagonal sternum fissure seam across crossguard hub
    sternum_vein = [
        ( 0.036, 0.175, 0.038),
        ( 0.014, 0.215, 0.044),
        (-0.012, 0.268, 0.041),
        (-0.025, 0.335, 0.026),
    ]
    _add_inlaid_surface_ribbon(mb, sternum_vein, [0.0055, 0.0070, 0.0055, 0.0020], z_base_off=-0.001, z_peak_off=0.0042, v_scale=1.2)

    # Crossguard arm glowing spine filaments (proper 3D diamond veins proud along +/-Z ridge of each arm)
    for sx in (-1.0, 1.0):
        for sz in (1.0, -1.0):
            arm_vein_pts = [
                (sx * 0.045, 0.204, sz * 0.029),
                (sx * 0.145, 0.200, sz * 0.023),
                (sx * 0.275, 0.205, sz * 0.020),
                (sx * 0.340, 0.209, sz * 0.021),
                (sx * 0.405, 0.198, sz * 0.015),
            ]
            add_faceted_diamond_limb(
                mb, arm_vein_pts,
                [0.0038, 0.0032, 0.0026, 0.0026, 0.0014],
                [0.0030, 0.0026, 0.0022, 0.0022, 0.0012],
                [c_core, c_core, c_core, c_rim, c_rim],
                u_span=0.8, v_repeat=1.0, sharp_tip=True
            )

    # Glowing core veins tracing the 4 main costal rib arches over the hub/forte (+Z/-Z faces)
    rib_arch_veins = [
        (0.220, 0.235, 0.040),
        (0.255, 0.275, 0.039),
        (0.290, 0.315, 0.037),
        (0.325, 0.355, 0.035),
    ]
    for yr, yc, zw in rib_arch_veins:
        for sz in (1.0, -1.0):
            vein_pts = [
                ( 0.052, yr + 0.010, sz * (zw + 0.016)),
                (-0.005, yc,         sz * (zw + 0.022)),
                (-0.052, yc + 0.020, sz * (zw * 0.72 + 0.015)),
                (-0.115, yc + 0.012, sz * 0.019),
            ]
            add_faceted_diamond_limb(
                mb, vein_pts,
                [0.0035, 0.0042, 0.0032, 0.0014],
                [0.0028, 0.0034, 0.0026, 0.0012],
                [c_rim, c_core, c_core, c_rim],
                u_span=0.8, v_repeat=1.0, sharp_tip=True
            )

    # 4) Smooth helical molten seam along the twisted grip groove (tracks exact grip ellipse + 0.003m!)
    n_hel = 36
    y_hel = np.linspace(-0.33, 0.18, n_hel, dtype=np.float32)
    hel_pts = []
    hel_rx = []
    for i, yv in enumerate(y_hel):
        t = (yv - (-0.34)) / (0.20 - (-0.34))
        base_r = grip_base_radius(t)
        twist = t * 2.1 * 2.0 * math.pi
        theta = twist + math.pi / 3.0  # sits right inside a helical groove (cos(3*(theta-twist)) = -1)
        lobe = 1.0 - (0.5 - 0.5 * math.cos(3.0 * (theta - twist))) ** 1.9  # = 0.0
        r_surf = base_r * (0.90 + 0.18 * lobe) + 0.0032
        hel_pts.append((math.cos(theta) * r_surf * 1.08, float(yv), math.sin(theta) * r_surf * 0.86))
        hel_rx.append(0.0030 if (1 < i < n_hel - 2) else 0.0015)
    add_faceted_diamond_limb(
        mb, hel_pts, hel_rx, hel_rx,
        [c_core if 2 <= i <= n_hel - 3 else c_rim for i in range(n_hel)],
        u_span=0.8, v_repeat=2.0, sharp_tip=False
    )

    # Pommel neck collar glow ring & subtle root ember seam
    ring_pts = [
        (math.cos(a) * 0.029 * 1.08, -0.354, math.sin(a) * 0.029 * 0.90)
        for a in np.linspace(0.0, 2.0 * math.pi, 13, dtype=np.float32)
    ]
    add_faceted_diamond_limb(
        mb, ring_pts, [0.0032] * len(ring_pts), [0.0032] * len(ring_pts),
        [c_core] * len(ring_pts), u_span=0.8, v_repeat=1.0, cap_start=False, cap_end=False
    )
    for sz in (1.0, -1.0):
        pom_vein = [
            (0.000, -0.365, sz * 0.018),
            (-0.008, -0.405, sz * 0.015),
            (-0.014, -0.445, sz * 0.011),
        ]
        add_faceted_diamond_limb(
            mb, pom_vein, [0.0030, 0.0024, 0.0012], [0.0025, 0.0020, 0.0010],
            [c_core, c_core, c_rim], u_span=0.8, v_repeat=0.8, sharp_tip=True
        )


# ---------------------------------------------------------------------------
# 7. Main Build, Export & Preview Render
# ---------------------------------------------------------------------------

def main():
    os.makedirs(TEX_DIR, exist_ok=True)
    os.makedirs(PREVIEW_DIR, exist_ok=True)

    tex_hilt = os.path.join(TEX_DIR, "tex_lamina_ardente_hilt.png")
    tex_blade = os.path.join(TEX_DIR, "tex_lamina_ardente_blade.png")
    tex_ember = os.path.join(TEX_DIR, "tex_lamina_ardente_ember.png")

    make_lamina_ardente_hilt_tex(tex_hilt)
    make_lamina_ardente_blade_tex(tex_blade)
    make_lamina_ardente_ember_tex(tex_ember)

    mb_hilt = MeshBuilder("Ardente_Hilt_Guard", "mat_ardente_hilt")
    build_hilt_and_guard(mb_hilt)
    mb_hilt.recompute_smooth_normals()

    mb_blade = MeshBuilder("Ardente_Blade", "mat_ardente_blade")
    build_ardente_blade(mb_blade)

    mb_ember = MeshBuilder("Ardente_Ember_Core", "mat_ardente_ember")
    build_ember_core(mb_ember)

    materials_spec = {
        "mat_ardente_hilt": {
            "image_path": tex_hilt,
            "roughness": 0.36,
            "metallic": 0.65,
            "double_sided": True,
        },
        "mat_ardente_blade": {
            "image_path": tex_blade,
            "roughness": 0.22,
            "metallic": 0.85,
            "emissive_factor": [0.08, 0.015, 0.01],
            "use_emissive_texture": True,
            "double_sided": True,
        },
        "mat_ardente_ember": {
            "image_path": tex_ember,
            "roughness": 0.18,
            "metallic": 0.10,
            "emissive_factor": [1.0, 0.52, 0.12],
            "use_emissive_texture": True,
            "double_sided": True,
        },
    }

    info = export_glb(GLB_OUT, [mb_hilt, mb_blade, mb_ember], materials_spec, root_name="Lamina_Ardente")
    print(f"Exported Lamina_Ardente.glb: {info}")

    gltf, meshes = load_glb(GLB_OUT)
    node_names = [n["name"] for n in gltf["nodes"]]
    print(f"GLB nodes ({len(node_names)}): {node_names}")
    for m in meshes:
        p = m["positions"]
        print(
            f"  Mesh '{m['name']}': verts={len(p)}, tris={len(m['indices'])}, "
            f"bounds_min=({p[:,0].min():.3f},{p[:,1].min():.3f},{p[:,2].min():.3f}), "
            f"bounds_max=({p[:,0].max():.3f},{p[:,1].max():.3f},{p[:,2].max():.3f})"
        )

    # Preview 1: Straight-on XY silhouette (+Z side looking toward -Z so -X is left and +X is right)
    im_front = render_view(
        meshes,
        cam_pos=(-0.05, 0.81, 3.88),
        target=(-0.05, 0.81, 0.0),
        width=540,
        height=840,
        fov_deg=42.0,
        light_dir=(0.40, 0.55, 0.72),
    )
    front_path = os.path.join(PREVIEW_DIR, "preview_lamina_ardente_front.png")
    im_front.save(front_path)
    print("Saved preview:", front_path)

    # Preview 2: 3/4 Isometric perspective view showing 3D bevels, rib-cage depth & ember ridge
    im_iso = render_view(
        meshes,
        cam_pos=(-2.15, 1.45, 3.45),
        target=(-0.05, 0.81, 0.0),
        width=640,
        height=840,
        fov_deg=44.0,
        light_dir=(0.50, 0.65, 0.58),
    )
    iso_path = os.path.join(PREVIEW_DIR, "preview_lamina_ardente_iso.png")
    im_iso.save(iso_path)
    print("Saved preview:", iso_path)


if __name__ == "__main__":
    main()
