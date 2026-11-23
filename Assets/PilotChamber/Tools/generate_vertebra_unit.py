import math
import os
import sys
import numpy as np

TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
if TOOLS_DIR not in sys.path:
    sys.path.insert(0, TOOLS_DIR)

from glb_exporter import MeshBuilder, export_glb
from retro_textures import generate_all_textures
from cathedral_textures import generate_cathedral_textures


# ==============================================================================
# COLOR & GEOMETRY HELPERS (OUTWARD-VERIFIED, ZERO DEGENERATE TRIANGLES)
# ==============================================================================

def _rgba(r, g, b, a=1.0):
    return (
        float(max(0.0, min(1.0, r))),
        float(max(0.0, min(1.0, g))),
        float(max(0.0, min(1.0, b))),
        float(max(0.0, min(1.0, a))),
    )


def scorched_bone_col(bright=0.85, char=0.0):
    """Weathered bone transitioning to charred dark soot on the underbelly."""
    r = bright * (0.95 - 0.34 * char)
    g = bright * (0.88 - 0.42 * char)
    b = bright * (0.76 - 0.44 * char)
    return _rgba(r, g, b)


def marrow_deck_col(bright=0.90, stain=0.0):
    """Ossified spinal walkway slab with subtle dark crimson-ochre marrow tint."""
    r = bright * (0.94 + 0.04 * stain)
    g = bright * (0.86 - 0.16 * stain)
    b = bright * (0.78 - 0.20 * stain)
    return _rgba(r, g, b)


def rib_arch_col(bright=0.88, tip=0.0):
    """Ivory ribcage bone darkening slightly toward scorched horn tips."""
    r = bright * (0.98 - 0.22 * tip)
    g = bright * (0.93 - 0.36 * tip)
    b = bright * (0.84 - 0.42 * tip)
    return _rgba(r, g, b)


def iron_hardware_col(bright=0.72, rust=0.0):
    """Forged dark iron with subtle heat-oxidized rust tone."""
    r = bright * (0.58 + 0.22 * rust)
    g = bright * (0.54 + 0.04 * rust)
    b = bright * (0.56 - 0.08 * rust)
    return _rgba(r, g, b)


def ember_col(heat=1.0):
    """Glowing hellfire ember vertex color (warm orange-gold core to deep red edge)."""
    return _rgba(
        1.0,
        0.35 + 0.55 * heat,
        0.08 + 0.32 * (heat ** 1.5),
    )


def add_oriented_quad(mb, p00, p10, p11, p01, uv00, uv10, uv11, uv01, c00, c10, c11, c01, outward_hint):
    """Emits a flat quad oriented so its normal strictly aligns with outward_hint."""
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
    """Emits a flat triangle oriented so its normal strictly aligns with outward_hint."""
    p0 = np.asarray(p0, dtype=np.float32)
    p1 = np.asarray(p1, dtype=np.float32)
    p2 = np.asarray(p2, dtype=np.float32)
    n = np.cross(p1 - p0, p2 - p0)
    if float(np.dot(n, np.asarray(outward_hint, dtype=np.float32))) >= 0.0:
        mb.add_flat_triangle(p0, p1, p2, uv0, uv1, uv2, c0, c1, c2)
    else:
        mb.add_flat_triangle(p0, p2, p1, uv0, uv2, uv1, c0, c2, c1)


