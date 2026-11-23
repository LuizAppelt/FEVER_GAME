import math
import os
import sys
import numpy as np

# Ensure PilotChamber tools (glb_exporter, preview_render) can be imported cleanly
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
STOMACH_ABYSS_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, ".."))
PROJECT_ASSETS_ROOT = os.path.abspath(os.path.join(STOMACH_ABYSS_ROOT, ".."))
PILOT_TOOLS_DIR = os.path.join(PROJECT_ASSETS_ROOT, "PilotChamber", "Tools")
PILOT_TEX_DIR = os.path.join(PROJECT_ASSETS_ROOT, "PilotChamber", "Textures")
ABYSS_TEX_DIR = os.path.join(STOMACH_ABYSS_ROOT, "Textures")
MODELS_OUT_DIR = os.path.join(STOMACH_ABYSS_ROOT, "Models")
PREVIEW_OUT_DIR = os.path.join(STOMACH_ABYSS_ROOT, "Preview")

if PILOT_TOOLS_DIR not in sys.path:
    sys.path.insert(0, PILOT_TOOLS_DIR)

from glb_exporter import MeshBuilder, export_glb  # noqa: E402
from preview_render import load_glb, render_view  # noqa: E402


# ==============================================================================
# 1. TEXTURE RESOLUTION & MATERIAL DEFINITIONS
# ==============================================================================

def resolve_tex(tex_filename):
    """
    Checks StomachAbyss/Textures/<name>.png (and StomachAbyss aliases) first,
    then PilotChamber/Textures/<name>.png (or known equivalents),
    and falls back cleanly to PilotChamber/Textures/tex_flesh_floor.png.
    Unity's C# setup script rebinds final materials by material_name.
    """
    abyss_aliases = {
        "tex_vein_tendon.png": "tex_vein_tendon_bridge.png",
    }
    for cand in (tex_filename, abyss_aliases.get(tex_filename)):
        if cand:
            abyss_path = os.path.join(ABYSS_TEX_DIR, cand)
            if os.path.exists(abyss_path):
                return abyss_path
    pilot_aliases = {
        "tex_gastric_acid.png": "tex_acid.png",
    }
    pilot_name = pilot_aliases.get(tex_filename, tex_filename)
    pilot_path = os.path.join(PILOT_TEX_DIR, pilot_name)
    if os.path.exists(pilot_path):
        return pilot_path
    return os.path.join(PILOT_TEX_DIR, "tex_flesh_floor.png")


def build_materials_spec(used_names):
    all_specs = {
        "Mat_FleshFloor": {
            "image_path": resolve_tex("tex_flesh_floor.png"),
            "roughness": 0.80,
            "metallic": 0.0,
            "double_sided": False,
        },
        "Mat_FleshWall": {
            "image_path": resolve_tex("tex_flesh_wall.png"),
            "roughness": 0.78,
            "metallic": 0.0,
            "double_sided": False,
        },
        "Mat_DigestedCalcified": {
            "image_path": resolve_tex("tex_digested_calcified.png"),
            "roughness": 0.74,
            "metallic": 0.0,
            "double_sided": False,
        },
        "Mat_Cartilage": {
            "image_path": resolve_tex("tex_cartilage.png"),
            "roughness": 0.66,
            "metallic": 0.0,
            "double_sided": False,
        },
        "Mat_OssifiedStone": {
            "image_path": resolve_tex("tex_ossified_stone.png"),
            "roughness": 0.85,
            "metallic": 0.0,
            "double_sided": False,
        },
        "Mat_VeinTendon": {
            "image_path": resolve_tex("tex_vein_tendon.png"),
            "roughness": 0.52,
            "metallic": 0.0,
            "double_sided": False,
        },
        "Mat_Teeth": {
            "image_path": resolve_tex("tex_teeth.png"),
            "roughness": 0.44,
            "metallic": 0.0,
            "double_sided": False,
        },
        "Mat_GastricAcid": {
            "image_path": resolve_tex("tex_gastric_acid.png"),
            "roughness": 0.24,
            "metallic": 0.0,
            "emissive_factor": [0.65, 1.0, 0.26],
            "use_emissive_texture": True,
            "double_sided": False,
        },
        "Mat_PortalVortex": {
            "image_path": resolve_tex("tex_portal_vortex.png"),
            "roughness": 0.20,
            "metallic": 0.0,
            "emissive_factor": [0.55, 1.0, 0.40],
            "use_emissive_texture": True,
            "double_sided": True,
        },
    }
    return {name: all_specs[name] for name in used_names}


# ==============================================================================
# 2. DETERMINISTIC NOISE & PS1 VERTEX COLOR PALETTE
# ==============================================================================

def rgba(r, g, b, a=1.0):
    return (
        float(max(0.0, min(1.0, r))),
        float(max(0.0, min(1.0, g))),
        float(max(0.0, min(1.0, b))),
        float(max(0.0, min(1.0, a))),
    )


def lerp_col(c0, c1, t):
    t = max(0.0, min(1.0, float(t)))
    return rgba(
        c0[0] + (c1[0] - c0[0]) * t,
        c0[1] + (c1[1] - c0[1]) * t,
        c0[2] + (c1[2] - c0[2]) * t,
        1.0,
    )


def organic_noise(x, y, z, seed=0.0):
    return (
        0.46 * math.sin(x * 0.53 + z * 0.41 + y * 0.37 + seed)
        + 0.34 * math.cos(x * 1.17 - z * 0.93 + y * 0.71 - seed * 0.7)
        + 0.20 * math.sin(x * 2.31 + z * 2.09 - y * 1.41 + seed * 1.3)
    )


def apply_acid_rim_tint(col, y):
    """Blends toxic chartreuse-green stain near the gastric acid waterline Y=0 and darkens below."""
    r, g, b, _ = col
    # Peak green glow-stain right around Y = -0.35 .. +0.75
    if -1.1 <= y <= 1.15:
        band = 1.0 - abs(y - 0.10) / 1.15
        band = max(0.0, min(1.0, band)) ** 0.80
        stain = 0.62 * band
        r = r * (1.0 - stain) + 0.56 * stain
        g = g * (1.0 - stain) + 0.98 * stain
        b = b * (1.0 - stain) + 0.32 * stain
    # Murky submerged abyss fade below Y = -0.7
    if y < -0.7:
        sub = min(1.0, (-0.7 - y) / 2.6)
        r *= 1.0 - 0.58 * sub
        g *= 1.0 - 0.44 * sub
        b *= 1.0 - 0.56 * sub
    return rgba(r, g, b)


def col_flesh_floor(x, z, r_norm=0.5, seed=0.0):
    """Warm bruised mucosa skin floor with darker rim folds and mottled organic patches."""
    n = 0.5 + 0.5 * organic_noise(x * 0.45, 0.0, z * 0.45, seed)
    rim_shadow = max(0.0, min(1.0, (r_norm - 0.76) / 0.24))
    bright = (0.88 + 0.12 * n) * (1.0 - 0.24 * rim_shadow)
    r = bright * (0.95 + 0.05 * n)
    g = bright * (0.72 + 0.12 * (1.0 - n))
    b = bright * (0.74 + 0.08 * (1.0 - n))
    return rgba(r, g, b)


def col_flesh_wall(x, y, z, crevice_ao=0.0, seed=0.0):
    """Deep visceral cliff flesh-rock with undercut AO and acid-rim tinting."""
    n = 0.5 + 0.5 * organic_noise(x * 0.65, y * 0.85, z * 0.65, seed)
    ao = 1.0 - 0.52 * max(0.0, min(1.0, crevice_ao))
    r = ao * (0.76 + 0.18 * n)
    g = ao * (0.40 + 0.14 * n)
    b = ao * (0.44 + 0.12 * (1.0 - n))
    return apply_acid_rim_tint(rgba(r, g, b), y)


def col_calcified(x, y, z, crevice_ao=0.0, seed=0.0):
    """Corroded pale ochre-grey-green calcified strata near the acid waterline."""
    n = 0.5 + 0.5 * organic_noise(x * 0.75, y * 0.90, z * 0.75, seed)
    ao = 1.0 - 0.46 * max(0.0, min(1.0, crevice_ao))
    r = ao * (0.76 + 0.16 * n)
    g = ao * (0.74 + 0.16 * n)
    b = ao * (0.60 + 0.14 * (1.0 - n))
    return apply_acid_rim_tint(rgba(r, g, b), y)


def col_cartilage(bright=0.90, blood_stain=0.0, y=None):
    """Ivory bone-cartilage with optional blood-marrow stain and acid-line greening."""
    st = max(0.0, min(1.0, blood_stain))
    r = bright * (0.96 - 0.16 * st)
    g = bright * (0.90 - 0.48 * st)
    b = bright * (0.78 - 0.48 * st)
    c = rgba(r, g, b)
    if y is not None:
        c = apply_acid_rim_tint(c, y)
    return c


def col_ossified_stone(bright=0.86, grime=0.0, y=None):
    """Weathered ossuary stone slabs and carved pylons."""
    gr = max(0.0, min(1.0, grime))
    r = bright * (0.84 - 0.24 * gr)
    g = bright * (0.81 - 0.26 * gr)
    b = bright * (0.76 - 0.25 * gr)
    c = rgba(r, g, b)
    if y is not None:
        c = apply_acid_rim_tint(c, y)
    return c


def col_vein(bright=0.90, pulse=0.0, y=None):
    """Rich crimson arterial muscle vein darkening into plum-burgundy."""
    p = max(0.0, min(1.0, pulse))
    r = bright * (0.68 + 0.28 * p)
    g = bright * (0.20 + 0.16 * p)
    b = bright * (0.24 + 0.10 * p)
    c = rgba(r, g, b)
    if y is not None:
        c = apply_acid_rim_tint(c, y)
    return c


def col_tooth(tip_t):
    """Blood-gum socket at 0.0 transitioning to bright ivory enamel at 1.0."""
    return lerp_col(rgba(0.56, 0.24, 0.24), rgba(1.00, 0.96, 0.84), tip_t)


def col_acid(bright=1.0):
    return rgba(0.75 * bright, 1.00 * bright, 0.36 * bright)


# ==============================================================================
# 3. ORIENTED GEOMETRY & VERIFIED WINDING PRIMITIVES
# ==============================================================================

def _v3(p):
    return np.array([float(p[0]), float(p[1]), float(p[2])], dtype=np.float32)


def add_tri_oriented(mb, p0, p1, p2, outward_hint, uv0=(0.0, 0.0), uv1=(1.0, 0.0), uv2=(0.5, 1.0),
                     c0=(1.0, 1.0, 1.0, 1.0), c1=None, c2=None):
    """Adds a flat triangle guaranteed to face along outward_hint."""
    if c1 is None:
        c1 = c0
    if c2 is None:
        c2 = c0
    v0, v1, v2 = _v3(p0), _v3(p1), _v3(p2)
    n = np.cross(v1 - v0, v2 - v0)
    if float(np.dot(n, _v3(outward_hint))) >= 0.0:
        mb.add_flat_triangle(v0, v1, v2, uv0, uv1, uv2, c0, c1, c2)
    else:
        mb.add_flat_triangle(v0, v2, v1, uv0, uv2, uv1, c0, c2, c1)


def add_quad_oriented(mb, p0, p1, p2, p3, outward_hint,
                      uv0=(0.0, 0.0), uv1=(1.0, 0.0), uv2=(1.0, 1.0), uv3=(0.0, 1.0),
                      c0=(1.0, 1.0, 1.0, 1.0), c1=None, c2=None, c3=None):
    """Adds a flat quad (p0->p1->p2->p3) guaranteed to face along outward_hint."""
    if c1 is None:
        c1 = c0
    if c2 is None:
        c2 = c1
    if c3 is None:
        c3 = c0
    v0, v1, v2, v3 = _v3(p0), _v3(p1), _v3(p2), _v3(p3)
    n = np.cross(v1 - v0, v2 - v0)
    if float(np.dot(n, _v3(outward_hint))) >= 0.0:
        mb.add_flat_quad(v0, v1, v2, v3, uv0, uv1, uv2, uv3, c0, c1, c2, c3)
    else:
        mb.add_flat_quad(v0, v3, v2, v1, uv0, uv3, uv2, uv1, c0, c3, c2, c1)


def add_hex_prism_corners(mb, bot4, top4, c_bot, c_top, uv_scale=0.35, skip_bottom=False, skip_top=False):
    """
    Builds a crisp 6-face faceted rock/stone block from arbitrary 4 bottom and 4 top 3D corners
    ordered CCW around +Y (e.g. (-x,-z), (-x,+z), (+x,+z), (+x,-z)).
    Normals are oriented outward from the block centroid automatically.
    """
    b = [_v3(p) for p in bot4]
    t = [_v3(p) for p in top4]
    center = 0.125 * (sum(b) + sum(t))

    def _c(arr, i):
        return arr[i] if isinstance(arr, list) else arr

    for i in range(4):
        ni = (i + 1) % 4
        face_mid = 0.25 * (b[i] + b[ni] + t[ni] + t[i])
        out_dir = face_mid - center
        w = float(np.linalg.norm(b[ni] - b[i])) * uv_scale
        h = float(np.linalg.norm(t[i] - b[i])) * uv_scale
        add_quad_oriented(
            mb, b[i], b[ni], t[ni], t[i], out_dir,
            (0.0, 0.0), (w, 0.0), (w, h), (0.0, h),
            _c(c_bot, i), _c(c_bot, ni), _c(c_top, ni), _c(c_top, i),
        )

    if not skip_top:
        t_mid = 0.25 * sum(t)
        u1 = float(np.linalg.norm(t[1] - t[0])) * uv_scale
        v1 = float(np.linalg.norm(t[3] - t[0])) * uv_scale
        add_quad_oriented(
            mb, t[0], t[1], t[2], t[3], t_mid - center,
            (0.0, 0.0), (0.0, v1), (u1, v1), (u1, 0.0),
            _c(c_top, 0), _c(c_top, 1), _c(c_top, 2), _c(c_top, 3),
        )

    if not skip_bottom:
        b_mid = 0.25 * sum(b)
        u1 = float(np.linalg.norm(b[1] - b[0])) * uv_scale
        v1 = float(np.linalg.norm(b[3] - b[0])) * uv_scale
        add_quad_oriented(
            mb, b[0], b[3], b[2], b[1], b_mid - center,
            (0.0, 0.0), (u1, 0.0), (u1, v1), (0.0, v1),
            _c(c_bot, 0), _c(c_bot, 3), _c(c_bot, 2), _c(c_bot, 1),
        )


def add_oriented_box(mb, center, size, yaw=0.0, pitch=0.0, roll=0.0,
                     c_bot=(0.8, 0.8, 0.8, 1.0), c_top=(0.95, 0.95, 0.95, 1.0),
                     taper_top=(1.0, 1.0), uv_scale=0.35, skip_bottom=False, skip_top=False):
    """Builds a rotated, optionally tapered faceted box centered at `center`."""
    hx, hy, hz = size[0] * 0.5, size[1] * 0.5, size[2] * 0.5
    tx, tz = taper_top
    local_b = [(-hx, -hy, -hz), (-hx, -hy, hz), (hx, -hy, hz), (hx, -hy, -hz)]
    local_t = [(-hx * tx, hy, -hz * tz), (-hx * tx, hy, hz * tz), (hx * tx, hy, hz * tz), (hx * tx, hy, -hz * tz)]

    cy_a, sy_a = math.cos(yaw), math.sin(yaw)
    cp_a, sp_a = math.cos(pitch), math.sin(pitch)
    cr_a, sr_a = math.cos(roll), math.sin(roll)

    def xform(p):
        x, y, z = p
        # Roll around Z
        x1 = x * cr_a - y * sr_a
        y1 = x * sr_a + y * cr_a
        z1 = z
        # Pitch around X
        y2 = y1 * cp_a - z1 * sp_a
        z2 = y1 * sp_a + z1 * cp_a
        x2 = x1
        # Yaw around Y
        x3 = x2 * cy_a + z2 * sy_a
        z3 = -x2 * sy_a + z2 * cy_a
        return (center[0] + x3, center[1] + y2, center[2] + z3)

    add_hex_prism_corners(
        mb,
        [xform(p) for p in local_b],
        [xform(p) for p in local_t],
        c_bot,
        c_top,
        uv_scale=uv_scale,
        skip_bottom=skip_bottom,
        skip_top=skip_top,
    )


