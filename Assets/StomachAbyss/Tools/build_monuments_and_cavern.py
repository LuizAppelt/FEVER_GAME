import math
import os
import sys
import numpy as np

# Ensure PilotChamber Tools (glb_exporter, preview_render) are importable
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
STOMACH_ABYSS_ROOT = os.path.dirname(SCRIPT_DIR)
ASSETS_ROOT = os.path.dirname(STOMACH_ABYSS_ROOT)
PILOT_TOOLS_DIR = os.path.join(ASSETS_ROOT, "PilotChamber", "Tools")
PILOT_TEXTURES_DIR = os.path.join(ASSETS_ROOT, "PilotChamber", "Textures")
STOMACH_TEXTURES_DIR = os.path.join(STOMACH_ABYSS_ROOT, "Textures")
MODELS_OUT_DIR = os.path.join(STOMACH_ABYSS_ROOT, "Models")
PREVIEW_OUT_DIR = os.path.join(STOMACH_ABYSS_ROOT, "Preview")

if PILOT_TOOLS_DIR not in sys.path:
    sys.path.insert(0, PILOT_TOOLS_DIR)

from glb_exporter import MeshBuilder, export_glb
from preview_render import load_glb, render_view


# ==============================================================================
# 1. TEXTURE FALLBACK & MATERIAL REGISTRY
# ==============================================================================

FALLBACK_TEXTURE = os.path.join(PILOT_TEXTURES_DIR, "tex_flesh_wall.png")

MATERIAL_DEFS = {
    "Mat_GastricAcid": {
        "candidates": ["tex_gastric_acid.png", "tex_acid.png"],
        "roughness": 0.22,
        "metallic": 0.0,
        "emissive_factor": [0.45, 0.95, 0.22],
        "use_emissive_texture": True,
        "double_sided": True,
    },
    "Mat_BloodWaves": {
        "candidates": ["tex_blood_waves.png"],
        "roughness": 0.28,
        "metallic": 0.0,
        "alpha_mode": "MASK",
        "alpha_cutoff": 0.45,
        "double_sided": True,
    },
    "Mat_FleshWall": {
        "candidates": ["tex_flesh_wall.png"],
        "roughness": 0.78,
        "metallic": 0.0,
        "double_sided": False,
    },
    "Mat_DigestedCalcified": {
        "candidates": ["tex_digested_calcified.png"],
        "roughness": 0.75,
        "metallic": 0.0,
        "double_sided": False,
    },
    "Mat_Cartilage": {
        "candidates": ["tex_cartilage.png"],
        "roughness": 0.65,
        "metallic": 0.0,
        "double_sided": False,
    },
    "Mat_OssifiedStone": {
        "candidates": ["tex_ossified_stone.png"],
        "roughness": 0.78,
        "metallic": 0.0,
        "double_sided": False,
    },
    "Mat_HornedBone": {
        "candidates": ["tex_horned_bone.png", "tex_horn_bone.png"],
        "roughness": 0.70,
        "metallic": 0.0,
        "double_sided": False,
    },
    "Mat_Teeth": {
        "candidates": ["tex_teeth.png"],
        "roughness": 0.50,
        "metallic": 0.0,
        "double_sided": False,
    },
    "Mat_VeinTendon": {
        "candidates": ["tex_vein_tendon.png", "tex_hell_organs.png"],
        "roughness": 0.62,
        "metallic": 0.0,
        "double_sided": True,
    },
    "Mat_MembraneBacklit": {
        "candidates": ["tex_membrane_backlit.png"],
        "roughness": 0.35,
        "metallic": 0.0,
        "emissive_factor": [0.95, 0.42, 0.14],
        "use_emissive_texture": True,
        "double_sided": True,
    },
}


def resolve_texture_path(candidates):
    """Checks Assets/StomachAbyss/Textures/<tex>.png first, falling back to tex_flesh_wall.png."""
    for fname in candidates:
        p = os.path.join(STOMACH_TEXTURES_DIR, fname)
        if os.path.isfile(p):
            return p
    return FALLBACK_TEXTURE


def build_materials_spec(meshes):
    """Builds materials_spec strictly for the materials actually referenced by `meshes`."""
    used = []
    for mb in meshes:
        if mb.positions and mb.indices and mb.material_name not in used:
            used.append(mb.material_name)

    spec = {}
    for mat_name in used:
        cfg = MATERIAL_DEFS[mat_name]
        entry = {
            "image_path": resolve_texture_path(cfg["candidates"]),
            "roughness": cfg["roughness"],
            "metallic": cfg["metallic"],
            "double_sided": cfg["double_sided"],
        }
        if "emissive_factor" in cfg:
            entry["emissive_factor"] = cfg["emissive_factor"]
            entry["use_emissive_texture"] = cfg.get("use_emissive_texture", True)
        if "alpha_mode" in cfg:
            entry["alpha_mode"] = cfg["alpha_mode"]
            entry["alpha_cutoff"] = cfg.get("alpha_cutoff", 0.45)
        spec[mat_name] = entry
    return spec


# ==============================================================================
# 2. DETERMINISTIC ORGANIC NOISE & COLOR PALETTES
# ==============================================================================

def _rgba(r, g, b, a=1.0):
    return (
        float(max(0.0, min(1.0, r))),
        float(max(0.0, min(1.0, g))),
        float(max(0.0, min(1.0, b))),
        float(max(0.0, min(1.0, a))),
    )


def lerp_col(c0, c1, t):
    t = max(0.0, min(1.0, float(t)))
    return (
        c0[0] + (c1[0] - c0[0]) * t,
        c0[1] + (c1[1] - c0[1]) * t,
        c0[2] + (c1[2] - c0[2]) * t,
        c0[3] + (c1[3] - c0[3]) * t,
    )