def add_oriented_box(
    mb,
    cx, cy, cz,
    sx, sy, sz,
    c_bot, c_top,
    uv_scale=0.35,
    skip_top=False,
    skip_bottom=False,
):
    """Axis-aligned box centered at (cx, cy, cz) with verified outward normals."""
    hx, hy, hz = sx * 0.5, sy * 0.5, sz * 0.5
    x0, x1 = cx - hx, cx + hx
    y0, y1 = cy - hy, cy + hy
    z0, z1 = cz - hz, cz + hz
    ux, uz, vy = sx * uv_scale, sz * uv_scale, sy * uv_scale

    add_oriented_quad(
        mb, (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
        (0.0, 0.0), (ux, 0.0), (ux, vy), (0.0, vy),
        c_bot, c_bot, c_top, c_top, (0.0, 0.0, -1.0),
    )
    add_oriented_quad(
        mb, (x1, y0, z1), (x0, y0, z1), (x0, y1, z1), (x1, y1, z1),
        (0.0, 0.0), (ux, 0.0), (ux, vy), (0.0, vy),
        c_bot, c_bot, c_top, c_top, (0.0, 0.0, 1.0),
    )
    add_oriented_quad(
        mb, (x0, y0, z1), (x0, y0, z0), (x0, y1, z0), (x0, y1, z1),
        (0.0, 0.0), (uz, 0.0), (uz, vy), (0.0, vy),
        c_bot, c_bot, c_top, c_top, (-1.0, 0.0, 0.0),
    )
    add_oriented_quad(
        mb, (x1, y0, z0), (x1, y0, z1), (x1, y1, z1), (x1, y1, z0),
        (0.0, 0.0), (uz, 0.0), (uz, vy), (0.0, vy),
        c_bot, c_bot, c_top, c_top, (1.0, 0.0, 0.0),
    )
    if not skip_top:
        add_oriented_quad(
            mb, (x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1),
            (0.0, 0.0), (ux, 0.0), (ux, uz), (0.0, uz),
            c_top, c_top, c_top, c_top, (0.0, 1.0, 0.0),
        )
    if not skip_bottom:
        add_oriented_quad(
            mb, (x0, y0, z1), (x1, y0, z1), (x1, y0, z0), (x0, y0, z0),
            (0.0, 0.0), (ux, 0.0), (ux, uz), (0.0, uz),
            c_bot, c_bot, c_bot, c_bot, (0.0, -1.0, 0.0),
        )


def add_beam_segment(mb, p_start, p_end, r_x, r_z, n_sides, c_start, c_end, uv_scale=0.35, cap_start=True, cap_end=True):
    """Builds a straight polygonal strut/beam from p_start to p_end with outward normals."""
    p0 = np.asarray(p_start, dtype=np.float32)
    p1 = np.asarray(p_end, dtype=np.float32)
    axis = p1 - p0
    length = float(np.linalg.norm(axis))
    if length < 1e-5:
        return
    t_dir = axis / length
    ref = np.array([0.0, 0.0, 1.0], dtype=np.float32) if abs(t_dir[2]) < 0.85 else np.array([1.0, 0.0, 0.0], dtype=np.float32)
    n_dir = np.cross(ref, t_dir)
    n_dir /= float(np.linalg.norm(n_dir))
    b_dir = np.cross(t_dir, n_dir)

    ring0 = []
    ring1 = []
    for i in range(n_sides):
        a = 2.0 * math.pi * i / float(n_sides) + (math.pi / float(n_sides))
        ca, sa = math.cos(a), math.sin(a)
        off = n_dir * (r_x * ca) + b_dir * (r_z * sa)
        ring0.append(p0 + off)
        ring1.append(p1 + off)

    v_len = length * uv_scale
    u_circ = max(0.3, 2.0 * math.pi * 0.5 * (r_x + r_z) * uv_scale)
    for i in range(n_sides):
        ni = (i + 1) % n_sides
        q00, q10, q11, q01 = ring0[i], ring0[ni], ring1[ni], ring1[i]
        outward = 0.25 * (q00 + q10 + q11 + q01) - 0.5 * (p0 + p1)
        u0 = u_circ * i / float(n_sides)
        u1 = u_circ * (i + 1) / float(n_sides)
        add_oriented_quad(
            mb, q00, q10, q11, q01,
            (u0, 0.0), (u1, 0.0), (u1, v_len), (u0, v_len),
            c_start, c_start, c_end, c_end, outward,
        )

    if cap_start:
        for i in range(n_sides):
            ni = (i + 1) % n_sides
            add_oriented_tri(
                mb, p0, ring0[ni], ring0[i],
                (0.5, 0.5), (1.0, 0.0), (0.0, 0.0),
                c_start, c_start, c_start, -t_dir,
            )
    if cap_end:
        for i in range(n_sides):
            ni = (i + 1) % n_sides
            add_oriented_tri(
                mb, p1, ring1[i], ring1[ni],
                (0.5, 0.5), (0.0, 0.0), (1.0, 0.0),
                c_end, c_end, c_end, t_dir,
            )


def add_pyramid_stud(mb, base_center, outward_normal, half_w=0.08, height=0.11, c_base=None, c_tip=None):
    """Adds a 4-sided pyramidal iron stud pointing along outward_normal."""
    bc = np.asarray(base_center, dtype=np.float32)
    n = np.asarray(outward_normal, dtype=np.float32)
    n /= max(1e-6, float(np.linalg.norm(n)))
    ref = np.array([0.0, 1.0, 0.0], dtype=np.float32) if abs(n[1]) < 0.85 else np.array([0.0, 0.0, 1.0], dtype=np.float32)
    u_ax = np.cross(ref, n)
    u_ax /= float(np.linalg.norm(u_ax))
    v_ax = np.cross(n, u_ax)

    b0 = bc - u_ax * half_w - v_ax * half_w
    b1 = bc + u_ax * half_w - v_ax * half_w
    b2 = bc + u_ax * half_w + v_ax * half_w
    b3 = bc - u_ax * half_w + v_ax * half_w
    tip = bc + n * height

    if c_base is None:
        c_base = iron_hardware_col(0.55)
    if c_tip is None:
        c_tip = iron_hardware_col(0.95)

    corners = [b0, b1, b2, b3]
    for i in range(4):
        p_a = corners[i]
        p_b = corners[(i + 1) % 4]
        hint = (0.5 * (p_a + p_b) - bc) + n * 0.5
        add_oriented_tri(
            mb, p_a, p_b, tip,
            (0.0, 0.0), (0.25, 0.0), (0.125, 0.25),
            c_base, c_base, c_tip, hint,
        )


def add_lofted_horn_faceted(mb, spine_pts, radii_xy, n_sides, colors, uv_scale=0.35, cap_base=True):
    """
    Lofts a tapered curved horn/fang from spine_pts[0] to spine_pts[-1] (tapering to sharp tip if last radius==0).
    Uses flat facets with verified outward normals.
    """
    pts = [np.asarray(p, dtype=np.float32) for p in spine_pts]
    n_rings = len(pts)

    # Parallel-transport orthonormal frames along the spine
    frames = []
    for k in range(n_rings):
        if k == 0:
            t = pts[1] - pts[0]
        elif k == n_rings - 1:
            t = pts[-1] - pts[-2]
        else:
            t = pts[k + 1] - pts[k - 1]
        t_len = float(np.linalg.norm(t))
        t = t / max(1e-6, t_len)
        if k == 0:
            ref = np.array([0.0, 1.0, 0.0], dtype=np.float32) if abs(t[1]) < 0.85 else np.array([0.0, 0.0, 1.0], dtype=np.float32)
            n_vec = np.cross(ref, t)
            n_vec /= float(np.linalg.norm(n_vec))
        else:
            prev_n = frames[-1][1]
            n_vec = prev_n - t * float(np.dot(prev_n, t))
            n_len = float(np.linalg.norm(n_vec))
            if n_len < 1e-5:
                ref = np.array([0.0, 1.0, 0.0], dtype=np.float32) if abs(t[1]) < 0.85 else np.array([0.0, 0.0, 1.0], dtype=np.float32)
                n_vec = np.cross(ref, t)
                n_len = float(np.linalg.norm(n_vec))
            n_vec /= n_len
        b_vec = np.cross(t, n_vec)
        frames.append((t, n_vec, b_vec))

    rings = []
    v_coords = [0.0]
    for k in range(n_rings):
        if k > 0:
            v_coords.append(v_coords[-1] + float(np.linalg.norm(pts[k] - pts[k - 1])) * uv_scale)
        rx, rz = radii_xy[k]
        if rx <= 1e-4 and rz <= 1e-4:
            rings.append(None)  # Sharp apex point
            continue
        _, n_vec, b_vec = frames[k]
        ring = []
        for j in range(n_sides):
            a = 2.0 * math.pi * j / float(n_sides)
            # Subtle dorsal/ventral ridge pinch for organic bone horn look
            ridge = 1.0 + 0.10 * math.cos(3.0 * a)
            off = n_vec * (rx * ridge * math.cos(a)) + b_vec * (rz * ridge * math.sin(a))
            ring.append(pts[k] + off)
        rings.append(ring)

    u_circ = max(0.4, 2.0 * math.pi * 0.5 * (radii_xy[0][0] + radii_xy[0][1]) * uv_scale)
    for k in range(n_rings - 1):
        r0 = rings[k]
        r1 = rings[k + 1]
        v0, v1 = v_coords[k], v_coords[k + 1]
        c0, c1 = colors[k], colors[k + 1]
        if r0 is not None and r1 is not None:
            for j in range(n_sides):
                nj = (j + 1) % n_sides
                q00, q10, q11, q01 = r0[j], r0[nj], r1[nj], r1[j]
                outward = 0.25 * (q00 + q10 + q11 + q01) - 0.5 * (pts[k] + pts[k + 1])
                u0 = u_circ * j / float(n_sides)
                u1 = u_circ * (j + 1) / float(n_sides)
                add_oriented_quad(
                    mb, q00, q10, q11, q01,
                    (u0, v0), (u1, v0), (u1, v1), (u0, v1),
                    c0, c0, c1, c1, outward,
                )
        elif r0 is not None and r1 is None:
            tip = pts[k + 1]
            for j in range(n_sides):
                nj = (j + 1) % n_sides
                q00, q10 = r0[j], r0[nj]
                outward = 0.5 * (q00 + q10) - pts[k]
                u0 = u_circ * j / float(n_sides)
                u1 = u_circ * (j + 1) / float(n_sides)
                add_oriented_tri(
                    mb, q00, q10, tip,
                    (u0, v0), (u1, v0), (0.5 * (u0 + u1), v1),
                    c0, c0, c1, outward,
                )

    if cap_base and rings[0] is not None:
        t0 = frames[0][0]
        for j in range(n_sides):
            nj = (j + 1) % n_sides
            add_oriented_tri(
                mb, pts[0], rings[0][nj], rings[0][j],
                (0.5, 0.5), (1.0, 0.0), (0.0, 0.0),
                colors[0], colors[0], colors[0], -t0,
            )


# ==============================================================================
# 1. VERTEBRA WALKWAY SLAB (Mat_Spine_MarrowDeck)
# ==============================================================================

def build_walkway_slab():
    """
    Thick carved spinal disc & ossified slab:
    - Exact flat walkable top surface at Y = 0.0 for X in [-3.4, 3.4], Z in [-2.2, 2.2]
      (interior clear deck is X in [-2.9, 2.9]; outer +-0.5m sits under sculpted bone curbs).
    - Beveled interlocking overlap lips extending Z in [-2.55, -2.2] and [2.2, 2.55],
      sloping smoothly down to Y = -0.04 at Z = +-2.55 so sequential scaled units
      interlock with ZERO gaps and ZERO z-fighting.
    """
    mb = MeshBuilder("Vertebra_Walkway_Slab", "Mat_Spine_MarrowDeck")

    uv_scale = 0.25  # 1 tile per 4.0m (clean 256x256 PS1 texel density)

    # X grid across the deck for spinal marrow channel & lateral bone plates (all Y=0.0 on top)
    xs_top = [-3.40, -2.90, -1.15, 0.00, 1.15, 2.90, 3.40]
    # Z stations: overlapping front/back bevel tips -> flat walkable core
    # (z, y_top, y_bot, x_scale)
    z_profile = [
        (-2.55, -0.04, -0.18, 0.96),
        (-2.20,  0.00, -0.44, 1.00),
        (-0.85,  0.00, -0.48, 1.00),
        ( 0.85,  0.00, -0.48, 1.00),
        ( 2.20,  0.00, -0.44, 1.00),
        ( 2.55, -0.04, -0.18, 0.96),
    ]

    def deck_color(x, z, is_top=True):
        ax = abs(x)
        az = abs(z)
        if not is_top:
            return marrow_deck_col(0.50, stain=0.35)
        # Central spinal marrow groove darker/redder, mid bone plates bright, edges AO-shaded under curbs
        if ax < 0.25:
            stain = 0.55
            bright = 0.78
        elif ax < 1.20:
            stain = 0.25
            bright = 0.90
        elif ax < 2.85:
            stain = 0.05
            bright = 0.96
        else:
            stain = 0.20
            bright = 0.68
        if az > 2.22:
            bright *= 0.82
        return marrow_deck_col(bright, stain=stain)

    # Top walkable surface & bottom slab face
    for zi in range(len(z_profile) - 1):
        z0, yt0, yb0, xs0 = z_profile[zi]
        z1, yt1, yb1, xs1 = z_profile[zi + 1]
        for xi in range(len(xs_top) - 1):
            x0a, x1a = xs_top[xi] * xs0, xs_top[xi + 1] * xs0
            x0b, x1b = xs_top[xi] * xs1, xs_top[xi + 1] * xs1

            # Top face (strictly Y = 0.0 inside [-2.2, 2.2])
            add_oriented_quad(
                mb,
                (x0a, yt0, z0), (x1a, yt0, z0), (x1b, yt1, z1), (x0b, yt1, z1),
                (x0a * uv_scale, z0 * uv_scale),
                (x1a * uv_scale, z0 * uv_scale),
                (x1b * uv_scale, z1 * uv_scale),
                (x0b * uv_scale, z1 * uv_scale),
                deck_color(x0a, z0, True),
                deck_color(x1a, z0, True),
                deck_color(x1b, z1, True),
                deck_color(x0b, z1, True),
                (0.0, 1.0, 0.0),
            )
            # Bottom face (sloped bevels at tips, sturdy underside in middle)
            add_oriented_quad(
                mb,
                (x0b, yb1, z1), (x1b, yb1, z1), (x1a, yb0, z0), (x0a, yb0, z0),
                (x0b * uv_scale, z1 * uv_scale),
                (x1b * uv_scale, z1 * uv_scale),
                (x1a * uv_scale, z0 * uv_scale),
                (x0a * uv_scale, z0 * uv_scale),
                deck_color(x0b, z1, False),
                deck_color(x1b, z1, False),
                deck_color(x1a, z0, False),
                deck_color(x0a, z0, False),
                (0.0, -1.0, 0.0),
            )

        # Left (-X) & Right (+X) slab edges
        xl0, xr0 = xs_top[0] * xs0, xs_top[-1] * xs0
        xl1, xr1 = xs_top[0] * xs1, xs_top[-1] * xs1
        c_edge_top = marrow_deck_col(0.72, 0.15)
        c_edge_bot = marrow_deck_col(0.46, 0.30)
        add_oriented_quad(
            mb,
            (xl1, yb1, z1), (xl0, yb0, z0), (xl0, yt0, z0), (xl1, yt1, z1),
            (z1 * uv_scale, yb1 * uv_scale), (z0 * uv_scale, yb0 * uv_scale),
            (z0 * uv_scale, yt0 * uv_scale), (z1 * uv_scale, yt1 * uv_scale),
            c_edge_bot, c_edge_bot, c_edge_top, c_edge_top, (-1.0, 0.0, 0.0),
        )
        add_oriented_quad(
            mb,
            (xr0, yb0, z0), (xr1, yb1, z1), (xr1, yt1, z1), (xr0, yt0, z0),
            (z0 * uv_scale, yb0 * uv_scale), (z1 * uv_scale, yb1 * uv_scale),
            (z1 * uv_scale, yt1 * uv_scale), (z0 * uv_scale, yt0 * uv_scale),
            c_edge_bot, c_edge_bot, c_edge_top, c_edge_top, (1.0, 0.0, 0.0),
        )

    # Front (-Z) and Back (+Z) beveled end caps at Z = +-2.55
    zf, ytf, ybf, xsf = z_profile[0]
    zb, ytb, ybb, xsb = z_profile[-1]
    c_lip_t = marrow_deck_col(0.76, 0.25)
    c_lip_b = marrow_deck_col(0.42, 0.40)
    add_oriented_quad(
        mb,
        (xs_top[0] * xsf, ybf, zf), (xs_top[-1] * xsf, ybf, zf),
        (xs_top[-1] * xsf, ytf, zf), (xs_top[0] * xsf, ytf, zf),
        (0.0, 0.0), (6.5 * uv_scale, 0.0), (6.5 * uv_scale, 0.15), (0.0, 0.15),
        c_lip_b, c_lip_b, c_lip_t, c_lip_t, (0.0, 0.0, -1.0),
    )
    add_oriented_quad(
        mb,
        (xs_top[-1] * xsb, ybb, zb), (xs_top[0] * xsb, ybb, zb),
        (xs_top[0] * xsb, ytb, zb), (xs_top[-1] * xsb, ytb, zb),
        (0.0, 0.0), (6.5 * uv_scale, 0.0), (6.5 * uv_scale, 0.15), (0.0, 0.15),
        c_lip_b, c_lip_b, c_lip_t, c_lip_t, (0.0, 0.0, 1.0),
    )

    return mb


# ==============================================================================
# 2. VERTEBRA BODY, CURBS & HORNS (Mat_Vertebra_ScorchedBone)
# ==============================================================================

def build_body_and_spines():
    """
    - Massive anatomical vertebral centrum below the deck (Y = -0.1 down to -3.6),
      pinched hourglass waist with fluted bone ridges and charred underbelly AO.
    - Sculpted bone curbs (Y = +0.32m at X = +-3.0..3.6), keeping X in [-2.9, 2.9] clear.
    - Jagged lateral transverse processes & infernal swept-back dorsal bone horns/fangs
      jutting from the sides (X = +-3.5 .. +-5.6).
    """
    mb = MeshBuilder("Vertebra_Body_And_Spines", "Mat_Vertebra_ScorchedBone")
    uv_scale = 0.30

    # ------------------------------------------------------------------
    # A. Hourglass Vertebral Centrum (Y = -0.10 down to -3.60)
    # ------------------------------------------------------------------
    # (y, rx, rz, flute_amp, bright, char)
    centrum_layers = [
        (-0.10, 3.35, 2.22, 0.04, 0.88, 0.05),
        (-0.55, 2.92, 2.02, 0.10, 0.82, 0.12),
        (-1.15, 2.22, 1.56, 0.16, 0.74, 0.25),
        (-1.75, 1.88, 1.32, 0.20, 0.64, 0.42),  # Pinched hourglass waist
        (-2.45, 2.18, 1.54, 0.17, 0.60, 0.55),
        (-3.10, 2.68, 1.94, 0.12, 0.52, 0.75),  # Flared lower epiphysis
        (-3.60, 2.25, 1.68, 0.06, 0.38, 0.92),  # Charred acid-facing underbelly
    ]
    n_radial = 16
    rings = []
    ring_cols = []
    v_acc = [0.0]

    for li, (y, rx, rz, f_amp, bright, char) in enumerate(centrum_layers):
        ring = []
        cols = []
        if li > 0:
            dy = abs(y - centrum_layers[li - 1][0])
            v_acc.append(v_acc[-1] + dy * uv_scale)
        for j in range(n_radial):
            a = 2.0 * math.pi * j / float(n_radial)
            # Alternating fluted bone ridges (8 major ridges around the centrum)
            flute_wave = math.cos(8.0 * a)
            ridge = 1.0 + f_amp * flute_wave
            # Pronounced anterior/posterior ventral keel at a=pi/2, 3pi/2 (Z peaks)
            keel = 0.10 * (abs(math.sin(a)) ** 3) if y < -1.5 else 0.0
            px = rx * ridge * math.cos(a)
            pz = (rz * ridge + keel) * math.sin(a)
            ring.append(np.array([px, y, pz], dtype=np.float32))
            # Darken grooves between ridges for strong PS1 AO
            groove_ao = 0.86 + 0.14 * flute_wave
            cols.append(scorched_bone_col(bright * groove_ao, char=char))
        rings.append(ring)
        ring_cols.append(cols)

    u_circ = 16.0 * uv_scale
    for li in range(len(rings) - 1):
        r0, r1 = rings[li], rings[li + 1]
        c0, c1 = ring_cols[li], ring_cols[li + 1]
        v0, v1 = v_acc[li], v_acc[li + 1]
        cy_mid = 0.5 * (centrum_layers[li][0] + centrum_layers[li + 1][0])
        for j in range(n_radial):
            nj = (j + 1) % n_radial
            q00, q10, q11, q01 = r0[j], r0[nj], r1[nj], r1[j]
            outward = 0.25 * (q00 + q10 + q11 + q01) - np.array([0.0, cy_mid, 0.0], dtype=np.float32)
            u0 = u_circ * j / float(n_radial)
            u1 = u_circ * (j + 1) / float(n_radial)
            add_oriented_quad(
                mb, q00, q10, q11, q01,
                (u0, v0), (u1, v0), (u1, v1), (u0, v1),
                c0[j], c0[nj], c1[nj], c1[j], outward,
            )

    # Cap top (hidden under deck) & charred bottom keel cap at Y = -3.60
    top_center = np.array([0.0, -0.10, 0.0], dtype=np.float32)
    bot_center = np.array([0.0, -3.65, 0.0], dtype=np.float32)
    c_top_cap = scorched_bone_col(0.65, 0.20)
    c_bot_cap = scorched_bone_col(0.32, 0.98)
    for j in range(n_radial):
        nj = (j + 1) % n_radial
        add_oriented_tri(
            mb, top_center, rings[0][j], rings[0][nj],
            (0.5, 0.5), (0.0, 0.0), (1.0, 0.0),
            c_top_cap, ring_cols[0][j], ring_cols[0][nj], (0.0, 1.0, 0.0),
        )
        add_oriented_tri(
            mb, bot_center, rings[-1][nj], rings[-1][j],
            (0.5, 0.5), (1.0, 0.0), (0.0, 0.0),
            c_bot_cap, ring_cols[-1][nj], ring_cols[-1][j], (0.0, -1.0, 0.0),
        )

    # ------------------------------------------------------------------
    # B. Sculpted Flanking Bone Curbs (X = +-3.0..3.6, Y = 0.0..+0.32m)
    # ------------------------------------------------------------------
    # Clear interior deck is strictly X in [-2.9, 2.9]; curbs start at |X| = 2.98!
    # Profile along Z tapers slightly at |Z| > 2.20 so consecutive units interlock cleanly.
    curb_z = [
        (-2.46, 0.14, 3.04, 3.48, 0.72),
        (-2.18, 0.28, 2.98, 3.58, 0.88),
        (-1.10, 0.32, 2.98, 3.60, 0.95),
        ( 0.00, 0.32, 2.98, 3.62, 0.96),
        ( 1.10, 0.32, 2.98, 3.60, 0.95),
        ( 2.18, 0.28, 2.98, 3.58, 0.88),
        ( 2.46, 0.14, 3.04, 3.48, 0.72),
    ]
    for side in (-1.0, 1.0):
        loops = []
        loop_cols = []
        for z, y_top, x_in, x_out, bright in curb_z:
            # 4-point cross-section loop for each curb station
            p_in_bot = np.array([side * x_in, -0.14, z], dtype=np.float32)
            p_in_top = np.array([side * (x_in + 0.12), y_top, z], dtype=np.float32)
            p_out_top = np.array([side * (x_out - 0.08), y_top - 0.02, z], dtype=np.float32)
            p_out_bot = np.array([side * x_out, -0.20, z], dtype=np.float32)
            loops.append([p_in_bot, p_in_top, p_out_top, p_out_bot])
            loop_cols.append([
                scorched_bone_col(bright * 0.65, 0.15),
                scorched_bone_col(bright * 1.00, 0.02),
                scorched_bone_col(bright * 0.92, 0.08),
                scorched_bone_col(bright * 0.58, 0.22),
            ])

        for zi in range(len(loops) - 1):
            l0, l1 = loops[zi], loops[zi + 1]
            c0, c1 = loop_cols[zi], loop_cols[zi + 1]
            z0, z1 = curb_z[zi][0], curb_z[zi + 1][0]
            mid_x = side * 3.28
            mid_y = 0.08
            for k in range(4):
                nk = (k + 1) % 4
                q00, q10, q11, q01 = l0[k], l0[nk], l1[nk], l1[k]
                outward = 0.25 * (q00 + q10 + q11 + q01) - np.array([mid_x, mid_y, 0.5 * (z0 + z1)], dtype=np.float32)
                add_oriented_quad(
                    mb, q00, q10, q11, q01,
                    (k * 0.25, z0 * uv_scale), (nk * 0.25, z0 * uv_scale),
                    (nk * 0.25, z1 * uv_scale), (k * 0.25, z1 * uv_scale),
                    c0[k], c0[nk], c1[nk], c1[k], outward,
                )
        # Cap curb ends at Z = -2.46 and Z = +2.46
        add_oriented_quad(
            mb, loops[0][0], loops[0][1], loops[0][2], loops[0][3],
            (0.0, 0.0), (0.0, 0.3), (0.3, 0.3), (0.3, 0.0),
            loop_cols[0][0], loop_cols[0][1], loop_cols[0][2], loop_cols[0][3],
            (0.0, 0.0, -1.0),
        )
        add_oriented_quad(
            mb, loops[-1][0], loops[-1][1], loops[-1][2], loops[-1][3],
            (0.0, 0.0), (0.0, 0.3), (0.3, 0.3), (0.3, 0.0),
            loop_cols[-1][0], loop_cols[-1][1], loop_cols[-1][2], loop_cols[-1][3],
            (0.0, 0.0, 1.0),
        )

    # ------------------------------------------------------------------
    # C. Lateral Transverse Processes & Swept-Back Infernal Horns/Fangs
    # ------------------------------------------------------------------
    for side in (-1.0, 1.0):
        # 1. Colossal main transverse process sweeping out & back to X = +-5.60
        main_horn_pts = [
            (side * 2.55, -0.55,  0.10),
            (side * 3.55, -0.35, -0.15),
            (side * 4.45,  0.05, -0.58),
            (side * 5.15,  0.48, -1.12),
            (side * 5.60,  0.96, -1.68),
        ]
        main_horn_r = [
            (0.58, 0.52),
            (0.46, 0.42),
            (0.32, 0.28),
            (0.18, 0.16),
            (0.00, 0.00),
        ]
        main_horn_c = [
            scorched_bone_col(0.62, 0.25),
            scorched_bone_col(0.80, 0.12),
            scorched_bone_col(0.92, 0.05),
            scorched_bone_col(0.84, 0.22),
            scorched_bone_col(0.52, 0.65),
        ]
        add_lofted_horn_faceted(mb, main_horn_pts, main_horn_r, 6, main_horn_c, uv_scale=uv_scale)

        # 2. Lower downward-swept infernal fang (X = +-2.2 .. +-4.75, Y = -1.45 .. -2.35)
        lower_fang_pts = [
            (side * 2.10, -1.35,  0.25),
            (side * 3.15, -1.58, -0.12),
            (side * 4.05, -1.92, -0.58),
            (side * 4.75, -2.32, -1.05),
        ]
        lower_fang_r = [
            (0.40, 0.36),
            (0.28, 0.25),
            (0.15, 0.13),
            (0.00, 0.00),
        ]
        lower_fang_c = [
            scorched_bone_col(0.52, 0.45),
            scorched_bone_col(0.66, 0.38),
            scorched_bone_col(0.58, 0.55),
            scorched_bone_col(0.36, 0.85),
        ]
        add_lofted_horn_faceted(mb, lower_fang_pts, lower_fang_r, 5, lower_fang_c, uv_scale=uv_scale)

        # 3. Forward-swept articular bone spur bracketing the rib socket (X = +-3.2 .. +-4.35)
        spur_pts = [
            (side * 3.10, -0.15, 0.72),
            (side * 3.72,  0.22, 1.10),
            (side * 4.28,  0.76, 1.38),
        ]
        spur_r = [
            (0.28, 0.26),
            (0.16, 0.14),
            (0.00, 0.00),
        ]
        spur_c = [
            scorched_bone_col(0.75, 0.12),
            scorched_bone_col(0.88, 0.06),
            scorched_bone_col(0.65, 0.40),
        ]
        add_lofted_horn_faceted(mb, spur_pts, spur_r, 5, spur_c, uv_scale=uv_scale)

    return mb


# ==============================================================================
# 3. VERTEBRA RIB ARCHES (Mat_Vertebra_RibArch)
# ==============================================================================

def _cubic_bezier(p0, p1, p2, p3, t):
    u = 1.0 - t
    return (
        (u ** 3) * p0
        + 3.0 * (u ** 2) * t * p1
        + 3.0 * u * (t ** 2) * p2
        + (t ** 3) * p3
    )


def rib_curve_pos(side, t):
    """Evaluates left/right sweeping gothic rib centerline for t in [0, 1]."""
    p0 = np.array([side * 3.25, 0.15,  0.00], dtype=np.float32)
    p1 = np.array([side * 7.55, 1.85,  0.55], dtype=np.float32)
    p2 = np.array([side * 5.45, 9.25, -0.10], dtype=np.float32)
    p3 = np.array([side * 1.40, 7.00, -0.45], dtype=np.float32)
    return _cubic_bezier(p0, p1, p2, p3, t)


def rib_curve_tangent(side, t):
    eps = 1e-3
    t0 = max(0.0, t - eps)
    t1 = min(1.0, t + eps)
    d = rib_curve_pos(side, t1) - rib_curve_pos(side, t0)
    return d / max(1e-6, float(np.linalg.norm(d)))


def build_rib_arches():
    """
    Twin sweeping gothic ribcage arches curving from (X = +-3.25, Y = 0.15)
    outward to X = +-5.8, arching up to Y = 7.4m, and tapering into curved
    demon-horn rib tips at X = +-1.40. Smooth vertex normals + clean PS1 facets.
    """
    mb = MeshBuilder("Vertebra_Rib_Arches", "Mat_Vertebra_RibArch")
    uv_scale = 0.28
    n_segs = 12   # 12 curve segments per rib (clean PS1 low-poly silhouette)
    n_sides = 6   # 6-sided hexagonal rib cross-section

    for side in (-1.0, 1.0):
        ts = [k / float(n_segs) for k in range(n_segs + 1)]
        pts = [rib_curve_pos(side, t) for t in ts]
        tangents = [rib_curve_tangent(side, t) for t in ts]

        # Build right-handed parallel-transport frames (T, N, B) with cross(N, B) == T
        frames = []
        for k in range(len(pts)):
            t_vec = tangents[k]
            if k == 0:
                ref = np.array([0.0, 0.0, 1.0], dtype=np.float32)
                n_vec = np.cross(ref, t_vec)
                n_vec /= float(np.linalg.norm(n_vec))
            else:
                prev_n = frames[-1][1]
                n_vec = prev_n - t_vec * float(np.dot(prev_n, t_vec))
                n_vec /= float(np.linalg.norm(n_vec))
            b_vec = np.cross(t_vec, n_vec)
            # Ensure cross(n_vec, b_vec) points along +t_vec
            if float(np.dot(np.cross(n_vec, b_vec), t_vec)) < 0.0:
                b_vec = -b_vec
            frames.append((t_vec, n_vec, b_vec))

        # Accumulate arc length for uniform UVs
        arc_len = [0.0]
        for k in range(1, len(pts)):
            arc_len.append(arc_len[-1] + float(np.linalg.norm(pts[k] - pts[k - 1])))

        # Compute ring vertices & smooth vertex normals analytically across shared rings
        ring_positions = []
        ring_colors = []
        ring_normals_acc = []

        for k, t in enumerate(ts):
            if k == len(ts) - 1:
                # Sharp demon-horn tip apex
                ring_positions.append([pts[k]])
                ring_colors.append([rib_arch_col(0.55, tip=0.90)])
                ring_normals_acc.append([np.zeros(3, dtype=np.float32)])
                continue

            # Rib cross-section thickness: robust near root/haunch, tapering gracefully to horn tip
            thick = 0.36 + 0.08 * math.sin(min(1.0, t * 2.5) * math.pi)
            taper = (1.0 - t) ** 0.72
            r_in = max(0.06, thick * taper)
            r_z = r_in * 1.18

            _, n_vec, b_vec = frames[k]
            pos_row = []
            col_row = []
            nrm_row = []
            bright = 0.80 + 0.18 * math.sin(t * math.pi)
            tip_char = max(0.0, (t - 0.70) / 0.30)

            for j in range(n_sides):
                a = 2.0 * math.pi * j / float(n_sides)
                # Slight almond keel on outer/dorsal edge
                keel = 1.0 + 0.14 * math.cos(2.0 * a)
                off = n_vec * (r_in * keel * math.cos(a)) + b_vec * (r_z * math.sin(a))
                p = pts[k] + off
                pos_row.append(p)
                # Subtle inner-side AO darkening
                in_shade = 0.88 + 0.12 * math.sin(a)
                col_row.append(rib_arch_col(bright * in_shade, tip=tip_char))
                nrm_row.append(np.zeros(3, dtype=np.float32))

            ring_positions.append(pos_row)
            ring_colors.append(col_row)
            ring_normals_acc.append(nrm_row)

        # Pass 1: Accumulate area-weighted face normals for smooth normals without UV-seam splits
        tris_to_emit = []
        for k in range(len(ts) - 1):
            v0 = arc_len[k] * uv_scale
            v1 = arc_len[k + 1] * uv_scale
            if k < len(ts) - 2:
                for j in range(n_sides):
                    nj = (j + 1) % n_sides
                    p00 = ring_positions[k][j]
                    p10 = ring_positions[k][nj]
                    p11 = ring_positions[k + 1][nj]
                    p01 = ring_positions[k + 1][j]

                    fn1 = np.cross(p10 - p00, p11 - p00)
                    fn2 = np.cross(p11 - p00, p01 - p00)
                    # Check against radial outward vector
                    outward = 0.25 * (p00 + p10 + p11 + p01) - 0.5 * (pts[k] + pts[k + 1])
                    flip = float(np.dot(fn1 + fn2, outward)) < 0.0
                    if flip:
                        fn1 = -fn1
                        fn2 = -fn2

                    ring_normals_acc[k][j] += fn1 + fn2
                    ring_normals_acc[k][nj] += fn1
                    ring_normals_acc[k + 1][nj] += fn1 + fn2
                    ring_normals_acc[k + 1][j] += fn2

                    u0 = j / float(n_sides) * 1.2
                    u1 = (j + 1) / float(n_sides) * 1.2
                    if not flip:
                        tris_to_emit.append(((k, j, u0, v0), (k, nj, u1, v0), (k + 1, nj, u1, v1)))
                        tris_to_emit.append(((k, j, u0, v0), (k + 1, nj, u1, v1), (k + 1, j, u0, v1)))
                    else:
                        tris_to_emit.append(((k, j, u0, v0), (k + 1, nj, u1, v1), (k, nj, u1, v0)))
                        tris_to_emit.append(((k, j, u0, v0), (k + 1, j, u0, v1), (k + 1, nj, u1, v1)))
            else:
                # Tip cone triangles to apex ring_positions[k+1][0]
                for j in range(n_sides):
                    nj = (j + 1) % n_sides
                    p00 = ring_positions[k][j]
                    p10 = ring_positions[k][nj]
                    tip = ring_positions[k + 1][0]
                    fn = np.cross(p10 - p00, tip - p00)
                    outward = 0.5 * (p00 + p10) - pts[k]
                    flip = float(np.dot(fn, outward)) < 0.0
                    if flip:
                        fn = -fn
                    ring_normals_acc[k][j] += fn
                    ring_normals_acc[k][nj] += fn
                    ring_normals_acc[k + 1][0] += fn
                    u0 = j / float(n_sides) * 1.2
                    u1 = (j + 1) / float(n_sides) * 1.2
                    um = 0.5 * (u0 + u1)
                    if not flip:
                        tris_to_emit.append(((k, j, u0, v0), (k, nj, u1, v0), (k + 1, 0, um, v1)))
                    else:
                        tris_to_emit.append(((k, j, u0, v0), (k + 1, 0, um, v1), (k, nj, u1, v0)))

        # Normalize accumulated smooth normals
        for k in range(len(ring_normals_acc)):
            for j in range(len(ring_normals_acc[k])):
                n = ring_normals_acc[k][j]
                nlen = float(np.linalg.norm(n))
                ring_normals_acc[k][j] = (n / nlen) if nlen > 1e-6 else np.array([0.0, 1.0, 0.0], dtype=np.float32)

        # Emit smooth-shaded indexed vertices (unique per (ring, j, uv) while preserving smooth normal)
        vert_cache = {}
        for a, b, c in tris_to_emit:
            tri_idx = []
            for key in (a, b, c):
                if key not in vert_cache:
                    rk, rj, uu, vv = key
                    vert_cache[key] = mb.add_vertex(
                        ring_positions[rk][rj],
                        ring_normals_acc[rk][rj],
                        (uu, vv),
                        ring_colors[rk][rj],
                    )
                tri_idx.append(vert_cache[key])
            mb.add_tri_indices(tri_idx[0], tri_idx[1], tri_idx[2])

        # Cap base inside socket
        base_n = -tangents[0]
        base_c_idx = mb.add_vertex(pts[0], base_n, (0.5, 0.5), rib_arch_col(0.55))
        for j in range(n_sides):
            nj = (j + 1) % n_sides
            p_a = ring_positions[0][j]
            p_b = ring_positions[0][nj]
            ia = mb.add_vertex(p_a, base_n, (0.0, 0.0), ring_colors[0][j])
            ib = mb.add_vertex(p_b, base_n, (1.0, 0.0), ring_colors[0][nj])
            if float(np.dot(np.cross(p_b - pts[0], p_a - pts[0]), base_n)) >= 0.0:
                mb.add_tri_indices(base_c_idx, ib, ia)
            else:
                mb.add_tri_indices(base_c_idx, ia, ib)

    return mb


# ==============================================================================
# 4. VERTEBRA INFERNAL HARDWARE (Mat_Infernal_IronBone)
# ==============================================================================

def _add_iron_collar_on_rib(mb, side, t_start, t_end, radial_pad=0.065, n_sides=8, n_studs=6):
    """Builds a forged iron shackle collar tightly clamped around the rib arch with pyramidal studs."""
    p0 = rib_curve_pos(side, t_start)
    p1 = rib_curve_pos(side, t_end)
    t_mid = 0.5 * (t_start + t_end)
    p_mid = 0.5 * (p0 + p1)
    t_dir = p1 - p0
    t_dir /= max(1e-6, float(np.linalg.norm(t_dir)))

    ref = np.array([0.0, 0.0, 1.0], dtype=np.float32)
    n_dir = np.cross(ref, t_dir)
    n_dir /= float(np.linalg.norm(n_dir))
    b_dir = np.cross(t_dir, n_dir)

    thick = 0.36 + 0.08 * math.sin(min(1.0, t_mid * 2.5) * math.pi)
    taper = (1.0 - t_mid) ** 0.72
    r_in = max(0.06, thick * taper) + radial_pad
    r_z = r_in * 1.16

    r_outer = []
    r_inner = []
    for k, center in enumerate((p0, p1)):
        ro = []
        ri = []
        for j in range(n_sides):
            a = 2.0 * math.pi * j / float(n_sides)
            off = n_dir * (r_in * math.cos(a)) + b_dir * (r_z * math.sin(a))
            ro.append(center + off)
            ri.append(center + off * 0.78)
        r_outer.append(ro)
        r_inner.append(ri)

    c_dark = iron_hardware_col(0.48, rust=0.25)
    c_mid = iron_hardware_col(0.76, rust=0.15)
    c_rim = iron_hardware_col(0.92, rust=0.05)
    uv_s = 0.6

    for j in range(n_sides):
        nj = (j + 1) % n_sides
        # Outer cylindrical strap face
        q00, q10, q11, q01 = r_outer[0][j], r_outer[0][nj], r_outer[1][nj], r_outer[1][j]
        outward = 0.25 * (q00 + q10 + q11 + q01) - p_mid
        add_oriented_quad(
            mb, q00, q10, q11, q01,
            (j * 0.2, 0.0), (nj * 0.2, 0.0), (nj * 0.2, 0.3 * uv_s), (j * 0.2, 0.3 * uv_s),
            c_mid, c_mid, c_rim, c_rim, outward,
        )
        # Bottom ring rim (-t_dir)
        add_oriented_quad(
            mb, r_inner[0][j], r_inner[0][nj], r_outer[0][nj], r_outer[0][j],
            (0.0, 0.0), (0.2, 0.0), (0.2, 0.1), (0.0, 0.1),
            c_dark, c_dark, c_mid, c_mid, -t_dir,
        )
        # Top ring rim (+t_dir)
        add_oriented_quad(
            mb, r_outer[1][j], r_outer[1][nj], r_inner[1][nj], r_inner[1][j],
            (0.0, 0.0), (0.2, 0.0), (0.2, 0.1), (0.0, 0.1),
            c_rim, c_rim, c_dark, c_dark, t_dir,
        )

    # Pyramidal iron studs around the collar perimeter
    for s in range(n_studs):
        a = 2.0 * math.pi * s / float(n_studs)
        off = n_dir * (r_in * math.cos(a)) + b_dir * (r_z * math.sin(a))
        stud_base = p_mid + off * 0.96
        stud_n = off / max(1e-6, float(np.linalg.norm(off)))
        add_pyramid_stud(mb, stud_base, stud_n, half_w=0.065, height=0.10, c_base=c_dark, c_tip=c_rim)


def _add_waist_shackle_band(mb):
    """Forged iron shackle band clamped tightly around the pinched vertebral waist (Y ~ -1.75)."""
    y_bot, y_top = -1.96, -1.54
    # Centrum at Y=-1.75 has rx=1.88, rz=1.32 with 1.0 + 0.20*cos(8a) -> max ~2.256, ~1.584
    rx_out, rz_out = 2.28, 1.64
    rx_in, rz_in = 1.82, 1.28
    n_seg = 16
    c_bot = iron_hardware_col(0.45, rust=0.35)
    c_top = iron_hardware_col(0.84, rust=0.15)

    ro_b, ro_t, ri_b, ri_t = [], [], [], []
    for j in range(n_seg):
        a = 2.0 * math.pi * j / float(n_seg)
        flute = 1.0 + 0.06 * math.cos(8.0 * a)
        ca, sa = math.cos(a), math.sin(a)
        ro_b.append(np.array([rx_out * flute * ca, y_bot, rz_out * flute * sa], dtype=np.float32))
        ro_t.append(np.array([rx_out * flute * ca, y_top, rz_out * flute * sa], dtype=np.float32))
        ri_b.append(np.array([rx_in * ca, y_bot, rz_in * sa], dtype=np.float32))
        ri_t.append(np.array([rx_in * ca, y_top, rz_in * sa], dtype=np.float32))

    y_mid = 0.5 * (y_bot + y_top)
    for j in range(n_seg):
        nj = (j + 1) % n_seg
        outward = 0.5 * (ro_b[j] + ro_b[nj]) - np.array([0.0, y_bot, 0.0], dtype=np.float32)
        add_oriented_quad(
            mb, ro_b[j], ro_b[nj], ro_t[nj], ro_t[j],
            (j * 0.25, 0.0), (nj * 0.25, 0.0), (nj * 0.25, 0.25), (j * 0.25, 0.25),
            c_bot, c_bot, c_top, c_top, outward,
        )
        add_oriented_quad(
            mb, ri_t[j], ri_t[nj], ro_t[nj], ro_t[j],
            (0.0, 0.0), (0.25, 0.0), (0.25, 0.15), (0.0, 0.15),
            c_bot, c_bot, c_top, c_top, (0.0, 1.0, 0.0),
        )
        add_oriented_quad(
            mb, ro_b[j], ro_b[nj], ri_b[nj], ri_b[j],
            (0.0, 0.0), (0.25, 0.0), (0.25, 0.15), (0.0, 0.15),
            c_bot, c_bot, c_bot, c_bot, (0.0, -1.0, 0.0),
        )

    # 8 heavy pyramidal iron studs around front/back/sides of waist shackle
    for j in range(0, n_seg, 2):
        p_stud = 0.5 * (ro_b[j] + ro_t[j])
        n_stud = p_stud - np.array([0.0, y_mid, 0.0], dtype=np.float32)
        add_pyramid_stud(mb, p_stud * 0.98, n_stud, half_w=0.09, height=0.16, c_base=c_bot, c_tip=c_top)


def _add_cage_brazier_and_bracket(mb, side):
    """
    Hanging skeletal lantern cage-brazier at (side * 3.50, 2.55, 0.0),
    suspended from an iron cantilever bracket bolted to the rib arch around Y = 2.95.
    """
    cx = side * 3.50
    c_iron = iron_hardware_col(0.68, rust=0.20)
    c_dark = iron_hardware_col(0.42, rust=0.35)
    c_hi = iron_hardware_col(0.92, rust=0.05)

    # Rib attachment points around t = 0.35 (Y~3.10) and t = 0.24 (Y~2.15)
    p_rib_hi = rib_curve_pos(side, 0.35)
    p_rib_lo = rib_curve_pos(side, 0.24)
    p_hook = np.array([cx, 3.04, 0.0], dtype=np.float32)
    # Exact point along the upper cantilever beam so the diagonal brace meets cleanly
    p_knee = 0.52 * p_rib_hi + 0.48 * p_hook

    # Upper horizontal cantilever beam + diagonal brace strut + hanging stem
    add_beam_segment(mb, p_rib_hi, p_hook, 0.07, 0.08, 4, c_dark, c_hi)
    add_beam_segment(mb, p_rib_lo, p_knee, 0.055, 0.055, 4, c_dark, c_iron)
    add_beam_segment(mb, p_hook, (cx, 2.86, 0.0), 0.045, 0.045, 4, c_hi, c_iron)

    # Iron clamp cuff on the rib where the bracket bolts in
    _add_iron_collar_on_rib(mb, side, 0.33, 0.37, radial_pad=0.055, n_sides=6, n_studs=4)

    # Lantern cage roof pyramid (Y = 2.78 .. 2.90) & bottom ash-pan (Y = 2.16 .. 2.30)
    r_cage = 0.27
    add_beam_segment(mb, (cx, 2.89, 0.0), (cx, 2.77, 0.0), 0.09, 0.09, 4, c_hi, c_iron)
    # 4 skeletal cage bars bowing around the ember core at (cx, 2.55, 0.0)
    for k in range(4):
        ang = (math.pi * 0.25) + k * (math.pi * 0.5)
        ox = r_cage * math.cos(ang)
        oz = r_cage * math.sin(ang)
        p_top = (cx + ox * 0.72, 2.80, oz * 0.72)
        p_mid = (cx + ox * 1.08, 2.55, oz * 1.08)
        p_bot = (cx + ox * 0.76, 2.28, oz * 0.76)
        add_beam_segment(mb, (cx, 2.84, 0.0), p_top, 0.032, 0.032, 4, c_hi, c_iron, cap_start=False, cap_end=False)
        add_beam_segment(mb, p_top, p_mid, 0.030, 0.030, 4, c_iron, c_hi, cap_start=False, cap_end=False)
        add_beam_segment(mb, p_mid, p_bot, 0.030, 0.030, 4, c_hi, c_dark, cap_start=False, cap_end=False)
        add_beam_segment(mb, p_bot, (cx, 2.22, 0.0), 0.032, 0.032, 4, c_dark, c_dark, cap_start=False, cap_end=False)

    # Bottom spiked finial beneath lantern
    add_beam_segment(mb, (cx, 2.25, 0.0), (cx, 2.04, 0.0), 0.09, 0.09, 4, c_iron, c_dark)
    add_pyramid_stud(mb, (cx, 2.06, 0.0), (0.0, -1.0, 0.0), half_w=0.075, height=0.18, c_base=c_dark, c_tip=c_hi)


def _add_dangling_chain(mb, anchor_x, anchor_z, y_top=-0.50, y_bot=-5.20, n_links=8):
    """
    Heavy interlocking iron chain links hanging from the lower vertebral flanks
    (Y = -0.50 down to -5.20) toward the acid sea.
    """
    # Anchor shackle block at y_top
    c_top = iron_hardware_col(0.75, rust=0.20)
    c_bot = iron_hardware_col(0.32, rust=0.60)
    add_oriented_box(mb, anchor_x, y_top + 0.08, anchor_z, 0.24, 0.22, 0.24, c_bot, c_top)

    span = y_top - y_bot
    step = span / float(n_links)
    link_h = step * 1.24
    link_w = 0.22
    bar_r = 0.044

    for i in range(n_links):
        yc = y_top - (i + 0.5) * step
        # Alternate 90 degrees around Y axis for interlocking links
        rot = (i % 2) * (math.pi * 0.5)
        # Subtle organic pendulum curve
        sway_x = 0.08 * math.sin(i * 0.45) * (1.0 if anchor_x > 0 else -1.0)
        sway_z = 0.06 * math.cos(i * 0.35)
        dx = math.cos(rot) * link_w
        dz = math.sin(rot) * link_w

        t_fade = i / float(max(1, n_links - 1))
        col_u = iron_hardware_col(0.78 - 0.36 * t_fade, rust=0.15 + 0.55 * t_fade)
        col_d = iron_hardware_col(0.68 - 0.36 * t_fade, rust=0.25 + 0.55 * t_fade)

        p_tl = (anchor_x + sway_x - dx, yc + link_h * 0.45, anchor_z + sway_z - dz)
        p_tr = (anchor_x + sway_x + dx, yc + link_h * 0.45, anchor_z + sway_z + dz)
        p_bl = (anchor_x + sway_x - dx, yc - link_h * 0.45, anchor_z + sway_z - dz)
        p_br = (anchor_x + sway_x + dx, yc - link_h * 0.45, anchor_z + sway_z + dz)

        # 4 bars forming one heavy rectangular iron chain link
        add_beam_segment(mb, p_tl, p_tr, bar_r, bar_r, 4, col_u, col_u)
        add_beam_segment(mb, p_bl, p_br, bar_r, bar_r, 4, col_d, col_d)
        add_beam_segment(mb, p_tl, p_bl, bar_r, bar_r, 4, col_u, col_d)
        add_beam_segment(mb, p_tr, p_br, bar_r, bar_r, 4, col_u, col_d)


def build_infernal_hardware():
    """
    Combines:
    - Forged iron shackle collars with studs on both rib bases + vertebral waist band
    - Twin hanging skeletal lantern cage-braziers & brackets at X = +-3.5, Y = 2.55..2.95
    - Heavy dangling iron chains (Y = -0.5 down to -5.2)
    """
    mb = MeshBuilder("Vertebra_Infernal_Hardware", "Mat_Infernal_IronBone")

    _add_waist_shackle_band(mb)
    for side in (-1.0, 1.0):
        _add_iron_collar_on_rib(mb, side, 0.08, 0.13, radial_pad=0.07, n_sides=8, n_studs=6)
        _add_cage_brazier_and_bracket(mb, side)
        _add_dangling_chain(mb, anchor_x=side * 2.85, anchor_z=side * 0.40, y_top=-0.50, y_bot=-5.20, n_links=8)

    return mb


# ==============================================================================
# 5. VERTEBRA EMBER CORE (Mat_Hell_EmberLantern)
# ==============================================================================

def _add_octa_ember_core(mb, cx, cy, cz, rx=0.16, ry=0.20, rz=0.16):
    """Jagged crystalline glowing ember core inside a brazier cage."""
    top = np.array([cx, cy + ry, cz], dtype=np.float32)
    bot = np.array([cx, cy - ry * 0.85, cz], dtype=np.float32)
    ring = [
        np.array([cx + rx, cy, cz], dtype=np.float32),
        np.array([cx, cy + 0.03, cz + rz], dtype=np.float32),
        np.array([cx - rx, cy - 0.02, cz], dtype=np.float32),
        np.array([cx, cy + 0.02, cz - rz], dtype=np.float32),
    ]
    c_top = ember_col(1.0)
    c_mid = ember_col(0.75)
    c_bot = ember_col(0.35)
    center = np.array([cx, cy, cz], dtype=np.float32)

    for i in range(4):
        ni = (i + 1) % 4
        p_a, p_b = ring[i], ring[ni]
        hint_u = (p_a + p_b + top) / 3.0 - center
        hint_d = (p_a + p_b + bot) / 3.0 - center
        add_oriented_tri(mb, p_a, p_b, top, (0.0, 0.2), (1.0, 0.2), (0.5, 1.0), c_mid, c_mid, c_top, hint_u)
        add_oriented_tri(mb, p_b, p_a, bot, (1.0, 0.2), (0.0, 0.2), (0.5, 0.0), c_mid, c_mid, c_bot, hint_d)


def _add_rib_socket_fissure_strip(mb, side):
    """
    Glowing ember fissure strips tucked tightly along the rib-socket joints on Y = 0.25..0.80,
    seating into the joint between the bone curb, rib root, and iron rib-base collar.
    """
    c_hot = ember_col(1.0)
    c_warm = ember_col(0.65)
    c_deep = ember_col(0.30)

    # Sample along the rib root curve t = 0.015 .. 0.075 (Y ~ 0.26 .. 0.78)
    p_a = rib_curve_pos(side, 0.015)
    p_b = rib_curve_pos(side, 0.045)
    p_c = rib_curve_pos(side, 0.075)

    # Front (-Z) and Back (+Z) glowing socket seam strips hugging the rib root surface
    for z_sign in (-1.0, 1.0):
        off_lo = np.array([-side * 0.14, 0.00, z_sign * 0.34], dtype=np.float32)
        off_mid = np.array([-side * 0.16, 0.02, z_sign * 0.35], dtype=np.float32)
        off_hi = np.array([-side * 0.12, 0.00, z_sign * 0.30], dtype=np.float32)
        add_beam_segment(mb, p_a + off_lo, p_b + off_mid, 0.055, 0.048, 4, c_deep, c_hot)
        add_beam_segment(mb, p_b + off_mid, p_c + off_hi, 0.048, 0.040, 4, c_hot, c_warm)

    # Inner-curb horizontal glowing fissure tucked right at the base of the rib socket (Y ~ 0.30)
    add_beam_segment(
        mb,
        (side * 3.16, 0.30, -0.36),
        (side * 3.24, 0.30,  0.36),
        0.050, 0.050, 4, c_warm, c_hot,
    )


def build_ember_core():
    """
    Twin hellfire ember cores inside the side lantern cages (X = +-3.5, Y = 2.55)
    plus glowing ember fissure strips tucked along the rib-socket joints (Y = 0.25..0.80).
    """
    mb = MeshBuilder("Vertebra_Ember_Core", "Mat_Hell_EmberLantern")
    for side in (-1.0, 1.0):
        _add_octa_ember_core(mb, side * 3.50, 2.55, 0.0, rx=0.15, ry=0.20, rz=0.15)
        _add_rib_socket_fissure_strip(mb, side)
    return mb


# ==============================================================================
# BUILD, VALIDATE & EXPORT
# ==============================================================================

def validate_meshes(meshes):
    """Verifies zero degenerate/NaN triangles and returns detailed per-mesh bounds & counts."""
    report = []
    g_min = np.array([1e9, 1e9, 1e9], dtype=np.float32)
    g_max = np.array([-1e9, -1e9, -1e9], dtype=np.float32)
    total_verts = 0
    total_tris = 0

    for mb in meshes:
        pos = np.asarray(mb.positions, dtype=np.float32)
        nrm = np.asarray(mb.normals, dtype=np.float32)
        uv = np.asarray(mb.uvs, dtype=np.float32)
        col = np.asarray(mb.colors, dtype=np.float32)
        idx = np.asarray(mb.indices, dtype=np.int32).reshape(-1, 3)

        assert np.all(np.isfinite(pos)), f"{mb.name}: non-finite positions"
        assert np.all(np.isfinite(nrm)), f"{mb.name}: non-finite normals"
        assert np.all(np.isfinite(uv)), f"{mb.name}: non-finite uvs"
        assert np.all(np.isfinite(col)), f"{mb.name}: non-finite colors"

        v0, v1, v2 = pos[idx[:, 0]], pos[idx[:, 1]], pos[idx[:, 2]]
        areas = 0.5 * np.linalg.norm(np.cross(v1 - v0, v2 - v0), axis=1)
        degen = int(np.sum(areas < 1e-7))
        assert degen == 0, f"{mb.name}: found {degen} degenerate triangles!"

        nrm_lens = np.linalg.norm(nrm, axis=1)
        assert np.all(nrm_lens > 0.9), f"{mb.name}: unnormalized normals detected"

        b_min = pos.min(axis=0)
        b_max = pos.max(axis=0)
        g_min = np.minimum(g_min, b_min)
        g_max = np.maximum(g_max, b_max)
        total_verts += len(pos)
        total_tris += len(idx)

        report.append({
            "name": mb.name,
            "material": mb.material_name,
            "vertices": len(pos),
            "triangles": len(idx),
            "min": [round(float(x), 3) for x in b_min],
            "max": [round(float(x), 3) for x in b_max],
        })

    return {
        "total_vertices": total_verts,
        "total_triangles": total_tris,
        "bounds_min": [round(float(x), 3) for x in g_min],
        "bounds_max": [round(float(x), 3) for x in g_max],
        "submeshes": report,
    }


def build_vertebra_unit_glb(tex_dir, output_glb_path):
    # Reuse existing PS1 textures in tex_dir (generate only if missing)
    needed = {
        "tex_ossified_stone.png",
        "tex_cathedral_floor.png",
        "tex_spire_shingles.png",
        "tex_hell_organs.png",
    }
    if not all(os.path.exists(os.path.join(tex_dir, f)) for f in needed):
        generate_all_textures(tex_dir)
        generate_cathedral_textures(tex_dir, size=256)

    meshes = [
        build_body_and_spines(),
        build_walkway_slab(),
        build_rib_arches(),
        build_infernal_hardware(),
        build_ember_core(),
    ]

    validation = validate_meshes(meshes)

    materials_spec = {
        "Mat_Vertebra_ScorchedBone": {
            "image_path": os.path.join(tex_dir, "tex_ossified_stone.png"),
            "roughness": 0.78,
            "metallic": 0.04,
            "double_sided": False,
        },
        "Mat_Spine_MarrowDeck": {
            "image_path": os.path.join(tex_dir, "tex_cathedral_floor.png"),
            "roughness": 0.58,
            "metallic": 0.06,
            "double_sided": False,
        },
        "Mat_Vertebra_RibArch": {
            "image_path": os.path.join(tex_dir, "tex_ossified_stone.png"),
            "roughness": 0.65,
            "metallic": 0.05,
            "double_sided": False,
        },
        "Mat_Infernal_IronBone": {
            "image_path": os.path.join(tex_dir, "tex_spire_shingles.png"),
            "roughness": 0.42,
            "metallic": 0.55,
            "double_sided": False,
        },
        "Mat_Hell_EmberLantern": {
            "image_path": os.path.join(tex_dir, "tex_hell_organs.png"),
            "roughness": 0.25,
            "metallic": 0.0,
            "emissive_factor": [1.0, 0.42, 0.10],
            "use_emissive_texture": True,
            "double_sided": False,
        },
    }

    stats = export_glb(
        output_path=output_glb_path,
        meshes=meshes,
        materials_spec=materials_spec,
        root_name="Spinal_Vertebra_Unit",
    )
    return stats, validation


if __name__ == "__main__":
    base_dir = r"C:\Users\user\IdeaProjects\Game final\Assets\PilotChamber"
    tex_dir = os.path.join(base_dir, "Textures")
    out_glb = os.path.join(base_dir, "Spinal_Vertebra_Unit.glb")
    stats, validation = build_vertebra_unit_glb(tex_dir, out_glb)
    print(f"[generate_vertebra_unit] Exported: {stats}")
    print(f"[generate_vertebra_unit] Total: {validation['total_vertices']} verts, {validation['total_triangles']} tris")
    print(f"[generate_vertebra_unit] Overall bounds: min={validation['bounds_min']} max={validation['bounds_max']}")
    for s in validation["submeshes"]:
        print(f"  - {s['name']} ({s['material']}): {s['vertices']} verts, {s['triangles']} tris, min={s['min']}, max={s['max']}")