def add_spline_tube(mb, pts, radii, sides=6, colors=None, uv_scale=0.45,
                    cap_start=True, cap_end=True, flat=False, ellipse_ratio=1.0):
    """
    Extrudes a closed, outward-oriented polygonal tube along `pts`.
    When flat=False, vertices are shared per ring and should get smooth normals via
    recompute_smooth_normals() (or already approximate smooth radial normals).
    """
    n_pts = len(pts)
    if n_pts < 2:
        return
    p_arr = [_v3(p) for p in pts]
    if isinstance(radii, (int, float)):
        radii = [float(radii)] * n_pts
    if colors is None:
        colors = [rgba(0.85, 0.25, 0.25)] * n_pts

    # Parallel-transport framing along polyline to avoid twisting pinch
    tangents = []
    for i in range(n_pts):
        if i == 0:
            t = p_arr[1] - p_arr[0]
        elif i == n_pts - 1:
            t = p_arr[-1] - p_arr[-2]
        else:
            t = p_arr[i + 1] - p_arr[i - 1]
        ln = float(np.linalg.norm(t))
        tangents.append(t / ln if ln > 1e-6 else np.array([0.0, 1.0, 0.0], dtype=np.float32))

    ref = np.array([0.0, 1.0, 0.0], dtype=np.float32) if abs(tangents[0][1]) < 0.85 else np.array([1.0, 0.0, 0.0], dtype=np.float32)
    n_vec = np.cross(ref, tangents[0])
    n_vec /= float(np.linalg.norm(n_vec))
    b_vec = np.cross(tangents[0], n_vec)

    rings = []
    ring_normals = []
    cum_len = [0.0]
    for i in range(n_pts):
        if i > 0:
            cum_len.append(cum_len[-1] + float(np.linalg.norm(p_arr[i] - p_arr[i - 1])))
            # Transport frame
            n_proj = n_vec - tangents[i] * float(np.dot(n_vec, tangents[i]))
            nl = float(np.linalg.norm(n_proj))
            if nl > 1e-5:
                n_vec = n_proj / nl
            b_vec = np.cross(tangents[i], n_vec)
            bl = float(np.linalg.norm(b_vec))
            if bl > 1e-5:
                b_vec /= bl

        r = float(radii[i])
        ring = []
        rn = []
        for s in range(sides):
            ang = 2.0 * math.pi * s / float(sides)
            ca, sa = math.cos(ang), math.sin(ang)
            off = n_vec * (r * ca) + b_vec * (r * ellipse_ratio * sa)
            ring.append(p_arr[i] + off)
            ol = float(np.linalg.norm(off))
            rn.append(off / ol if ol > 1e-6 else n_vec)
        rings.append(ring)
        ring_normals.append(rn)

    if flat:
        for i in range(n_pts - 1):
            v0 = cum_len[i] * uv_scale
            v1 = cum_len[i + 1] * uv_scale
            for s in range(sides):
                ns = (s + 1) % sides
                u0 = float(s) / sides
                u1 = float(s + 1) / sides
                q00, q10 = rings[i][s], rings[i][ns]
                q11, q01 = rings[i + 1][ns], rings[i + 1][s]
                out_hint = 0.25 * (q00 + q10 + q11 + q01) - 0.5 * (p_arr[i] + p_arr[i + 1])
                add_quad_oriented(
                    mb, q00, q10, q11, q01, out_hint,
                    (u0, v0), (u1, v0), (u1, v1), (u0, v1),
                    colors[i], colors[i], colors[i + 1], colors[i + 1],
                )
    else:
        # Shared-vertex rings with explicit winding check on the very first quad to guarantee outward
        v_idx = []
        for i in range(n_pts):
            row = []
            vv = cum_len[i] * uv_scale
            for s in range(sides + 1):
                si = s % sides
                uu = float(s) / sides
                idx = mb.add_vertex(rings[i][si], ring_normals[i][si], (uu, vv), colors[i])
                row.append(idx)
            v_idx.append(row)

        # Determine consistent orientation from sample quad
        flip_wind = False
        q00 = rings[0][0]
        q10 = rings[0][1]
        q11 = rings[1][1]
        out_test = 0.25 * (q00 + q10 + q11 + rings[1][0]) - 0.5 * (p_arr[0] + p_arr[1])
        if float(np.dot(np.cross(q10 - q00, q11 - q00), out_test)) < 0.0:
            flip_wind = True

        for i in range(n_pts - 1):
            for s in range(sides):
                i00 = v_idx[i][s]
                i10 = v_idx[i][s + 1]
                i11 = v_idx[i + 1][s + 1]
                i01 = v_idx[i + 1][s]
                if not flip_wind:
                    mb.add_tri_indices(i00, i10, i11)
                    mb.add_tri_indices(i00, i11, i01)
                else:
                    mb.add_tri_indices(i00, i11, i10)
                    mb.add_tri_indices(i00, i01, i11)

    if cap_start and radii[0] > 1e-4:
        c0 = p_arr[0]
        for s in range(sides):
            ns = (s + 1) % sides
            add_tri_oriented(
                mb, c0, rings[0][ns], rings[0][s], -tangents[0],
                (0.5, 0.5), (1.0, 0.0), (0.0, 0.0),
                colors[0], colors[0], colors[0],
            )
    if cap_end and radii[-1] > 1e-4:
        c1 = p_arr[-1]
        for s in range(sides):
            ns = (s + 1) % sides
            add_tri_oriented(
                mb, c1, rings[-1][s], rings[-1][ns], tangents[-1],
                (0.5, 0.5), (0.0, 0.0), (1.0, 0.0),
                colors[-1], colors[-1], colors[-1],
            )


def sample_catenary_points(p0, p1, sag=1.2, n_segs=10, sway_x=0.0, sway_freq=1.5, phase=0.0):
    """Generates a draping catenary/sinusoidal curve between two 3D points."""
    p0 = _v3(p0)
    p1 = _v3(p1)
    pts = []
    horiz = p1 - p0
    horiz[1] = 0.0
    hl = float(np.linalg.norm(horiz))
    perp = np.array([-horiz[2] / hl, 0.0, horiz[0] / hl], dtype=np.float32) if hl > 1e-4 else np.array([1.0, 0.0, 0.0], dtype=np.float32)
    for i in range(n_segs + 1):
        t = i / float(n_segs)
        base = p0 * (1.0 - t) + p1 * t
        drop = sag * 4.0 * t * (1.0 - t)
        wave = sway_x * math.sin(t * math.pi) * math.sin(t * math.pi * sway_freq + phase)
        pts.append(base + np.array([0.0, -drop, 0.0], dtype=np.float32) + perp * wave)
    return pts


def add_curved_fang(mb, base_pos, out_dir, height=1.3, base_r=0.30, lean_out=0.22, curve_in=-0.12, sides=5):
    """Adds a chunky faceted low-poly fang/tooth curving organically upward."""
    base = _v3(base_pos)
    out = np.array([out_dir[0], 0.0, out_dir[2]], dtype=np.float32)
    ol = float(np.linalg.norm(out))
    out = out / ol if ol > 1e-5 else np.array([1.0, 0.0, 0.0], dtype=np.float32)
    side = np.cross(np.array([0.0, 1.0, 0.0], dtype=np.float32), out)

    tiers = [
        (0.00, 1.00, 0.00),
        (0.32, 0.72, lean_out * 0.65),
        (0.68, 0.38, lean_out + curve_in * 0.35),
        (1.00, 0.04, lean_out + curve_in),
    ]
    rings = []
    cols = []
    for yt, r_sc, fwd_off in tiers:
        center = base + np.array([0.0, yt * height, 0.0], dtype=np.float32) + out * fwd_off
        r_ring = base_r * r_sc
        ring = []
        for s in range(sides):
            a = 2.0 * math.pi * s / float(sides) + 0.3
            ring.append(center + out * (r_ring * math.cos(a)) + side * (r_ring * 0.85 * math.sin(a)))
        rings.append((center, ring))
        cols.append(col_tooth(yt))

    for ti in range(len(tiers) - 1):
        c_mid = 0.5 * (rings[ti][0] + rings[ti + 1][0])
        r0, r1 = rings[ti][1], rings[ti + 1][1]
        for s in range(sides):
            ns = (s + 1) % sides
            q_mid = 0.25 * (r0[s] + r0[ns] + r1[ns] + r1[s])
            add_quad_oriented(
                mb, r0[s], r0[ns], r1[ns], r1[s], q_mid - c_mid,
                (s / float(sides), ti / 3.0),
                ((s + 1) / float(sides), ti / 3.0),
                ((s + 1) / float(sides), (ti + 1) / 3.0),
                (s / float(sides), (ti + 1) / 3.0),
                cols[ti], cols[ti], cols[ti + 1], cols[ti + 1],
            )
    # Cap tip
    tip_c = rings[-1][0] + np.array([0.0, height * 0.06, 0.0], dtype=np.float32)
    top_r = rings[-1][1]
    for s in range(sides):
        ns = (s + 1) % sides
        add_tri_oriented(mb, top_r[s], top_r[ns], tip_c, np.array([0.0, 1.0, 0.0]) + out * 0.2,
                         (0.0, 0.8), (1.0, 0.8), (0.5, 1.0), cols[-1], cols[-1], cols[-1])


# ==============================================================================
# 4. ARENA ISLAND SCULPTING ENGINE (SHARED MULTI-STRATA CLIFF + FLAT DECK)
# ==============================================================================

def angular_diff(a, b):
    return math.atan2(math.sin(a - b), math.cos(a - b))


def compute_dock_suppression(theta, dock_angles, half_width=0.35):
    """Returns 1.0 inside a bridge docking corridor, smoothly falling to 0.0 outside."""
    if not dock_angles:
        return 0.0
    m = 0.0
    for da in dock_angles:
        d = abs(angular_diff(theta, da))
        if d < half_width:
            w = 0.5 * (1.0 + math.cos(math.pi * d / half_width))
            m = max(m, w)
    return m


def arena_footprint_radius(theta, rx, rz, seed=0.0, dock_angles=None, asymmetry_fn=None):
    """Organic scalloped low-poly footprint radius centered on (rx, rz), smoothed at docks."""
    ct, st = math.cos(theta), math.sin(theta)
    r_base = 1.0 / math.sqrt((ct / rx) ** 2 + (st / rz) ** 2)
    lobe = (
        0.065 * math.sin(3.0 * theta + seed * 1.1)
        + 0.048 * math.cos(5.0 * theta - seed * 0.8)
        + 0.028 * math.sin(7.0 * theta + seed * 2.3)
        + 0.018 * math.cos(11.0 * theta + seed * 0.4)
    )
    if asymmetry_fn is not None:
        lobe += asymmetry_fn(theta)
    dock_w = compute_dock_suppression(theta, dock_angles, half_width=0.38)
    lobe *= (1.0 - 0.72 * dock_w)
    return r_base * (1.0 + lobe)


def build_arena_island_core(
    name_prefix,
    rx,
    rz,
    deck_y,
    seed=0.0,
    dock_angles=None,
    n_theta=36,
    rim_ridge_amp=0.32,
    overhang_strength=1.0,
    asymmetry_fn=None,
):
    """
    Constructs the core multi-layered island meshes:
      - Deck (<prefix>_Deck, Mat_FleshFloor): strictly flat/playable top at deck_y (+Y CCW verified!)
      - Upper Flesh Cliff (<prefix>_CliffFlesh, Mat_FleshWall): scalloped rim + overhanging strata
      - Lower Calcified Skirt (<prefix>_CliffCalcified, Mat_DigestedCalcified): plunging to Y=-3.5
    Returns (deck_mb, wall_mb, calc_mb, deck_tris_count, radi_list).
    """
    deck_mb = MeshBuilder(f"{name_prefix}_Deck", "Mat_FleshFloor")
    wall_mb = MeshBuilder(f"{name_prefix}_CliffFlesh", "Mat_FleshWall")
    calc_mb = MeshBuilder(f"{name_prefix}_CliffCalcified", "Mat_DigestedCalcified")

    thetas = [2.0 * math.pi * i / float(n_theta) for i in range(n_theta)]
    radii = [
        arena_footprint_radius(th, rx, rz, seed=seed, dock_angles=dock_angles, asymmetry_fn=asymmetry_fn)
        for th in thetas
    ]

    # ---- A. WALKABLE FLAT TOP DECK (Mat_FleshFloor) ----
    # Radial rings from center -> outer edge (0.985 * R) at exact Y = deck_y
    n_rings = 5
    ring_fracs = [0.22, 0.46, 0.70, 0.88, 0.985]
    uv_scale = 0.24

    c_idx = deck_mb.add_vertex((0.0, deck_y, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0), col_flesh_floor(0.0, 0.0, 0.0, seed))
    ring_verts = []
    for rf in ring_fracs:
        row = []
        for ti, th in enumerate(thetas):
            r = radii[ti] * rf
            x = r * math.cos(th)
            z = r * math.sin(th)
            u = x * uv_scale + 0.12 * math.sin(z * 0.4)
            v = z * uv_scale + 0.12 * math.cos(x * 0.4)
            idx = deck_mb.add_vertex((x, deck_y, z), (0.0, 1.0, 0.0), (u, v), col_flesh_floor(x, z, rf, seed))
            row.append(idx)
        ring_verts.append(row)

    # IMPORTANT: In (+X right, +Y up, +Z forward), going from th1 (higher angle) -> th0 (lower angle)
    # around center gives (p1 - p0) x (p2 - p0) pointing +Y!
    for ti in range(n_theta):
        nti = (ti + 1) % n_theta
        i0 = ring_verts[0][ti]
        i1 = ring_verts[0][nti]
        # Verify cross product points +Y
        p0 = np.array(deck_mb.positions[c_idx])
        p1 = np.array(deck_mb.positions[i1])
        p2 = np.array(deck_mb.positions[i0])
        if np.cross(p1 - p0, p2 - p0)[1] >= 0.0:
            deck_mb.add_tri_indices(c_idx, i1, i0)
        else:
            deck_mb.add_tri_indices(c_idx, i0, i1)

    for ri in range(n_rings - 1):
        for ti in range(n_theta):
            nti = (ti + 1) % n_theta
            a0 = ring_verts[ri][ti]
            a1 = ring_verts[ri][nti]
            b1 = ring_verts[ri + 1][nti]
            b0 = ring_verts[ri + 1][ti]
            pa0 = np.array(deck_mb.positions[a0])
            pa1 = np.array(deck_mb.positions[a1])
            pb0 = np.array(deck_mb.positions[b0])
            if np.cross(pa1 - pa0, pb0 - pa0)[1] >= 0.0:
                deck_mb.add_tri_indices(a0, a1, b1)
                deck_mb.add_tri_indices(a0, b1, b0)
            else:
                deck_mb.add_tri_indices(a0, b1, a1)
                deck_mb.add_tri_indices(a0, b0, b1)

    # ---- B. MULTI-TIER OVERHANGING FLESH CLIFF (Mat_FleshWall) ----
    # Strata from deck rim (0.985*R) -> scalloped mucosa ridge -> bulbous overhang -> deep undercut -> mid crag -> lower seam
    seam_y = min(1.05, max(0.75, deck_y * 0.34))
    wall_strata = [
        # (r_mul, y_val_fn, ao, ridge_amp)
        (0.985, lambda th, r, d: deck_y, 0.14, 0.0),
        (1.035, lambda th, r, d: deck_y + rim_ridge_amp * (1.0 - 0.92 * d) * (0.55 + 0.45 * math.sin(6.0 * th + seed)), 0.05, 0.04),
        (1.095 + 0.03 * overhang_strength, lambda th, r, d: deck_y - 0.38, 0.18, 0.06),
        (0.875 - 0.04 * overhang_strength, lambda th, r, d: deck_y - 1.28, 0.72, -0.05),
        (1.045 + 0.04 * overhang_strength, lambda th, r, d: max(seam_y + 0.42, deck_y * 0.50), 0.28, 0.08),
        (0.960, lambda th, r, d: seam_y - 0.28, 0.45, 0.03),
    ]

    wall_rows = []
    for si, (r_mul, y_fn, ao_base, r_jagg) in enumerate(wall_strata):
        row = []
        for ti, th in enumerate(thetas):
            dock_w = compute_dock_suppression(th, dock_angles, half_width=0.38)
            jagg = r_jagg * math.cos(9.0 * th - si * 1.4 + seed) * (1.0 - 0.75 * dock_w)
            rr = radii[ti] * (r_mul + jagg)
            # Pull outer rim slightly inward at docks so bridge decks embed smoothly
            if si in (1, 2) and dock_w > 0.2:
                rr = radii[ti] * (0.99 + (r_mul - 0.99) * (1.0 - 0.75 * dock_w))
            x = rr * math.cos(th)
            z = rr * math.sin(th)
            y = y_fn(th, rr, dock_w)
            y_j = 0.0 if si == 0 else 0.14 * math.sin(5.0 * th + si * 1.9 + seed) * (1.0 - 0.6 * dock_w)
            yy = y + y_j
            col = col_flesh_wall(x, yy, z, crevice_ao=ao_base + 0.15 * max(0.0, math.sin(4.0 * th + seed)), seed=seed)
            u = th / (2.0 * math.pi) * 6.0
            v = yy * 0.32
            idx = wall_mb.add_vertex((x, yy, z), (math.cos(th), 0.0, math.sin(th)), (u, v), col)
            row.append(idx)
        wall_rows.append(row)

    def _connect_closed_rings(mb, rows):
        # Rows proceed top->bottom around CCW (+theta) ring:
        # (p10 - p00) x (p01 - p00) = (+theta) x (meridional_down) = exact outward normal
        # for EVERY slope, raised rim inner wall, overhang, and undercut!
        n_r = len(rows)
        for ri in range(n_r - 1):
            for ti in range(n_theta):
                nti = (ti + 1) % n_theta
                i00 = rows[ri][ti]
                i10 = rows[ri][nti]
                i11 = rows[ri + 1][nti]
                i01 = rows[ri + 1][ti]
                mb.add_tri_indices(i00, i10, i11)
                mb.add_tri_indices(i00, i11, i01)

    _connect_closed_rings(wall_mb, wall_rows)
    wall_mb.recompute_smooth_normals()

    # ---- C. LOWER DIGESTED CALCIFIED SKIRT TO Y = -3.5 (Mat_DigestedCalcified) ----
    # Overlaps slightly into wall_rows[-2..-1] so there are zero cracks between strata!
    calc_strata = [
        (0.985, seam_y + 0.18, 0.36, 0.04),
        (1.075, 0.32, 0.12, 0.09),   # jagged acid-line shelf flaring outward
        (0.955, -0.45, 0.32, -0.06), # submerged acid undercut
        (1.015, -1.65, 0.38, 0.07),  # deep rocky ledge
        (0.760, -3.50, 0.65, 0.04),  # tapered plunged root at Y=-3.5
    ]

    calc_rows = []
    for si, (r_mul, y_base, ao_base, r_jagg) in enumerate(calc_strata):
        row = []
        for ti, th in enumerate(thetas):
            jagg = r_jagg * math.sin(8.0 * th + si * 2.1 + seed)
            rr = radii[ti] * (r_mul + jagg)
            x = rr * math.cos(th)
            z = rr * math.sin(th)
            y_jit = 0.0 if si == len(calc_strata) - 1 else 0.16 * math.cos(6.0 * th - si * 1.3 + seed)
            yy = max(-3.5, y_base + y_jit)
            if si == len(calc_strata) - 1:
                yy = -3.5
            col = col_calcified(x, yy, z, crevice_ao=ao_base, seed=seed)
            u = th / (2.0 * math.pi) * 6.0
            v = yy * 0.32
            idx = calc_mb.add_vertex((x, yy, z), (math.cos(th), 0.0, math.sin(th)), (u, v), col)
            row.append(idx)
        calc_rows.append(row)

    _connect_closed_rings(calc_mb, calc_rows)

    # Bottom cap at Y = -3.5
    bot_c = calc_mb.add_vertex((0.0, -3.5, 0.0), (0.0, -1.0, 0.0), (0.0, 0.0), col_calcified(0.0, -3.5, 0.0, 0.85, seed))
    for ti in range(n_theta):
        nti = (ti + 1) % n_theta
        i0 = calc_rows[-1][ti]
        i1 = calc_rows[-1][nti]
        p0 = np.array(calc_mb.positions[bot_c])
        p1 = np.array(calc_mb.positions[i0])
        p2 = np.array(calc_mb.positions[i1])
        if np.cross(p1 - p0, p2 - p0)[1] <= 0.0:
            calc_mb.add_tri_indices(bot_c, i0, i1)
        else:
            calc_mb.add_tri_indices(bot_c, i1, i0)

    calc_mb.recompute_smooth_normals()

    # ---- D. ROCKY-FLESH OVERHANGING BUTTRESS SPURS (Chunks gripping lower cliffs) ----
    n_spurs = 8
    for si in range(n_spurs):
        th = 2.0 * math.pi * (si + 0.35) / n_spurs + 0.15 * math.sin(si + seed)
        if compute_dock_suppression(th, dock_angles, half_width=0.34) > 0.35:
            continue
        r_at = arena_footprint_radius(th, rx, rz, seed=seed, dock_angles=dock_angles, asymmetry_fn=asymmetry_fn)
        sx = (r_at * 0.96) * math.cos(th)
        sz = (r_at * 0.96) * math.sin(th)
        # Angled rock-flesh buttress plunging from below deck rim down into acid
        spur_pts = [
            (sx * 0.94, deck_y - 0.55, sz * 0.94),
            (sx * 1.08, deck_y * 0.48, sz * 1.08),
            (sx * 1.12, 0.30, sz * 1.12),
            (sx * 0.92, -3.45, sz * 0.92),
        ]
        spur_r = [0.65, 1.05, 1.25, 0.72]
        spur_cols = [
            col_flesh_wall(sx, p[1], sz, 0.32, seed) if p[1] > 0.6 else col_calcified(sx, p[1], sz, 0.25, seed)
            for p in spur_pts
        ]
        add_spline_tube(calc_mb if si % 2 == 0 else wall_mb, spur_pts, spur_r, sides=5, colors=spur_cols, flat=True)

    return deck_mb, wall_mb, calc_mb, thetas, radii