def smoothstep(edge0, edge1, x):
    if abs(edge1 - edge0) < 1e-8:
        return 0.0
    t = max(0.0, min(1.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3.0 - 2.0 * t)


def fbm2(x, z, seed=0.0):
    return (
        0.50 * math.sin(x * 0.37 + z * 0.29 + seed)
        + 0.30 * math.cos(x * 0.81 - z * 0.67 + seed * 1.7)
        + 0.20 * math.sin(x * 1.63 + z * 1.41 - seed * 0.8)
    )


def fbm3(x, y, z, seed=0.0):
    return (
        0.48 * math.sin(x * 0.41 + y * 0.33 - z * 0.27 + seed)
        + 0.32 * math.cos(y * 0.79 + z * 0.61 - x * 0.52 + seed * 1.3)
        + 0.20 * math.sin(x * 1.37 - y * 1.19 + z * 1.28 - seed * 0.7)
    )


def acid_bounce_tint(base_col, y, strength=0.45):
    """Adds subtle toxic emerald bounce near waterline Y=0."""
    w = math.exp(-max(0.0, y + 0.2) ** 2 / 2.2) * strength if y > -2.5 else 0.0
    return _rgba(
        base_col[0] * (1.0 - 0.35 * w) + 0.25 * w,
        base_col[1] * (1.0 - 0.15 * w) + 0.92 * w,
        base_col[2] * (1.0 - 0.45 * w) + 0.18 * w,
    )


def col_flesh(bright=0.75, y=2.0):
    c = _rgba(bright * 0.94, bright * 0.40, bright * 0.44)
    return acid_bounce_tint(c, y, 0.28)


def col_calcified(bright=0.78, y=2.0):
    c = _rgba(bright * 0.84, bright * 0.82, bright * 0.66)
    return acid_bounce_tint(c, y, 0.32)


def col_cartilage(bright=0.88):
    return _rgba(bright * 0.98, bright * 0.92, bright * 0.78)


def col_ossified(bright=0.78):
    return _rgba(bright * 0.80, bright * 0.78, bright * 0.72)


def col_horn(bright=0.68):
    return _rgba(bright * 0.74, bright * 0.64, bright * 0.50)


def col_tooth(bright=0.95):
    return _rgba(bright * 0.99, bright * 0.95, bright * 0.82)


def col_tendon(bright=0.72):
    return _rgba(bright * 0.88, bright * 0.22, bright * 0.26)


def col_acid(bright=0.92):
    return _rgba(bright * 0.58, bright * 1.00, bright * 0.32)


def col_blood_wave(bright=0.85):
    return _rgba(bright * 0.78, bright * 0.24, bright * 0.28)


def col_membrane(bright=0.95):
    return _rgba(bright * 1.00, bright * 0.58, bright * 0.24)


# ==============================================================================
# 3. LOW-LEVEL SAFE GEOMETRY & ORIENTATION HELPERS
# ==============================================================================

def add_oriented_quad(mb, p00, p10, p11, p01, uv00, uv10, uv11, uv01, c00, c10, c11, c01, outward_hint):
    """Emits a crisp flat-shaded quad whose face normal matches outward_hint."""
    p00 = np.asarray(p00, dtype=np.float32)
    p10 = np.asarray(p10, dtype=np.float32)
    p11 = np.asarray(p11, dtype=np.float32)
    p01 = np.asarray(p01, dtype=np.float32)
    n = np.cross(p10 - p00, p11 - p00)
    if float(np.dot(n, np.asarray(outward_hint, dtype=np.float32))) >= 0.0:
        mb.add_flat_quad(p00, p10, p11, p01, uv00, uv10, uv11, uv01, c00, c10, c11, c01)
    else:
        mb.add_flat_quad(p00, p01, p11, p10, uv00, uv01, uv11, uv10, c00, c01, c11, c10)


def add_oriented_tri(mb, p0, p1, p2, uv0, uv1, uv2, c0, c1, c2, outward_hint):
    """Emits a crisp flat-shaded triangle whose face normal matches outward_hint."""
    p0 = np.asarray(p0, dtype=np.float32)
    p1 = np.asarray(p1, dtype=np.float32)
    p2 = np.asarray(p2, dtype=np.float32)
    n = np.cross(p1 - p0, p2 - p0)
    if float(np.dot(n, np.asarray(outward_hint, dtype=np.float32))) >= 0.0:
        mb.add_flat_triangle(p0, p1, p2, uv0, uv1, uv2, c0, c1, c2)
    else:
        mb.add_flat_triangle(p0, p2, p1, uv0, uv2, uv1, c0, c2, c1)


def _add_indexed_quad_checked(mb, i00, i10, i11, i01, outward_hint):
    """Adds two indexed triangles with winding verified against outward_hint."""
    p00 = np.asarray(mb.positions[i00], dtype=np.float32)
    p10 = np.asarray(mb.positions[i10], dtype=np.float32)
    p11 = np.asarray(mb.positions[i11], dtype=np.float32)
    p01 = np.asarray(mb.positions[i01], dtype=np.float32)
    hint = np.asarray(outward_hint, dtype=np.float32)

    # Check triangle 1 area and winding
    def emit_tri(a, b, c, pa, pb, pc):
        n = np.cross(pb - pa, pc - pa)
        if float(np.linalg.norm(n)) < 1e-7:
            return
        if float(np.dot(n, hint)) >= 0.0:
            mb.add_tri_indices(a, b, c)
        else:
            mb.add_tri_indices(a, c, b)

    emit_tri(i00, i10, i11, p00, p10, p11)
    emit_tri(i00, i11, i01, p00, p11, p01)


def add_indexed_grid(mb, pts, uvs, cols, outward_hints=None, wrap_i=False, wrap_j=False):
    """
    Adds a smooth shared-vertex grid of dimensions (ni, nj).
    outward_hints: either None (assumes standard p00->p10 x p11->p00), a fixed 3-vec,
    or a (ni, nj, 3) array of per-vertex outward hints.
    """
    ni = len(pts)
    nj = len(pts[0])
    v_idx = [[0] * nj for _ in range(ni)]
    for i in range(ni):
        for j in range(nj):
            v_idx[i][j] = mb.add_vertex(pts[i][j], (0.0, 1.0, 0.0), uvs[i][j], cols[i][j])

    i_max = ni if wrap_i else ni - 1
    j_max = nj if wrap_j else nj - 1
    for i in range(i_max):
        inext = (i + 1) % ni
        for j in range(j_max):
            jnext = (j + 1) % nj
            i00 = v_idx[i][j]
            i10 = v_idx[inext][j]
            i11 = v_idx[inext][jnext]
            i01 = v_idx[i][jnext]

            if outward_hints is None:
                p00 = np.asarray(pts[i][j], dtype=np.float32)
                p10 = np.asarray(pts[inext][j], dtype=np.float32)
                p11 = np.asarray(pts[inext][jnext], dtype=np.float32)
                hint = np.cross(p10 - p00, p11 - p00)
                if np.linalg.norm(hint) < 1e-8:
                    hint = np.array([0.0, 1.0, 0.0], dtype=np.float32)
            elif isinstance(outward_hints, (tuple, list)) and len(outward_hints) == 3 and not isinstance(outward_hints[0], (tuple, list, np.ndarray)):
                hint = outward_hints
            else:
                hint = outward_hints[i][j]

            _add_indexed_quad_checked(mb, i00, i10, i11, i01, hint)


def compute_parallel_frames(spine_pts):
    """Computes smooth twist-free Bishop frames (tangent, b1, b2) along a 3D spine."""
    n = len(spine_pts)
    pts = [np.asarray(p, dtype=np.float32) for p in spine_pts]
    tangents = []
    for i in range(n):
        if i == 0:
            t = pts[1] - pts[0]
        elif i == n - 1:
            t = pts[-1] - pts[-2]
        else:
            t = pts[i + 1] - pts[i - 1]
        tl = float(np.linalg.norm(t))
        t = t / tl if tl > 1e-7 else np.array([0.0, 1.0, 0.0], dtype=np.float32)
        tangents.append(t)

    up_ref = np.array([0.0, 1.0, 0.0], dtype=np.float32)
    if abs(float(np.dot(tangents[0], up_ref))) > 0.92:
        up_ref = np.array([1.0, 0.0, 0.0], dtype=np.float32)
    b1 = np.cross(tangents[0], up_ref)
    b1 /= np.linalg.norm(b1)
    b2 = np.cross(tangents[0], b1)
    b2 /= np.linalg.norm(b2)

    frames = [(tangents[0], b1, b2)]
    for i in range(1, n):
        t_prev = tangents[i - 1]
        t_cur = tangents[i]
        # Project previous b1 onto plane perpendicular to t_cur
        b1_next = b1 - float(np.dot(b1, t_cur)) * t_cur
        bl = float(np.linalg.norm(b1_next))
        if bl < 1e-6:
            ref = np.array([0.0, 1.0, 0.0], dtype=np.float32)
            if abs(float(np.dot(t_cur, ref))) > 0.92:
                ref = np.array([1.0, 0.0, 0.0], dtype=np.float32)
            b1_next = np.cross(t_cur, ref)
            b1_next /= np.linalg.norm(b1_next)
        else:
            b1_next /= bl
        b2_next = np.cross(t_cur, b1_next)
        b2_next /= np.linalg.norm(b2_next)
        b1, b2 = b1_next, b2_next
        frames.append((t_cur, b1, b2))
    return pts, frames


def add_swept_tube(
    mb,
    spine_pts,
    radii,
    n_sides=10,
    u_tiles=1.0,
    v_tiles=4.0,
    radius_mod_fn=None,
    color_fn=None,
    cap_start=False,
    cap_end=False,
    end_tilt_normal=None,
    smooth_sides=True,
):
    """
    Sweeps a smooth closed cross-section along 3D spine_pts with optional radial modulation
    (e.g. ribs, ridges, elliptical flattening) and crisp flat end caps.
    """
    pts, frames = compute_parallel_frames(spine_pts)
    n_spine = len(pts)

    ring_verts = []
    ring_pos = []
    ring_cols = []

    for i in range(n_spine):
        t = i / float(max(1, n_spine - 1))
        center = pts[i]
        tang, b1, b2 = frames[i]
        r_spec = radii[i]
        if isinstance(r_spec, (tuple, list)):
            rx_base, ry_base = float(r_spec[0]), float(r_spec[1])
        else:
            rx_base = ry_base = float(r_spec)

        r_idx_row = []
        p_row = []
        c_row = []
        for k in range(n_sides):
            phi = (k / float(n_sides)) * 2.0 * math.pi
            rx, ry = rx_base, ry_base
            if radius_mod_fn is not None:
                mod = radius_mod_fn(t, phi, i, k)
                if isinstance(mod, (tuple, list)):
                    rx *= mod[0]
                    ry *= mod[1]
                else:
                    rx *= mod
                    ry *= mod
            offset = b1 * (math.cos(phi) * rx) + b2 * (math.sin(phi) * ry)
            wp = center + offset
            if end_tilt_normal is not None and i == n_spine - 1:
                # Project ring onto jagged/angled fracture plane
                en = np.asarray(end_tilt_normal, dtype=np.float32)
                en = en / max(1e-6, float(np.linalg.norm(en)))
                denom = float(np.dot(tang, en))
                if abs(denom) > 0.2:
                    dist_plane = float(np.dot(wp - center, en))
                    wp = wp - tang * (dist_plane / denom) * 0.65

            col = color_fn(t, phi, wp) if color_fn is not None else col_cartilage(0.85)
            p_row.append(wp)
            c_row.append(col)
            if smooth_sides:
                u_val = (k / float(n_sides)) * u_tiles
                v_val = t * v_tiles
                r_idx_row.append(mb.add_vertex(wp, (0.0, 1.0, 0.0), (u_val, v_val), col))
        # For UV continuity on wrapped seam when smooth, duplicate first vertex at k=n_sides
        if smooth_sides:
            col0 = c_row[0]
            wp0 = p_row[0]
            r_idx_row.append(mb.add_vertex(wp0, (0.0, 1.0, 0.0), (u_tiles, t * v_tiles), col0))
        ring_verts.append(r_idx_row)
        ring_pos.append(p_row)
        ring_cols.append(c_row)

    for i in range(n_spine - 1):
        c_mid = 0.5 * (pts[i] + pts[i + 1])
        v0 = (i / float(n_spine - 1)) * v_tiles
        v1 = ((i + 1) / float(n_spine - 1)) * v_tiles
        for k in range(n_sides):
            kn = (k + 1) % n_sides
            p00 = ring_pos[i][k]
            p10 = ring_pos[i][kn]
            p11 = ring_pos[i + 1][kn]
            p01 = ring_pos[i + 1][k]
            out_hint = 0.25 * (p00 + p10 + p11 + p01) - c_mid
            if smooth_sides:
                i00 = ring_verts[i][k]
                i10 = ring_verts[i][k + 1]
                i11 = ring_verts[i + 1][k + 1]
                i01 = ring_verts[i + 1][k]
                _add_indexed_quad_checked(mb, i00, i10, i11, i01, out_hint)
            else:
                u0 = (k / float(n_sides)) * u_tiles
                u1 = ((k + 1) / float(n_sides)) * u_tiles
                add_oriented_quad(
                    mb, p00, p10, p11, p01,
                    (u0, v0), (u1, v0), (u1, v1), (u0, v1),
                    ring_cols[i][k], ring_cols[i][kn], ring_cols[i + 1][kn], ring_cols[i + 1][k],
                    out_hint,
                )

    if cap_start:
        c0 = pts[0]
        hint0 = -frames[0][0]
        c_col = ring_cols[0][0]
        for k in range(n_sides):
            kn = (k + 1) % n_sides
            add_oriented_tri(
                mb, c0, ring_pos[0][kn], ring_pos[0][k],
                (0.5, 0.5), (0.8, 0.5), (0.5, 0.8),
                c_col, ring_cols[0][kn], ring_cols[0][k],
                hint0,
            )

    if cap_end:
        c1 = pts[-1]
        hint1 = frames[-1][0] if end_tilt_normal is None else np.asarray(end_tilt_normal, dtype=np.float32)
        c_col = ring_cols[-1][0]
        for k in range(n_sides):
            kn = (k + 1) % n_sides
            add_oriented_tri(
                mb, c1, ring_pos[-1][k], ring_pos[-1][kn],
                (0.5, 0.5), (0.8, 0.5), (0.5, 0.8),
                c_col, ring_cols[-1][k], ring_cols[-1][kn],
                hint1,
            )


def add_curved_fang(mb, base_pos, tip_pos, bend_vec, base_r=0.35, n_steps=7, n_sides=6, color_fn=None):
    """Sculpts a curved low-poly conical fang/tusk from base_pos to sharp tip_pos."""
    b = np.asarray(base_pos, dtype=np.float32)
    tip = np.asarray(tip_pos, dtype=np.float32)
    bend = np.asarray(bend_vec, dtype=np.float32)
    spine = []
    radii = []
    for s in range(n_steps):
        t = s / float(n_steps - 1)
        p = b * (1.0 - t) + tip * t + bend * math.sin(t * math.pi)
        r = max(0.025, base_r * ((1.0 - t) ** 0.82))
        spine.append(p)
        radii.append(r)

    def default_c(t, phi, wp):
        b_col = 0.72 + 0.28 * t
        return col_tooth(b_col)

    add_swept_tube(
        mb,
        spine,
        radii,
        n_sides=n_sides,
        u_tiles=1.0,
        v_tiles=1.5,
        color_fn=color_fn or default_c,
        cap_start=False,
        cap_end=True,
        smooth_sides=False,
    )


def add_organic_crag(mb, cx, cz, y_bot, y_top, rx, rz, n_rad=12, n_h=8, seed=1.0, lean=(0.0, 0.0), color_fn=None, top_taper=0.25):
    """Builds a jagged organic flesh-or-calcified crag spire/mound with crisp rock facets."""
    rings = []
    for ih in range(n_h):
        t = ih / float(n_h - 1)
        y = y_bot + (y_top - y_bot) * t
        scale_t = (1.0 - t * (1.0 - top_taper)) * (1.0 + 0.14 * math.sin(t * math.pi * 2.5 + seed))
        lx = cx + lean[0] * (t ** 1.3)
        lz = cz + lean[1] * (t ** 1.3)
        row = []
        for ir in range(n_rad):
            ang = (ir / float(n_rad)) * 2.0 * math.pi
            lobe = 1.0 + 0.24 * math.sin(3.0 * ang + seed + t * 2.2) + 0.14 * math.cos(5.0 * ang - seed * 1.4)
            wx = lx + math.cos(ang) * rx * scale_t * lobe
            wz = lz + math.sin(ang) * rz * scale_t * lobe
            wy = y + (0.28 * math.sin(4.0 * ang + seed) if 0 < ih < n_h - 1 else 0.0)
            row.append(np.array([wx, wy, wz], dtype=np.float32))
        rings.append(row)

    for ih in range(n_h - 1):
        t0 = ih / float(n_h - 1)
        t1 = (ih + 1) / float(n_h - 1)
        c_center = np.array([cx + lean[0] * (t0 ** 1.3), y_bot + (y_top - y_bot) * 0.5 * (t0 + t1), cz + lean[1] * (t0 ** 1.3)], dtype=np.float32)
        for ir in range(n_rad):
            irn = (ir + 1) % n_rad
            p00 = rings[ih][ir]
            p10 = rings[ih][irn]
            p11 = rings[ih + 1][irn]
            p01 = rings[ih + 1][ir]
            hint = 0.25 * (p00 + p10 + p11 + p01) - c_center
            c00 = color_fn(p00, t0) if color_fn else col_flesh(0.55 + 0.35 * t0, float(p00[1]))
            c10 = color_fn(p10, t0) if color_fn else col_flesh(0.55 + 0.35 * t0, float(p10[1]))
            c11 = color_fn(p11, t1) if color_fn else col_flesh(0.55 + 0.35 * t1, float(p11[1]))
            c01 = color_fn(p01, t1) if color_fn else col_flesh(0.55 + 0.35 * t1, float(p01[1]))
            u0 = ir / float(n_rad) * 3.0
            u1 = (ir + 1) / float(n_rad) * 3.0
            add_oriented_quad(
                mb, p00, p10, p11, p01,
                (u0, t0 * 3.0), (u1, t0 * 3.0), (u1, t1 * 3.0), (u0, t1 * 3.0),
                c00, c10, c11, c01, hint,
            )

    # Cap top cleanly as a jagged rock peak (avoid flat stump caps)
    top_center = np.mean(rings[-1], axis=0)
    top_center[1] += 0.95 * min(rx, rz) * max(0.25, top_taper)
    cc_top = color_fn(top_center, 1.0) if color_fn else col_flesh(0.90, float(top_center[1]))
    for ir in range(n_rad):
        irn = (ir + 1) % n_rad
        p0 = rings[-1][ir]
        p1 = rings[-1][irn]
        c0 = color_fn(p0, 1.0) if color_fn else col_flesh(0.85, float(p0[1]))
        c1 = color_fn(p1, 1.0) if color_fn else col_flesh(0.85, float(p1[1]))
        add_oriented_tri(
            mb, top_center, p0, p1,
            (float(top_center[0]) * 0.25, float(top_center[2]) * 0.25),
            (float(p0[0]) * 0.25, float(p0[2]) * 0.25),
            (float(p1[0]) * 0.25, float(p1[2]) * 0.25),
            cc_top, c0, c1, (0.0, 1.0, 0.0),
        )


def add_faceted_orb(mb, cx, cy, cz, rx, ry, rz, color_fn=None, subdivisions=1):
    """Low-poly icosahedral/octahedral glowing lantern orb with crisp facets."""
    # Base octahedron vertices
    top = np.array([0.0, 1.0, 0.0], dtype=np.float32)
    bot = np.array([0.0, -1.0, 0.0], dtype=np.float32)
    eq = [
        np.array([1.0, 0.0, 0.0], dtype=np.float32),
        np.array([0.0, 0.0, 1.0], dtype=np.float32),
        np.array([-1.0, 0.0, 0.0], dtype=np.float32),
        np.array([0.0, 0.0, -1.0], dtype=np.float32),
    ]
    tris = []
    for k in range(4):
        kn = (k + 1) % 4
        tris.append((top, eq[k], eq[kn]))
        tris.append((bot, eq[kn], eq[k]))

    for _ in range(subdivisions):
        new_tris = []
        for a, b, c in tris:
            ab = (a + b) * 0.5
            bc = (b + c) * 0.5
            ca = (c + a) * 0.5
            ab /= np.linalg.norm(ab)
            bc /= np.linalg.norm(bc)
            ca /= np.linalg.norm(ca)
            new_tris.extend([
                (a, ab, ca),
                (ab, b, bc),
                (ca, bc, c),
                (ab, bc, ca),
            ])
        tris = new_tris

    center = np.array([cx, cy, cz], dtype=np.float32)
    scale = np.array([rx, ry, rz], dtype=np.float32)
    for a, b, c in tris:
        pa = center + a * scale
        pb = center + b * scale
        pc = center + c * scale
        hint = (pa + pb + pc) / 3.0 - center
        ca = color_fn(pa) if color_fn else col_acid(0.98)
        cb = color_fn(pb) if color_fn else col_acid(0.95)
        cc = color_fn(pc) if color_fn else col_acid(0.92)
        add_oriented_tri(mb, pa, pb, pc, (0.0, 0.0), (1.0, 0.0), (0.5, 1.0), ca, cb, cc, hint)


def add_tattered_ribbon(mb, top_left, top_right, length, sway_dir=(0.0, 0.0, 0.4), n_w=4, n_h=6, seed=0.0, color_top=None, color_bot=None):
    """Hanging vertical tattered flesh/tendon ribbon with jagged V-notched bottom tips."""
    tl = np.asarray(top_left, dtype=np.float32)
    tr = np.asarray(top_right, dtype=np.float32)
    sw = np.asarray(sway_dir, dtype=np.float32)
    if color_top is None:
        color_top = col_tendon(0.82)
    if color_bot is None:
        color_bot = col_tendon(0.45)

    pts = []
    uvs = []
    cols = []
    for ih in range(n_h):
        v = ih / float(n_h - 1)
        row_p = []
        row_u = []
        row_c = []
        for iw in range(n_w + 1):
            u = iw / float(n_w)
            top_p = tl * (1.0 - u) + tr * u
            # Jagged bottom taper
            jagged = 1.0 - 0.36 * abs(math.sin(u * math.pi * 2.5 + seed))
            drop = length * v * (1.0 if v < 0.72 else (0.72 + (v - 0.72) * jagged / 0.28) if False else 1.0 - 0.35 * (v ** 2) * abs(math.sin(u * math.pi * 3.0 + seed)))
            wave = sw * math.sin(v * math.pi * 1.4 + u * 1.2 + seed)
            p = top_p + np.array([0.0, -drop, 0.0], dtype=np.float32) + wave
            row_p.append(p)
            row_u.append((u, v * 2.0))
            row_c.append(lerp_col(color_top, color_bot, v))
        pts.append(row_p)
        uvs.append(row_u)
        cols.append(row_c)

    for ih in range(n_h - 1):
        for iw in range(n_w):
            p00 = pts[ih][iw]
            p10 = pts[ih][iw + 1]
            p11 = pts[ih + 1][iw + 1]
            p01 = pts[ih + 1][iw]
            hint = np.cross(p10 - p00, p11 - p00)
            if np.linalg.norm(hint) < 1e-7:
                hint = np.array([0.0, 0.0, 1.0], dtype=np.float32)
            add_oriented_quad(
                mb, p00, p10, p11, p01,
                uvs[ih][iw], uvs[ih][iw + 1], uvs[ih + 1][iw + 1], uvs[ih + 1][iw],
                cols[ih][iw], cols[ih][iw + 1], cols[ih + 1][iw + 1], cols[ih + 1][iw],
                hint,
            )


def add_cuboid(mb, cx, cy, cz, sx, sy, sz, c_bot, c_top, uv_scale=0.25, taper_x=1.0, taper_z=1.0, z_shift_top=0.0, x_shift_top=0.0):
    """Adds a tapered ossified-stone block centered in XZ from cy - sy/2 to cy + sy/2."""
    hx0, hz0 = sx * 0.5, sz * 0.5
    hx1, hz1 = hx0 * taper_x, hz0 * taper_z
    y0, y1 = cy - sy * 0.5, cy + sy * 0.5
    cx1, cz1 = cx + x_shift_top, cz + z_shift_top

    b00 = (cx - hx0, y0, cz - hz0)
    b10 = (cx + hx0, y0, cz - hz0)
    b11 = (cx + hx0, y0, cz + hz0)
    b01 = (cx - hx0, y0, cz + hz0)

    t00 = (cx1 - hx1, y1, cz1 - hz1)
    t10 = (cx1 + hx1, y1, cz1 - hz1)
    t11 = (cx1 + hx1, y1, cz1 + hz1)
    t01 = (cx1 - hx1, y1, cz1 + hz1)

    ux = sx * uv_scale
    uz = sz * uv_scale
    vy = sy * uv_scale

    # Front (+Z)
    add_oriented_quad(mb, b01, b11, t11, t01, (0, 0), (ux, 0), (ux, vy), (0, vy), c_bot, c_bot, c_top, c_top, (0, 0, 1))
    # Back (-Z)
    add_oriented_quad(mb, b10, b00, t00, t10, (0, 0), (ux, 0), (ux, vy), (0, vy), c_bot, c_bot, c_top, c_top, (0, 0, -1))
    # Left (-X)
    add_oriented_quad(mb, b00, b01, t01, t00, (0, 0), (uz, 0), (uz, vy), (0, vy), c_bot, c_bot, c_top, c_top, (-1, 0, 0))
    # Right (+X)
    add_oriented_quad(mb, b11, b10, t10, t11, (0, 0), (uz, 0), (uz, vy), (0, vy), c_bot, c_bot, c_top, c_top, (1, 0, 0))
    # Top (+Y)
    add_oriented_quad(mb, t00, t01, t11, t10, (0, 0), (0, uz), (ux, uz), (ux, 0), c_top, c_top, c_top, c_top, (0, 1, 0))
    # Bottom (-Y)
    add_oriented_quad(mb, b01, b00, b10, b11, (0, 0), (0, uz), (ux, uz), (ux, 0), c_bot, c_bot, c_bot, c_bot, (0, -1, 0))


# ==============================================================================
# 4. MODEL BUILDERS (ALL 14 STANDALONE .GLB LANDMARKS)
# ==============================================================================

def build_abyss_acid_lake():
    """1. Abyss_Acid_Lake.glb: 80x80 subdivided boiling acid surface spanning [-115, +115] at Y=0."""
    mb = MeshBuilder("Abyss_Acid_Lake", "Mat_GastricAcid")
    nx, nz = 80, 80
    span = 115.0
    xs = np.linspace(-span, span, nx + 1)
    zs = np.linspace(-span, span, nz + 1)

    pts = []
    uvs = []
    cols = []
    uv_tiles = 18.0
    for i, x in enumerate(xs):
        row_p, row_u, row_c = [], [], []
        for j, z in enumerate(zs):
            r = math.hypot(float(x), float(z))
            swirl = 0.5 + 0.5 * fbm2(float(x) * 0.11, float(z) * 0.11, seed=2.4)
            basin_t = 1.0 - smoothstep(32.0, 112.0, r)
            bright = 0.34 + 0.54 * basin_t + 0.12 * swirl
            c = _rgba(
                bright * (0.46 + 0.20 * basin_t),
                bright * (0.98 + 0.04 * swirl),
                bright * (0.20 + 0.18 * basin_t),
            )
            row_p.append([float(x), 0.0, float(z)])
            row_u.append([(float(x) + span) / (2.0 * span) * uv_tiles, (float(z) + span) / (2.0 * span) * uv_tiles])
            row_c.append(c)
        pts.append(row_p)
        uvs.append(row_u)
        cols.append(row_c)

    add_indexed_grid(mb, pts, uvs, cols, outward_hints=(0.0, 1.0, 0.0))
    mb.recompute_smooth_normals()
    return "Abyss_Acid_Lake", [mb]


def build_waterfall_acid_blood():
    """2. Waterfall_AcidBlood.glb: Spout rock, arched fluted acid sheet, crimson foam ribbons, splash ring."""
    mb_spout = MeshBuilder("Waterfall_Spout_Rock", "Mat_FleshWall")
    mb_sheet = MeshBuilder("Waterfall_Acid_Sheet", "Mat_GastricAcid")
    mb_foam = MeshBuilder("Waterfall_BloodAcid_Foam", "Mat_BloodWaves")
    mb_splash = MeshBuilder("Waterfall_Splash_Ring", "Mat_GastricAcid")

    # 2A. Curved rock-flesh cascade spout lip at Y = 17.5..24.5, Z = -5.5..0.3
    n_u, n_v = 18, 12
    sp_pts, sp_uvs, sp_cols = [], [], []
    for iu in range(n_u):
        u = iu / float(n_u - 1)
        ang = (u - 0.5) * math.pi * 0.95
        x = math.sin(ang) * 5.8
        row_p, row_u, row_c = [], [], []
        for iv in range(n_v):
            v = iv / float(n_v - 1)
            # Carve a central chute depressed at Y=19.8..21.5 around |X|<2.8
            chute = math.exp(-((x / 2.6) ** 2))
            y_lip = 20.6 - 1.1 * chute + 0.4 * math.cos(u * math.pi * 4.0)
            # Profile goes from under-overhang (v=0) up to top ridge (v=1)
            if v < 0.45:
                tv = v / 0.45
                z = -5.2 + 5.2 * (tv ** 0.75) - 0.6 * (1.0 - chute)
                y = 17.2 + (y_lip - 17.2) * tv
            else:
                tv = (v - 0.45) / 0.55
                z = 0.0 - 5.4 * (tv ** 0.85) - 0.5 * (1.0 - chute)
                y = y_lip + (24.4 - y_lip) * math.sin(tv * math.pi * 0.65)
            bump = 0.45 * fbm3(x * 0.5, y * 0.5, z * 0.5, seed=3.1) * (1.0 - 0.55 * chute)
            wx = x + bump * 0.4
            wy = y + bump * 0.35
            wz = z + bump * 0.55
            sh = 0.46 + 0.44 * v + 0.14 * chute
            c = _rgba(sh * 0.92, sh * (0.36 + 0.18 * chute), sh * 0.40)
            row_p.append([wx, wy, wz])
            row_u.append((u * 4.0, v * 2.5))
            row_c.append(c)
        sp_pts.append(row_p)
        sp_uvs.append(row_u)
        sp_cols.append(row_c)
    add_indexed_grid(mb_spout, sp_pts, sp_uvs, sp_cols, outward_hints=(0.0, 0.25, 1.0))
    # Flanking side rock buttresses behind spout
    add_organic_crag(mb_spout, -4.6, -3.2, 16.5, 24.8, 2.5, 2.6, n_rad=9, n_h=5, seed=1.8, lean=(0.5, 0.4))
    add_organic_crag(mb_spout, 4.6, -3.2, 16.5, 24.8, 2.5, 2.6, n_rad=9, n_h=5, seed=4.2, lean=(-0.5, 0.4))
    mb_spout.recompute_smooth_normals()

    # 2B. Tall falling arched acid sheet (Y = 21.2 -> -0.5, wavy vertical flutes, front faces +Z)
    nu_w, nv_w = 24, 28
    sh_pts, sh_uvs, sh_cols = [], [], []
    for iu in range(nu_w):
        u = iu / float(nu_w - 1)
        row_p, row_u, row_c = [], [], []
        for iv in range(nv_w):
            v = iv / float(nv_w - 1)  # 0 at top lip, 1 at lake base
            y = 21.2 * (1.0 - v) - 0.5 * v
            half_w = 2.25 + 0.72 * (v ** 0.7) + 0.18 * math.sin(v * math.pi * 2.0)
            x = (u - 0.5) * 2.0 * half_w
            # Parabolic forward arch curving gently into splash center Z=0.05
            arch_z = -0.20 * (1.0 - v) + 1.55 * math.sin(v * math.pi) + 0.10 * v
            # Wavy vertical flutes along X
            flute = (0.36 + 0.18 * v) * math.sin(u * math.pi * 5.0 + 0.4 * math.sin(v * 3.0))
            flute += 0.14 * math.cos(u * math.pi * 9.0)
            # Round edges back slightly like a thick torrent column
            edge_back = -0.65 * ((abs(u - 0.5) * 2.0) ** 2.2)
            z = arch_z + flute + edge_back

            foam_t = max(0.0, 1.0 - v / 0.15) * 0.45 + smoothstep(0.75, 1.0, v) * 0.55
            col = _rgba(
                0.52 + 0.45 * foam_t,
                0.96 + 0.04 * foam_t,
                0.26 + 0.35 * foam_t,
            )
            row_p.append([x, y, z])
            row_u.append((u * 3.5, -y * 0.15))
            row_c.append(col)
        sh_pts.append(row_p)
        sh_uvs.append(row_u)
        sh_cols.append(row_c)
    add_indexed_grid(mb_sheet, sh_pts, sh_uvs, sh_cols, outward_hints=(0.0, 0.0, 1.0))
    mb_sheet.recompute_smooth_normals()

    # 2C. Frothy outer blood-acid drip ribbons (Mat_BloodWaves)
    ribbon_specs = [
        (-2.35, -0.45, 0.95, 0.25, 0.1),
        (-1.15, -0.15, 0.85, 0.22, 1.7),
        ( 0.05,  0.00, 1.05, 0.24, 3.1),
        ( 1.25,  0.18, 0.88, 0.22, 4.4),
        ( 2.38,  0.48, 0.96, 0.25, 5.9),
    ]
    for cx_off, x_drift, w_rib, z_off, r_seed in ribbon_specs:
        nr_u, nr_v = 5, 22
        r_pts, r_uvs, r_cols = [], [], []
        for iu in range(nr_u):
            uu = iu / float(nr_u - 1)
            row_p, row_u, row_c = [], [], []
            for iv in range(nr_v):
                vv = iv / float(nr_v - 1)
                y = 20.9 * (1.0 - vv) - 0.4 * vv
                x = cx_off + x_drift * vv + (uu - 0.5) * w_rib * (0.85 + 0.45 * vv)
                arch_z = -0.12 * (1.0 - vv) + 1.65 * math.sin(vv * math.pi) + 0.22
                flute = 0.22 * math.sin(vv * 8.0 + r_seed) + z_off
                # Taper rib slightly near edges in Z
                z = arch_z + flute - 0.18 * ((uu - 0.5) * 2.0) ** 2
                col = lerp_col(col_blood_wave(0.90), col_acid(0.95), 0.28 + 0.40 * (vv ** 2))
                row_p.append([x, y, z])
                row_u.append((uu * 1.0, -y * 0.18))
                row_c.append(col)
            r_pts.append(row_p)
            r_uvs.append(row_u)
            r_cols.append(row_c)
        add_indexed_grid(mb_foam, r_pts, r_uvs, r_cols, outward_hints=(0.0, 0.0, 1.0))
    mb_foam.recompute_smooth_normals()

    # 2D. Octagonal churning acid splash ring at Y = 0.12
    n_sp = 16
    for ring_idx, (r_in, r_out, y_base, peak_h) in enumerate([
        (1.4, 3.8, 0.12, 1.45),
        (3.2, 6.0, 0.05, 0.85),
    ]):
        sp_ring_pts, sp_ring_uvs, sp_ring_cols = [], [], []
        for ia in range(n_sp):
            th = (ia / float(n_sp)) * 2.0 * math.pi
            lobe = 0.5 + 0.5 * math.sin(th * 4.0 + ring_idx * 1.1)
            row_p, row_u, row_c = [], [], []
            for ir, (rr, yy, br) in enumerate([
                (r_in, y_base + 0.10, 0.90),
                (0.5 * (r_in + r_out), y_base + peak_h * (0.40 + 0.60 * lobe), 1.00),
                (r_out, y_base - 0.05, 0.74),
            ]):
                wx = math.cos(th) * rr * 1.15
                wz = math.sin(th) * rr * 0.92 + 0.20
                row_p.append([wx, yy, wz])
                row_u.append((ia / float(n_sp) * 4.0, ir * 0.5))
                row_c.append(_rgba(0.68 * br, 1.00 * br, 0.36 * br))
            sp_ring_pts.append(row_p)
            sp_ring_uvs.append(row_u)
            sp_ring_cols.append(row_c)
        add_indexed_grid(mb_splash, sp_ring_pts, sp_ring_uvs, sp_ring_cols, outward_hints=(0.0, 1.0, 0.0), wrap_i=True)
    mb_splash.recompute_smooth_normals()

    return "Waterfall_AcidBlood", [mb_spout, mb_sheet, mb_foam, mb_splash]


def build_monument_giant_skull_west():
    """3. Monument_GiantSkull_West.glb: Colossal half-submerged horned beast skull + cliff mass, front +Z."""
    mb_cliff = MeshBuilder("Skull_Cliff_Foundation", "Mat_FleshWall")
    mb_calc = MeshBuilder("Skull_Calcified_Crags", "Mat_DigestedCalcified")
    mb_skull = MeshBuilder("Skull_Cranium_And_Mandible", "Mat_Cartilage")
    mb_glow = MeshBuilder("Skull_Eye_And_Maw_Glow", "Mat_GastricAcid")
    mb_teeth = MeshBuilder("Skull_Fangs_And_Tusks", "Mat_Teeth")
    mb_horns = MeshBuilder("Skull_Colossal_Horns", "Mat_HornedBone")

    # 3A. Jagged flesh-and-rock cliff mass (Y = -3.5..+14, width ~18m)
    crags_flesh = [
        (0.0, -5.8, -3.5, 13.8, 6.8, 4.4, 12, 8, 1.1, (0.0, 0.6)),
        (-5.8, -4.4, -3.5, 11.4, 4.2, 3.8, 10, 7, 2.3, (-1.2, 0.8)),
        (5.8, -4.4, -3.5, 11.2, 4.2, 3.8, 10, 7, 3.7, (1.2, 0.8)),
        (-7.8, -1.6, -3.5, 7.6, 2.8, 2.9, 9, 6, 4.9, (-0.8, 1.0)),
        (7.8, -1.6, -3.5, 7.4, 2.8, 2.9, 9, 6, 6.1, (0.8, 1.0)),
    ]
    for cx, cz, yb, yt, rx, rz, nr, nh, sd, ln in crags_flesh:
        add_organic_crag(mb_cliff, cx, cz, yb, yt, rx, rz, nr, nh, sd, ln)

    crags_calc = [
        (-4.8, 0.2, -3.5, 4.4, 3.4, 2.5, 9, 5, 7.2, (-0.5, 0.4)),
        (4.8, 0.2, -3.5, 4.2, 3.4, 2.5, 9, 5, 8.4, (0.5, 0.4)),
        (-7.4, 1.4, -3.5, 3.2, 2.4, 2.2, 8, 4, 9.1, (-0.4, 0.5)),
        (7.4, 1.4, -3.5, 3.0, 2.4, 2.2, 8, 4, 9.8, (0.4, 0.5)),
    ]
    for cx, cz, yb, yt, rx, rz, nr, nh, sd, ln in crags_calc:
        add_organic_crag(
            mb_calc, cx, cz, yb, yt, rx, rz, nr, nh, sd, ln,
            color_fn=lambda p, t: col_calcified(0.55 + 0.38 * t, float(p[1])),
        )

    # 3B. Massive elongated beast cranium (~14m long, Z = -5.2..+8.6, facing +Z)
    nz_slices = 18
    n_ang = 20
    cr_pts, cr_uvs, cr_cols = [], [], []
    cr_hints = []
    for iz in range(nz_slices):
        tz = iz / float(nz_slices - 1)
        z = -5.2 + 13.8 * tz

        # Profile dimensions along Z
        if tz < 0.35:
            # Occipital -> wide brow vault
            lt = tz / 0.35
            rx = 3.4 + 1.0 * math.sin(lt * math.pi * 0.65)
            ry = 2.6 + 0.85 * math.sin(lt * math.pi * 0.7)
            cy = 7.4 + 0.45 * lt
        elif tz < 0.58:
            # Orbital / cheekbone zone
            lt = (tz - 0.35) / 0.23
            rx = 4.35 - 0.85 * lt
            ry = 3.25 - 0.75 * lt
            cy = 7.85 - 0.95 * lt
        else:
            # Elongated beast snout tapering toward front +Z
            lt = (tz - 0.58) / 0.42
            rx = 3.50 * (1.0 - 0.62 * (lt ** 0.85))
            ry = 2.50 * (1.0 - 0.56 * (lt ** 0.9))
            cy = 6.90 - 2.25 * (lt ** 1.15)

        row_p, row_u, row_c, row_h = [], [], [], []
        for ia in range(n_ang):
            phi = (ia / float(n_ang)) * 2.0 * math.pi
            cp, sp = math.cos(phi), math.sin(phi)

            # Base cross section (flattens slightly at palate bottom sp < 0)
            y_scale = 0.65 if sp < 0.0 else 1.0
            lx = cp * rx
            ly = sp * ry * y_scale

            # Anatomical sculpting features:
            # 1) Sagittal crest along top center (phi ~ pi/2)
            crest = max(0.0, sp) ** 4.0 * 0.65 * (1.0 - 0.5 * tz)
            # 2) Heavy supraorbital brow ridges (Z ~ 0.5..3.5, upper sides)
            brow_w = math.exp(-((z - 2.1) ** 2) / 2.2)
            brow_ang = math.exp(-((abs(cp) - 0.70) ** 2) / 0.08) if sp > 0.25 else 0.0
            brow = 0.88 * brow_w * brow_ang
            # 3) Deep sunken orbital eye sockets (Z ~ 1.6..3.6, mid-upper flanks)
            eye_w = math.exp(-((z - 2.4) ** 2) / 1.1)
            eye_ang = math.exp(-((abs(cp) - 0.86) ** 2) / 0.045) * math.exp(-((sp - 0.16) ** 2) / 0.08)
            eye_indent = -1.55 * eye_w * eye_ang
            # 4) Sharp zygomatic cheekbone ridge below eye
            cheek_ang = math.exp(-((abs(cp) - 0.92) ** 2) / 0.04) * math.exp(-((sp + 0.18) ** 2) / 0.06)
            cheek = 0.62 * brow_w * cheek_ang
            # 5) Deep sunken nasal pit near snout bridge (Z ~ 5.8..7.5, top center)
            nasal_w = math.exp(-((z - 6.6) ** 2) / 0.75)
            nasal_ang = max(0.0, sp) ** 3.5 * math.exp(-(cp ** 2) / 0.18)
            nasal_indent = -1.10 * nasal_w * nasal_ang

            # Apply radial anatomical offsets
            rad_dir = np.array([cp, sp, 0.0], dtype=np.float32)
            nl = float(np.linalg.norm(rad_dir))
            if nl > 1e-6:
                rad_dir /= nl
            disp = crest + brow + eye_indent + cheek + nasal_indent
            wx = lx + rad_dir[0] * disp
            wy = cy + ly + rad_dir[1] * disp
            wz = z

            # Close snout tip smoothly
            if iz == nz_slices - 1:
                wx *= 0.28
                wy = cy + (wy - cy) * 0.28 - 0.35
                wz += 0.35
            elif iz == 0:
                wx *= 0.45
                wy = cy + (wy - cy) * 0.45

            ao = 0.78 + 0.20 * max(0.0, sp)
            # Darken deeply inside eye socket and nasal cavity
            ao -= 0.56 * min(1.0, abs(eye_indent) / 1.3)
            ao -= 0.48 * min(1.0, abs(nasal_indent) / 1.0)
            ao += 0.14 * min(1.0, (brow + cheek) / 0.8)
            ao = max(0.22, min(1.0, ao))

            row_p.append([wx, wy, wz])
            row_u.append((ia / float(n_ang) * 4.0, tz * 4.0))
            row_c.append(col_cartilage(ao))
            row_h.append([cp, sp, 0.25 if iz == nz_slices - 1 else (-0.25 if iz == 0 else 0.0)])

        cr_pts.append(row_p)
        cr_uvs.append(row_u)
        cr_cols.append(row_c)
        cr_hints.append(row_h)

    add_indexed_grid(mb_skull, cr_pts, cr_uvs, cr_cols, outward_hints=cr_hints, wrap_j=True)

    # Lower mandible hanging agape over the acid lake (Left branch, Right branch, fused armored Chin Prow)
    for side in (-1.0, 1.0):
        mand_spine = [
            [side * 2.90, 3.8, -1.6],
            [side * 3.00, 2.5,  0.8],
            [side * 2.60, 1.4,  3.4],
            [side * 1.85, 0.4,  5.9],
            [side * 0.78, -0.15, 7.6],
        ]
        mand_rad = [
            (0.95, 0.85),
            (0.88, 0.78),
            (0.82, 0.72),
            (0.76, 0.70),
            (0.68, 0.65),
        ]
        add_swept_tube(
            mb_skull, mand_spine, mand_rad, n_sides=10, u_tiles=1.5, v_tiles=3.0,
            color_fn=lambda t, phi, wp: col_cartilage(0.62 + 0.30 * (0.5 + 0.5 * math.sin(phi))),
            cap_start=True, cap_end=False,
        )
    # Armored chin symphysis prow dipping right into/over the acid surface
    chin_spine = [
        [0.0, -0.10, 6.2],
        [0.0, -0.28, 7.6],
        [0.0,  0.28, 8.4],
    ]
    add_swept_tube(
        mb_skull, chin_spine, [(1.38, 0.85), (1.18, 0.78), (0.58, 0.45)],
        n_sides=10, u_tiles=1.5, v_tiles=1.5,
        color_fn=lambda t, phi, wp: col_cartilage(0.75 + 0.22 * t),
        cap_start=True, cap_end=True,
    )
    mb_skull.recompute_smooth_normals()

    # 3C. Recessed Gastric Acid Eye Glow, Nasal Glow & Agape Maw Acid Pool
    for side in (-1.0, 1.0):
        add_faceted_orb(mb_glow, side * 2.52, 7.62, 2.38, 0.84, 0.68, 0.78, color_fn=lambda p: col_acid(1.0), subdivisions=1)
    add_faceted_orb(mb_glow, 0.0, 5.88, 6.35, 0.66, 0.48, 0.58, color_fn=lambda p: col_acid(0.92), subdivisions=1)
    # Boiling acid pool inside the open lower throat
    add_faceted_orb(mb_glow, 0.0, 1.35, 1.85, 1.95, 0.72, 2.45, color_fn=lambda p: col_acid(0.88), subdivisions=1)

    # 3D. Sharp Curved Fangs (upper jaw, lower jaw, and flanking cliff tusks)
    upper_fangs = [
        (1.32, 3.80, 7.42, (0.08, -2.35, 0.45), (0.0, 0.0, 0.30), 0.40),
        (1.92, 4.05, 6.10, (0.16, -1.95, 0.25), (0.0, 0.0, 0.24), 0.34),
        (2.42, 4.30, 4.75, (0.20, -1.68, 0.18), (0.0, 0.0, 0.20), 0.31),
        (2.75, 4.55, 3.45, (0.22, -1.45, 0.12), (0.0, 0.0, 0.18), 0.28),
        (3.00, 4.80, 2.15, (0.24, -1.25, 0.08), (0.0, 0.0, 0.16), 0.25),
    ]
    for fx, fy, fz, dvec, bvec, r0 in upper_fangs:
        for side in (-1.0, 1.0):
            base = (side * fx, fy, fz)
            tip = (side * (fx + dvec[0]), fy + dvec[1], fz + dvec[2])
            bend = (side * bvec[0], bvec[1], bvec[2])
            add_curved_fang(mb_teeth, base, tip, bend, base_r=r0)

    lower_fangs = [
        (0.96, 0.25, 7.35, (-0.05, 2.05, -0.25), (0.0, 0.0, -0.22), 0.34),
        (1.68, 0.65, 5.90, (-0.10, 1.68, -0.20), (0.0, 0.0, -0.18), 0.28),
        (2.22, 1.15, 4.35, (-0.12, 1.45, -0.15), (0.0, 0.0, -0.15), 0.25),
        (2.60, 1.75, 2.80, (-0.12, 1.25, -0.10), (0.0, 0.0, -0.12), 0.22),
    ]
    for fx, fy, fz, dvec, bvec, r0 in lower_fangs:
        for side in (-1.0, 1.0):
            base = (side * fx, fy, fz)
            tip = (side * (fx + dvec[0]), fy + dvec[1], fz + dvec[2])
            bend = (side * bvec[0], bvec[1], bvec[2])
            add_curved_fang(mb_teeth, base, tip, bend, base_r=r0)

    cliff_tusks = [
        ((-6.6, 1.2, 1.5), (-8.4, 5.2, 3.6), (-0.4, 0.6, 0.5), 0.62),
        (( 6.6, 1.2, 1.5), ( 8.4, 5.2, 3.6), ( 0.4, 0.6, 0.5), 0.62),
        ((-7.8, 3.5, -1.2), (-9.6, 7.8, 0.8), (-0.3, 0.7, 0.3), 0.55),
        (( 7.8, 3.5, -1.2), ( 9.6, 7.8, 0.8), ( 0.3, 0.7, 0.3), 0.55),
    ]
    for b, t_p, bv, r0 in cliff_tusks:
        add_curved_fang(mb_teeth, b, t_p, bv, base_r=r0, n_steps=8, n_sides=6)

    # 3E. Twin huge backward-sweeping segmented horns (Mat_HornedBone)
    for side in (-1.0, 1.0):
        h_spine = []
        h_rad = []
        n_hs = 18
        for i in range(n_hs):
            t = i / float(n_hs - 1)
            # Sweeps from upper rear brow backward, outward, then curling upward!
            hx = side * (2.65 + 4.4 * math.sin(t * math.pi * 0.78))
            hy = 9.2 + 9.4 * (t ** 1.05) + 0.8 * math.sin(t * math.pi)
            hz = -0.8 - 6.8 * math.sin(t * math.pi * 0.62) + 1.2 * (t ** 2.4)
            hr = 1.22 * ((1.0 - t) ** 0.75) + 0.08
            h_spine.append([hx, hy, hz])
            h_rad.append(hr)
        add_swept_tube(
            mb_horns, h_spine, h_rad, n_sides=12, u_tiles=2.0, v_tiles=6.0,
            radius_mod_fn=lambda t, phi, i, k: 1.0 + 0.14 * math.sin(t * 26.0 * math.pi),
            color_fn=lambda t, phi, wp: col_horn(0.45 + 0.46 * (t ** 0.7) + 0.10 * math.sin(t * 26.0 * math.pi)),
            cap_start=False, cap_end=True,
        )
    mb_horns.recompute_smooth_normals()

    return "Monument_GiantSkull_West", [mb_cliff, mb_calc, mb_skull, mb_glow, mb_teeth, mb_horns]


def build_monument_giant_bone_wing_east():
    """4. Monument_GiantBoneWing_East.glb: Cliff outcrop + 3 colossal wing ribs sweeping +Z + hanging tendon webs."""
    mb_cliff = MeshBuilder("BoneWing_Cliff_Outcrop", "Mat_FleshWall")
    mb_calc = MeshBuilder("BoneWing_Calcified_Base", "Mat_DigestedCalcified")
    mb_bone = MeshBuilder("BoneWing_Skeletal_Ribs", "Mat_Cartilage")
    mb_tendon = MeshBuilder("BoneWing_Tendon_Webs", "Mat_VeinTendon")

    # 4A. Rugged cliff outcrop rising from Y = -3.5 to Y = +9.5
    for cx, cz, yb, yt, rx, rz, sd, ln in [
        (0.0, -4.5, -3.5, 9.6, 5.8, 4.4, 2.2, (0.0, 0.5)),
        (-4.4, -2.6, -3.5, 7.6, 3.6, 3.4, 3.5, (-0.8, 0.6)),
        (4.4, -2.6, -3.5, 7.8, 3.6, 3.4, 4.8, (0.8, 0.6)),
        (0.0, -1.2, -3.5, 6.2, 4.0, 2.6, 5.9, (0.0, 0.6)),
    ]:
        add_organic_crag(mb_cliff, cx, cz, yb, yt, rx, rz, n_rad=11, n_h=7, seed=sd, lean=ln)

    for cx, cz, yb, yt, rx, rz, sd, ln in [
        (-3.2, 0.8, -3.5, 3.4, 2.8, 2.2, 7.1, (-0.3, 0.4)),
        (3.2, 0.8, -3.5, 3.6, 2.8, 2.2, 8.3, (0.3, 0.4)),
    ]:
        add_organic_crag(
            mb_calc, cx, cz, yb, yt, rx, rz, n_rad=9, n_h=5, seed=sd, lean=ln,
            color_fn=lambda p, t: col_calcified(0.55 + 0.35 * t, float(p[1])),
        )

    # 4B. Colossal articulated bone joint + 3 giant elongated skeletal wing-bones/ribs sweeping +Z (~20m reach!)
    # Articulated shoulder condyle / scapula joint
    joint_spine = [
        [-2.8, 6.8, -2.0],
        [-1.0, 7.2, -0.8],
        [ 0.8, 7.0,  0.2],
        [ 2.6, 6.4, -0.4],
    ]
    add_swept_tube(
        mb_bone, joint_spine, [1.55, 2.25, 2.15, 1.45], n_sides=12, u_tiles=2.0, v_tiles=2.5,
        radius_mod_fn=lambda t, phi, i, k: 1.0 + 0.16 * math.cos(3.0 * phi),
        color_fn=lambda t, phi, wp: col_cartilage(0.70 + 0.25 * (0.5 + 0.5 * math.sin(phi))),
        cap_start=True, cap_end=True,
    )

    wing_ribs = [
        # Primary upper wing spar (~20m reach)
        (
            [
                [0.2, 7.2,  0.0],
                [0.8, 10.6, 4.0],
                [1.4, 13.2, 8.8],
                [2.0, 12.6, 14.2],
                [2.5, 9.6,  19.8],
            ],
            [1.15, 0.96, 0.82, 0.55, 0.16],
        ),
        # Middle sweeping rib (~18m reach)
        (
            [
                [-1.1, 6.5,  0.3],
                [-2.4, 9.2,  4.2],
                [-3.9, 10.4, 8.8],
                [-4.8, 8.6,  13.6],
                [-5.6, 5.8,  17.8],
            ],
            [0.95, 0.78, 0.65, 0.44, 0.14],
        ),
        # Lower outer wing rib (~16m reach, dipping low over lake)
        (
            [
                [1.8, 5.8, 0.4],
                [3.2, 7.6, 3.8],
                [4.6, 8.0, 7.8],
                [5.6, 6.2, 11.8],
                [6.4, 3.6, 15.8],
            ],
            [0.88, 0.72, 0.58, 0.38, 0.12],
        ),
    ]

    # Interpolate smooth dense spines for each giant rib and sweep with knuckle bulges
    def eval_catmull_polyline(ctrl_pts, n_out=22):
        arr = np.asarray(ctrl_pts, dtype=np.float32)
        ts_in = np.linspace(0.0, 1.0, len(arr))
        ts_out = np.linspace(0.0, 1.0, n_out)
        # Cubic-like smoothed linear blend
        out = []
        for t in ts_out:
            idx = min(len(arr) - 2, int(t * (len(arr) - 1)))
            lt = (t - ts_in[idx]) / max(1e-6, (ts_in[idx + 1] - ts_in[idx]))
            p_lin = arr[idx] * (1.0 - lt) + arr[idx + 1] * lt
            # Slight upward parabolic smoothing
            if 0 < idx < len(arr) - 2:
                p_prev = arr[idx - 1]
                p_next = arr[idx + 2]
                p_smooth = 0.72 * p_lin + 0.14 * (p_prev + p_next)
            else:
                p_smooth = p_lin
            out.append(p_smooth)
        # Keep exact endpoints
        out[0] = arr[0]
        out[-1] = arr[-1]
        return out

    rib_eval_spines = []
    for ctrl_pts, r_ctrl in wing_ribs:
        dense_sp = eval_catmull_polyline(ctrl_pts, n_out=20)
        ts = np.linspace(0.0, 1.0, 20)
        r_dense = np.interp(ts, np.linspace(0.0, 1.0, len(r_ctrl)), r_ctrl).tolist()
        rib_eval_spines.append(dense_sp)
        add_swept_tube(
            mb_bone, dense_sp, r_dense, n_sides=10, u_tiles=2.0, v_tiles=6.0,
            radius_mod_fn=lambda t, phi, i, k: (1.0 + 0.22 * math.exp(-((t - 0.45) ** 2) / 0.008) + 0.12 * math.sin(t * 10.0 * math.pi)) * (1.0 + 0.10 * math.cos(2.0 * phi)),
            color_fn=lambda t, phi, wp: col_cartilage(0.76 + 0.20 * (0.5 + 0.5 * math.sin(phi)) - 0.12 * (1.0 - t)),
            cap_start=True, cap_end=True,
        )

    # Cross-strut bone ribs connecting primary spar to side spars
    cross_struts = [
        (rib_eval_spines[0][7], rib_eval_spines[1][8], 0.36),
        (rib_eval_spines[0][13], rib_eval_spines[1][14], 0.28),
        (rib_eval_spines[0][6], rib_eval_spines[2][7], 0.34),
        (rib_eval_spines[0][12], rib_eval_spines[2][13], 0.26),
    ]
    for a, b, rad in cross_struts:
        mid = 0.5 * (a + b) + np.array([0.0, 0.85, 0.0], dtype=np.float32)
        add_swept_tube(
            mb_bone, [a, mid, b], [rad, rad * 0.72, rad * 0.92], n_sides=8,
            color_fn=lambda t, phi, wp: col_cartilage(0.80),
            cap_start=True, cap_end=True,
        )
    mb_bone.recompute_smooth_normals()

    # 4C. Hanging tattered webs of red muscle fibers, torn tendons & sinew sheets draping vertically near Y=0
    for r_idx, sp in enumerate(rib_eval_spines):
        # Tattered vertical curtains hanging from segments 3..17 almost touching acid surface (Y = 0.2..1.0)
        for seg_idx in range(3, 17, 2):
            p_top_a = sp[seg_idx]
            p_top_b = sp[min(len(sp) - 1, seg_idx + 2)]
            # Keep bottom ragged edge hovering just above Y=0.18..1.2
            avg_y = 0.5 * (float(p_top_a[1]) + float(p_top_b[1]))
            drop_len = max(1.8, avg_y - (0.22 + 0.45 * ((seg_idx % 5) / 4.0)))
            add_tattered_ribbon(
                mb_tendon,
                p_top_a + np.array([0.0, -0.25, 0.0], dtype=np.float32),
                p_top_b + np.array([0.0, -0.25, 0.0], dtype=np.float32),
                length=drop_len,
                sway_dir=(0.35 * (r_idx - 1), 0.0, 0.45),
                n_w=4,
                n_h=7,
                seed=r_idx * 3.1 + seg_idx * 0.9,
                color_top=col_tendon(0.86),
                color_bot=col_tendon(0.38),
            )

    # Catenary muscular tendon ropes draped between spars and from ribs down to cliff base
    tendon_cables = [
        (rib_eval_spines[0][5], rib_eval_spines[1][6], -1.8, 0.24),
        (rib_eval_spines[0][11], rib_eval_spines[1][12], -2.4, 0.20),
        (rib_eval_spines[0][5], rib_eval_spines[2][6], -1.6, 0.22),
        (rib_eval_spines[0][11], rib_eval_spines[2][12], -2.2, 0.18),
        (rib_eval_spines[1][10], np.array([-3.6, 0.3, 6.2], dtype=np.float32), -0.8, 0.24),
        (rib_eval_spines[2][10], np.array([ 4.4, 0.3, 5.8], dtype=np.float32), -0.8, 0.22),
    ]
    for a, b, sag, rad in tendon_cables:
        c_pts = []
        c_rads = []
        for s in range(10):
            t = s / 9.0
            p = a * (1.0 - t) + b * t + np.array([0.0, sag * math.sin(t * math.pi), 0.0], dtype=np.float32)
            c_pts.append(p)
            c_rads.append(rad * (0.75 + 0.45 * abs(t - 0.5) * 2.0))
        add_swept_tube(
            mb_tendon, c_pts, c_rads, n_sides=6, u_tiles=1.0, v_tiles=3.0,
            color_fn=lambda t, phi, wp: col_tendon(0.55 + 0.35 * abs(t - 0.5) * 2.0),
            cap_start=True, cap_end=True,
        )
    mb_tendon.recompute_smooth_normals()

    return "Monument_GiantBoneWing_East", [mb_cliff, mb_calc, mb_bone, mb_tendon]


def build_monument_horned_colossus_north():
    """
    5. Monument_HornedColossus_North.glb (Reference Photo 3 centerpiece!):
    - Towering jagged flesh-rock crag spire (18m wide, Y=-3..+15) studded with curved flank bone spikes/tusks
    - Two colossal broken crescent horns (Y=13..24) + 3 floating broken horn chunks between tips
    - Grimacing fanged Demon/Parasite Skull Idol suspended in center (Y=18.5) with fiery green acid eyes & mouth
    - Taut crisscrossing multi-strand tendon cables tying skull in X-fashion to horns + helical horn coils + draped ribbons
    - 3 glowing hanging green acid soul orbs dangling from the cables
    """
    mb_crag = MeshBuilder("Colossus_Crag_Spire", "Mat_FleshWall")
    mb_calc = MeshBuilder("Colossus_Calcified_Flanks", "Mat_DigestedCalcified")
    mb_tusks = MeshBuilder("Colossus_Flank_Tusks", "Mat_Teeth")
    mb_horns = MeshBuilder("Colossus_Broken_Horns", "Mat_HornedBone")
    mb_idol = MeshBuilder("Colossus_Skull_Idol", "Mat_OssifiedStone")
    mb_glow = MeshBuilder("Colossus_Acid_Glow_And_Orbs", "Mat_GastricAcid")
    mb_tendon = MeshBuilder("Colossus_Lashing_Cables", "Mat_VeinTendon")

    # 5A. Towering jagged organic flesh-and-rock crag spire (width 18m, Y = -3..+15)
    for cx, cz, yb, yt, rx, rz, nr, nh, sd, ln, tap in [
        ( 0.0, -0.5, -3.0, 15.2, 5.2, 4.6, 14, 10, 1.4, (0.0, 0.2), 0.48),
        (-4.5,  0.4, -3.0, 13.8, 3.6, 3.4, 12, 9,  2.7, (-0.8, 0.3), 0.38),
        ( 4.5,  0.4, -3.0, 13.8, 3.6, 3.4, 12, 9,  4.1, ( 0.8, 0.3), 0.38),
        (-7.4, -0.4, -3.0,  9.2, 2.8, 3.0, 10, 7,  5.5, (-1.1, 0.4), 0.25),
        ( 7.4, -0.4, -3.0,  9.2, 2.8, 3.0, 10, 7,  6.8, ( 1.1, 0.4), 0.25),
        ( 0.0, -3.8, -3.0, 14.5, 4.6, 3.2, 12, 8,  8.1, ( 0.0, -0.5), 0.32),
    ]:
        add_organic_crag(mb_crag, cx, cz, yb, yt, rx, rz, nr, nh, sd, ln, top_taper=tap)

    for cx, cz, yb, yt, rx, rz, sd, ln in [
        (-5.2, 2.8, -3.0, 5.2, 2.9, 2.3, 1.9, (-0.4, 0.5)),
        ( 5.2, 2.8, -3.0, 5.2, 2.9, 2.3, 3.3, ( 0.4, 0.5)),
        ( 0.0, 3.6, -3.0, 4.6, 3.2, 2.2, 4.7, ( 0.0, 0.6)),
        (-8.2, 1.4, -3.0, 3.6, 2.4, 2.0, 6.1, (-0.5, 0.3)),
        ( 8.2, 1.4, -3.0, 3.6, 2.4, 2.0, 7.4, ( 0.5, 0.3)),
    ]:
        add_organic_crag(
            mb_calc, cx, cz, yb, yt, rx, rz, n_rad=9, n_h=5, seed=sd, lean=ln,
            color_fn=lambda p, t: col_calcified(0.54 + 0.36 * t, float(p[1])),
            top_taper=0.35,
        )

    # 5B. Heavily studded flanks with ~28 protruding curved bone spikes / tusks
    tusk_rows = [
        # (x_mag, y, z, dx, dy, dz, r_base)
        (4.2,  2.2,  3.2,  2.2,  1.6,  1.6, 0.52),
        (6.4,  3.8,  1.8,  2.8,  2.1,  1.0, 0.56),
        (7.6,  5.8,  0.2,  2.6,  2.4,  0.4, 0.50),
        (5.4,  6.5,  2.8,  2.1,  2.2,  1.4, 0.46),
        (4.8,  9.2,  2.0,  2.2,  2.4,  1.2, 0.48),
        (5.8, 10.8,  0.4,  2.6,  2.5,  0.5, 0.52),
        (3.6, 12.4,  1.8,  1.6,  2.2,  1.1, 0.42),
        (8.1,  1.8, -0.8,  2.4,  1.5,  0.6, 0.48),
        (6.8,  7.8, -1.6,  2.5,  2.0, -0.4, 0.45),
        (4.2, 11.4, -2.2,  1.8,  2.4, -0.8, 0.42),
        (2.4,  5.2,  3.8,  0.8,  2.2,  2.0, 0.46),
        (1.8,  8.8,  3.2,  0.6,  2.4,  1.8, 0.44),
        (2.2, 12.2,  2.6,  0.5,  2.0,  1.4, 0.38),
    ]
    for xm, y, z, dx, dy, dz, rb in tusk_rows:
        for side in (-1.0, 1.0):
            base = (side * xm, y, z)
            tip = (side * (xm + dx), y + dy, z + dz)
            bend = (side * 0.35 * dx, 0.55 * dy, 0.30 * dz)
            add_curved_fang(mb_tusks, base, tip, bend, base_r=rb, n_steps=7, n_sides=6)
    # Central front lower tusk cluster
    add_curved_fang(mb_tusks, (0.0, 3.6, 4.1), (0.2, 6.2, 6.2), (0.0, 0.8, 0.5), base_r=0.52, n_steps=7, n_sides=6)
    add_curved_fang(mb_tusks, (-1.1, 10.2, 3.0), (-0.4, 12.8, 4.8), (0.0, 0.6, 0.4), base_r=0.42, n_steps=7, n_sides=6)

    # 5C. TWO COLOSSAL CURVED BROKEN HORNS (Y = 13..24) snapping near the top + 3 floating chunks
    def horn_curve(side, t):
        # Sweeps out to +-7.5 at mid-height then arcs fiercely inward to +-2.75 at Y=23.6
        hx = side * (4.3 + 3.25 * math.sin(t * math.pi * 0.85) - 2.1 * (t ** 2.1))
        hy = 13.0 + 10.8 * (t ** 0.92)
        hz = -0.3 + 1.05 * math.sin(t * math.pi)
        return np.array([hx, hy, hz], dtype=np.float32)

    for side in (-1.0, 1.0):
        h_spine = [horn_curve(side, i / 17.0) for i in range(18)]
        # Thick horn stopping abruptly at fractured stump (radius stays ~0.62 at break!)
        h_rad = [1.58 * (1.0 - 0.56 * (i / 17.0) ** 0.85) for i in range(18)]
        add_swept_tube(
            mb_horns, h_spine, h_rad, n_sides=14, u_tiles=2.0, v_tiles=6.0,
            radius_mod_fn=lambda t, phi, i, k: 1.0 + 0.13 * math.sin(t * 22.0 * math.pi),
            color_fn=lambda t, phi, wp: col_horn(0.46 + 0.42 * (t ** 0.7) + 0.10 * math.sin(t * 22.0 * math.pi)),
            cap_start=False, cap_end=True,
            end_tilt_normal=(-side * 0.7, 0.5, 0.25),
        )

    # 3 small floating broken horn chunks between the fractured tips
    float_chunks = [
        ([-1.35, 22.9, 0.25], [-0.55, 23.65, 0.38], [0.54, 0.36]),
        ([ 0.05, 23.55, 0.42], [ 0.42, 24.35, 0.30], [0.46, 0.28]),
        ([ 1.40, 22.85, 0.22], [ 0.72, 23.55, 0.40], [0.52, 0.34]),
    ]
    for p0, p1, (r0, r1) in float_chunks:
        pm = 0.5 * (np.asarray(p0, dtype=np.float32) + np.asarray(p1, dtype=np.float32)) + np.array([0.0, 0.12, 0.0], dtype=np.float32)
        add_swept_tube(
            mb_horns, [p0, pm, p1], [r0, 0.5 * (r0 + r1), r1], n_sides=8,
            color_fn=lambda t, phi, wp: col_horn(0.76 + 0.15 * t),
            cap_start=True, cap_end=True,
            end_tilt_normal=(0.4, 0.8, 0.2),
        )
    mb_horns.recompute_smooth_normals()

    # 5D. Suspended Grimacing Fanged Demon/Parasite Skull Idol at (0, 18.5, +1.8), facing +Z
    idol_cy = 18.6
    idol_cz = 1.9
    # Sculpted angular cranium block + cheekbones + brow crown
    add_cuboid(mb_idol, 0.0, idol_cy + 1.0, idol_cz - 0.15, 3.2, 2.3, 2.2, col_ossified(0.72), col_ossified(0.92), taper_x=0.84, taper_z=0.90)
    # Heavy furrowed demon brow plates angling outward
    for side in (-1.0, 1.0):
        add_cuboid(mb_idol, side * 1.15, idol_cy + 1.55, idol_cz + 0.65, 1.7, 0.85, 1.4, col_ossified(0.78), col_ossified(0.96), taper_x=0.75, x_shift_top=side * 0.25)
        # Flaring zygomatic cheek wings
        add_cuboid(mb_idol, side * 1.48, idol_cy - 0.10, idol_cz + 0.45, 1.15, 1.5, 1.4, col_ossified(0.65), col_ossified(0.86), taper_x=0.82)
        # Crown horn-ears on idol skull
        add_curved_fang(mb_idol, (side * 1.25, idol_cy + 1.9, idol_cz + 0.1), (side * 2.15, idol_cy + 3.4, idol_cz - 0.4), (side * 0.2, 0.4, 0.0), base_r=0.45, color_fn=lambda t, p, w: col_ossified(0.82 + 0.15 * t))
    # Nose bridge ridge & heavy jaw frame leaving central mouth cavity open
    add_cuboid(mb_idol, 0.0, idol_cy + 0.35, idol_cz + 0.88, 0.85, 1.35, 0.85, col_ossified(0.84), col_ossified(0.94), taper_x=0.65)
    add_cuboid(mb_idol, 0.0, idol_cy - 1.65, idol_cz + 0.45, 2.75, 0.90, 1.75, col_ossified(0.64), col_ossified(0.82), taper_x=0.86)
    # Back spine housing behind skull idol
    add_cuboid(mb_idol, 0.0, idol_cy - 0.15, idol_cz - 0.85, 2.5, 3.6, 1.35, col_ossified(0.55), col_ossified(0.76))

    # Fiery glowing green gastric acid eye sockets & grimacing mouth
    for side in (-1.0, 1.0):
        add_faceted_orb(mb_glow, side * 0.88, idol_cy + 0.82, idol_cz + 0.86, 0.56, 0.42, 0.48, color_fn=lambda p: col_acid(1.0), subdivisions=1)
    # Glowing acid mouth furnace
    add_faceted_orb(mb_glow, 0.0, idol_cy - 0.85, idol_cz + 0.72, 1.05, 0.52, 0.58, color_fn=lambda p: col_acid(0.98), subdivisions=1)

    # Sharp grimacing fangs inside idol jaw
    for sx in (-0.85, -0.32, 0.32, 0.85):
        add_curved_fang(mb_tusks, (sx, idol_cy - 0.35, idol_cz + 1.08), (sx * 0.95, idol_cy - 1.15, idol_cz + 1.18), (0.0, 0.0, 0.08), base_r=0.16)
        add_curved_fang(mb_tusks, (sx * 0.92, idol_cy - 1.30, idol_cz + 1.05), (sx * 0.88, idol_cy - 0.58, idol_cz + 1.15), (0.0, 0.0, 0.06), base_r=0.15)

    # 5E. Lashing Cable Web (Mat_VeinTendon): X-crisscross cables + helical horn coils + draped ribbons
    def add_taut_cable(p0, p1, sag=-0.25, rad=0.14, strands=2):
        a = np.asarray(p0, dtype=np.float32)
        b = np.asarray(p1, dtype=np.float32)
        tang = b - a
        perp = np.cross(tang, np.array([0.0, 0.0, 1.0], dtype=np.float32))
        pl = float(np.linalg.norm(perp))
        perp = perp / pl if pl > 1e-5 else np.array([1.0, 0.0, 0.0], dtype=np.float32)
        for st in range(strands):
            off = perp * ((st - 0.5 * (strands - 1)) * rad * 1.9)
            sp = []
            rads = []
            for s in range(9):
                t = s / 8.0
                p = (a + off) * (1.0 - t) + (b + off) * t + np.array([0.0, sag * math.sin(t * math.pi), 0.12 * math.sin(t * math.pi)], dtype=np.float32)
                sp.append(p)
                rads.append(rad)
            add_swept_tube(mb_tendon, sp, rads, n_sides=6, color_fn=lambda t, phi, wp: col_tendon(0.72 + 0.18 * math.sin(phi)), cap_start=True, cap_end=True)

    # X-crisscross multi-stranded tendon ropes tying skull directly to both colossal horns!
    lh_hi = horn_curve(-1.0, 0.72) + np.array([0.0, 0.0, 0.3], dtype=np.float32)
    rh_hi = horn_curve( 1.0, 0.72) + np.array([0.0, 0.0, 0.3], dtype=np.float32)
    lh_lo = horn_curve(-1.0, 0.24) + np.array([0.0, 0.0, 0.4], dtype=np.float32)
    rh_lo = horn_curve( 1.0, 0.24) + np.array([0.0, 0.0, 0.4], dtype=np.float32)
    lh_mid = horn_curve(-1.0, 0.48) + np.array([0.0, 0.0, 0.35], dtype=np.float32)
    rh_mid = horn_curve( 1.0, 0.48) + np.array([0.0, 0.0, 0.35], dtype=np.float32)

    skull_center_f = np.array([0.0, idol_cy + 0.1, idol_cz + 0.3], dtype=np.float32)
    add_taut_cable(lh_hi, skull_center_f + np.array([-0.8, 0.8, 0.0]), sag=-0.18, rad=0.14, strands=2)
    add_taut_cable(skull_center_f + np.array([0.8, -0.8, 0.0]), rh_lo, sag=-0.22, rad=0.14, strands=2)
    add_taut_cable(rh_hi, skull_center_f + np.array([0.8, 0.8, 0.0]), sag=-0.18, rad=0.14, strands=2)
    add_taut_cable(skull_center_f + np.array([-0.8, -0.8, 0.0]), lh_lo, sag=-0.22, rad=0.14, strands=2)
    # Lateral tension cables
    add_taut_cable(lh_mid, skull_center_f + np.array([-1.5, 0.0, -0.2]), sag=-0.35, rad=0.15, strands=2)
    add_taut_cable(rh_mid, skull_center_f + np.array([ 1.5, 0.0, -0.2]), sag=-0.35, rad=0.15, strands=2)
    # Lower horizontal sling between horn trunks
    add_taut_cable(lh_lo + np.array([0.0, 0.0, 0.6]), rh_lo + np.array([0.0, 0.0, 0.6]), sag=-0.95, rad=0.16, strands=2)

    # Spiraling tendon coils wrapped tightly around both colossal horn trunks!
    for side in (-1.0, 1.0):
        coil_pts = []
        coil_rads = []
        n_coil = 44
        for ic in range(n_coil):
            u = ic / float(n_coil - 1)
            t_h = 0.18 + 0.60 * u
            c_h = horn_curve(side, t_h)
            r_h = 1.58 * (1.0 - 0.56 * (t_h ** 0.85)) + 0.14
            # Tangent approx
            c_h2 = horn_curve(side, min(1.0, t_h + 0.02))
            tang = c_h2 - c_h
            tang /= max(1e-6, float(np.linalg.norm(tang)))
            ref = np.array([0.0, 0.0, 1.0], dtype=np.float32)
            b1 = np.cross(tang, ref)
            b1 /= max(1e-6, float(np.linalg.norm(b1)))
            b2 = np.cross(tang, b1)
            ang = u * 5.0 * 2.0 * math.pi * side
            wp = c_h + (b1 * math.cos(ang) + b2 * math.sin(ang)) * r_h
            coil_pts.append(wp)
            coil_rads.append(0.15)
        add_swept_tube(mb_tendon, coil_pts, coil_rads, n_sides=6, color_fn=lambda t, phi, wp: col_tendon(0.78), cap_start=True, cap_end=True)

        # Draped tattered cloth/flesh ribbons hanging from horn lashings
        for th_rib, rlen in [(0.26, 4.0), (0.48, 3.5), (0.68, 2.9)]:
            hp = horn_curve(side, th_rib) + np.array([0.0, -0.3, 0.6], dtype=np.float32)
            add_tattered_ribbon(
                mb_tendon, hp + np.array([-0.55, 0.0, 0.0]), hp + np.array([0.55, 0.0, 0.0]),
                length=rlen, sway_dir=(side * 0.28, 0.0, 0.38), n_w=4, n_h=5, seed=th_rib * 10.0 + side,
            )

    # Tattered flesh banner hanging directly below the skull idol chin
    add_tattered_ribbon(
        mb_tendon,
        np.array([-1.25, idol_cy - 1.9, idol_cz + 0.6], dtype=np.float32),
        np.array([ 1.25, idol_cy - 1.9, idol_cz + 0.6], dtype=np.float32),
        length=3.6, sway_dir=(0.0, 0.0, 0.48), n_w=4, n_h=6, seed=9.2,
    )

    # 5F. 3 Glowing Hanging Acid Soul Orbs (Mat_GastricAcid) with stone lantern caps dangling from cables
    orb_anchors = [
        ((-3.95, 16.5, 2.05), (-3.95, 14.0, 2.20), 0.86),
        (( 0.00, 16.5, 2.45), ( 0.00, 13.0, 2.65), 1.02),
        (( 3.95, 16.5, 2.05), ( 3.95, 14.0, 2.20), 0.86),
    ]
    for top_p, orb_p, r_orb in orb_anchors:
        add_taut_cable(top_p, (orb_p[0], orb_p[1] + r_orb * 0.9, orb_p[2]), sag=0.0, rad=0.11, strands=1)
        # Carved ossified stone lantern crown collar above each soul orb
        add_cuboid(mb_idol, orb_p[0], orb_p[1] + r_orb * 1.12, orb_p[2], r_orb * 0.85, r_orb * 0.38, r_orb * 0.85, col_ossified(0.65), col_ossified(0.92), taper_x=0.55, taper_z=0.55)
        add_faceted_orb(mb_glow, orb_p[0], orb_p[1], orb_p[2], r_orb * 0.94, r_orb * 1.18, r_orb * 0.94, color_fn=lambda p: col_acid(1.0), subdivisions=1)

    mb_tendon.recompute_smooth_normals()
    return "Monument_HornedColossus_North", [mb_crag, mb_calc, mb_tusks, mb_horns, mb_idol, mb_glow, mb_tendon]


def build_monument_gothic_buttress_south():
    """6. Monument_GothicButtress_South.glb: Twin curved towers at X=+-6.8, 9m open center, rib arches & jaw fangs."""
    mb_stone = MeshBuilder("Buttress_Stone_Towers", "Mat_OssifiedStone")
    mb_flesh = MeshBuilder("Buttress_Flesh_Masonry", "Mat_FleshWall")
    mb_calc = MeshBuilder("Buttress_Calcified_Bases", "Mat_DigestedCalcified")
    mb_arch = MeshBuilder("Buttress_Threshold_RibArch", "Mat_Cartilage")
    mb_fangs = MeshBuilder("Buttress_Jaw_Fangs", "Mat_Teeth")
    mb_tendon = MeshBuilder("Buttress_Tendon_Bindings", "Mat_VeinTendon")

    # 6A. Twin imposing curved gothic buttress towers at X = +-6.8m, leaving X in [-4.5, +4.5] open!
    for side in (-1.0, 1.0):
        tx = side * 6.8
        # Stepped battered gothic pier tiers (Y = -3..+15.2)
        tiers = [
            # (y_bot, y_top, sx, sz, x_shift, taper_x, taper_z)
            (-3.0,  2.5, 4.6, 6.8, -side * 0.1, 0.94, 0.92),
            ( 2.5,  7.5, 4.3, 6.1, -side * 0.25, 0.92, 0.90),
            ( 7.5, 12.2, 3.8, 5.2, -side * 0.40, 0.86, 0.86),
            (12.2, 14.8, 3.0, 4.0, -side * 0.45, 0.72, 0.78),
        ]
        for yb, yt, sx, sz, xs, tx_r, tz_r in tiers:
            cy = 0.5 * (yb + yt)
            sy = yt - yb
            add_cuboid(
                mb_stone, tx + xs, cy, 0.0, sx, sy, sz,
                col_ossified(0.56 + 0.02 * max(0, yb)),
                col_ossified(0.84 + 0.01 * yt),
                taper_x=tx_r, taper_z=tz_r, x_shift_top=-side * 0.18,
            )

        # Flanking gothic pilaster ribs & front/rear buttress fins
        for pz in (-2.2, 0.0, 2.2):
            add_cuboid(mb_stone, tx - side * 2.05, 5.2, pz, 0.75, 13.8, 0.95, col_ossified(0.62), col_ossified(0.88), taper_x=0.85, x_shift_top=-side * 0.25)
        add_cuboid(mb_stone, tx - side * 0.4, 4.5, 3.2, 2.4, 13.5, 1.15, col_ossified(0.66), col_ossified(0.90), taper_z=0.72, z_shift_top=-0.3)

        # Pointed gothic pinnacle spire crowning each tower (Y = 14.8..17.6)
        add_curved_fang(
            mb_stone,
            (tx - side * 0.55, 14.6, 0.0),
            (tx - side * 0.75, 17.8, 0.0),
            (0.0, 0.0, 0.15),
            base_r=1.35, n_steps=6, n_sides=6,
            color_fn=lambda t, p, w: col_ossified(0.75 + 0.20 * t),
        )

        # Organic flesh-wall infill recesses & calcified cliff skirts at tower bases
        add_organic_crag(mb_flesh, tx + side * 1.6, -1.0, -3.0, 9.2, 2.5, 3.6, n_rad=10, n_h=7, seed=3.0 + side, lean=(side * 0.3, 0.0))
        add_organic_crag(
            mb_calc, tx + side * 0.6, 1.8, -3.0, 3.2, 3.4, 2.8, n_rad=9, n_h=5, seed=5.5 + side,
            color_fn=lambda p, t: col_calcified(0.55 + 0.35 * t, float(p[1])),
        )

    # 6B. Overhead Gothic Rib Arches framing the 9m open threshold (Y = +9.0..+15.0)
    for z_off, apex_y, r_arch in [(-1.6, 14.2, 0.78), (0.0, 14.9, 0.95), (1.6, 14.2, 0.78)]:
        n_a = 19
        a_sp, a_rad = [], []
        for ia in range(n_a):
            u = ia / float(n_a - 1)  # 0..1 from -X tower to +X tower
            s = (u - 0.5) * 2.0      # -1..+1
            x = s * 5.6
            # Pointed gothic ogive curve
            y = 9.0 + (apex_y - 9.0) * (1.0 - abs(s) ** 1.45)
            a_sp.append([x, y, z_off])
            a_rad.append(r_arch * (1.0 + 0.18 * math.cos(s * math.pi * 3.0)))
        add_swept_tube(
            mb_arch, a_sp, a_rad, n_sides=10, u_tiles=2.0, v_tiles=5.0,
            radius_mod_fn=lambda t, phi, i, k: 1.0 + 0.14 * math.sin(3.0 * phi),
            color_fn=lambda t, phi, wp: col_cartilage(0.74 + 0.22 * (0.5 + 0.5 * math.sin(phi))),
            cap_start=True, cap_end=True,
        )
    mb_arch.recompute_smooth_normals()

    # 6C. Hanging jaw fangs under overhead arch (Y = 8.4..13.5) + flank tusks
    fang_xs = [-3.6, -2.5, -1.4, -0.5, 0.5, 1.4, 2.5, 3.6]
    for idx, fx in enumerate(fang_xs):
        s = abs(fx) / 5.6
        arch_y = 14.6 - (14.6 - 9.0) * (s ** 1.45)
        f_len = 2.4 + 0.8 * (1.0 - s) if idx % 2 == 0 else 1.7 + 0.5 * (1.0 - s)
        for fz in (-0.8, 1.0):
            add_curved_fang(
                mb_fangs,
                (fx, arch_y - 0.35, fz),
                (fx * 0.92, arch_y - f_len, fz + 0.22),
                (0.0, 0.0, 0.18),
                base_r=0.34 if idx % 2 == 0 else 0.26,
            )
    # Tower front tusks pointing inward into threshold frame
    for side in (-1.0, 1.0):
        for py in (2.5, 5.4):
            add_curved_fang(
                mb_fangs,
                (side * 4.75, py, 1.4),
                (side * 3.15, py + 1.1, 2.3),
                (0.0, 0.35, 0.2),
                base_r=0.42,
            )

    # 6D. Tendon bindings strapping bone arch to towers + draped curtains
    for side in (-1.0, 1.0):
        for y_wrap in (9.6, 11.4):
            sp = [
                [side * 4.5, y_wrap, -2.4],
                [side * 4.3, y_wrap + 0.4, 0.0],
                [side * 4.5, y_wrap, 2.4],
                [side * 7.8, y_wrap - 0.3, 2.2],
                [side * 7.8, y_wrap - 0.3, -2.2],
            ]
            add_swept_tube(mb_tendon, sp, [0.22] * 5, n_sides=6, color_fn=lambda t, phi, wp: col_tendon(0.75), cap_start=True, cap_end=True)
        add_tattered_ribbon(
            mb_tendon,
            (side * 5.0, 9.2, 2.0), (side * 3.8, 10.6, 2.0),
            length=2.8, sway_dir=(-side * 0.2, 0.0, 0.35), n_w=3, n_h=5, seed=side * 2.5,
        )
    mb_tendon.recompute_smooth_normals()

    return "Monument_GothicButtress_South", [mb_stone, mb_flesh, mb_calc, mb_arch, mb_fangs, mb_tendon]


def _build_sunken_spire(name, tilt_x, tilt_z, height_top, base_r, has_companion, seed):
    """Helper for asymmetric tilted fractured hexagonal/gothic bone-stone spires A & B."""
    mb_stone = MeshBuilder(f"{name}_Obelisk", "Mat_OssifiedStone")
    mb_calc = MeshBuilder(f"{name}_ErodedBase", "Mat_DigestedCalcified")
    mb_teeth = MeshBuilder(f"{name}_Spurs", "Mat_Teeth")

    # 1. Fractured tilted gothic hexagonal obelisk
    n_seg = 6 if not has_companion else 8
    n_tiers = 11
    ring_pts = []
    ring_cols = []
    for it in range(n_tiers):
        t = it / float(n_tiers - 1)
        y = -3.5 + (height_top + 3.5) * t
        # Asymmetric taper with slight gothic stepped ledges
        step_notch = 0.90 if it in (3, 7) else 1.0
        r = base_r * (1.0 - 0.54 * (t ** 0.85)) * step_notch
        cx = tilt_x * (y + 2.0) * 0.14
        cz = tilt_z * (y + 2.0) * 0.14
        row_p = []
        row_c = []
        for k in range(n_seg):
            ang = (k / float(n_seg)) * 2.0 * math.pi + seed * 0.3
            flute = 0.88 if (k % 2 == 1 and t > 0.2) else 1.04
            wx = cx + math.cos(ang) * r * flute
            wz = cz + math.sin(ang) * r * flute
            wy = y
            # Jagged diagonal break at top ring!
            if it == n_tiers - 1:
                wy += 0.95 * math.cos(ang + seed) - 0.55 * math.sin(2.0 * ang)
            row_p.append(np.array([wx, wy, wz], dtype=np.float32))
            sh = 0.56 + 0.36 * t + 0.08 * math.cos(ang)
            row_c.append(acid_bounce_tint(col_ossified(sh), wy, 0.30))
        ring_pts.append(row_p)
        ring_cols.append(row_c)

    for it in range(n_tiers - 1):
        t0 = it / float(n_tiers - 1)
        t1 = (it + 1) / float(n_tiers - 1)
        c_mid = np.mean(ring_pts[it], axis=0)
        for k in range(n_seg):
            kn = (k + 1) % n_seg
            p00, p10 = ring_pts[it][k], ring_pts[it][kn]
            p11, p01 = ring_pts[it + 1][kn], ring_pts[it + 1][k]
            hint = 0.25 * (p00 + p10 + p11 + p01) - c_mid
            add_oriented_quad(
                mb_stone, p00, p10, p11, p01,
                (k / float(n_seg) * 2.0, t0 * 4.0), ((k + 1) / float(n_seg) * 2.0, t0 * 4.0),
                ((k + 1) / float(n_seg) * 2.0, t1 * 4.0), (k / float(n_seg) * 2.0, t1 * 4.0),
                ring_cols[it][k], ring_cols[it][kn], ring_cols[it + 1][kn], ring_cols[it + 1][k],
                hint,
            )

    # Jagged fractured stone top cap
    top_c = np.mean(ring_pts[-1], axis=0) + np.array([0.0, 0.35, 0.0], dtype=np.float32)
    for k in range(n_seg):
        kn = (k + 1) % n_seg
        add_oriented_tri(
            mb_stone, top_c, ring_pts[-1][k], ring_pts[-1][kn],
            (0.5, 0.5), (1.0, 0.5), (0.5, 1.0),
            col_ossified(0.90), ring_cols[-1][k], ring_cols[-1][kn],
            (0.0, 1.0, 0.0),
        )

    # Optional companion broken pinnacle for Spire B or wedged shard for Spire A
    if has_companion:
        add_organic_crag(
            mb_stone, -2.2, 1.1, -3.5, 6.4, 1.35, 1.25, n_rad=6, n_h=6, seed=seed + 2.2,
            lean=(-1.4, 0.8), color_fn=lambda p, t: acid_bounce_tint(col_ossified(0.55 + 0.35 * t), float(p[1]), 0.32), top_taper=0.35,
        )
    else:
        # Wedged diagonal gothic buttress shard near crown
        add_organic_crag(
            mb_stone, 1.4, 0.8, 3.0, 9.8, 1.05, 0.95, n_rad=6, n_h=6, seed=seed + 1.5,
            lean=(1.1, 0.4), color_fn=lambda p, t: col_ossified(0.65 + 0.30 * t), top_taper=0.18,
        )

    # 2. Acid-eroded digested calcified base ridges
    for bx, bz, yt, rx, rz, sd in [
        ( 0.0,  0.0, 2.8, base_r * 1.45, base_r * 1.35, seed),
        (-1.8,  1.4, 1.9, base_r * 0.95, base_r * 0.85, seed + 1.1),
        ( 1.9, -1.2, 2.2, base_r * 0.90, base_r * 0.95, seed + 2.3),
    ]:
        add_organic_crag(
            mb_calc, bx, bz, -3.5, yt, rx, rz, n_rad=10, n_h=5, seed=sd,
            color_fn=lambda p, t: col_calcified(0.52 + 0.38 * t, float(p[1])), top_taper=0.52,
        )

    # 3. Embedded jutting teeth/spurs around eroded collar
    for ia in range(4):
        ang = (ia / 4.0) * 2.0 * math.pi + seed
        bx = math.cos(ang) * base_r * 1.15
        bz = math.sin(ang) * base_r * 1.15
        add_curved_fang(
            mb_teeth, (bx, 0.6, bz),
            (bx * 1.65, 2.8 + 0.5 * (ia % 2), bz * 1.65),
            (0.0, 0.4, 0.0), base_r=0.32,
        )

    return name, [mb_stone, mb_calc, mb_teeth]


def build_monument_sunken_spire_a():
    """7. Monument_SunkenSpire_A.glb: Tall tilted hexagonal fractured gothic obelisk (Y = -3.5..+13.2m)."""
    return _build_sunken_spire("Monument_SunkenSpire_A", tilt_x=1.25, tilt_z=0.55, height_top=13.2, base_r=2.15, has_companion=False, seed=1.3)


def build_monument_sunken_spire_b():
    """8. Monument_SunkenSpire_B.glb: Broader sheared twin-pronged octagonal spire (Y = -3.5..+10.2m)."""
    return _build_sunken_spire("Monument_SunkenSpire_B", tilt_x=-1.45, tilt_z=0.70, height_top=10.2, base_r=2.55, has_companion=True, seed=4.7)


def _build_floating_islet(name, radius_x, radius_z, seed, spine_style):
    """Helper for Prop_FloatingIslet_A & B (Y = -3.0..+2.2)."""
    mb_calc = MeshBuilder(f"{name}_Calcified_Crust", "Mat_DigestedCalcified")
    mb_flesh = MeshBuilder(f"{name}_Flesh_Plateau", "Mat_FleshWall")
    mb_bone = MeshBuilder(f"{name}_Exposed_Ribs", "Mat_Cartilage")

    # 1. Irregular rocky-calcified underwater skirt & top rim (Y = -3.0..+0.95)
    n_rad = 18
    n_rings = 7
    rings = []
    cols = []
    for ir in range(n_rings):
        t = ir / float(n_rings - 1)  # 0 at bottom keel (-3.0), 1 at plateau rim (+0.85)
        if t < 0.65:
            lt = t / 0.65
            y = -3.0 + 3.25 * lt
            rad_s = 0.28 + 0.72 * (lt ** 0.62)
        else:
            lt = (t - 0.65) / 0.35
            y = 0.25 + 0.65 * lt
            rad_s = 1.0 - 0.14 * lt
        row_p, row_c = [], []
        for ia in range(n_rad):
            ang = (ia / float(n_rad)) * 2.0 * math.pi
            lobe = 1.0 + 0.18 * math.sin(3.0 * ang + seed) + 0.11 * math.cos(5.0 * ang - seed * 1.6)
            wx = math.cos(ang) * radius_x * rad_s * lobe
            wz = math.sin(ang) * radius_z * rad_s * lobe
            wy = y + 0.16 * math.sin(4.0 * ang + seed) * (1.0 - 0.5 * t)
            row_p.append(np.array([wx, wy, wz], dtype=np.float32))
            row_c.append(col_calcified(0.46 + 0.44 * t, wy))
        rings.append(row_p)
        cols.append(row_c)

    for ir in range(n_rings - 1):
        t0 = ir / float(n_rings - 1)
        t1 = (ir + 1) / float(n_rings - 1)
        for ia in range(n_rad):
            ian = (ia + 1) % n_rad
            p00, p10 = rings[ir][ia], rings[ir][ian]
            p11, p01 = rings[ir + 1][ian], rings[ir + 1][ia]
            hint = 0.25 * (p00 + p10 + p11 + p01) - np.array([0.0, -1.2, 0.0], dtype=np.float32)
            add_oriented_quad(
                mb_calc, p00, p10, p11, p01,
                (ia / float(n_rad) * 3.0, t0 * 2.5), ((ia + 1) / float(n_rad) * 3.0, t0 * 2.5),
                ((ia + 1) / float(n_rad) * 3.0, t1 * 2.5), (ia / float(n_rad) * 3.0, t1 * 2.5),
                cols[ir][ia], cols[ir][ian], cols[ir + 1][ian], cols[ir + 1][ia],
                hint,
            )

    # Close bottom keel
    bot_c = np.array([0.0, -3.15, 0.0], dtype=np.float32)
    for ia in range(n_rad):
        ian = (ia + 1) % n_rad
        add_oriented_tri(mb_calc, bot_c, rings[0][ian], rings[0][ia], (0.5, 0.5), (0.8, 0.5), (0.5, 0.8), col_calcified(0.32, -3.0), cols[0][ian], cols[0][ia], (0.0, -1.0, 0.0))

    # 2. Walkable fleshy plateau top cap (Mat_FleshWall, gentle mound Y = +0.85..+1.10, planar UVs!)
    top_center = np.array([0.0, 1.08, 0.0], dtype=np.float32)
    mid_ring = []
    mid_cols = []
    for ia in range(n_rad):
        p_rim = rings[-1][ia]
        p_m = np.array([p_rim[0] * 0.56, 1.00 + 0.06 * math.sin(ia * 1.3 + seed), p_rim[2] * 0.56], dtype=np.float32)
        mid_ring.append(p_m)
        mid_cols.append(col_flesh(0.84, float(p_m[1])))

    def p_uv(pt):
        return (float(pt[0]) * 0.24, float(pt[2]) * 0.24)

    for ia in range(n_rad):
        ian = (ia + 1) % n_rad
        r0, r1 = rings[-1][ia], rings[-1][ian]
        m0, m1 = mid_ring[ia], mid_ring[ian]
        add_oriented_quad(
            mb_flesh, r0, r1, m1, m0,
            p_uv(r0), p_uv(r1), p_uv(m1), p_uv(m0),
            col_flesh(0.72, float(r0[1])), col_flesh(0.72, float(r1[1])),
            mid_cols[ian], mid_cols[ia],
            (0.0, 1.0, 0.0),
        )
        add_oriented_tri(
            mb_flesh, top_center, m0, m1,
            p_uv(top_center), p_uv(m0), p_uv(m1),
            col_flesh(0.92, 1.1), mid_cols[ia], mid_cols[ian],
            (0.0, 1.0, 0.0),
        )

    # 3. Exposed giant rib bones curving out of the islet up to Y = +2.2
    if spine_style == "A":
        rib_specs = [
            ([[-2.4, 0.2, -1.6], [-2.9, 1.5, -1.2], [-2.2, 2.22, -0.4], [-1.3, 1.75, 0.4]], [0.42, 0.36, 0.28, 0.14]),
            ([[-1.8, 0.2,  0.4], [-2.3, 1.4,  0.8], [-1.6, 2.10, 1.5], [-0.8, 1.55, 2.1]], [0.38, 0.32, 0.24, 0.12]),
            ([[ 2.2, 0.2, -1.2], [ 2.7, 1.3, -0.8], [ 2.0, 1.95, 0.0], [ 1.2, 1.40, 0.6]], [0.36, 0.30, 0.22, 0.12]),
        ]
    else:
        rib_specs = [
            ([[-1.6, 0.2, -2.0], [-1.9, 1.45, -1.5], [-1.2, 2.18, -0.8], [-0.4, 1.70, -0.1]], [0.38, 0.32, 0.25, 0.12]),
            ([[ 1.5, 0.2,  1.2], [ 2.0, 1.40,  1.6], [ 1.4, 2.05,  2.2], [ 0.7, 1.50,  2.6]], [0.36, 0.30, 0.22, 0.11]),
        ]
        # Extra sharp tooth-spurs for Islet B
        mb_teeth = MeshBuilder(f"{name}_ToothSpurs", "Mat_Teeth")
        add_curved_fang(mb_teeth, (2.3, 0.4, -0.6), (3.1, 2.15, -0.9), (0.2, 0.3, 0.0), base_r=0.30)
        add_curved_fang(mb_teeth, (-2.2, 0.4, 1.4), (-2.9, 1.95, 1.9), (-0.2, 0.3, 0.1), base_r=0.28)
    for sp, rads in rib_specs:
        add_swept_tube(mb_bone, sp, rads, n_sides=8, color_fn=lambda t, phi, wp: col_cartilage(0.76 + 0.20 * t), cap_start=True, cap_end=True)
    mb_bone.recompute_smooth_normals()

    meshes = [mb_calc, mb_flesh, mb_bone]
    if spine_style == "B":
        meshes.append(mb_teeth)
    return name, meshes


def build_prop_floating_islet_a():
    """9. Prop_FloatingIslet_A.glb: Chunky flat-topped flesh-rock islet + exposed giant ribs (r~5.4m)."""
    return _build_floating_islet("Prop_FloatingIslet_A", 5.5, 4.9, seed=1.7, spine_style="A")


def build_prop_floating_islet_b():
    """10. Prop_FloatingIslet_B.glb: Elongated floating islet + spinal rib & fang spurs (r~4.6m)."""
    return _build_floating_islet("Prop_FloatingIslet_B", 4.9, 4.1, seed=4.9, spine_style="B")


def _build_cavern_wall(name, span_x, has_alcoves=False):
    """
    11 & 12. Cavern_Wall_Long (span 190m) & Cavern_Wall_End (span 165m):
    - Extends along local X in [-span_x/2, +span_x/2], depth Z in [-16, ~+12] (overhang at top), Y = -4..+54m.
    - Inner stomach face faces +Z, pivoted at bottom-center (0, 0, 0).
    """
    mb_wall = MeshBuilder(f"{name}_FleshCliffs", "Mat_FleshWall")
    mb_calc = MeshBuilder(f"{name}_ShoreBase", "Mat_DigestedCalcified")
    mb_mem = MeshBuilder(f"{name}_AlveoliAlcoves", "Mat_MembraneBacklit") if has_alcoves else None

    nx = 46 if not has_alcoves else 44
    ny = 16
    half_x = span_x * 0.5
    xs = np.linspace(-half_x, half_x, nx)

    # Inner stomach wall surface facing +Z
    w_pts, w_uvs, w_cols = [], [], []
    # Outer back wall closing at Z = -16 so wall has full 16m thickness
    b_pts, b_uvs, b_cols = [], [], []

    arch_centers = [-span_x * 0.28, 0.0, span_x * 0.28] if has_alcoves else []

    for ix, x in enumerate(xs):
        xf = float(x)
        u = ix / float(nx - 1)
        # Curve inward slightly toward the ends (+Z) to soften rectangular cavern corners
        corner_bow = 5.5 * ((abs(u - 0.5) * 2.0) ** 2.6)
        buttress_wave = 2.6 * math.sin(u * math.pi * (10.0 if not has_alcoves else 9.0)) + 1.4 * math.cos(u * math.pi * 19.0)

        row_p, row_u, row_c = [], [], []
        brow_p, brow_u, brow_c = [], [], []
        for iy in range(ny):
            v = iy / float(ny - 1)
            y = -4.0 + 58.0 * (v ** 0.96)

            # Inward overhang arching dramatically toward +Z at top (Y = 34..54) to meet ceiling vault
            overhang = 12.5 * (max(0.0, v - 0.45) / 0.55) ** 1.45
            # Horizontal peristaltic stomach folds (rugae)
            rugae = 1.65 * math.sin(v * 5.0 * math.pi + 0.5 * math.sin(u * 12.0)) * math.sin(v * math.pi)
            buttress_env = math.sin(v * math.pi * 0.92)

            # For Cavern_Wall_End: carve 3 grand organic archway reliefs / upper alveoli pockets!
            arch_recess = 0.0
            in_alcove = False
            if has_alcoves:
                for ac in arch_centers:
                    dx_a = abs(xf - ac)
                    if dx_a < 13.5 and 6.0 < y < 44.0:
                        arch_h = 38.0 * math.sqrt(max(0.0, 1.0 - (dx_a / 13.5) ** 2))
                        if y < 6.0 + arch_h:
                            t_ar = 1.0 - (dx_a / 13.5) ** 2
                            t_y = math.sin(max(0.0, min(1.0, (y - 6.0) / max(1.0, arch_h))) * math.pi)
                            arch_recess = -6.8 * t_ar * t_y
                            if 18.0 < y < 42.0 and dx_a < 9.5:
                                in_alcove = True

            z_inner = corner_bow + overhang + buttress_wave * buttress_env + rugae + arch_recess
            # Slight rocky shoreline shelf flare near Y = -4..+5
            shore_flare = 2.8 * max(0.0, 1.0 - (y + 4.0) / 9.5)
            z_inner += shore_flare

            ao = 0.36 + 0.48 * (v ** 0.65) + 0.14 * (0.5 + 0.5 * math.sin(v * 5.0 * math.pi))
            if arch_recess < -1.0:
                ao *= 0.85
            col = col_flesh(ao, y)

            row_p.append([xf, y, z_inner])
            row_u.append((u * 16.0, v * 6.0))
            row_c.append(col)

            # Back solid outer boundary at Z = -16.0
            brow_p.append([xf, y, -16.0])
            brow_u.append((u * 16.0, v * 6.0))
            brow_c.append(col_flesh(0.28, y))

        w_pts.append(row_p)
        w_uvs.append(row_u)
        w_cols.append(row_c)
        b_pts.append(brow_p)
        b_uvs.append(brow_u)
        b_cols.append(brow_c)

    add_indexed_grid(mb_wall, w_pts, w_uvs, w_cols, outward_hints=(0.0, 0.0, 1.0))
    add_indexed_grid(mb_wall, b_pts, b_uvs, b_cols, outward_hints=(0.0, 0.0, -1.0))

    # Top roof cap & side end-caps so wall is a solid 16m thick volume
    for ix in range(nx - 1):
        # Top cap at iy = ny-1
        add_oriented_quad(
            mb_wall, w_pts[ix][-1], w_pts[ix + 1][-1], b_pts[ix + 1][-1], b_pts[ix][-1],
            (0, 0), (1, 0), (1, 1), (0, 1),
            w_cols[ix][-1], w_cols[ix + 1][-1], b_cols[ix + 1][-1], b_cols[ix][-1],
            (0.0, 1.0, 0.0),
        )
    for iy in range(ny - 1):
        # Left end cap (ix = 0, facing -X)
        add_oriented_quad(
            mb_wall, b_pts[0][iy], w_pts[0][iy], w_pts[0][iy + 1], b_pts[0][iy + 1],
            (0, 0), (1, 0), (1, 1), (0, 1),
            b_cols[0][iy], w_cols[0][iy], w_cols[0][iy + 1], b_cols[0][iy + 1],
            (-1.0, 0.0, 0.0),
        )
        # Right end cap (ix = nx-1, facing +X)
        add_oriented_quad(
            mb_wall, w_pts[-1][iy], b_pts[-1][iy], b_pts[-1][iy + 1], w_pts[-1][iy + 1],
            (0, 0), (1, 0), (1, 1), (0, 1),
            w_cols[-1][iy], b_cols[-1][iy], b_cols[-1][iy + 1], w_cols[-1][iy + 1],
            (1.0, 0.0, 0.0),
        )

    # Eroded calcified cliff shelves along the acid shoreline (Y = -4..+7)
    n_shelves = 9 if not has_alcoves else 8
    for ishelf in range(n_shelves):
        sx = -half_x * 0.88 + (ishelf / float(n_shelves - 1)) * (span_x * 0.88)
        if has_alcoves and any(abs(sx - ac) < 8.5 for ac in arch_centers):
            continue
        u_s = (sx + half_x) / span_x
        z_s = 5.5 * ((abs(u_s - 0.5) * 2.0) ** 2.6) + 2.0
        add_organic_crag(
            mb_calc, sx, z_s, -4.0, 6.8 + 2.2 * math.sin(ishelf * 1.9),
            6.5, 4.2, n_rad=10, n_h=5, seed=ishelf * 2.1, lean=(0.0, 0.8),
            color_fn=lambda p, t: col_calcified(0.50 + 0.42 * t, float(p[1])),
            top_taper=0.45,
        )

    # Embedded glowing backlit alveoli membrane alcoves & archway framing ribs for Cavern_Wall_End
    if has_alcoves and mb_mem is not None:
        for idx_a, ac in enumerate(arch_centers):
            # Massive organic archway pillar framing each alcove
            for side in (-1.0, 1.0):
                pil_sp = [
                    [ac + side * 14.2, -3.5, 2.2],
                    [ac + side * 13.8, 12.0, 1.6],
                    [ac + side * 11.2, 26.0, 2.6],
                    [ac + side *  5.5, 39.5, 5.2],
                    [ac,               44.5, 6.8],
                ]
                add_swept_tube(
                    mb_wall, pil_sp, [3.2, 2.8, 2.6, 2.3, 2.0], n_sides=8,
                    color_fn=lambda t, phi, wp: col_flesh(0.62 + 0.30 * t, float(wp[1])),
                    cap_start=True, cap_end=True,
                )
            # Thick organic flesh rim framing the recessed backlit alveoli alcove
            rim_pts = []
            rim_rads = []
            for ir in range(17):
                th = (ir / 16.0) * 2.0 * math.pi
                rx_a, ry_a = 8.4, 9.6
                wx = ac + math.cos(th) * rx_a
                wy = 27.5 + math.sin(th) * ry_a
                wz = -2.2 + 1.8 * max(0.0, math.sin(th))
                rim_pts.append([wx, wy, wz])
                rim_rads.append(1.85)
            add_swept_tube(
                mb_wall, rim_pts, rim_rads, n_sides=8,
                color_fn=lambda t, phi, wp: col_flesh(0.55 + 0.32 * (0.5 + 0.5 * math.sin(phi)), float(wp[1])),
                cap_start=False, cap_end=False,
            )
            # Glowing backlit translucent alveoli membrane dome tucked cleanly inside the recessed pocket!
            add_faceted_orb(
                mb_mem, ac, 27.5, -4.6, 8.2, 9.4, 2.6,
                color_fn=lambda p: col_membrane(0.86 + 0.14 * smoothstep(20.0, 35.0, float(p[1]))),
                subdivisions=2,
            )

    mb_wall.recompute_smooth_normals()
    meshes = [mb_wall, mb_calc]
    if mb_mem is not None:
        meshes.append(mb_mem)
    return name, meshes


def build_cavern_wall_long():
    """11. Cavern_Wall_Long.glb: 190m span along X, Z in [-16, ~+12], Y=-4..+54m, facing +Z."""
    return _build_cavern_wall("Cavern_Wall_Long", span_x=190.0, has_alcoves=False)


def build_cavern_wall_end():
    """12. Cavern_Wall_End.glb: 165m span along X, 3 organic archways + backlit alveoli alcoves, facing +Z."""
    return _build_cavern_wall("Cavern_Wall_End", span_x=165.0, has_alcoves=True)


def build_cavern_rib_arch():
    """13. Cavern_RibArch.glb: Massive sweeping overhead cartilage rib-arch X in [-75, +75], apex Y=+48."""
    mb_cart = MeshBuilder("Cavern_RibArch_Cartilage", "Mat_Cartilage")
    mb_flesh = MeshBuilder("Cavern_RibArch_FleshRidges", "Mat_FleshWall")
    mb_tendon = MeshBuilder("Cavern_RibArch_TendonWraps", "Mat_VeinTendon")

    n_sp = 33
    # Main twin-braided giant cartilage arches (front & back parallel ribs merging near roots)
    for z_off, y_apex, r_base in [(-2.6, 47.2, 3.2), (2.6, 47.2, 3.2), (0.0, 48.2, 3.6)]:
        sp = []
        rads = []
        for i in range(n_sp):
            t = i / float(n_sp - 1)
            s = (t - 0.5) * 2.0  # -1..+1
            x = s * 75.0
            # Soaring parabolic/catenary arch from Y=-4 up to apex Y=+48
            y = -4.0 + (y_apex + 4.0) * (1.0 - (abs(s) ** 1.72))
            z = z_off * (1.0 - 0.45 * (s ** 2)) + 1.2 * math.sin(s * math.pi * 2.0) * (0.3 if z_off == 0 else 1.0)
            # Segmented vertebral/rib joint swells
            node = 1.0 + 0.16 * math.sin(t * 14.0 * math.pi)
            r = r_base * (1.15 - 0.38 * (1.0 - s * s)) * node
            sp.append([x, y, z])
            rads.append(r)

        target_mb = mb_cart if z_off != 0.0 else mb_flesh
        c_fn = (
            (lambda t, phi, wp: col_cartilage(0.72 + 0.24 * (0.5 + 0.5 * math.sin(phi))))
            if z_off != 0.0
            else (lambda t, phi, wp: col_flesh(0.58 + 0.34 * (0.5 + 0.5 * math.sin(phi)), float(wp[1])))
        )
        add_swept_tube(
            target_mb, sp, rads, n_sides=10, u_tiles=2.0, v_tiles=14.0,
            radius_mod_fn=lambda t, phi, i, k: 1.0 + 0.12 * math.cos(3.0 * phi),
            color_fn=c_fn,
            cap_start=True, cap_end=True,
        )

    # Braided longitudinal flesh ridges & tendon slings spiraling across the rib arch
    for strand in range(2):
        b_sp = []
        b_rad = []
        for i in range(n_sp):
            t = i / float(n_sp - 1)
            s = (t - 0.5) * 2.0
            x = s * 75.0
            y_core = -4.0 + 52.0 * (1.0 - (abs(s) ** 1.72))
            ang = t * 5.0 * math.pi + strand * math.pi
            y = y_core + math.sin(ang) * 3.1
            z = math.cos(ang) * 3.6
            b_sp.append([x, y, z])
            b_rad.append(1.15 * (1.1 - 0.25 * (1.0 - s * s)))
        add_swept_tube(
            mb_flesh if strand == 0 else mb_tendon,
            b_sp, b_rad, n_sides=8, u_tiles=1.5, v_tiles=14.0,
            color_fn=(lambda t, phi, wp: col_flesh(0.68, float(wp[1]))) if strand == 0 else (lambda t, phi, wp: col_tendon(0.76)),
            cap_start=True, cap_end=True,
        )

    # Submerged flared muscular root crags at X = +-75, Y = -4..+8
    for side in (-1.0, 1.0):
        add_organic_crag(mb_flesh, side * 74.5, 0.0, -4.0, 10.5, 6.2, 6.8, n_rad=10, n_h=6, seed=side * 2.2, lean=(-side * 1.4, 0.0))

    # Lateral bone rib spurs jutting along the upper arch silhouette
    for i_spur in range(5, n_sp - 5, 3):
        t = i_spur / float(n_sp - 1)
        s = (t - 0.5) * 2.0
        x = s * 75.0
        y = -4.0 + 52.0 * (1.0 - (abs(s) ** 1.72))
        for side_z in (-1.0, 1.0):
            add_curved_fang(
                mb_cart,
                (x, y + 0.5, side_z * 2.6),
                (x * 0.96, y - 3.6, side_z * 5.6),
                (0.0, -0.8, side_z * 0.6),
                base_r=0.95, n_steps=6, n_sides=6,
                color_fn=lambda tv, phi, wp: col_cartilage(0.75 + 0.22 * tv),
            )

    mb_cart.recompute_smooth_normals()
    mb_flesh.recompute_smooth_normals()
    mb_tendon.recompute_smooth_normals()
    return "Cavern_RibArch", [mb_cart, mb_flesh, mb_tendon]


def build_cavern_membrane_ceiling_vault():
    """
    14. Cavern_Membrane_CeilingVault.glb (Reference Photo 2 translucent alveoli roof!):
    - Spans 175m x 205m (X in [-87.5, +87.5], Z in [-102.5, +102.5]) at Y = 44..72m, pivoted at (0,0,0).
    - Heavy crimson visceral web-beams (Mat_FleshWall) with smooth 3D oval skylight collar rings
      framing 17 smooth oval backlit translucent alveoli skylights (Mat_MembraneBacklit, double_sided=True)
      + concentric & radial visceral beams dipping down at the horizon.
    """
    mb_beams = MeshBuilder("Ceiling_Visceral_WebBeams", "Mat_FleshWall")
    mb_mem = MeshBuilder("Ceiling_Backlit_Alveoli", "Mat_MembraneBacklit")

    hx, hz = 87.5, 102.5
    nx, nz = 56, 64

    # 17 large oval translucent alveoli skylights: (cx, cz, rx, rz, angle_rad)
    alveoli = [
        (0.0, 0.0, 16.5, 20.5, 0.0),  # Grand central skylight
    ]
    for k in range(6):
        th = (k / 6.0) * 2.0 * math.pi + 0.25
        alveoli.append((math.cos(th) * 38.0, math.sin(th) * 44.0, 12.2, 14.2, th))
    for k in range(10):
        th = (k / 10.0) * 2.0 * math.pi + 0.1
        alveoli.append((math.cos(th) * 63.0, math.sin(th) * 74.0, 10.0, 11.4, th))

    def eval_alveoli_sdf(x, z):
        best = 1e9
        for cx, cz, rx, rz, ang in alveoli:
            ca, sa = math.cos(ang), math.sin(ang)
            dx, dz = x - cx, z - cz
            lx = dx * ca + dz * sa
            lz = -dx * sa + dz * ca
            d = math.sqrt((lx / rx) ** 2 + (lz / rz) ** 2) - 1.0
            if d < best:
                best = d
        return best

    def dome_base_y(x, z):
        rn = math.hypot(x / hx, z / hz)
        base_y = 69.5 - 25.5 * (min(1.18, rn) ** 1.65)
        base_y += 1.4 * math.cos(rn * 3.0 * math.pi) * (1.0 - 0.5 * min(1.0, rn))
        return base_y, rn

    # 1. Outer visceral flesh ceiling shell (Mat_FleshWall) with smooth punch-outs inside oval skylights
    xs = np.linspace(-hx, hx, nx + 1)
    zs = np.linspace(-hz, hz, nz + 1)
    grid_pos = [[None] * (nz + 1) for _ in range(nx + 1)]
    grid_alv = [[0.0] * (nz + 1) for _ in range(nx + 1)]
    grid_rn = [[0.0] * (nz + 1) for _ in range(nx + 1)]

    for ix, x in enumerate(xs):
        for iz, z in enumerate(zs):
            xf, zf = float(x), float(z)
            d_alv = eval_alveoli_sdf(xf, zf)
            by, rn = dome_base_y(xf, zf)
            beam_drop = 2.4 * min(1.0, max(0.0, d_alv + 0.05) / 0.25)
            grid_pos[ix][iz] = np.array([xf, by - beam_drop, zf], dtype=np.float32)
            grid_alv[ix][iz] = d_alv
            grid_rn[ix][iz] = rn

    beam_v = {}
    def get_beam_v(ix, iz):
        key = (ix, iz)
        if key in beam_v:
            return beam_v[key]
        rn = grid_rn[ix][iz]
        sh = 0.42 + 0.36 * (1.0 - min(1.0, rn))
        idx = mb_beams.add_vertex(
            grid_pos[ix][iz], (0.0, -1.0, 0.0),
            (ix / float(nx) * 14.0, iz / float(nz) * 16.0),
            col_flesh(sh, 50.0),
        )
        beam_v[key] = idx
        return idx

    for ix in range(nx):
        for iz in range(nz):
            d_vals = (
                grid_alv[ix][iz],
                grid_alv[ix + 1][iz],
                grid_alv[ix + 1][iz + 1],
                grid_alv[ix][iz + 1],
            )
            d_min = min(d_vals)
            rn_avg = 0.25 * (grid_rn[ix][iz] + grid_rn[ix + 1][iz] + grid_rn[ix + 1][iz + 1] + grid_rn[ix][iz + 1])
            # Punch out oval skylights cleanly inside the thick 3.1m collar rings so zero grid corners peek in or gap out!
            if d_min < -0.06 and rn_avg < 0.88:
                continue
            i00 = get_beam_v(ix, iz)
            i10 = get_beam_v(ix + 1, iz)
            i11 = get_beam_v(ix + 1, iz + 1)
            i01 = get_beam_v(ix, iz + 1)
            _add_indexed_quad_checked(mb_beams, i00, i01, i11, i10, (0.0, -1.0, 0.0))

    # 2. 17 Smooth Elliptical Domed Backlit Membranes (Mat_MembraneBacklit) + Thick Visceral Rim Collars (Mat_FleshWall)
    n_ang = 24
    n_rings = 5
    for cx, cz, rx, rz, ang in alveoli:
        ca, sa = math.cos(ang), math.sin(ang)
        # 2A. Smooth oval alveoli dome sac (zero staircase jaggedness!)
        m_pts, m_uvs, m_cols = [], [], []
        for ir in range(n_rings):
            rt = (ir + 0.15) / float(n_rings - 1)  # 0.04 .. 1.08 (slight overlap over collar!)
            r_fac = min(1.08, rt)
            sac_lift = 3.8 * math.sqrt(max(0.0, 1.0 - min(1.0, rt) ** 2))
            row_p, row_u, row_c = [], [], []
            for ia in range(n_ang):
                phi = (ia / float(n_ang)) * 2.0 * math.pi
                lx = math.cos(phi) * rx * r_fac
                lz = math.sin(phi) * rz * r_fac
                wx = cx + lx * ca - lz * sa
                wz = cz + lx * sa + lz * ca
                by, _ = dome_base_y(wx, wz)
                wy = by + sac_lift
                glow = 1.0 - min(1.0, rt) * 0.35
                row_p.append([wx, wy, wz])
                row_u.append((0.5 + 0.5 * math.cos(phi) * r_fac, 0.5 + 0.5 * math.sin(phi) * r_fac))
                row_c.append(col_membrane(glow))
            m_pts.append(row_p)
            m_uvs.append(row_u)
            m_cols.append(row_c)
        add_indexed_grid(mb_mem, m_pts, m_uvs, m_cols, outward_hints=(0.0, -1.0, 0.0), wrap_j=True)

        # Center apex cap for membrane sac
        c_top_y, _ = dome_base_y(cx, cz)
        c_top = np.array([cx, c_top_y + 4.1, cz], dtype=np.float32)
        for ia in range(n_ang):
            ian = (ia + 1) % n_ang
            add_oriented_tri(
                mb_mem, c_top, m_pts[0][ian], m_pts[0][ia],
                (0.5, 0.5), m_uvs[0][ian], m_uvs[0][ia],
                col_membrane(1.0), m_cols[0][ian], m_cols[0][ia],
                (0.0, -1.0, 0.0),
            )

        # 2B. Smooth 3D swept visceral collar ring framing the oval skylight cleanly!
        col_sp = []
        col_rad = []
        for ia in range(n_ang + 1):
            phi = (ia / float(n_ang)) * 2.0 * math.pi
            lx = math.cos(phi) * rx * 0.98
            lz = math.sin(phi) * rz * 0.98
            wx = cx + lx * ca - lz * sa
            wz = cz + lx * sa + lz * ca
            by, _ = dome_base_y(wx, wz)
            col_sp.append([wx, by - 1.3, wz])
            col_rad.append(3.15 * (1.0 + 0.12 * math.sin(phi * 4.0)))
        add_swept_tube(
            mb_beams, col_sp, col_rad, n_sides=8, u_tiles=2.0, v_tiles=10.0,
            color_fn=lambda t, phi, wp: col_flesh(0.52 + 0.28 * (0.5 + 0.5 * math.sin(phi)), 50.0),
            cap_start=False, cap_end=False,
        )

    # 3. Concentric & radial 3D hanging muscular visceral rings dipping down toward far horizon
    for ring_scale, r_tube in [(0.34, 2.5), (0.64, 2.9), (0.94, 3.4)]:
        n_ring = 36
        r_sp = []
        r_rads = []
        for ir in range(n_ring + 1):
            th = (ir / float(n_ring)) * 2.0 * math.pi
            rx_r = hx * ring_scale * (1.0 + 0.05 * math.sin(4.0 * th))
            rz_r = hz * ring_scale * (1.0 + 0.05 * math.cos(3.0 * th))
            wx = math.cos(th) * rx_r
            wz = math.sin(th) * rz_r
            by, _ = dome_base_y(wx, wz)
            r_sp.append([wx, by - 2.3, wz])
            r_rads.append(r_tube * (1.0 + 0.16 * math.sin(th * 8.0)))
        add_swept_tube(
            mb_beams, r_sp, r_rads, n_sides=8, u_tiles=2.0, v_tiles=18.0,
            color_fn=lambda t, phi, wp: col_flesh(0.55 + 0.25 * (0.5 + 0.5 * math.sin(phi)), 50.0),
            cap_start=False, cap_end=False,
        )

    mb_beams.recompute_smooth_normals()
    mb_mem.recompute_smooth_normals()
    return "Cavern_Membrane_CeilingVault", [mb_beams, mb_mem]


# ==============================================================================
# 5. MESH VALIDATION & PREVIEW RENDERING
# ==============================================================================

def validate_glb_meshes(glb_path, expected_name):
    """Validates GLB structure, finite attributes, unit normals, and zero degenerate tris."""
    gltf, loaded = load_glb(glb_path)
    total_tris = 0
    total_verts = 0
    bmin = np.array([1e9, 1e9, 1e9], dtype=np.float32)
    bmax = np.array([-1e9, -1e9, -1e9], dtype=np.float32)

    for m in loaded:
        pos = m["positions"]
        nrm = m["normals"]
        uv = m["uvs"]
        col = m["colors"]
        tris = m["indices"]

        assert len(pos) > 0 and len(tris) > 0, f"{expected_name}/{m['name']}: empty mesh"
        assert np.all(np.isfinite(pos)), f"{expected_name}/{m['name']}: non-finite positions"
        assert np.all(np.isfinite(nrm)), f"{expected_name}/{m['name']}: non-finite normals"
        assert np.all(np.isfinite(uv)), f"{expected_name}/{m['name']}: non-finite uvs"
        assert np.all(np.isfinite(col)), f"{expected_name}/{m['name']}: non-finite colors"

        nlens = np.linalg.norm(nrm, axis=1)
        assert np.all((nlens > 0.95) & (nlens < 1.05)), f"{expected_name}/{m['name']}: unnormalized normals ({nlens.min()}..{nlens.max()})"

        v0 = pos[tris[:, 0]]
        v1 = pos[tris[:, 1]]
        v2 = pos[tris[:, 2]]
        areas = 0.5 * np.linalg.norm(np.cross(v1 - v0, v2 - v0), axis=1)
        min_area = float(areas.min())
        assert min_area > 1e-8, f"{expected_name}/{m['name']}: degenerate triangle found (min_area={min_area})"

        bmin = np.minimum(bmin, pos.min(axis=0))
        bmax = np.maximum(bmax, pos.max(axis=0))
        total_tris += len(tris)
        total_verts += len(pos)

    return gltf, loaded, {
        "verts": total_verts,
        "tris": total_tris,
        "meshes": len(loaded),
        "bmin": [round(float(x), 2) for x in bmin],
        "bmax": [round(float(x), 2) for x in bmax],
    }


PREVIEW_CAMERAS = {
    "Abyss_Acid_Lake": ((0.0, 95.0, 125.0), (0.0, 0.0, 0.0), 48.0),
    "Waterfall_AcidBlood": ((14.0, 13.0, 26.0), (0.0, 10.5, 0.5), 48.0),
    "Monument_GiantSkull_West": ((13.5, 9.5, 22.0), (0.0, 6.2, 1.0), 46.0),
    "Monument_GiantBoneWing_East": ((22.0, 12.0, 18.0), (0.0, 6.0, 7.5), 48.0),
    "Monument_HornedColossus_North": ((11.0, 14.0, 32.0), (0.0, 12.5, 0.5), 46.0),
    "Monument_GothicButtress_South": ((10.0, 10.0, 25.0), (0.0, 7.5, 0.0), 48.0),
    "Monument_SunkenSpire_A": ((12.0, 8.0, 18.0), (0.0, 5.5, 0.0), 46.0),
    "Monument_SunkenSpire_B": ((-11.0, 7.0, 16.0), (-0.5, 4.0, 0.0), 46.0),
    "Prop_FloatingIslet_A": ((8.5, 5.0, 10.5), (0.0, 0.2, 0.0), 44.0),
    "Prop_FloatingIslet_B": ((-8.0, 4.8, 9.5), (0.0, 0.2, 0.0), 44.0),
    "Cavern_Wall_Long": ((45.0, 35.0, 130.0), (0.0, 22.0, 0.0), 50.0),
    "Cavern_Wall_End": ((30.0, 28.0, 115.0), (0.0, 24.0, 0.0), 50.0),
    "Cavern_RibArch": ((42.0, 28.0, 105.0), (0.0, 22.0, 0.0), 50.0),
    "Cavern_Membrane_CeilingVault": ((0.0, 4.0, -55.0), (0.0, 62.0, 10.0), 68.0),
}


def build_all(render_previews=False):
    os.makedirs(MODELS_OUT_DIR, exist_ok=True)
    if render_previews:
        os.makedirs(PREVIEW_OUT_DIR, exist_ok=True)

    builders = [
        build_abyss_acid_lake,
        build_waterfall_acid_blood,
        build_monument_giant_skull_west,
        build_monument_giant_bone_wing_east,
        build_monument_horned_colossus_north,
        build_monument_gothic_buttress_south,
        build_monument_sunken_spire_a,
        build_monument_sunken_spire_b,
        build_prop_floating_islet_a,
        build_prop_floating_islet_b,
        build_cavern_wall_long,
        build_cavern_wall_end,
        build_cavern_rib_arch,
        build_cavern_membrane_ceiling_vault,
    ]

    results = []
    for fn in builders:
        root_name, meshes = fn()
        mat_spec = build_materials_spec(meshes)
        out_path = os.path.join(MODELS_OUT_DIR, f"{root_name}.glb")
        stats = export_glb(out_path, meshes, mat_spec, root_name=root_name)
        _, loaded, vinfo = validate_glb_meshes(out_path, root_name)
        results.append((root_name, out_path, stats, vinfo))
        print(
            f"[OK] {root_name}.glb | tris={vinfo['tris']:6d} | verts={vinfo['verts']:6d} "
            f"| meshes={vinfo['meshes']} | bmin={vinfo['bmin']} | bmax={vinfo['bmax']}"
        )

        if render_previews and root_name in PREVIEW_CAMERAS:
            cam, tgt, fov = PREVIEW_CAMERAS[root_name]
            im = render_view(loaded, cam, tgt, width=560, height=420, fov_deg=fov)
            prev_p = os.path.join(PREVIEW_OUT_DIR, f"preview_{root_name}.png")
            im.save(prev_p)

    return results


if __name__ == "__main__":
    do_prev = "--preview" in sys.argv
    build_all(render_previews=do_prev)