def add_gripping_cliff_ribs(mb, rx, rz, deck_y, n_ribs, seed, dock_angles=None,
                            asymmetry_fn=None, hook_over_rim=0.22, reach_y=-1.4):
    """Sculpts chunky segmented cartilage bone ribs clutching the island cliffs."""
    for i in range(n_ribs):
        th = 2.0 * math.pi * (i + 0.5) / n_ribs + 0.08 * math.cos(i * 2.1 + seed)
        if compute_dock_suppression(th, dock_angles, half_width=0.34) > 0.28:
            continue
        r_at = arena_footprint_radius(th, rx, rz, seed=seed, dock_angles=dock_angles, asymmetry_fn=asymmetry_fn)
        ca, sa = math.cos(th), math.sin(th)
        p0 = (r_at * 0.95 * ca, reach_y, r_at * 0.95 * sa)
        p1 = (r_at * 1.09 * ca, max(0.35, deck_y * 0.25), r_at * 1.09 * sa)
        p2 = (r_at * 1.11 * ca, deck_y * 0.72, r_at * 1.11 * sa)
        p3 = (r_at * 1.02 * ca, deck_y + hook_over_rim, r_at * 1.02 * sa)
        p4 = (r_at * 0.96 * ca, deck_y + hook_over_rim * 0.45, r_at * 0.96 * sa)

        pts = [p0, p1, p2, p3, p4]
        rad = [0.42, 0.48, 0.40, 0.30, 0.16]
        cols = [
            col_cartilage(0.75, 0.45, p[1]) if idx in (0, 4) else col_cartilage(0.94, 0.15, p[1])
            for idx, p in enumerate(pts)
        ]
        add_spline_tube(mb, pts, rad, sides=5, colors=cols, flat=True, ellipse_ratio=0.72)


# ==============================================================================
# 5. ARENAS 1 - 6
# ==============================================================================

def build_arena_1_south_ossuary_entry():
    """Arena_South_OssuaryEntry.glb: rx=11.5, rz=9.5, deckY=2.0."""
    rx, rz, deck_y, seed = 11.5, 9.5, 2.0, 1.1
    docks = [math.pi * 0.5, -math.pi * 0.5]  # +Z bridge exit, -Z spawn entry
    deck_mb, wall_mb, calc_mb, thetas, radii = build_arena_island_core(
        "SouthOssuary", rx, rz, deck_y, seed=seed, dock_angles=docks, rim_ridge_amp=0.20, overhang_strength=0.9
    )

    cart_mb = MeshBuilder("SouthOssuary_CartilageBricksAndRibs", "Mat_Cartilage")
    acid_mb = MeshBuilder("SouthOssuary_AcidSeepage", "Mat_GastricAcid")

    # Raised stone/cartilage ritual rim bricks framing the landing plaza
    n_bricks = 24
    for i in range(n_bricks):
        th = 2.0 * math.pi * i / n_bricks
        if compute_dock_suppression(th, docks, half_width=0.38) > 0.25:
            continue
        r_at = arena_footprint_radius(th, rx, rz, seed=seed, dock_angles=docks) * 0.955
        bx = r_at * math.cos(th)
        bz = r_at * math.sin(th)
        bw = 1.95 + 0.35 * math.sin(i * 1.7)
        bh = 0.34 + 0.16 * (i % 3)
        bd = 0.85 + 0.15 * math.cos(i * 2.3)
        yaw = -th + math.pi * 0.5 + 0.08 * math.sin(i * 3.1)
        pitch = 0.05 * math.cos(i * 2.7)
        add_oriented_box(
            cart_mb, (bx, deck_y + bh * 0.42, bz), (bw, bh, bd),
            yaw=yaw, pitch=pitch,
            c_bot=col_cartilage(0.68, 0.38), c_top=col_cartilage(0.95, 0.08),
            taper_top=(0.86, 0.82),
        )

    # Gripping cartilage bone ribs around cliffs
    add_gripping_cliff_ribs(cart_mb, rx, rz, deck_y, n_ribs=10, seed=seed, dock_angles=docks, hook_over_rim=0.28, reach_y=-1.5)

    # Atmospheric gastric acid seepage veins/pools tucked at lower rock shelves
    for i in range(5):
        th = 0.45 + i * 1.25
        if compute_dock_suppression(th, docks, half_width=0.35) > 0.3:
            continue
        r_at = arena_footprint_radius(th, rx, rz, seed=seed, dock_angles=docks) * 1.03
        ax, az = r_at * math.cos(th), r_at * math.sin(th)
        add_oriented_box(
            acid_mb, (ax, 0.22, az), (1.35, 0.18, 0.95),
            yaw=-th, c_bot=col_acid(0.85), c_top=col_acid(1.0), taper_top=(0.7, 0.7),
        )

    return [deck_mb, wall_mb, calc_mb, cart_mb, acid_mb]


def build_arena_2_south_central_hub():
    """Arena_SouthCentral_Hub.glb: rx=13.5, rz=12.0, deckY=3.2."""
    rx, rz, deck_y, seed = 13.5, 12.0, 3.2, 2.4
    docks = [-math.pi * 0.5, math.pi * 0.5]  # South stair dock, North grand vein-bridge dock
    deck_mb, wall_mb, calc_mb, thetas, radii = build_arena_island_core(
        "SouthCentralHub", rx, rz, deck_y, seed=seed, dock_angles=docks, rim_ridge_amp=0.48, overhang_strength=1.1
    )

    cart_mb = MeshBuilder("SouthCentralHub_CartilageButtresses", "Mat_Cartilage")
    teeth_mb = MeshBuilder("SouthCentralHub_OuterTeeth", "Mat_Teeth")

    # Sculptural gripping cartilage ribs
    add_gripping_cliff_ribs(cart_mb, rx, rz, deck_y, n_ribs=12, seed=seed, dock_angles=docks, hook_over_rim=0.22, reach_y=-1.6)

    # Outer jagged bone teeth clusters safely OUTSIDE the walkable combat circle (r >= 1.03 * R)
    cluster_angles = [0.22, 0.85, 2.25, 2.95, 3.40, 4.05, 5.45, 6.02]
    for ci, base_th in enumerate(cluster_angles):
        if compute_dock_suppression(base_th, docks, half_width=0.45) > 0.15:
            continue
        # Gum socket cartilage mound
        r_gum = arena_footprint_radius(base_th, rx, rz, seed=seed, dock_angles=docks) * 1.035
        gx, gz = r_gum * math.cos(base_th), r_gum * math.sin(base_th)
        add_oriented_box(
            cart_mb, (gx, deck_y + 0.15, gz), (2.4, 0.65, 1.3),
            yaw=-base_th + math.pi * 0.5,
            c_bot=col_cartilage(0.66, 0.50), c_top=col_cartilage(0.86, 0.25),
            taper_top=(0.82, 0.78),
        )
        # 3 fangs per cluster leaning outward/upward
        for fi, d_th in enumerate((-0.09, 0.0, 0.09)):
            th = base_th + d_th
            r_f = arena_footprint_radius(th, rx, rz, seed=seed, dock_angles=docks) * (1.03 + 0.02 * (fi == 1))
            fx, fz = r_f * math.cos(th), r_f * math.sin(th)
            h_fang = (1.55 if fi == 1 else 1.05) + 0.25 * math.sin(ci * 1.7 + fi)
            add_curved_fang(
                teeth_mb, (fx, deck_y + 0.28, fz), (math.cos(th), 0.0, math.sin(th)),
                height=h_fang, base_r=0.34 if fi == 1 else 0.25, lean_out=0.35, curve_in=-0.10,
            )

    return [deck_mb, wall_mb, calc_mb, cart_mb, teeth_mb]


def build_arena_3_central_ritual_plateau():
    """Arena_Central_RitualPlateau.glb: rx=15.0, rz=13.5, deckY=4.6."""
    rx, rz, deck_y, seed = 15.0, 13.5, 4.6, 3.7
    # 4 cardinal docks: South (VeinBridge), North (NorthStairs), East (PlankSpan), West (WestStairs)
    docks = [-math.pi * 0.5, math.pi * 0.5, 0.0, math.pi]
    deck_mb, wall_mb, calc_mb, thetas, radii = build_arena_island_core(
        "CentralPlateau", rx, rz, deck_y, seed=seed, dock_angles=docks,
        n_theta=40, rim_ridge_amp=0.22, overhang_strength=1.35,
    )

    cart_mb = MeshBuilder("CentralPlateau_CalcifiedButtresses", "Mat_Cartilage")
    stone_mb = MeshBuilder("CentralPlateau_PerimeterStones", "Mat_OssifiedStone")

    # Extra dramatic overhanging crag ledges sculpted organically around the diagonal cliff faces
    for si in range(8):
        th = (si + 0.5) * (2.0 * math.pi / 8.0)
        if compute_dock_suppression(th, docks, half_width=0.35) > 0.2:
            continue
        r_at = arena_footprint_radius(th, rx, rz, seed=seed, dock_angles=docks)
        ca, sa = math.cos(th), math.sin(th)
        pts_ledge = [
            (r_at * 0.88 * ca, deck_y - 0.85, r_at * 0.88 * sa),
            (r_at * 1.12 * ca, deck_y - 1.55, r_at * 1.12 * sa),
            (r_at * 1.16 * ca, deck_y - 2.35, r_at * 1.16 * sa),
            (r_at * 0.94 * ca, 0.35, r_at * 0.94 * sa),
            (r_at * 0.84 * ca, -2.95, r_at * 0.84 * sa),
        ]
        target_mb = wall_mb if si % 2 == 0 else calc_mb
        cols_l = [
            col_flesh_wall(p[0], p[1], p[2], 0.28, seed) if si % 2 == 0 else col_calcified(p[0], p[1], p[2], 0.28, seed)
            for p in pts_ledge
        ]
        add_spline_tube(target_mb, pts_ledge, [0.95, 1.55, 1.75, 1.25, 0.72], sides=6, colors=cols_l, flat=False, ellipse_ratio=0.72)

    # Heavy calcified buttress fins gripping the plateau shoulders
    add_gripping_cliff_ribs(cart_mb, rx, rz, deck_y, n_ribs=12, seed=seed, dock_angles=docks, hook_over_rim=0.25, reach_y=-1.9)

    # Low outer ossified stone border plinth blocks along diagonal rims (safe outside inner combat circle)
    for qi in range(4):
        base_th = math.pi * 0.25 + qi * math.pi * 0.5
        for sub in (-0.16, 0.0, 0.16):
            th = base_th + sub
            r_s = arena_footprint_radius(th, rx, rz, seed=seed, dock_angles=docks) * 0.96
            sx, sz = r_s * math.cos(th), r_s * math.sin(th)
            add_oriented_box(
                stone_mb, (sx, deck_y + 0.12, sz), (2.25, 0.26, 0.85),
                yaw=-th + math.pi * 0.5,
                c_bot=col_ossified_stone(0.66, 0.35), c_top=col_ossified_stone(0.90, 0.05),
                taper_top=(0.88, 0.85),
            )

    return [deck_mb, wall_mb, calc_mb, cart_mb, stone_mb]


def build_arena_4_east_tendon_overlook():
    """Arena_East_TendonOverlook.glb: rx=9.5, rz=9.0, deckY=5.4."""
    rx, rz, deck_y, seed = 9.5, 9.0, 5.4, 4.9
    docks = [math.pi]  # -X bridge dock from Central Plateau

    def east_asym(th):
        # Asymmetric craggy eastern prow (+X) and jagged northeast/southeast spurs
        return 0.09 * math.cos(th) + 0.055 * math.sin(2.0 * th - 0.5)

    deck_mb, wall_mb, calc_mb, thetas, radii = build_arena_island_core(
        "EastOverlook", rx, rz, deck_y, seed=seed, dock_angles=docks,
        n_theta=32, rim_ridge_amp=0.35, overhang_strength=1.15, asymmetry_fn=east_asym,
    )

    cart_mb = MeshBuilder("EastOverlook_CartilageSpurs", "Mat_Cartilage")
    vein_mb = MeshBuilder("EastOverlook_SinewyRoots", "Mat_VeinTendon")

    # Protruding asymmetric cartilage spurs jutting out from East/NE/SE cliff shoulders
    spur_angles = [-0.95, -0.45, 0.05, 0.55, 1.15, -1.55, 1.65]
    for idx, th in enumerate(spur_angles):
        r_at = arena_footprint_radius(th, rx, rz, seed=seed, dock_angles=docks, asymmetry_fn=east_asym)
        ca, sa = math.cos(th), math.sin(th)
        p0 = (r_at * 0.90 * ca, deck_y - 0.65, r_at * 0.90 * sa)
        p1 = (r_at * 1.12 * ca, deck_y - 0.05, r_at * 1.12 * sa)
        p2 = (r_at * 1.28 * ca, deck_y + 0.68 + 0.25 * (idx % 2), r_at * 1.28 * sa)
        add_spline_tube(
            cart_mb, [p0, p1, p2], [0.46, 0.34, 0.08], sides=5,
            colors=[col_cartilage(0.72, 0.45), col_cartilage(0.90, 0.18), col_cartilage(1.0, 0.02)],
            flat=True, ellipse_ratio=0.68,
        )

    add_gripping_cliff_ribs(cart_mb, rx, rz, deck_y, n_ribs=7, seed=seed, dock_angles=docks,
                            asymmetry_fn=east_asym, hook_over_rim=0.22, reach_y=-1.2)

    # Sinewy crimson tendon roots draping from cliff shoulders and plunging to Y = -3.5
    n_roots = 14
    for ri in range(n_roots):
        th = 2.0 * math.pi * (ri + 0.25) / n_roots
        if compute_dock_suppression(th, docks, half_width=0.30) > 0.45:
            continue
        r_at = arena_footprint_radius(th, rx, rz, seed=seed, dock_angles=docks, asymmetry_fn=east_asym)
        ca, sa = math.cos(th), math.sin(th)
        sway = 0.42 * math.sin(ri * 2.1)
        pts = [
            (r_at * 0.97 * ca, deck_y - 0.15, r_at * 0.97 * sa),
            (r_at * 1.07 * ca - sa * sway, deck_y * 0.68, r_at * 1.07 * sa + ca * sway),
            (r_at * 0.96 * ca + sa * sway * 0.6, deck_y * 0.26, r_at * 0.96 * sa - ca * sway * 0.6),
            (r_at * 1.04 * ca, 0.35, r_at * 1.04 * sa),
            (r_at * 0.92 * ca, -1.6, r_at * 0.92 * sa),
            (r_at * 0.80 * ca, -3.5, r_at * 0.80 * sa),
        ]
        rad = [0.24, 0.28, 0.22, 0.26, 0.20, 0.12]
        cols = [col_vein(0.92, 0.85 - 0.1 * k, p[1]) for k, p in enumerate(pts)]
        add_spline_tube(vein_mb, pts, rad, sides=6, colors=cols, flat=False)

    vein_mb.recompute_smooth_normals()
    return [deck_mb, wall_mb, calc_mb, cart_mb, vein_mb]


def build_arena_5_west_crater_bastion():
    """Arena_West_CraterBastion.glb: rx=8.8, rz=8.8, deckY=4.0."""
    rx, rz, deck_y, seed = 8.8, 8.8, 4.0, 6.2
    docks = [0.0]  # +X bridge dock from Central Plateau

    def west_asym(th):
        return 0.05 * math.cos(3.0 * th + 1.2)

    deck_mb, wall_mb, calc_mb, thetas, radii = build_arena_island_core(
        "WestBastion", rx, rz, deck_y, seed=seed, dock_angles=docks,
        n_theta=32, rim_ridge_amp=0.38, overhang_strength=1.1, asymmetry_fn=west_asym,
    )

    cart_mb = MeshBuilder("WestBastion_JawSockets", "Mat_Cartilage")
    teeth_mb = MeshBuilder("WestBastion_RimFangs", "Mat_Teeth")
    acid_mb = MeshBuilder("WestBastion_AcidAccents", "Mat_GastricAcid")

    add_gripping_cliff_ribs(cart_mb, rx, rz, deck_y, n_ribs=8, seed=seed, dock_angles=docks,
                            asymmetry_fn=west_asym, hook_over_rim=0.25, reach_y=-1.4)

    # Fierce bone-tooth rim spikes facing West (-X, toward the giant skull across the acid!)
    n_teeth = 15
    for ti in range(n_teeth):
        # Arc concentrated along West/NW/SW rim (theta from 0.65 rad to 5.63 rad)
        th = 0.65 + (2.0 * math.pi - 1.30) * (ti / float(n_teeth - 1))
        r_at = arena_footprint_radius(th, rx, rz, seed=seed, dock_angles=docks, asymmetry_fn=west_asym) * 1.01
        ca, sa = math.cos(th), math.sin(th)
        fx, fz = r_at * ca, r_at * sa
        west_focus = max(0.0, -ca)  # peaks directly West (-X)
        # Cartilage jaw socket collar
        if ti % 2 == 0:
            add_oriented_box(
                cart_mb, (fx * 1.01, deck_y + 0.14, fz * 1.01), (1.45, 0.52, 0.95),
                yaw=-th + math.pi * 0.5,
                c_bot=col_cartilage(0.65, 0.45), c_top=col_cartilage(0.90, 0.18),
                taper_top=(0.82, 0.80),
            )
        h_fang = 1.25 + 1.35 * west_focus + 0.35 * math.sin(ti * 2.3)
        r_fang = 0.28 + 0.12 * west_focus
        add_curved_fang(
            teeth_mb, (fx, deck_y + 0.22, fz), (ca, 0.0, sa),
            height=h_fang, base_r=r_fang, lean_out=0.38 * (0.6 + west_focus), curve_in=-0.16,
        )

    # Acid seepage accents on western cliff pockets
    for i in range(4):
        th = math.pi + (i - 1.5) * 0.52
        r_at = arena_footprint_radius(th, rx, rz, seed=seed, dock_angles=docks, asymmetry_fn=west_asym) * 1.04
        add_oriented_box(
            acid_mb, (r_at * math.cos(th), 0.24, r_at * math.sin(th)), (1.15, 0.18, 0.85),
            yaw=-th, c_bot=col_acid(0.85), c_top=col_acid(1.0), taper_top=(0.72, 0.72),
        )

    return [deck_mb, wall_mb, calc_mb, cart_mb, teeth_mb, acid_mb]


def build_arena_6_north_titan_altar():
    """
    Arena_North_TitanAltar.glb: rx=15.0, rz=13.0, deckY=6.2.
    Includes elevated semi-circular stone/cartilage altar dais at Z_local in [+4.5, +11.0]
    rising softly by +0.5m (Y=6.7) via wide easy steps!
    """
    rx, rz, deck_y, seed = 15.0, 13.0, 6.2, 7.5
    docks = [-math.pi * 0.5]  # -Z grand northern stairs dock
    deck_mb, wall_mb, calc_mb, thetas, radii = build_arena_island_core(
        "NorthAltar", rx, rz, deck_y, seed=seed, dock_angles=docks,
        n_theta=40, rim_ridge_amp=0.30, overhang_strength=1.25,
    )

    cart_mb = MeshBuilder("NorthAltar_CartilageDaisAndRibs", "Mat_Cartilage")
    stone_mb = MeshBuilder("NorthAltar_StoneDaisAndTotems", "Mat_OssifiedStone")
    vein_mb = MeshBuilder("NorthAltar_TendonRoots", "Mat_VeinTendon")

    # ---- Semi-Circular Altar Dais at Z_local in [+4.5, +11.0], rising +0.5m (Y=6.7) ----
    # Center of semicircular front curve at (0, +11.0) curving south to Z=+4.5 -> Z-radius=6.5, X-radius=7.8
    # We create 3 wide concentric stepped horseshoe/semi-circular terraces from Z=+4.5..+11.0:
    # Step 1: Y=6.37 (r_scale=1.00), Step 2: Y=6.54 (r_scale=0.90), Step 3 Top: Y=6.70 (r_scale=0.80)
    dais_cz = 11.0
    dais_rx = 7.8
    dais_rz = 6.5
    n_arc = 18
    step_specs = [
        (1.00, deck_y + 0.17, deck_y - 0.05, cart_mb, True),
        (0.90, deck_y + 0.34, deck_y + 0.14, stone_mb, False),
        (0.80, deck_y + 0.50, deck_y + 0.31, stone_mb, False),
    ]

    for s_scale, y_top, y_bot, mb_target, is_cart in step_specs:
        arx = dais_rx * s_scale
        arz = dais_rz * s_scale
        # Semi-circular arc from angle pi (West: -arx, 11.0) through 1.5*pi (South: 0, 11.0 - arz) to 2*pi (East: +arx, 11.0)
        arc_pts = []
        for ai in range(n_arc + 1):
            ang = math.pi + math.pi * (ai / float(n_arc))
            px = arx * math.cos(ang)
            pz = dais_cz + arz * math.sin(ang)  # sin(pi..2pi) <= 0 so pz <= 11.0
            arc_pts.append((px, pz))

        col_fn = col_cartilage if is_cart else col_ossified_stone
        # Riser band around curved front + back chord closure
        for ai in range(n_arc):
            p0 = arc_pts[ai]
            p1 = arc_pts[ai + 1]
            mx, mz = 0.5 * (p0[0] + p1[0]), 0.5 * (p0[1] + p1[1])
            out_n = (mx, 0.0, mz - dais_cz)
            add_quad_oriented(
                mb_target,
                (p0[0], y_bot, p0[1]), (p1[0], y_bot, p1[1]),
                (p1[0], y_top, p1[1]), (p0[0], y_top, p0[1]),
                out_n,
                (ai / float(n_arc) * 4.0, 0.0), ((ai + 1) / float(n_arc) * 4.0, 0.0),
                ((ai + 1) / float(n_arc) * 4.0, 0.35), (ai / float(n_arc) * 4.0, 0.35),
                col_fn(0.70, 0.30), col_fn(0.70, 0.30), col_fn(0.92, 0.06), col_fn(0.92, 0.06),
            )
            # Top walkable fan wedge to (0, y_top, dais_cz)
            add_tri_oriented(
                mb_target,
                (0.0, y_top, dais_cz), (p0[0], y_top, p0[1]), (p1[0], y_top, p1[1]),
                (0.0, 1.0, 0.0),
                (0.5, 1.0), (0.5 + 0.5 * p0[0] / arx, (p0[1] - (dais_cz - arz)) / arz),
                (0.5 + 0.5 * p1[0] / arx, (p1[1] - (dais_cz - arz)) / arz),
                col_fn(0.95, 0.04), col_fn(0.88, 0.08), col_fn(0.88, 0.08),
            )
        # Close north chord wall at Z = 11.0
        add_quad_oriented(
            mb_target,
            (arc_pts[-1][0], y_bot, dais_cz), (arc_pts[0][0], y_bot, dais_cz),
            (arc_pts[0][0], y_top, dais_cz), (arc_pts[-1][0], y_top, dais_cz),
            (0.0, 0.0, 1.0),
            (0.0, 0.0), (4.0, 0.0), (4.0, 0.35), (0.0, 0.35),
            col_fn(0.68, 0.30), col_fn(0.68, 0.30), col_fn(0.90, 0.08), col_fn(0.90, 0.08),
        )

    # Carved sanctuary rib-totems flanking the northern altar rim (clear of central r=3.8m portal zone!)
    totem_x = [-7.2, -5.2, 5.2, 7.2]
    for ti, tx in enumerate(totem_x):
        tz = 10.6 - 0.25 * abs(tx - math.copysign(5.2, tx))
        h_tot = 3.4 if abs(tx) < 6.0 else 2.5
        add_oriented_box(
            stone_mb, (tx, deck_y + h_tot * 0.45, tz), (1.15, h_tot, 1.15),
            yaw=0.18 * (-1 if tx < 0 else 1), roll=0.10 * (-1 if tx < 0 else 1),
            c_bot=col_ossified_stone(0.62, 0.38), c_top=col_ossified_stone(0.94, 0.05),
            taper_top=(0.55, 0.55),
        )
        # Cartilage horn cap
        add_curved_fang(
            cart_mb, (tx, deck_y + h_tot * 0.86, tz), (-tx, 0.0, -1.0),
            height=1.25, base_r=0.34, lean_out=0.22, curve_in=-0.18,
        )

    # Heavy cartilage buttress ribs clutching the tall 6.2m cliff drop
    add_gripping_cliff_ribs(cart_mb, rx, rz, deck_y, n_ribs=12, seed=seed, dock_angles=docks, hook_over_rim=0.28, reach_y=-2.2)

    # Sweeping crimson tendon roots cascading down the northern and flank cliffs
    for ri in range(10):
        th = 2.0 * math.pi * (ri + 0.4) / 10.0
        if compute_dock_suppression(th, docks, half_width=0.38) > 0.35:
            continue
        r_at = arena_footprint_radius(th, rx, rz, seed=seed, dock_angles=docks)
        ca, sa = math.cos(th), math.sin(th)
        pts = [
            (r_at * 0.98 * ca, deck_y - 0.2, r_at * 0.98 * sa),
            (r_at * 1.09 * ca, deck_y * 0.62, r_at * 1.09 * sa),
            (r_at * 0.95 * ca, deck_y * 0.22, r_at * 0.95 * sa),
            (r_at * 1.03 * ca, 0.25, r_at * 1.03 * sa),
            (r_at * 0.86 * ca, -3.5, r_at * 0.86 * sa),
        ]
        add_spline_tube(
            vein_mb, pts, [0.24, 0.30, 0.22, 0.28, 0.14], sides=6,
            colors=[col_vein(0.90, 0.8 - 0.12 * k, p[1]) for k, p in enumerate(pts)], flat=False,
        )
    vein_mb.recompute_smooth_normals()

    return [deck_mb, wall_mb, calc_mb, cart_mb, stone_mb, vein_mb]


# ==============================================================================
# 6. BRIDGES & STAIRS 7 - 11
# ==============================================================================

def build_bridge_7_ossuary_stairs_south():
    """
    Bridge_OssuaryStairs_South.glb:
    Spans Z_local in [-7.0, +7.0] (14m), Y=2.0 (Z=-7) to Y=3.2 (Z=+7), width 6.4m.
    Exact nodes: Deck_Stairs (Mat_Cartilage), Parapet_Bones (Mat_OssifiedStone), UnderArch_Flesh (Mat_FleshWall).
    """
    deck_mb = MeshBuilder("Deck_Stairs", "Mat_Cartilage")
    parapet_mb = MeshBuilder("Parapet_Bones", "Mat_OssifiedStone")
    arch_mb = MeshBuilder("UnderArch_Flesh", "Mat_FleshWall")

    z0, z1 = -7.0, 7.0
    y0, y1 = 2.0, 3.2
    hw = 3.2  # total width 6.4m

    # 1. Walkable stepped treads (16 broad steps of 0.075m rise each -> effortless CharacterController climb!)
    n_steps = 16
    dz = (z1 - z0) / float(n_steps)
    dy = (y1 - y0) / float(n_steps)
    for si in range(n_steps):
        sz0 = z0 + si * dz
        sz1 = sz0 + dz
        sy_bot = y0 + si * dy - 0.42
        sy_top = y0 + (si + 1) * dy
        # Subtle worn organic asymmetry per stone step
        jit_x = 0.06 * math.sin(si * 1.9)
        c_t = col_cartilage(0.92 - 0.05 * (si % 2), 0.12 + 0.08 * math.sin(si * 0.9))
        c_b = col_cartilage(0.64, 0.42)
        add_oriented_box(
            deck_mb,
            (jit_x, 0.5 * (sy_bot + sy_top), 0.5 * (sz0 + sz1)),
            (hw * 2.0 + 0.12, sy_top - sy_bot, dz + 0.06),
            c_bot=c_b, c_top=c_t, taper_top=(0.985, 0.96),
        )

    # 2. Parapet_Bones: flanking ribbed bone walls + 4 corner ossuary posts
    for side in (-1.0, 1.0):
        px = side * (hw + 0.34)
        # 4 sloped parapet wall sections
        n_seg = 7
        for si in range(n_seg):
            t0 = si / float(n_seg)
            t1 = (si + 1) / float(n_seg)
            tm = 0.5 * (t0 + t1)
            zm = z0 + tm * (z1 - z0)
            ym = y0 + tm * (y1 - y0)
            seg_len = (z1 - z0) / float(n_seg) + 0.08
            slope_pitch = -math.atan2(y1 - y0, z1 - z0)
            # Low continuous parapet kerb
            add_oriented_box(
                parapet_mb, (px, ym + 0.36, zm), (0.62, 0.85, seg_len),
                pitch=slope_pitch,
                c_bot=col_ossified_stone(0.66, 0.35), c_top=col_ossified_stone(0.90, 0.05),
                taper_top=(0.76, 0.98),
            )
            # Vertical carved bone rib fin sticking out/up from parapet
            add_oriented_box(
                parapet_mb, (px + side * 0.12, ym + 0.65, zm), (0.42, 1.15, 0.34),
                roll=-side * 0.14,
                c_bot=col_ossified_stone(0.62, 0.38), c_top=col_ossified_stone(0.95, 0.02),
                taper_top=(0.65, 0.68),
            )
        # Corner ossuary gateway posts at Z = -6.7 and +6.7
        for pz, py in ((z0 + 0.3, y0), (z1 - 0.3, y1)):
            add_oriented_box(
                parapet_mb, (px, py + 0.95, pz), (0.95, 2.25, 0.95),
                c_bot=col_ossified_stone(0.60, 0.40), c_top=col_ossified_stone(0.96, 0.04),
                taper_top=(0.72, 0.72),
            )
            add_curved_fang(
                parapet_mb, (px, py + 2.02, pz), (side, 0.0, 0.0),
                height=0.85, base_r=0.26, lean_out=0.10, curve_in=-0.12,
            )

    # 3. UnderArch_Flesh: gothic arched organic spine + dual piers diving down to Y = -3.2
    n_arch = 16
    arch_rings = []
    for ai in range(n_arch + 1):
        t = ai / float(n_arch)
        zz = z0 + t * (z1 - z0)
        y_deck = y0 + t * (y1 - y0)
        # Arch underside curves high at center (y_deck - 0.95) and plunges to deep piers around t=0.18 and t=0.82
        pier_drop = 0.0
        for pc in (-4.6, 4.6):
            d = abs(zz - pc)
            if d < 2.4:
                w = 0.5 * (1.0 + math.cos(math.pi * d / 2.4))
                pier_drop = max(pier_drop, w * 4.8)
        y_bot = max(-3.2, (y_deck - 1.15) - pier_drop + 0.45 * math.sin(t * math.pi))
        half_w = hw + 0.55 - 0.35 * math.sin(t * math.pi)
        prof = [
            (-half_w, y_deck - 0.10),
            (-half_w * 0.92, 0.5 * (y_deck + y_bot)),
            (-half_w * 0.55, y_bot),
            (0.0, y_bot - 0.25),
            (half_w * 0.55, y_bot),
            (half_w * 0.92, 0.5 * (y_deck + y_bot)),
            (half_w, y_deck - 0.10),
        ]
        ring = []
        for pi, (px, py) in enumerate(prof):
            col = col_flesh_wall(px, py, zz, crevice_ao=0.35 if py < y_deck - 0.6 else 0.15, seed=7.0)
            ring.append(arch_mb.add_vertex((px, py, zz), (0.0, -1.0, 0.0), (pi / 6.0, t * 4.0), col))
        arch_rings.append(ring)

    n_prof = len(arch_rings[0])
    for ai in range(n_arch):
        for pi in range(n_prof - 1):
            i00 = arch_rings[ai][pi]
            i10 = arch_rings[ai + 1][pi]
            i11 = arch_rings[ai + 1][pi + 1]
            i01 = arch_rings[ai][pi + 1]
            p00 = np.array(arch_mb.positions[i00])
            p10 = np.array(arch_mb.positions[i10])
            p11 = np.array(arch_mb.positions[i11])
            p01 = np.array(arch_mb.positions[i01])
            mid = 0.25 * (p00 + p10 + p11 + p01)
            out_hint = np.array([mid[0], -0.8, 0.0], dtype=np.float32)
            if np.dot(np.cross(p10 - p00, p11 - p00), out_hint) >= 0.0:
                arch_mb.add_tri_indices(i00, i10, i11)
                arch_mb.add_tri_indices(i00, i11, i01)
            else:
                arch_mb.add_tri_indices(i00, i11, i10)
                arch_mb.add_tri_indices(i00, i01, i11)

    # Extra deep solid piers at Z = -4.6 and +4.6 plunging cleanly to Y = -3.2
    for pz in (-4.6, 4.6):
        y_top_p = y0 + ((pz - z0) / (z1 - z0)) * (y1 - y0) - 0.35
        for side in (-1.65, 1.65):
            pts = [
                (side, y_top_p, pz),
                (side * 1.10, 0.85, pz),
                (side * 1.18, 0.12, pz),
                (side * 1.14, -1.10, pz),
                (side * 0.95, -3.2, pz),
            ]
            add_spline_tube(
                arch_mb, pts, [1.55, 1.75, 1.90, 1.72, 1.35], sides=6,
                colors=[col_flesh_wall(side, p[1], pz, 0.30, 7.0) for p in pts], flat=False,
            )

    arch_mb.recompute_smooth_normals()
    return [deck_mb, parapet_mb, arch_mb]


def _vein_bridge_deck_y(z):
    """Smooth arc: Z=-7.5 -> Y=3.2, Z=0 -> Y=4.9, Z=+7.5 -> Y=4.6."""
    t = (z + 7.5) / 15.0
    return 3.2 + 5.4 * t - 4.0 * t * t


def build_bridge_8_vein_tendon_grand():
    """
    Bridge_VeinTendon_Grand.glb (inspired by Reference Photo 4):
    Spans Z_local in [-7.5, +7.5] (15m), smoothly arcing Y=3.2 -> peak 4.9 -> Y=4.6, width 7.6m.
    Sub-nodes:
      - VeinBridge_Deck (Mat_FleshFloor)
      - VeinBridge_ArchBody (Mat_FleshWall)
      - VeinBridge_ArchSkirt_Calcified (Mat_DigestedCalcified)
      - VeinBridge_HangingTendons (Mat_VeinTendon)
      - VeinBridge_PylonsAndCables (Mat_VeinTendon)
    """
    deck_mb = MeshBuilder("VeinBridge_Deck", "Mat_FleshFloor")
    arch_mb = MeshBuilder("VeinBridge_ArchBody", "Mat_FleshWall")
    calc_mb = MeshBuilder("VeinBridge_ArchSkirt_Calcified", "Mat_DigestedCalcified")
    hang_mb = MeshBuilder("VeinBridge_HangingTendons", "Mat_VeinTendon")
    pylon_mb = MeshBuilder("VeinBridge_PylonsAndCables", "Mat_VeinTendon")

    z0, z1 = -7.5, 7.5
    hw = 3.8  # walkable width 7.6m
    nz = 24
    nx_walk = 8

    # 1. VeinBridge_Deck: cambered muscle-flesh walkable deck + low fleshy side curbs
    zs = [z0 + (z1 - z0) * i / float(nz) for i in range(nz + 1)]
    xs_walk = [-hw + (2.0 * hw) * j / float(nx_walk) for j in range(nx_walk + 1)]

    grid = []
    for zi, zz in enumerate(zs):
        yd = _vein_bridge_deck_y(zz)
        row = []
        for xi, xx in enumerate(xs_walk):
            # Very subtle crown (max +0.04m in center) for organic feel while staying super smooth to walk
            camber = 0.04 * (1.0 - (xx / hw) ** 2)
            yy = yd + camber
            r_norm = abs(xx) / hw
            col = col_flesh_floor(xx, zz, r_norm * 0.88, seed=8.1)
            u = xx * 0.25
            v = zz * 0.25
            row.append(deck_mb.add_vertex((xx, yy, zz), (0.0, 1.0, 0.0), (u, v), col))
        grid.append(row)

    for zi in range(nz):
        for xi in range(nx_walk):
            i00 = grid[zi][xi]
            i10 = grid[zi][xi + 1]
            i11 = grid[zi + 1][xi + 1]
            i01 = grid[zi + 1][xi]
            # Verify +Y upward normal
            p00 = np.array(deck_mb.positions[i00])
            p10 = np.array(deck_mb.positions[i10])
            p01 = np.array(deck_mb.positions[i01])
            if np.cross(p01 - p00, p10 - p00)[1] >= 0.0:
                deck_mb.add_tri_indices(i00, i01, i10)
                deck_mb.add_tri_indices(i10, i01, i11)
            else:
                deck_mb.add_tri_indices(i00, i10, i01)
                deck_mb.add_tri_indices(i10, i11, i01)

    # Low fleshy side lip trim along X = +-hw..+-(hw+0.42)
    for side in (-1.0, 1.0):
        for zi in range(nz):
            za, zb = zs[zi], zs[zi + 1]
            ya, yb = _vein_bridge_deck_y(za), _vein_bridge_deck_y(zb)
            x_in = side * hw
            x_out = side * (hw + 0.42)
            # Top curb strip
            add_quad_oriented(
                deck_mb,
                (x_in, ya, za), (x_in, yb, zb), (x_out, yb + 0.16, zb), (x_out, ya + 0.16, za),
                (0.0, 1.0, 0.0),
                (0.0, za * 0.3), (0.0, zb * 0.3), (0.2, zb * 0.3), (0.2, za * 0.3),
                col_flesh_floor(x_in, za, 0.85, 8.1), col_flesh_floor(x_in, zb, 0.85, 8.1),
                col_flesh_floor(x_out, zb, 1.0, 8.1), col_flesh_floor(x_out, za, 1.0, 8.1),
            )
            # Outer side cheek dropping to meet arch body (assigned to arch_mb / Mat_FleshWall)
            add_quad_oriented(
                arch_mb,
                (x_out, ya + 0.16, za), (x_out, yb + 0.16, zb), (x_out * 0.96, yb - 0.52, zb), (x_out * 0.96, ya - 0.52, za),
                (side, 0.0, 0.0),
                (za * 0.3, 0.4), (zb * 0.3, 0.4), (zb * 0.3, 0.0), (za * 0.3, 0.0),
                col_flesh_wall(x_out, ya, za, 0.2), col_flesh_wall(x_out, yb, zb, 0.2),
                col_flesh_wall(x_out, ya - 0.5, za, 0.5), col_flesh_wall(x_out, yb - 0.5, zb, 0.5),
            )

    deck_mb.recompute_smooth_normals()

    # 2. VeinBridge_ArchBody & ArchSkirt_Calcified: gothic arched rib belly + massive deep piers
    arch_rows = []
    for zi, zz in enumerate(zs):
        yd = _vein_bridge_deck_y(zz)
        t = zi / float(nz)
        # Gothic pointed arch underside curvature (highest under center Z=0, plunging at Z=+-5.4 piers)
        arch_cut = 2.6 * (math.sin(t * math.pi) ** 0.75)
        y_belly = yd - 3.6 + arch_cut
        w_belly = hw + 0.40 - 0.65 * math.sin(t * math.pi)
        prof = [
            (-hw - 0.40, yd - 0.38),
            (-w_belly, 0.5 * (yd + y_belly) - 0.15),
            (-w_belly * 0.60, y_belly),
            (0.0, y_belly - 0.32),
            (w_belly * 0.60, y_belly),
            (w_belly, 0.5 * (yd + y_belly) - 0.15),
            (hw + 0.40, yd - 0.38),
        ]
        row = []
        for pi, (px, py) in enumerate(prof):
            col = col_flesh_wall(px, py, zz, crevice_ao=0.45 if pi in (2, 3, 4) else 0.18, seed=8.2)
            row.append(arch_mb.add_vertex((px, py, zz), (0.0, -1.0, 0.0), (pi / 6.0, t * 5.0), col))
        arch_rows.append(row)

    for zi in range(nz):
        for pi in range(6):
            i00 = arch_rows[zi][pi]
            i10 = arch_rows[zi + 1][pi]
            i11 = arch_rows[zi + 1][pi + 1]
            i01 = arch_rows[zi][pi + 1]
            p00 = np.array(arch_mb.positions[i00])
            p10 = np.array(arch_mb.positions[i10])
            p11 = np.array(arch_mb.positions[i11])
            p01 = np.array(arch_mb.positions[i01])
            mid = 0.25 * (p00 + p10 + p11 + p01)
            out_hint = np.array([mid[0], -1.0, 0.0], dtype=np.float32)
            if np.dot(np.cross(p10 - p00, p11 - p00), out_hint) >= 0.0:
                arch_mb.add_tri_indices(i00, i10, i11)
                arch_mb.add_tri_indices(i00, i11, i01)
            else:
                arch_mb.add_tri_indices(i00, i11, i10)
                arch_mb.add_tri_indices(i00, i01, i11)

    # Massive gothic bridge piers at Z = -5.5 and +5.5 (left & right legs diving to Y=-3.2)
    for pz in (-5.5, 5.5):
        yd = _vein_bridge_deck_y(pz)
        for side in (-1.0, 1.0):
            px = side * 3.15
            # Upper flesh pier
            pier_pts = [
                (px, yd - 0.15, pz),
                (px * 1.12, 1.8, pz),
                (px * 1.18, 0.4, pz),
                (px * 0.96, -3.2, pz),
            ]
            add_spline_tube(
                arch_mb, pier_pts, [1.55, 1.75, 1.95, 1.45], sides=6,
                colors=[col_flesh_wall(px, p[1], pz, 0.25, 8.2) for p in pier_pts], flat=False,
            )
            # Lower encrusted calcified acid-skirt collar (Y=1.2 down to Y=-3.2)
            skirt_pts = [
                (px * 1.08, 1.15, pz),
                (px * 1.24, 0.15, pz),
                (px * 1.10, -1.55, pz),
                (px * 0.92, -3.20, pz),
            ]
            add_spline_tube(
                calc_mb, skirt_pts, [1.62, 2.15, 1.92, 1.55], sides=6,
                colors=[col_calcified(px, p[1], pz, 0.25, 8.3) for p in skirt_pts], flat=False,
            )

    arch_mb.recompute_smooth_normals()
    calc_mb.recompute_smooth_normals()

    # 3. VeinBridge_HangingTendons: dozens of catenary muscle veins, webs & dripping cords!
    # A) Longitudinal sweeping catenary veins under the arch (5 parallel tracks across X)
    for xi, vx in enumerate((-3.4, -2.0, 0.0, 2.0, 3.4)):
        sag = 2.4 + 0.55 * (1.0 - abs(vx) / 3.8)
        pts_a = sample_catenary_points(
            (vx, _vein_bridge_deck_y(-5.6) - 0.65, -5.6),
            (vx, _vein_bridge_deck_y(5.6) - 0.65, 5.6),
            sag=sag, n_segs=14, sway_x=0.35 * math.sin(xi * 1.7), sway_freq=2.0, phase=xi,
        )
        rad_a = [0.24 - 0.07 * math.sin(k / 14.0 * math.pi) for k in range(len(pts_a))]
        add_spline_tube(
            hang_mb, pts_a, rad_a, sides=6,
            colors=[col_vein(1.00, 0.95 - 0.25 * math.sin(k / 14.0 * math.pi), p[1]) for k, p in enumerate(pts_a)],
            flat=False,
        )

    # B) Transverse & diagonal criss-crossing vein webs hanging beneath the central arch
    web_pairs = [
        ((-3.8, -4.8), (3.8, -1.8), 1.85),
        ((3.8, -4.8), (-3.8, -1.8), 1.95),
        ((-3.8, -2.2), (3.8, 2.2), 2.35),
        ((3.8, -2.2), (-3.8, 2.2), 2.45),
        ((-3.8, 1.8), (3.8, 4.8), 1.90),
        ((3.8, 1.8), (-3.8, 4.8), 2.05),
        ((-3.9, 0.0), (3.9, 0.0), 2.20),
        ((-3.9, -3.4), (3.9, -3.4), 1.65),
        ((-3.9, 3.4), (3.9, 3.4), 1.70),
    ]
    for wi, ((x0, zz0), (x1, zz1), sag) in enumerate(web_pairs):
        p0 = (x0, _vein_bridge_deck_y(zz0) - 0.45, zz0)
        p1 = (x1, _vein_bridge_deck_y(zz1) - 0.45, zz1)
        pts = sample_catenary_points(p0, p1, sag=sag, n_segs=10, sway_x=0.22, sway_freq=1.5, phase=wi)
        r_mid = 0.14 + 0.04 * (wi % 3)
        rad = [r_mid * (1.2 - 0.45 * math.sin(k / 10.0 * math.pi)) for k in range(len(pts))]
        add_spline_tube(
            hang_mb, pts, rad, sides=5,
            colors=[col_vein(0.86, 0.65, p[1]) for p in pts], flat=False,
        )

    # C) Side-flank festooned blood cords + vertical dripping tendrils dangling almost to acid Y=0.1
    for side in (-1.0, 1.0):
        for zi_pair in ((-7.2, -5.5), (-5.5, 0.0), (0.0, 5.5), (5.5, 7.2)):
            za, zb = zi_pair
            pts = sample_catenary_points(
                (side * 4.05, _vein_bridge_deck_y(za) - 0.35, za),
                (side * 4.05, _vein_bridge_deck_y(zb) - 0.35, zb),
                sag=1.45 if abs(zb - za) > 3.0 else 0.75, n_segs=10, sway_x=0.18 * side,
            )
            add_spline_tube(
                hang_mb, pts, [0.16] * len(pts), sides=5,
                colors=[col_vein(0.90, 0.75, p[1]) for p in pts], flat=False,
            )
        # 10 dripping vertical tendon stalactites per side hanging down near Y = 0.15
        for di in range(10):
            zz = -6.2 + di * 1.38
            yd = _vein_bridge_deck_y(zz)
            x_hang = side * (3.6 + 0.45 * math.sin(di * 1.9))
            y_end = 0.15 + 0.45 * ((di % 3) * 0.5)
            pts_d = [
                (x_hang, yd - 0.55, zz),
                (x_hang + side * 0.25, 0.5 * (yd + y_end), zz + 0.18 * math.cos(di)),
                (x_hang - side * 0.10, y_end, zz),
            ]
            add_spline_tube(
                hang_mb, pts_d, [0.16, 0.11, 0.04], sides=5,
                colors=[col_vein(0.82, 0.7, p[1]) for p in pts_d], flat=False,
            )

    hang_mb.recompute_smooth_normals()

    # 4. VeinBridge_PylonsAndCables: 6 towering veined pylons (~7.5m above deck) + overhead suspension veins
    pylon_z_list = (-5.5, 0.0, 5.5)
    pylon_tops = {}  # (side, pz) -> top_pos

    for pz in pylon_z_list:
        yd = _vein_bridge_deck_y(pz)
        for side in (-1.0, 1.0):
            px = side * 4.28
            top_y = yd + 7.65
            pylon_tops[(side, pz)] = (px * 0.98, top_y - 0.25, pz)

            # Central thick veined core spire rising from pier up to +7.65m above deck
            core_pts = [
                (px, yd - 1.8, pz),
                (px * 1.02, yd + 0.8, pz),
                (px * 1.00, yd + 3.5, pz),
                (px * 0.98, yd + 6.0, pz),
                (px * 0.96, top_y, pz),
            ]
            add_spline_tube(
                pylon_mb, core_pts, [0.72, 0.56, 0.44, 0.34, 0.14], sides=6,
                colors=[col_vein(0.75 + 0.05 * k, 0.55 + 0.1 * k, p[1]) for k, p in enumerate(core_pts)],
                flat=False,
            )

            # 3 twisting braided tendon strands spiralling up each pylon
            for si in range(3):
                ang0 = 2.0 * math.pi * si / 3.0
                twist_pts = []
                twist_r = []
                for k in range(9):
                    kt = k / 8.0
                    yy = (yd - 0.8) + kt * 8.3
                    ang = ang0 + kt * math.pi * 1.8 * side
                    rad_off = 0.52 * (1.0 - 0.65 * kt)
                    twist_pts.append((px + rad_off * math.cos(ang), yy, pz + rad_off * math.sin(ang)))
                    twist_r.append(0.22 * (1.0 - 0.55 * kt) + 0.05)
                add_spline_tube(
                    pylon_mb, twist_pts, twist_r, sides=5,
                    colors=[col_vein(0.95, 0.85, p[1]) for p in twist_pts], flat=False,
                )

    # Overhead longitudinal catenary suspension cables connecting pylon tops along each side
    for side in (-1.0, 1.0):
        # End deck anchor -> pylon -5.5 -> pylon 0 -> pylon +5.5 -> end deck anchor
        end_s = (side * 4.08, _vein_bridge_deck_y(-7.35) + 0.85, -7.35)
        end_n = (side * 4.08, _vein_bridge_deck_y(7.35) + 0.85, 7.35)
        t_s = pylon_tops[(side, -5.5)]
        t_m = pylon_tops[(side, 0.0)]
        t_n = pylon_tops[(side, 5.5)]
        spans = [
            (end_s, t_s, 0.85),
            (t_s, t_m, 1.55),
            (t_m, t_n, 1.55),
            (t_n, end_n, 0.85),
        ]
        for p_a, p_b, sag in spans:
            pts = sample_catenary_points(p_a, p_b, sag=sag, n_segs=10)
            add_spline_tube(
                pylon_mb, pts, [0.16] * len(pts), sides=5,
                colors=[col_vein(0.94, 0.85) for _ in pts], flat=False,
            )

        # Low safety handrail vein running continuously along deck side at deck_y + 1.0m
        rail_pts = [
            (side * 4.02, _vein_bridge_deck_y(zz) + 0.98, zz)
            for zz in np.linspace(-7.45, 7.45, 16)
        ]
        add_spline_tube(
            pylon_mb, rail_pts, [0.13] * len(rail_pts), sides=5,
            colors=[col_vein(0.88, 0.72) for _ in rail_pts], flat=False,
        )

        # Diagonal suspender cables from pylon tops down to deck edges
        for pz in pylon_z_list:
            top_p = pylon_tops[(side, pz)]
            for dz in (-2.4, -1.2, 1.2, 2.4):
                zz = max(-7.3, min(7.3, pz + dz))
                deck_p = (side * 4.02, _vein_bridge_deck_y(zz) + 0.15, zz)
                pts = sample_catenary_points(top_p, deck_p, sag=0.22, n_segs=6)
                add_spline_tube(
                    pylon_mb, pts, [0.09] * len(pts), sides=4,
                    colors=[col_vein(0.86, 0.65) for _ in pts], flat=False,
                )

    # Overhead gothic cross-arch tendons linking left & right pylons high above deck (~7m clearance)
    for pz in pylon_z_list:
        pL = pylon_tops[(-1.0, pz)]
        pR = pylon_tops[(1.0, pz)]
        # Arch slightly upward/sagging double-catenary gothic rib overhead
        pts_arch = []
        for k in range(11):
            t = k / 10.0
            xx = pL[0] * (1.0 - t) + pR[0] * t
            yy = pL[1] * (1.0 - t) + pR[1] * t - 1.15 * 4.0 * t * (1.0 - t)
            pts_arch.append((xx, yy, pz))
        add_spline_tube(
            pylon_mb, pts_arch, [0.18 - 0.05 * math.sin(k / 10.0 * math.pi) for k in range(11)], sides=5,
            colors=[col_vein(0.92, 0.78) for _ in pts_arch], flat=False,
        )

    pylon_mb.recompute_smooth_normals()
    return [deck_mb, arch_mb, calc_mb, hang_mb, pylon_mb]


def build_bridge_9_plank_span_east():
    """
    Bridge_PlankSpan_East.glb (inspired by Photo 5 suspension bridge):
    Spans X_local in [-4.5, +4.5] (9m) from Y=4.6 (X=-4.5) to Y=5.4 (X=+4.5), width 5.6m.
    Nodes: PlankBridge_Deck (Mat_Cartilage), PlankBridge_TendonCables (Mat_VeinTendon).
    """
    deck_mb = MeshBuilder("PlankBridge_Deck", "Mat_Cartilage")
    cable_mb = MeshBuilder("PlankBridge_TendonCables", "Mat_VeinTendon")

    x0, x1 = -4.5, 4.5
    y0, y1 = 4.6, 5.4
    hw = 2.8  # width 5.6m (Z in [-2.8, +2.8])

    def deck_y(x):
        t = (x - x0) / (x1 - x0)
        # Subtle suspension sag (-0.12m at center) while matching exact endpoints
        return y0 + (y1 - y0) * t - 0.12 * 4.0 * t * (1.0 - t)

    # 1. Chunky asymmetric cartilage bone planks slightly spaced (~0.45m plank + 0.11m gap)
    n_planks = 16
    dx = (x1 - x0) / float(n_planks)
    for pi in range(n_planks):
        xc = x0 + (pi + 0.5) * dx
        yc = deck_y(xc)
        slope_roll = math.atan2(deck_y(xc + 0.25) - deck_y(xc - 0.25), 0.50)
        yaw_jit = 0.028 * math.sin(pi * 2.7)
        len_jit = 0.22 * math.cos(pi * 1.9)
        w_plank = dx * 0.80  # ~0.45m plank with 0.11m visual gap
        add_oriented_box(
            deck_mb,
            (xc, yc - 0.09, 0.08 * math.sin(pi * 2.1)),
            (w_plank, 0.20, hw * 2.0 + len_jit),
            yaw=yaw_jit, roll=slope_roll,
            c_bot=col_cartilage(0.64, 0.42), c_top=col_cartilage(0.95 - 0.06 * (pi % 3), 0.10),
            taper_top=(0.94, 0.96),
        )

    # Two sturdy cartilage longitudinal stringer beams beneath the planks
    for sz in (-1.85, 1.85):
        pts = [(xx, deck_y(xx) - 0.26, sz) for xx in np.linspace(x0, x1, 10)]
        add_spline_tube(
            deck_mb, pts, [0.22] * len(pts), sides=5,
            colors=[col_cartilage(0.72, 0.32) for _ in pts], flat=True,
        )

    # 2. PlankBridge_TendonCables: 6 side stanchions + 4 sagging crimson tendon ropes + suspender ties
    post_xs = (-4.30, 0.0, 4.30)
    for side in (-1.0, 1.0):
        pz = side * (hw + 0.18)
        tops = []
        mids = []
        for pxi, px in enumerate(post_xs):
            py = deck_y(px)
            h_post = 2.05 if pxi != 1 else 1.55
            p_bot = (px, py - 0.35, pz)
            p_mid = (px, py + 0.95, pz)
            p_top = (px, py + h_post, pz)
            add_spline_tube(
                cable_mb, [p_bot, p_mid, p_top], [0.22, 0.18, 0.12], sides=5,
                colors=[col_vein(0.80, 0.55), col_vein(0.90, 0.78), col_vein(0.96, 0.92)],
                flat=False,
            )
            if pxi != 1:
                # Bone finial cap gripping the tendon stanchion top
                add_oriented_box(
                    deck_mb, (px, py + h_post + 0.05, pz), (0.34, 0.24, 0.34),
                    c_bot=col_cartilage(0.75, 0.30), c_top=col_cartilage(0.96, 0.04),
                    taper_top=(0.55, 0.55),
                )
            tops.append(p_top)
            mids.append(p_mid)

        # Under-deck stabilizing catenary tendon chord
        under_pts = sample_catenary_points(
            (x0 + 0.25, deck_y(x0 + 0.25) - 0.35, side * 1.85),
            (x1 - 0.25, deck_y(x1 - 0.25) - 0.35, side * 1.85),
            sag=0.85, n_segs=10,
        )
        add_spline_tube(cable_mb, under_pts, [0.11] * len(under_pts), sides=5,
                        colors=[col_vein(0.88, 0.72) for _ in under_pts], flat=False)

        # Upper sweeping suspension tendon ropes + lower handrail tendon ropes
        for span_i in range(2):
            pts_up = sample_catenary_points(tops[span_i], tops[span_i + 1], sag=0.45, n_segs=10)
            pts_lo = sample_catenary_points(mids[span_i], mids[span_i + 1], sag=0.14, n_segs=10)
            add_spline_tube(cable_mb, pts_up, [0.13] * len(pts_up), sides=5,
                            colors=[col_vein(0.94, 0.88) for _ in pts_up], flat=False)
            add_spline_tube(cable_mb, pts_lo, [0.10] * len(pts_lo), sides=5,
                            colors=[col_vein(0.88, 0.72) for _ in pts_lo], flat=False)
            # Vertical suspender tendon ties linking upper cable -> plank edges
            for k in (2, 5, 8):
                pt_top = pts_up[k]
                pt_deck = (pt_top[0], deck_y(pt_top[0]) - 0.08, pz * 0.95)
                add_spline_tube(cable_mb, [pt_top, pt_deck], [0.06, 0.08], sides=4,
                                colors=[col_vein(0.84, 0.65), col_vein(0.75, 0.5)], flat=False)

    cable_mb.recompute_smooth_normals()
    return [deck_mb, cable_mb]


def build_bridge_10_stair_span_west():
    """
    Bridge_StairSpan_West.glb:
    Spans X_local in [-4.5, +4.5] from Y=4.6 (X=+4.5) down to Y=4.0 (X=-4.5), width 5.6m.
    Chunky calcified stone/cartilage staircase (Mat_Cartilage) with under-ribs (Mat_FleshWall).
    """
    stairs_mb = MeshBuilder("WestBridge_Stairs", "Mat_Cartilage")
    ribs_mb = MeshBuilder("WestBridge_UnderRibs", "Mat_FleshWall")

    x0, x1 = -4.5, 4.5
    y0, y1 = 4.0, 4.6  # X=-4.5 -> 4.0, X=+4.5 -> 4.6
    hw = 2.8  # width 5.6m

    # 10 broad chunky steps ascending from West (-4.5, Y=4.0) to East (+4.5, Y=4.6)
    n_steps = 10
    dx = (x1 - x0) / float(n_steps)
    dy = (y1 - y0) / float(n_steps)
    for si in range(n_steps):
        sx0 = x0 + si * dx
        sx1 = sx0 + dx
        sy_top = y0 + (si + 1) * dy
        sy_bot = y0 + si * dy - 0.45
        add_oriented_box(
            stairs_mb,
            (0.5 * (sx0 + sx1), 0.5 * (sy_bot + sy_top), 0.05 * math.cos(si * 2.1)),
            (dx + 0.06, sy_top - sy_bot, hw * 2.0 + 0.14),
            c_bot=col_cartilage(0.65, 0.42), c_top=col_cartilage(0.94 - 0.04 * (si % 2), 0.10),
            taper_top=(0.96, 0.985),
        )

    # Notched low bone side curbs
    for side in (-1.0, 1.0):
        for ki in range(5):
            kx = x0 + (ki + 0.5) * ((x1 - x0) / 5.0)
            ky = y0 + ((kx - x0) / (x1 - x0)) * (y1 - y0)
            add_oriented_box(
                stairs_mb, (kx, ky + 0.25, side * (hw + 0.22)), (1.65, 0.68, 0.48),
                roll=math.atan2(y1 - y0, x1 - x0),
                c_bot=col_cartilage(0.62, 0.45), c_top=col_cartilage(0.92, 0.08),
                taper_top=(0.94, 0.72),
            )

    # Curved organic flesh under-ribs + end piers cradling the stair span
    for rz in (-2.1, -0.7, 0.7, 2.1):
        pts = [
            (-4.2, y0 - 1.5, rz),
            (-2.0, y0 - 0.55, rz),
            (0.0, 0.5 * (y0 + y1) - 0.38, rz),
            (2.0, y1 - 0.55, rz),
            (4.2, y1 - 1.5, rz),
        ]
        add_spline_tube(
            ribs_mb, pts, [0.55, 0.42, 0.36, 0.42, 0.55], sides=6,
            colors=[col_flesh_wall(p[0], p[1], rz, 0.32, 10.0) for p in pts], flat=False,
        )
    # Two strong flesh piers at X = -3.3 and +3.3 plunging down to Y = -3.2 (with acid-rim ring at Y=0.1)
    for px, py in ((-3.3, y0 - 0.4), (3.3, y1 - 0.4)):
        for side in (-1.5, 1.5):
            pts = [
                (px, py, side),
                (px, 1.4, side * 1.06),
                (px, 0.15, side * 1.14),
                (px * 0.96, -1.5, side * 1.02),
                (px * 0.92, -3.2, side * 0.92),
            ]
            add_spline_tube(
                ribs_mb, pts, [1.05, 1.20, 1.32, 1.12, 0.88], sides=6,
                colors=[col_flesh_wall(px, p[1], side, 0.28, 10.1) for p in pts], flat=False,
            )

    ribs_mb.recompute_smooth_normals()
    return [stairs_mb, ribs_mb]


def build_bridge_11_stairs_north():
    """
    Bridge_Stairs_North.glb:
    Spans Z_local in [-8.0, +8.0] (16m) climbing from Y=4.6 (Z=-8) to Y=6.2 (Z=+8), width 7.2m.
    Grand ascending ossuary stairs (Mat_OssifiedStone & Mat_Cartilage) with twin monumental
    flanking stone pylons (Mat_OssifiedStone) and acid-stained foundation plunging to Y=-3.2.
    """
    treads_mb = MeshBuilder("NorthStairs_Treads", "Mat_Cartilage")
    pylons_mb = MeshBuilder("NorthStairs_PylonsAndParapets", "Mat_OssifiedStone")
    found_mb = MeshBuilder("NorthStairs_Foundation", "Mat_FleshWall")
    calc_mb = MeshBuilder("NorthStairs_Foundation_Calcified", "Mat_DigestedCalcified")

    z0, z1 = -8.0, 8.0
    y0, y1 = 4.6, 6.2
    hw = 3.6  # width 7.2m

    # 1. Grand ascending stepped treads (18 broad steps, ~0.089m rise per step)
    n_steps = 18
    dz = (z1 - z0) / float(n_steps)
    dy = (y1 - y0) / float(n_steps)
    for si in range(n_steps):
        sz0 = z0 + si * dz
        sz1 = sz0 + dz
        sy_top = y0 + (si + 1) * dy
        sy_bot = y0 + si * dy - 0.55
        add_oriented_box(
            treads_mb,
            (0.0, 0.5 * (sy_bot + sy_top), 0.5 * (sz0 + sz1)),
            (hw * 2.0 + 0.16, sy_top - sy_bot, dz + 0.08),
            c_bot=col_cartilage(0.66, 0.40), c_top=col_cartilage(0.95 - 0.04 * (si % 2), 0.08),
            taper_top=(0.985, 0.96),
        )

    # 2. Monumental flanking stone parapets & twin pairs of towering ossuary pylons
    slope_pitch = -math.atan2(y1 - y0, z1 - z0)
    for side in (-1.0, 1.0):
        px = side * (hw + 0.48)
        # Sloped massive stone parapet walls
        for si in range(8):
            t = (si + 0.5) / 8.0
            zm = z0 + t * (z1 - z0)
            ym = y0 + t * (y1 - y0)
            add_oriented_box(
                pylons_mb, (px, ym + 0.52, zm), (0.92, 1.35, (z1 - z0) / 8.0 + 0.12),
                pitch=slope_pitch,
                c_bot=col_ossified_stone(0.62, 0.36), c_top=col_ossified_stone(0.92, 0.05),
                taper_top=(0.78, 0.98),
            )

        # Twin monumental flanking stone pylons at South approach (Z=-6.6) & North summit (Z=+6.6)
        for pzi, pz in enumerate((-6.6, 6.6)):
            py = y0 + ((pz - z0) / (z1 - z0)) * (y1 - y0)
            h_pylon = 3.8 if pzi == 0 else 4.5
            # Stepped base plinth
            add_oriented_box(
                pylons_mb, (px + side * 0.15, py + 0.75, pz), (1.75, 2.2, 1.75),
                c_bot=col_ossified_stone(0.58, 0.40), c_top=col_ossified_stone(0.86, 0.12),
                taper_top=(0.86, 0.86),
            )
            # Towering tapered ossuary obelisk
            add_oriented_box(
                pylons_mb, (px + side * 0.15, py + 1.5 + h_pylon * 0.45, pz), (1.35, h_pylon, 1.35),
                c_bot=col_ossified_stone(0.72, 0.25), c_top=col_ossified_stone(0.96, 0.02),
                taper_top=(0.52, 0.52),
            )
            # Crown bone spike
            add_curved_fang(
                treads_mb, (px + side * 0.15, py + 1.5 + h_pylon * 0.88, pz), (-side, 0.0, 0.0),
                height=1.15, base_r=0.34, lean_out=0.08, curve_in=-0.16,
            )

    # 3. Massive acid-stained organic foundation & piers plunging to Y = -3.2
    for pz in (-5.2, 0.0, 5.2):
        py = y0 + ((pz - z0) / (z1 - z0)) * (y1 - y0)
        for side in (-2.3, 2.3):
            # Upper flesh pier
            f_pts = [(side, py - 0.3, pz), (side * 1.10, 2.2, pz), (side * 1.15, 0.6, pz)]
            add_spline_tube(
                found_mb, f_pts, [1.65, 1.85, 1.95], sides=6,
                colors=[col_flesh_wall(side, p[1], pz, 0.30, 11.0) for p in f_pts], flat=False,
            )
            # Lower calcified acid-stained foundation plunging to Y = -3.2
            c_pts = [(side * 1.14, 1.1, pz), (side * 1.24, 0.1, pz), (side * 1.05, -1.6, pz), (side * 0.88, -3.2, pz)]
            add_spline_tube(
                calc_mb, c_pts, [1.90, 2.25, 2.05, 1.65], sides=6,
                colors=[col_calcified(side, p[1], pz, 0.28, 11.1) for p in c_pts], flat=False,
            )

    # Arched underbelly connecting piers along Z (strictly below the 0.55m stair core!)
    for side in (-1.8, 0.0, 1.8):
        rib_pts = []
        rib_r = []
        for k in range(11):
            t = k / 10.0
            zz = (z0 + 0.45) + t * ((z1 - z0) - 0.90)
            yy = y0 + ((zz - z0) / (z1 - z0)) * (y1 - y0) - 1.72 - 0.55 * math.cos(t * math.pi * 2.0)
            rib_pts.append((side, yy, zz))
            rib_r.append(0.55 + 0.35 * math.sin(t * math.pi))
        add_spline_tube(
            found_mb, rib_pts, rib_r, sides=6,
            colors=[col_flesh_wall(side, p[1], p[2], 0.35, 11.2) for p in rib_pts], flat=False,
        )

    found_mb.recompute_smooth_normals()
    calc_mb.recompute_smooth_normals()
    return [treads_mb, pylons_mb, found_mb, calc_mb]


# ==============================================================================
# 7. RITUAL PROP 12 & EXIT PORTAL 13
# ==============================================================================

def build_prop_12_ritual_circle_center():
    """
    Prop_RitualCircle_Center.glb (inspired by Photo 5 top plateau circle):
    Origin at bottom-center Y=0.
    8 radial trapezoidal carved stone slabs (outerRadius=5.2m, innerRadius=2.2m, height=0.14m)
    arranged asymmetrically/slightly tilted with dark marrow-blood cracks & center bone medallion.
    """
    stone_mb = MeshBuilder("RitualCircle_StoneSlabs", "Mat_OssifiedStone")
    cart_mb = MeshBuilder("RitualCircle_BoneMedallion", "Mat_Cartilage")
    marrow_mb = MeshBuilder("RitualCircle_MarrowCracks", "Mat_VeinTendon")
    acid_mb = MeshBuilder("RitualCircle_SigilInlay", "Mat_GastricAcid")

    r_in = 2.2
    r_out = 5.2
    h_base = 0.14
    n_slabs = 8

    # 1. Dark marrow-blood recessed crack bed beneath/between the slabs (Y = 0.0 .. 0.055)
    c_m = marrow_mb.add_vertex((0.0, 0.05, 0.0), (0.0, 1.0, 0.0), (0.5, 0.5), col_vein(0.55, 0.25))
    n_mr = 24
    ring_m = []
    for i in range(n_mr):
        th = 2.0 * math.pi * i / float(n_mr)
        x = (r_out - 0.12) * math.cos(th)
        z = (r_out - 0.12) * math.sin(th)
        ring_m.append(marrow_mb.add_vertex((x, 0.045, z), (0.0, 1.0, 0.0), (x * 0.25, z * 0.25), col_vein(0.45, 0.15)))
    for i in range(n_mr):
        ni = (i + 1) % n_mr
        p0 = np.array(marrow_mb.positions[c_m])
        p1 = np.array(marrow_mb.positions[ring_m[ni]])
        p2 = np.array(marrow_mb.positions[ring_m[i]])
        if np.cross(p1 - p0, p2 - p0)[1] >= 0.0:
            marrow_mb.add_tri_indices(c_m, ring_m[ni], ring_m[i])
        else:
            marrow_mb.add_tri_indices(c_m, ring_m[i], ring_m[ni])

    # 2. 8 radial trapezoidal carved stone slabs (with subtle asymmetry, bevel & cracks)
    sector = 2.0 * math.pi / float(n_slabs)
    gap = 0.068  # crisp ~4 deg radial marrow crack between slabs
    for si in range(n_slabs):
        th_c = si * sector + 0.015 * math.sin(si * 2.3)
        th0 = th_c - sector * 0.5 + gap
        th1 = th_c + sector * 0.5 - gap
        # Subtle asymmetry per slab
        h_s = h_base + 0.025 * math.sin(si * 1.9)
        tilt_out = 0.018 * math.cos(si * 2.7)
        ri_s = r_in + 0.05 * math.sin(si * 3.1)
        ro_s = r_out + 0.08 * math.cos(si * 1.7)

        # Slightly tapered top corners for crisp PS1 beveled stone look
        inset_a = 0.012
        b0 = (ri_s * math.cos(th0), 0.0, ri_s * math.sin(th0))
        b1 = (ri_s * math.cos(th1), 0.0, ri_s * math.sin(th1))
        b2 = (ro_s * math.cos(th1), 0.0, ro_s * math.sin(th1))
        b3 = (ro_s * math.cos(th0), 0.0, ro_s * math.sin(th0))

        t0 = ((ri_s + 0.07) * math.cos(th0 + inset_a), h_s - tilt_out * 0.5, (ri_s + 0.07) * math.sin(th0 + inset_a))
        t1 = ((ri_s + 0.07) * math.cos(th1 - inset_a), h_s - tilt_out * 0.5, (ri_s + 0.07) * math.sin(th1 - inset_a))
        t2 = ((ro_s - 0.09) * math.cos(th1 - inset_a), h_s + tilt_out, (ro_s - 0.09) * math.sin(th1 - inset_a))
        t3 = ((ro_s - 0.09) * math.cos(th0 + inset_a), h_s + tilt_out, (ro_s - 0.09) * math.sin(th0 + inset_a))

        c_b = col_ossified_stone(0.58, 0.45)
        c_t_in = col_ossified_stone(0.92, 0.06)
        c_t_out = col_ossified_stone(0.82, 0.16)
        add_hex_prism_corners(
            stone_mb,
            [b0, b1, b2, b3], [t0, t1, t2, t3],
            c_bot=[c_b, c_b, c_b, c_b],
            c_top=[c_t_in, c_t_in, c_t_out, c_t_out],
            uv_scale=0.32,
        )

    # 3. Center bone medallion (r <= 2.02m, walkable low relief <= 0.15m)
    n_med = 16
    med_r_outer = 2.02
    med_r_inner = 1.18
    med_y_outer = 0.11
    med_y_center = 0.14

    c_med = cart_mb.add_vertex((0.0, med_y_center, 0.0), (0.0, 1.0, 0.0), (0.5, 0.5), col_cartilage(0.98, 0.05))
    ring_in = []
    ring_out = []
    ring_bot = []
    for i in range(n_med):
        th = 2.0 * math.pi * i / float(n_med)
        ca, sa = math.cos(th), math.sin(th)
        ring_in.append(cart_mb.add_vertex(
            (med_r_inner * ca, med_y_center - 0.02, med_r_inner * sa), (0.0, 1.0, 0.0),
            (0.5 + 0.3 * ca, 0.5 + 0.3 * sa), col_cartilage(0.84, 0.25),
        ))
        ring_out.append(cart_mb.add_vertex(
            (med_r_outer * ca, med_y_outer, med_r_outer * sa), (0.0, 1.0, 0.0),
            (0.5 + 0.5 * ca, 0.5 + 0.5 * sa), col_cartilage(0.92, 0.12),
        ))
        ring_bot.append(cart_mb.add_vertex(
            ((med_r_outer + 0.08) * ca, 0.0, (med_r_outer + 0.08) * sa), (ca, 0.0, sa),
            (i / float(n_med), 0.0), col_cartilage(0.62, 0.45),
        ))

    for i in range(n_med):
        ni = (i + 1) % n_med
        # Inner disc (+Y CCW)
        p0 = np.array(cart_mb.positions[c_med])
        p1 = np.array(cart_mb.positions[ring_in[ni]])
        p2 = np.array(cart_mb.positions[ring_in[i]])
        if np.cross(p1 - p0, p2 - p0)[1] >= 0.0:
            cart_mb.add_tri_indices(c_med, ring_in[ni], ring_in[i])
        else:
            cart_mb.add_tri_indices(c_med, ring_in[i], ring_in[ni])

        # Outer ring top
        a0, a1 = ring_in[i], ring_in[ni]
        b1, b0 = ring_out[ni], ring_out[i]
        pa0 = np.array(cart_mb.positions[a0])
        pa1 = np.array(cart_mb.positions[a1])
        pb0 = np.array(cart_mb.positions[b0])
        if np.cross(pa1 - pa0, pb0 - pa0)[1] >= 0.0:
            cart_mb.add_tri_indices(a0, a1, b1)
            cart_mb.add_tri_indices(a0, b1, b0)
        else:
            cart_mb.add_tri_indices(a0, b1, a1)
            cart_mb.add_tri_indices(a0, b0, b1)

        # Outer bevel skirt down to Y=0
        c0, c1 = ring_out[i], ring_out[ni]
        d1, d0 = ring_bot[ni], ring_bot[i]
        pc0 = np.array(cart_mb.positions[c0])
        pc1 = np.array(cart_mb.positions[c1])
        pd0 = np.array(cart_mb.positions[d0])
        mid = 0.5 * (pc0 + pc1)
        if np.dot(np.cross(pc1 - pc0, pd0 - pc0), np.array([mid[0], 0.2, mid[2]])) >= 0.0:
            cart_mb.add_tri_indices(c0, c1, d1)
            cart_mb.add_tri_indices(c0, d1, d0)
        else:
            cart_mb.add_tri_indices(c0, d1, c1)
            cart_mb.add_tri_indices(c0, d0, d1)

    # Ornate carved inner bone ring + 4 cardinal spokes + central gastric-acid ritual sigil
    for i in range(n_med):
        th = 2.0 * math.pi * (i + 0.5) / float(n_med)
        add_oriented_box(
            stone_mb, (1.62 * math.cos(th), med_y_outer + 0.015, 1.62 * math.sin(th)),
            (0.62, 0.055, 0.32), yaw=-th + math.pi * 0.5,
            c_bot=col_ossified_stone(0.65, 0.35), c_top=col_ossified_stone(0.92, 0.05),
            taper_top=(0.86, 0.80),
        )
    for k in range(4):
        th = k * math.pi * 0.5
        add_oriented_box(
            cart_mb, (0.98 * math.cos(th), med_y_center + 0.012, 0.98 * math.sin(th)),
            (1.15, 0.05, 0.24), yaw=-th,
            c_bot=col_cartilage(0.75, 0.30), c_top=col_cartilage(0.98, 0.04),
            taper_top=(0.90, 0.78),
        )
    # Central glowing sigil ring/core flush at Y = 0.145
    for k in range(8):
        th = k * math.pi * 0.25
        add_oriented_box(
            acid_mb, (0.48 * math.cos(th), med_y_center + 0.008, 0.48 * math.sin(th)),
            (0.24, 0.035, 0.16), yaw=-th + math.pi * 0.5,
            c_bot=col_acid(0.85), c_top=col_acid(1.0), taper_top=(0.78, 0.78),
        )
    add_oriented_box(
        acid_mb, (0.0, med_y_center + 0.010, 0.0), (0.34, 0.035, 0.34),
        yaw=0.785, c_bot=col_acid(0.9), c_top=col_acid(1.0), taper_top=(0.75, 0.75),
    )

    return [stone_mb, cart_mb, marrow_mb, acid_mb]


def build_prop_13_portal_exit_gate():
    """
    Portal_Exit_Gate.glb (inspired by Photo 5 bottom-right glowing portal + visceral veins):
    Origin at bottom-center Y=0, front faces +Z.
      - Stepped stone/bone circular plinth (radius=3.4m, height=0.55m)
      - Colossal carved ring archway (radius=2.9m, top Y=6.8m, front faces +Z)
        made of segmented ossified stone (Mat_OssifiedStone) wrapped with creeping crimson veins (Mat_VeinTendon)
      - Separate node Portal_Vortex_Core (Mat_PortalVortex) filling the ring with a glowing rippled vortex disc!
    """
    stone_mb = MeshBuilder("Portal_StoneArchAndPlinth", "Mat_OssifiedStone")
    cart_mb = MeshBuilder("Portal_BoneTrim", "Mat_Cartilage")
    vein_mb = MeshBuilder("Portal_CreepingVeins", "Mat_VeinTendon")
    acid_mb = MeshBuilder("Portal_AcidRunes", "Mat_GastricAcid")
    vortex_mb = MeshBuilder("Portal_Vortex_Core", "Mat_PortalVortex")

    # 1. Stepped stone/bone circular plinth (radius = 3.4m, height = 0.55m)
    plinth_tiers = [
        (3.40, 3.22, 0.00, 0.20, stone_mb, col_ossified_stone),
        (2.96, 2.82, 0.20, 0.38, cart_mb, col_cartilage),
        (2.58, 2.46, 0.38, 0.55, stone_mb, col_ossified_stone),
    ]
    n_plinth = 16
    for r_bot, r_top, yb, yt, mb_t, col_f in plinth_tiers:
        for i in range(n_plinth):
            th0 = 2.0 * math.pi * i / float(n_plinth)
            th1 = 2.0 * math.pi * (i + 1) / float(n_plinth)
            c0, s0 = math.cos(th0), math.sin(th0)
            c1, s1 = math.cos(th1), math.sin(th1)
            tm = 0.5 * (th0 + th1)
            out_n = (math.cos(tm), 0.1, math.sin(tm))
            add_quad_oriented(
                mb_t,
                (r_bot * c0, yb, r_bot * s0), (r_bot * c1, yb, r_bot * s1),
                (r_top * c1, yt, r_top * s1), (r_top * c0, yt, r_top * s0),
                out_n,
                (i / float(n_plinth) * 6.0, 0.0), ((i + 1) / float(n_plinth) * 6.0, 0.0),
                ((i + 1) / float(n_plinth) * 6.0, 0.3), (i / float(n_plinth) * 6.0, 0.3),
                col_f(0.66, 0.32), col_f(0.66, 0.32), col_f(0.92, 0.08), col_f(0.92, 0.08),
            )
            add_tri_oriented(
                mb_t,
                (0.0, yt, 0.0), (r_top * c0, yt, r_top * s0), (r_top * c1, yt, r_top * s1),
                (0.0, 1.0, 0.0),
                (0.5, 0.5), (0.5 + 0.5 * c0, 0.5 + 0.5 * s0), (0.5 + 0.5 * c1, 0.5 + 0.5 * s1),
                col_f(0.86, 0.12), col_f(0.94, 0.06), col_f(0.94, 0.06),
            )

    # Glowing gastric acid rune inlays along front (+Z) of plinth step
    for rx_off in (-1.4, -0.7, 0.0, 0.7, 1.4):
        rz_off = math.sqrt(max(0.1, 2.75 ** 2 - rx_off ** 2))
        add_oriented_box(
            acid_mb, (rx_off, 0.40, rz_off * 0.92), (0.28, 0.08, 0.22),
            yaw=math.atan2(rx_off, rz_off),
            c_bot=col_acid(0.88), c_top=col_acid(1.0), taper_top=(0.75, 0.75),
        )

    # 2. Colossal carved segmented ring archway (ring center Y=3.55, outer radius ~3.25 -> top Y=6.80!)
    ring_cy = 3.55
    ring_r_mid = 2.90  # exact radius=2.9m, outer ~3.25m -> top Y = 3.55 + 3.25 = 6.80m!
    n_voussoirs = 20
    for vi in range(n_voussoirs):
        ang_c = 2.0 * math.pi * vi / float(n_voussoirs)
        # Leave bottom-most inner sill slightly recessed into plinth (ang around 270 deg = 1.5*pi)
        ang0 = ang_c - (math.pi / float(n_voussoirs)) * 0.88
        ang1 = ang_c + (math.pi / float(n_voussoirs)) * 0.88
        is_keystone = (vi == 5)  # top 90 deg (pi/2)
        is_bottom = (vi in (14, 15, 16))

        thick_r = 0.74 if is_keystone else (0.56 if is_bottom else 0.64 + 0.08 * (vi % 2))
        depth_z = 1.15 if is_keystone else (0.98 + 0.10 * ((vi + 1) % 2))
        r_in_v = ring_r_mid - thick_r * 0.5
        r_out_v = ring_r_mid + thick_r * 0.5
        if is_bottom:
            # Flatten/sink bottom arch stones slightly into the plinth so player walks right through!
            r_in_v = max(r_in_v, 2.92)

        hz = depth_z * 0.5
        # Build chunky faceted stone arch segment (X, Y in ring plane, Z depth)
        p_in0_f = (r_in_v * math.cos(ang0), max(0.50, ring_cy + r_in_v * math.sin(ang0)), hz)
        p_in1_f = (r_in_v * math.cos(ang1), max(0.50, ring_cy + r_in_v * math.sin(ang1)), hz)
        p_out1_f = (r_out_v * math.cos(ang1), max(0.32, ring_cy + r_out_v * math.sin(ang1)), hz)
        p_out0_f = (r_out_v * math.cos(ang0), max(0.32, ring_cy + r_out_v * math.sin(ang0)), hz)

        p_in0_b = (p_in0_f[0], p_in0_f[1], -hz)
        p_in1_b = (p_in1_f[0], p_in1_f[1], -hz)
        p_out1_b = (p_out1_f[0], p_out1_f[1], -hz)
        p_out0_b = (p_out0_f[0], p_out0_f[1], -hz)

        c_in = col_ossified_stone(0.90, 0.08)
        c_out = col_ossified_stone(0.74, 0.28)
        # Beveled radial stone block using hex prism helper (bottom=back, top=front)
        add_hex_prism_corners(
            stone_mb,
            [p_in0_b, p_in1_b, p_out1_b, p_out0_b],
            [p_in0_f, p_in1_f, p_out1_f, p_out0_f],
            c_bot=[c_in, c_in, c_out, c_out],
            c_top=[c_in, c_in, c_out, c_out],
            uv_scale=0.35,
        )

        # Front (+Z) carved keystone / bone horn ornaments on upper arch
        if vi in (2, 5, 8):
            ca, sa = math.cos(ang_c), math.sin(ang_c)
            add_curved_fang(
                cart_mb,
                ((ring_r_mid + 0.15) * ca, ring_cy + (ring_r_mid + 0.15) * sa, hz * 0.72),
                (ca, 0.0, 0.65),
                height=1.10 if is_keystone else 0.78, base_r=0.28 if is_keystone else 0.22,
                lean_out=0.25, curve_in=-0.12,
            )

    # Twin flanking ossified gate buttresses anchoring the ring onto the plinth
    for side in (-1.0, 1.0):
        add_oriented_box(
            stone_mb, (side * 2.85, 1.45, 0.0), (1.15, 2.25, 1.55),
            roll=side * 0.12,
            c_bot=col_ossified_stone(0.60, 0.42), c_top=col_ossified_stone(0.90, 0.08),
            taper_top=(0.74, 0.78),
        )

    # 3. Creeping crimson muscle veins wrapping around BOTH pillars/sides of the stone ring & plinth
    n_veins = 12
    for vi in range(n_veins):
        side = -1.0 if vi < 6 else 1.0
        idx_s = vi % 6
        if side > 0:
            # Right pillar (-0.35*pi) up through 0 (+X) to upper-right / crown (+0.52*pi)
            ang_start = -0.35 * math.pi
            ang_end = (0.34 + 0.12 * (idx_s % 3)) * math.pi
        else:
            # Left pillar (-0.65*pi) up through -pi (-X) to upper-left / crown (-1.52*pi)
            ang_start = -0.65 * math.pi
            ang_end = -(1.34 + 0.12 * (idx_s % 3)) * math.pi
        pts = []
        rads = []
        cols = []
        n_vpts = 10
        # Start rooted on the plinth
        pts.append((side * (2.5 + 0.45 * (idx_s % 2)), 0.35, 0.55 * math.sin(idx_s * 1.4)))
        rads.append(0.24)
        cols.append(col_vein(0.72, 0.50))
        for k in range(1, n_vpts + 1):
            t = k / float(n_vpts)
            ang = ang_start + (ang_end - ang_start) * t
            spiral = t * math.pi * 2.6 + idx_s * 1.1
            r_ring = ring_r_mid + 0.40 * math.cos(spiral)
            zz = 0.56 * math.sin(spiral)
            xx = r_ring * math.cos(ang)
            yy = ring_cy + r_ring * math.sin(ang)
            if yy < 0.50:
                yy = 0.50 + 0.15 * t
            pts.append((xx, yy, zz))
            rads.append(0.20 * (1.0 - 0.58 * t) + 0.05)
            cols.append(col_vein(0.94, 0.85 - 0.25 * t))
        add_spline_tube(vein_mb, pts, rads, sides=5, colors=cols, flat=False)

    vein_mb.recompute_smooth_normals()

    # 4. Separate mesh node Portal_Vortex_Core (Mat_PortalVortex): glowing rippled swirling disc!
    # Fills inner ring centered at (0, ring_cy, 0) up to r=2.66m (dipped gently at bottom so it hits plinth)
    n_vr = 6
    n_va = 28
    v_radius = 2.66
    c_v = vortex_mb.add_vertex((0.0, ring_cy, 0.0), (0.0, 0.0, 1.0), (0.5, 0.5), rgba(0.92, 1.00, 0.65))
    v_rings = []
    for ri in range(1, n_vr + 1):
        rf = ri / float(n_vr)
        rr = v_radius * rf
        row = []
        for ai in range(n_va):
            ang = 2.0 * math.pi * ai / float(n_va)
            xx = rr * math.cos(ang)
            yy = max(0.54, ring_cy + rr * math.sin(ang))
            # Subtle vortex ripple along Z
            zz = 0.14 * math.sin(rf * math.pi * 2.0) * math.cos(3.0 * ang + rf * 3.0)
            # Gentle radial UVs aligned cleanly with swirling vortex texture
            swirl = ang + rf * 0.45
            u = 0.5 + 0.48 * rf * math.cos(swirl)
            v = 0.5 + 0.48 * rf * math.sin(swirl)
            col = lerp_col(rgba(0.95, 1.0, 0.85), rgba(0.45, 0.92, 0.65), rf ** 0.85)
            row.append(vortex_mb.add_vertex((xx, yy, zz), (0.0, 0.0, 1.0), (u, v), col))
        v_rings.append(row)

    # Front faces +Z
    for ai in range(n_va):
        nai = (ai + 1) % n_va
        p0 = np.array(vortex_mb.positions[c_v])
        p1 = np.array(vortex_mb.positions[v_rings[0][ai]])
        p2 = np.array(vortex_mb.positions[v_rings[0][nai]])
        if np.cross(p1 - p0, p2 - p0)[2] >= 0.0:
            vortex_mb.add_tri_indices(c_v, v_rings[0][ai], v_rings[0][nai])
        else:
            vortex_mb.add_tri_indices(c_v, v_rings[0][nai], v_rings[0][ai])

    for ri in range(n_vr - 1):
        for ai in range(n_va):
            nai = (ai + 1) % n_va
            a0, a1 = v_rings[ri][ai], v_rings[ri][nai]
            b1, b0 = v_rings[ri + 1][nai], v_rings[ri + 1][ai]
            pa0 = np.array(vortex_mb.positions[a0])
            pa1 = np.array(vortex_mb.positions[a1])
            pb0 = np.array(vortex_mb.positions[b0])
            if np.cross(pa1 - pa0, pb0 - pa0)[2] >= 0.0:
                vortex_mb.add_tri_indices(a0, a1, b1)
                vortex_mb.add_tri_indices(a0, b1, b0)
            else:
                vortex_mb.add_tri_indices(a0, b1, a1)
                vortex_mb.add_tri_indices(a0, b0, b1)

    vortex_mb.recompute_smooth_normals()
    return [stone_mb, cart_mb, vein_mb, acid_mb, vortex_mb]


# ==============================================================================
# 8. EXPORT, NORMAL/WINDING VERIFICATION & PREVIEW RENDERING
# ==============================================================================

def verify_walkable_deck_normals(glb_path, meshes, deck_node_keywords=("_Deck", "Deck_Stairs", "WestBridge_Stairs", "NorthStairs_Treads")):
    """Audits every top-facing triangle on walkable decks to confirm +Y outward normals."""
    for m in meshes:
        if any(k in m["name"] for k in deck_node_keywords):
            pos = m["positions"]
            tris = m["indices"]
            v0 = pos[tris[:, 0]]
            v1 = pos[tris[:, 1]]
            v2 = pos[tris[:, 2]]
            fn = np.cross(v1 - v0, v2 - v0)
            norms = np.linalg.norm(fn, axis=1, keepdims=True)
            unit_ny = fn[:, 1] / np.maximum(norms[:, 0], 1e-8)
            # Pure single-sided arena floor sheets must have ALL tris facing +Y
            if m["name"].endswith("_Deck") and "PlankBridge" not in m["name"] and "VeinBridge" not in m["name"]:
                bad = int(np.sum(unit_ny <= 0.5))
                assert bad == 0, f"{glb_path}::{m['name']} has {bad} inverted/downward deck triangles!"
            elif m["name"] == "VeinBridge_Deck":
                bad = int(np.sum(unit_ny < -0.2))
                assert bad == 0, f"{glb_path}::{m['name']} has {bad} downward triangles!"
                assert int(np.sum(unit_ny > 0.7)) > 0, f"{glb_path}::{m['name']} missing +Y walkable triangles!"
            else:
                # For 3D slabs (steps/planks), top faces (+Y) must exist
                up_cnt = int(np.sum(unit_ny > 0.7))
                assert up_cnt > 0, f"{glb_path}::{m['name']} has zero +Y walkable triangles!"


def generate_all(render_previews=True):
    os.makedirs(MODELS_OUT_DIR, exist_ok=True)
    if render_previews:
        os.makedirs(PREVIEW_OUT_DIR, exist_ok=True)

    asset_specs = [
        ("Arena_South_OssuaryEntry.glb", build_arena_1_south_ossuary_entry, (-20.0, 15.0, -22.0), (0.0, 0.5, 0.0)),
        ("Arena_SouthCentral_Hub.glb", build_arena_2_south_central_hub, (-24.0, 18.0, -24.0), (0.0, 1.0, 0.0)),
        ("Arena_Central_RitualPlateau.glb", build_arena_3_central_ritual_plateau, (-26.0, 20.0, -26.0), (0.0, 1.5, 0.0)),
        ("Arena_East_TendonOverlook.glb", build_arena_4_east_tendon_overlook, (22.0, 16.0, -18.0), (0.0, 2.0, 0.0)),
        ("Arena_West_CraterBastion.glb", build_arena_5_west_crater_bastion, (-22.0, 16.0, -18.0), (0.0, 1.5, 0.0)),
        ("Arena_North_TitanAltar.glb", build_arena_6_north_titan_altar, (-26.0, 22.0, -26.0), (0.0, 3.0, 2.0)),
        ("Bridge_OssuaryStairs_South.glb", build_bridge_7_ossuary_stairs_south, (-16.0, 12.0, -14.0), (0.0, 1.8, 0.0)),
        ("Bridge_VeinTendon_Grand.glb", build_bridge_8_vein_tendon_grand, (-20.0, 14.0, -16.0), (0.0, 4.5, 0.0)),
        ("Bridge_PlankSpan_East.glb", build_bridge_9_plank_span_east, (10.0, 11.0, -13.0), (0.0, 4.8, 0.0)),
        ("Bridge_StairSpan_West.glb", build_bridge_10_stair_span_west, (-10.0, 11.0, -13.0), (0.0, 3.8, 0.0)),
        ("Bridge_Stairs_North.glb", build_bridge_11_stairs_north, (-18.0, 15.0, -16.0), (0.0, 4.5, 0.0)),
        ("Prop_RitualCircle_Center.glb", build_prop_12_ritual_circle_center, (-8.0, 8.5, -8.0), (0.0, 0.0, 0.0)),
        ("Portal_Exit_Gate.glb", build_prop_13_portal_exit_gate, (7.0, 5.2, 11.0), (0.0, 3.2, 0.0)),
    ]

    print("=" * 92)
    print(f"{'GLB Asset':34s} | {'Meshes':6s} | {'Verts':7s} | {'Tris':7s} | {'Bytes':9s} | {'Y Bounds'}")
    print("-" * 92)

    for fname, builder_fn, cam_pos, cam_tgt in asset_specs:
        meshes = builder_fn()
        assert len(meshes) > 0, f"{fname} produced zero MeshBuilders!"
        for mb in meshes:
            assert len(mb.positions) > 0 and len(mb.indices) > 0, f"{fname}::{mb.name} is empty!"

        used_mats = []
        for mb in meshes:
            if mb.material_name not in used_mats:
                used_mats.append(mb.material_name)
        mats_spec = build_materials_spec(used_mats)

        out_path = os.path.join(MODELS_OUT_DIR, fname)
        root_name = os.path.splitext(fname)[0]
        stats = export_glb(out_path, meshes, mats_spec, root_name=root_name)

        # Load back & audit normals / bounds
        _, loaded = load_glb(out_path)
        assert len(loaded) == len(meshes), f"{fname} reload mesh count mismatch!"
        verify_walkable_deck_normals(fname, loaded)

        all_y = np.concatenate([m["positions"][:, 1] for m in loaded])
        y_min, y_max = float(all_y.min()), float(all_y.max())

        print(
            f"{fname:34s} | {stats['meshes']:6d} | {stats['vertices']:7d} | "
            f"{stats['triangles']:7d} | {stats['bytes']:9d} | [{y_min:+.2f}, {y_max:+.2f}]"
        )

        if render_previews:
            im = render_view(loaded, cam_pos, cam_tgt, width=640, height=480, fov_deg=44.0)
            prev_path = os.path.join(PREVIEW_OUT_DIR, f"{root_name}.png")
            im.save(prev_path)

    print("=" * 92)
    print(f"Successfully generated {len(asset_specs)} modular GLB files in: {MODELS_OUT_DIR}")


if __name__ == "__main__":
    generate_all(render_previews=True)
