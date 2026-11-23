import math
import numpy as np

from glb_exporter import MeshBuilder


# ==============================================================================
# COLOR & GEOMETRY PRIMITIVE HELPERS (WITH GUARANTEED OUTWARD NORMALS)
# ==============================================================================

def _rgba(r, g, b, a=1.0):
    return (
        float(max(0.0, min(1.0, r))),
        float(max(0.0, min(1.0, g))),
        float(max(0.0, min(1.0, b))),
        float(max(0.0, min(1.0, a))),
    )


def stone_col(bright=0.82, warm=0.0):
    """Weathered ossified gothic limestone tint."""
    return _rgba(
        bright * (0.94 + 0.06 * warm),
        bright * (0.89 + 0.02 * warm),
        bright * (0.83 - 0.04 * warm),
    )


def brick_col(bright=0.75, grime=0.0):
    """Dark gothic crimson-charcoal flesh-brick tint."""
    return _rgba(
        bright * (0.88 - 0.12 * grime),
        bright * (0.72 - 0.15 * grime),
        bright * (0.74 - 0.12 * grime),
    )


def shingle_col(bright=0.72):
    """Dark slate-violet spire shingle tint."""
    return _rgba(bright * 0.78, bright * 0.76, bright * 0.88)


def cartilage_col(bright=0.90):
    """Sharp ivory-bone cartilage tint."""
    return _rgba(bright * 0.98, bright * 0.93, bright * 0.82)


def rock_col(bright=0.68):
    """Calcified cliff crag tint."""
    return _rgba(bright * 0.82, bright * 0.78, bright * 0.72)


def organ_col(bright=0.78, pulse=0.0):
    """Visceral arterial tendril tint."""
    return _rgba(
        bright * (0.95 + 0.05 * pulse),
        bright * (0.58 + 0.08 * pulse),
        bright * (0.62 + 0.04 * pulse),
    )


def add_oriented_quad(mb, p00, p10, p11, p01, uv00, uv10, uv11, uv01, c00, c10, c11, c01, outward_hint):
    """Emits a flat quad oriented so its face normal strictly aligns with outward_hint."""
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
    """Emits a flat triangle oriented so its face normal strictly aligns with outward_hint."""
    p0 = np.asarray(p0, dtype=np.float32)
    p1 = np.asarray(p1, dtype=np.float32)
    p2 = np.asarray(p2, dtype=np.float32)
    n = np.cross(p1 - p0, p2 - p0)
    if float(np.dot(n, np.asarray(outward_hint, dtype=np.float32))) >= 0.0:
        mb.add_flat_triangle(p0, p1, p2, uv0, uv1, uv2, c0, c1, c2)
    else:
        mb.add_flat_triangle(p0, p2, p1, uv0, uv2, uv1, c0, c2, c1)


def add_box(
    mb,
    cx, cy, cz,
    sx, sy, sz,
    c_bot=None,
    c_top=None,
    uv_scale=0.35,
    skip_bottom=False,
    skip_top=False,
    skip_back=False,
):
    """Adds an axis-aligned box centered at (cx, cy, cz) with size (sx, sy, sz) and outward normals."""
    if c_bot is None:
        c_bot = stone_col(0.68)
    if c_top is None:
        c_top = stone_col(0.92)

    hx, hy, hz = sx * 0.5, sy * 0.5, sz * 0.5
    x0, x1 = cx - hx, cx + hx
    y0, y1 = cy - hy, cy + hy
    z0, z1 = cz - hz, cz + hz

    u_x = sx * uv_scale
    u_z = sz * uv_scale
    v_y = sy * uv_scale

    # Front (-Z)
    add_oriented_quad(
        mb,
        (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
        (0.0, 0.0), (u_x, 0.0), (u_x, v_y), (0.0, v_y),
        c_bot, c_bot, c_top, c_top,
        (0.0, 0.0, -1.0),
    )
    # Back (+Z)
    if not skip_back:
        add_oriented_quad(
            mb,
            (x1, y0, z1), (x0, y0, z1), (x0, y1, z1), (x1, y1, z1),
            (0.0, 0.0), (u_x, 0.0), (u_x, v_y), (0.0, v_y),
            c_bot, c_bot, c_top, c_top,
            (0.0, 0.0, 1.0),
        )
    # Left (-X)
    add_oriented_quad(
        mb,
        (x0, y0, z1), (x0, y0, z0), (x0, y1, z0), (x0, y1, z1),
        (0.0, 0.0), (u_z, 0.0), (u_z, v_y), (0.0, v_y),
        c_bot, c_bot, c_top, c_top,
        (-1.0, 0.0, 0.0),
    )
    # Right (+X)
    add_oriented_quad(
        mb,
        (x1, y0, z0), (x1, y0, z1), (x1, y1, z1), (x1, y1, z0),
        (0.0, 0.0), (u_z, 0.0), (u_z, v_y), (0.0, v_y),
        c_bot, c_bot, c_top, c_top,
        (1.0, 0.0, 0.0),
    )
    # Top (+Y)
    if not skip_top:
        add_oriented_quad(
            mb,
            (x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1),
            (0.0, 0.0), (u_x, 0.0), (u_x, u_z), (0.0, u_z),
            c_top, c_top, c_top, c_top,
            (0.0, 1.0, 0.0),
        )
    # Bottom (-Y)
    if not skip_bottom:
        add_oriented_quad(
            mb,
            (x0, y0, z1), (x1, y0, z1), (x1, y0, z0), (x0, y0, z0),
            (0.0, 0.0), (u_x, 0.0), (u_x, u_z), (0.0, u_z),
            c_bot, c_bot, c_bot, c_bot,
            (0.0, -1.0, 0.0),
        )


def add_sloped_weathering(mb, cx, y0, y1, z_front, z_back, w_bot, w_top, c_bot, c_top, uv_scale=0.35):
    """Adds a gothic buttress sloped weathering block (wider/projecting at bottom, narrower/recessed at top)."""
    hb0, ht0 = w_bot * 0.5, w_top * 0.5
    p_fl_b = (cx - hb0, y0, z_front)
    p_fr_b = (cx + hb0, y0, z_front)
    p_bl_b = (cx - hb0, y0, z_back)
    p_br_b = (cx + hb0, y0, z_back)

    p_fl_t = (cx - ht0, y1, z_front + (z_back - z_front) * 0.45)
    p_fr_t = (cx + ht0, y1, z_front + (z_back - z_front) * 0.45)
    p_bl_t = (cx - ht0, y1, z_back)
    p_br_t = (cx + ht0, y1, z_back)

    u = w_bot * uv_scale
    v = (y1 - y0) * uv_scale
    # Sloped front face
    add_oriented_quad(mb, p_fl_b, p_fr_b, p_fr_t, p_fl_t, (0, 0), (u, 0), (u, v), (0, v), c_bot, c_bot, c_top, c_top, (0.0, 0.6, -1.0))
    # Left & Right faces
    add_oriented_quad(mb, p_bl_b, p_fl_b, p_fl_t, p_bl_t, (0, 0), (u, 0), (u, v), (0, v), c_bot, c_bot, c_top, c_top, (-1.0, 0.2, 0.0))
    add_oriented_quad(mb, p_fr_b, p_br_b, p_br_t, p_fr_t, (0, 0), (u, 0), (u, v), (0, v), c_bot, c_bot, c_top, c_top, (1.0, 0.2, 0.0))
    # Top flat ledge
    add_oriented_quad(mb, p_fl_t, p_fr_t, p_br_t, p_bl_t, (0, 0), (u, 0), (u, u), (0, u), c_top, c_top, c_top, c_top, (0.0, 1.0, 0.0))


def add_prism_pillar(
    mb,
    cx0, y0, cz0,
    cx1, y1, cz1,
    rx0, rz0,
    rx1=None, rz1=None,
    n_sides=8,
    rot_deg=22.5,
    c_bot=None,
    c_top=None,
    uv_scale=0.35,
    cap_top=True,
    cap_bottom=False,
):
    """Builds a vertical or tilted tapered polygonal prism/column/spire segment with verified outward normals."""
    if rx1 is None:
        rx1 = rx0
    if rz1 is None:
        rz1 = rz0
    if c_bot is None:
        c_bot = stone_col(0.68)
    if c_top is None:
        c_top = stone_col(0.92)

    rot = math.radians(rot_deg)
    ring0 = []
    ring1 = []
    for i in range(n_sides):
        a = rot + 2.0 * math.pi * i / float(n_sides)
        ca, sa = math.cos(a), math.sin(a)
        ring0.append((cx0 + rx0 * ca, y0, cz0 + rz0 * sa))
        ring1.append((cx1 + rx1 * ca, y1, cz1 + rz1 * sa))

    h = max(0.05, abs(y1 - y0))
    v_top = h * uv_scale
    for i in range(n_sides):
        ni = (i + 1) % n_sides
        p00 = ring0[i]
        p10 = ring0[ni]
        p11 = ring1[ni]
        p01 = ring1[i]
        mx = 0.25 * (p00[0] + p10[0] + p11[0] + p01[0]) - 0.5 * (cx0 + cx1)
        mz = 0.25 * (p00[2] + p10[2] + p11[2] + p01[2]) - 0.5 * (cz0 + cz1)
        my = 0.25 * ((rx0 - rx1) + (rz0 - rz1)) if y1 > y0 else 0.0
        u0 = i / float(n_sides) * max(1.0, (rx0 + rz0) * uv_scale * 2.0)
        u1 = (i + 1) / float(n_sides) * max(1.0, (rx0 + rz0) * uv_scale * 2.0)
        add_oriented_quad(
            mb,
            p00, p10, p11, p01,
            (u0, 0.0), (u1, 0.0), (u1, v_top), (u0, v_top),
            c_bot, c_bot, c_top, c_top,
            (mx, my, mz),
        )

    if cap_top and (rx1 > 1e-3 or rz1 > 1e-3):
        pc = (cx1, y1, cz1)
        for i in range(n_sides):
            ni = (i + 1) % n_sides
            add_oriented_tri(
                mb,
                pc, ring1[i], ring1[ni],
                (0.5, 0.5), (0.0, 0.0), (1.0, 0.0),
                c_top, c_top, c_top,
                (0.0, 1.0, 0.0),
            )
    if cap_bottom and (rx0 > 1e-3 or rz0 > 1e-3):
        pc = (cx0, y0, cz0)
        for i in range(n_sides):
            ni = (i + 1) % n_sides
            add_oriented_tri(
                mb,
                pc, ring0[ni], ring0[i],
                (0.5, 0.5), (1.0, 0.0), (0.0, 0.0),
                c_bot, c_bot, c_bot,
                (0.0, -1.0, 0.0),
            )


def ogival_arch_point(cx, y_spring, half_w, apex_h, t):
    """
    Returns (x, y, nx_out, ny_out) along a pointed Gothic arch for t in [0.0, 1.0]:
    t = 0.0 is left springing (cx - half_w, y_spring),
    t = 0.5 is pointed apex (cx, y_spring + apex_h),
    t = 1.0 is right springing (cx + half_w, y_spring).
    """
    if t <= 0.5:
        s = t * 2.0  # 0 -> 1 up left side
        ang = s * (math.pi * 0.5)
        x = cx - half_w * math.cos(ang)
        y = y_spring + apex_h * math.sin(ang)
        nx = -math.cos(ang)
        ny = math.sin(ang) * (half_w / max(0.1, apex_h))
    else:
        s = (1.0 - t) * 2.0  # 1 -> 0 down right side
        ang = s * (math.pi * 0.5)
        x = cx + half_w * math.cos(ang)
        y = y_spring + apex_h * math.sin(ang)
        nx = math.cos(ang)
        ny = math.sin(ang) * (half_w / max(0.1, apex_h))
    nlen = max(1e-5, math.hypot(nx, ny))
    return x, y, nx / nlen, ny / nlen


def add_pointed_arch(
    mb,
    cx,
    y_spring,
    z_front,
    z_back,
    w_inner,
    w_outer,
    h_inner,
    h_outer,
    n_segs=18,
    c_inner=None,
    c_outer=None,
    uv_scale=0.35,
):
    """Builds a 3D Gothic pointed archivolt ring with front, back, inner soffit, and outer extrados."""
    if c_inner is None:
        c_inner = stone_col(0.66)
    if c_outer is None:
        c_outer = stone_col(0.92)

    in_pts = []
    out_pts = []
    norms = []
    for i in range(n_segs + 1):
        t = i / float(n_segs)
        ix, iy, nx, ny = ogival_arch_point(cx, y_spring, w_inner, h_inner, t)
        ox, oy, _, _ = ogival_arch_point(cx, y_spring, w_outer, h_outer, t)
        in_pts.append((ix, iy))
        out_pts.append((ox, oy))
        norms.append((nx, ny))

    dz = abs(z_back - z_front) * uv_scale
    for i in range(n_segs):
        u0 = i / float(n_segs) * 4.0
        u1 = (i + 1) / float(n_segs) * 4.0
        ix0, iy0 = in_pts[i]
        ix1, iy1 = in_pts[i + 1]
        ox0, oy0 = out_pts[i]
        ox1, oy1 = out_pts[i + 1]
        nx, ny = norms[i]

        # Front face (-Z)
        add_oriented_quad(
            mb,
            (ix0, iy0, z_front), (ox0, oy0, z_front), (ox1, oy1, z_front), (ix1, iy1, z_front),
            (u0, 0.0), (u0, 0.5), (u1, 0.5), (u1, 0.0),
            c_inner, c_outer, c_outer, c_inner,
            (0.0, 0.0, -1.0),
        )
        # Back face (+Z)
        add_oriented_quad(
            mb,
            (ox0, oy0, z_back), (ix0, iy0, z_back), (ix1, iy1, z_back), (ox1, oy1, z_back),
            (u0, 0.0), (u0, 0.5), (u1, 0.5), (u1, 0.0),
            c_outer, c_inner, c_inner, c_outer,
            (0.0, 0.0, 1.0),
        )
        # Inner intrados soffit (faces inward -nx, -ny)
        add_oriented_quad(
            mb,
            (ix0, iy0, z_front), (ix1, iy1, z_front), (ix1, iy1, z_back), (ix0, iy0, z_back),
            (u0, 0.0), (u1, 0.0), (u1, dz), (u0, dz),
            c_inner, c_inner, c_inner, c_inner,
            (-nx, -ny, -0.15),
        )
        # Outer extrados (faces outward +nx, +ny)
        add_oriented_quad(
            mb,
            (ox0, oy0, z_front), (ox0, oy0, z_back), (ox1, oy1, z_back), (ox1, oy1, z_front),
            (u0, 0.0), (u0, dz), (u1, dz), (u1, 0.0),
            c_outer, c_outer, c_outer, c_outer,
            (nx, ny, -0.15),
        )


def add_gablet(
    mb_stone,
    mb_cartilage,
    cx,
    y_base,
    y_apex,
    z_front,
    z_back,
    half_w,
    thickness=0.38,
    n_crockets=5,
    fill_tympanum=False,
    with_finial=True,
):
    """Adds a steep triangular Gothic Gablet (Frontão) with crocketed raking cornices and a bone finial."""
    c_lo = stone_col(0.76)
    c_hi = stone_col(0.96)
    c_in = stone_col(0.68)

    # Left and right raking cornice beams
    for side in (-1.0, 1.0):
        x_bot = cx + side * half_w
        for k in range(n_crockets):
            t0 = k / float(n_crockets)
            t1 = (k + 1) / float(n_crockets)
            x0 = x_bot * (1.0 - t0) + cx * t0
            y0 = y_base * (1.0 - t0) + y_apex * t0
            x1 = x_bot * (1.0 - t1) + cx * t1
            y1 = y_base * (1.0 - t1) + y_apex * t1

            dx = side * thickness
            dy = thickness * 1.15
            # Front face of raking beam
            add_oriented_quad(
                mb_stone,
                (x0, y0, z_front), (x0 + dx, y0 - dy, z_front),
                (x1 + dx * 0.5, y1 - dy, z_front), (x1, y1, z_front),
                (0, 0), (0.4, 0), (0.4, 1), (0, 1),
                c_hi, c_lo, c_lo, c_hi,
                (0.0, 0.0, -1.0),
            )
            # Upper sloped extrados of raking beam
            add_oriented_quad(
                mb_stone,
                (x0, y0, z_front), (x1, y1, z_front), (x1, y1, z_back), (x0, y0, z_back),
                (0, 0), (1, 0), (1, 0.4), (0, 0.4),
                c_hi, c_hi, c_lo, c_lo,
                (side * 0.7, 0.7, -0.1),
            )
            # Lower soffit of raking beam
            add_oriented_quad(
                mb_stone,
                (x0 + dx, y0 - dy, z_front), (x0 + dx, y0 - dy, z_back),
                (x1 + dx * 0.5, y1 - dy, z_back), (x1 + dx * 0.5, y1 - dy, z_front),
                (0, 0), (0, 0.4), (1, 0.4), (1, 0),
                c_in, c_in, c_in, c_in,
                (-side * 0.7, -0.7, -0.1),
            )
            # Projecting crocket bud along outer slope
            if k < n_crockets:
                xm = 0.5 * (x0 + x1)
                ym = 0.5 * (y0 + y1)
                cz_m = 0.5 * (z_front + z_back)
                add_prism_pillar(
                    mb_stone,
                    xm, ym, cz_m,
                    xm + side * 0.24, ym + 0.32, cz_m - 0.08,
                    0.12, 0.12, 0.03, 0.03,
                    n_sides=4, rot_deg=45.0,
                    c_bot=c_lo, c_top=c_hi,
                )

    if fill_tympanum:
        z_tym = z_front + 0.14
        add_oriented_tri(
            mb_stone,
            (cx - half_w + thickness * 0.8, y_base - thickness * 0.5, z_tym),
            (cx + half_w - thickness * 0.8, y_base - thickness * 0.5, z_tym),
            (cx, y_apex - thickness * 0.9, z_tym),
            (0.0, 0.0), (2.0, 0.0), (1.0, 2.0),
            c_in, c_in, c_hi,
            (0.0, 0.0, -1.0),
        )
        # Back face of tympanum
        add_oriented_tri(
            mb_stone,
            (cx + half_w - thickness * 0.8, y_base - thickness * 0.5, z_back),
            (cx - half_w + thickness * 0.8, y_base - thickness * 0.5, z_back),
            (cx, y_apex - thickness * 0.9, z_back),
            (0.0, 0.0), (2.0, 0.0), (1.0, 2.0),
            c_in, c_in, c_hi,
            (0.0, 0.0, 1.0),
        )

    if with_finial:
        cz_f = 0.5 * (z_front + z_back)
        add_pinnacle(
            mb_stone, mb_cartilage,
            cx, y_apex - 0.15, cz_f,
            base_w=thickness * 1.1,
            total_h=(y_apex - y_base) * 0.36 + 0.9,
            n_sides=4,
            with_crockets=True,
            with_finial=True,
        )


def add_pinnacle(
    mb_stone,
    mb_cartilage,
    cx, y0, cz,
    base_w=0.75,
    total_h=3.6,
    n_sides=4,
    with_crockets=True,
    with_finial=True,
):
    """Builds an ornate Gothic Pinnacle with shaft, miniature gablets, crocketed spirelet, and bone cross finial."""
    c_bot = stone_col(0.70)
    c_mid = stone_col(0.84)
    c_top = stone_col(0.96)

    shaft_h = total_h * 0.38
    spire_h = total_h * 0.48
    finial_h = total_h * 0.14

    y_shaft = y0 + shaft_h
    y_spire = y_shaft + spire_h
    r_base = base_w * 0.5

    # 1. Base plinth & molded shaft
    add_prism_pillar(
        mb_stone,
        cx, y0, cz,
        cx, y0 + shaft_h * 0.25, cz,
        r_base * 1.12, r_base * 1.12,
        r_base * 0.95, r_base * 0.95,
        n_sides=n_sides, rot_deg=45.0 if n_sides == 4 else 22.5,
        c_bot=c_bot, c_top=c_mid,
    )
    add_prism_pillar(
        mb_stone,
        cx, y0 + shaft_h * 0.25, cz,
        cx, y_shaft, cz,
        r_base * 0.88, r_base * 0.88,
        r_base * 0.84, r_base * 0.84,
        n_sides=n_sides, rot_deg=45.0 if n_sides == 4 else 22.5,
        c_bot=c_bot, c_top=c_mid,
    )
    # Collar cap at top of shaft
    add_prism_pillar(
        mb_stone,
        cx, y_shaft - 0.12, cz,
        cx, y_shaft + 0.12, cz,
        r_base * 1.06, r_base * 1.06,
        r_base * 0.92, r_base * 0.92,
        n_sides=n_sides, rot_deg=45.0 if n_sides == 4 else 22.5,
        c_bot=c_mid, c_top=c_top,
    )

    # 2. Tapering spirelet pyramid
    add_prism_pillar(
        mb_stone,
        cx, y_shaft + 0.10, cz,
        cx, y_spire, cz,
        r_base * 0.86, r_base * 0.86,
        r_base * 0.10, r_base * 0.10,
        n_sides=max(4, n_sides), rot_deg=45.0 if n_sides == 4 else 22.5,
        c_bot=c_mid, c_top=c_top,
    )

    # 3. Crocket buds along 4 ridges
    if with_crockets and spire_h >= 1.0:
        n_cr = 3 if spire_h < 2.5 else 4
        for k in range(1, n_cr + 1):
            f = k / float(n_cr + 1)
            yk = y_shaft + 0.10 + spire_h * f
            rk = r_base * 0.86 * (1.0 - f * 0.85)
            for ang_deg in (0.0, 90.0, 180.0, 270.0):
                ar = math.radians(ang_deg)
                dx, dz = math.cos(ar), math.sin(ar)
                add_prism_pillar(
                    mb_stone,
                    cx + dx * rk, yk, cz + dz * rk,
                    cx + dx * (rk + r_base * 0.25), yk + 0.20, cz + dz * (rk + r_base * 0.25),
                    r_base * 0.13, r_base * 0.13,
                    r_base * 0.03, r_base * 0.03,
                    n_sides=4, rot_deg=45.0,
                    c_bot=c_mid, c_top=c_top,
                )

    # 4. Cartilage / Bone Cross Finial
    if with_finial and mb_cartilage is not None:
        cb_lo = cartilage_col(0.80)
        cb_hi = cartilage_col(1.00)
        add_prism_pillar(
            mb_cartilage,
            cx, y_spire - 0.05, cz,
            cx, y_spire + finial_h, cz,
            r_base * 0.14, r_base * 0.14,
            0.015, 0.015,
            n_sides=4, rot_deg=45.0,
            c_bot=cb_lo, c_top=cb_hi,
        )
        # Crossbars
        yc = y_spire + finial_h * 0.55
        cw = max(0.24, r_base * 0.65)
        add_box(mb_cartilage, cx, yc, cz, cw, 0.08, 0.08, cb_lo, cb_hi)


def add_gothic_window(
    mb_stone,
    mb_glass,
    mb_cartilage,
    cx,
    y_sill,
    y_spring,
    y_apex,
    z_wall,
    half_w,
    depth=0.75,
    n_lancets=2,
    n_transoms=1,
    with_gablet=True,
    glass_tint=(0.85, 0.32, 0.45),
):
    """
    Builds a deeply recessed Gothic Pointed Lancet Window complete with:
    - Sloped window sill
    - 3 stepped splayed jambs & pointed archivolts
    - Vertical stone mullions & sub-lancet pointed arches
    - Quatrefoil / rosette head tracery ring
    - Glowing stained-glass lancet panes & head pane
    - Optional crocketed triangular gablet above the arch
    """
    c_s_dark = stone_col(0.62)
    c_s_mid = stone_col(0.80)
    c_s_bright = stone_col(0.95)

    # 1. Sloped stone window sill
    add_sloped_weathering(
        mb_stone,
        cx, y_sill - 0.42, y_sill + 0.05,
        z_wall - 0.35, z_wall + depth,
        half_w * 2.28, half_w * 2.08,
        c_s_dark, c_s_bright,
    )

    # 2. Stepped Jamb Colonnettes & Pointed Archivolts (3 concentric steps)
    apex_h = y_apex - y_spring
    for step in range(3):
        f = step / 2.0
        z_f = (z_wall - 0.24) + step * (depth * 0.36)
        z_b = z_f + depth * 0.38
        w_in = half_w * (1.02 - step * 0.09)
        w_out = w_in + 0.22
        h_in = apex_h * (1.02 - step * 0.08)
        h_out = h_in + 0.28

        # Left and right jamb colonnettes
        for side in (-1.0, 1.0):
            jx = cx + side * (w_in + 0.09)
            jz = 0.5 * (z_f + z_b)
            add_prism_pillar(
                mb_stone,
                jx, y_sill, jz,
                jx, y_spring, jz,
                0.12, 0.14, 0.11, 0.13,
                n_sides=6, rot_deg=30.0,
                c_bot=c_s_dark, c_top=c_s_mid,
            )
            # Capital block at springing
            add_box(mb_stone, jx, y_spring, jz, 0.28, 0.24, 0.28, c_s_mid, c_s_bright)

        # Pointed archivolt ring
        add_pointed_arch(
            mb_stone,
            cx, y_spring, z_f, z_b,
            w_in, w_out, h_in, h_out,
            n_segs=14,
            c_inner=c_s_dark if step == 2 else c_s_mid,
            c_outer=c_s_bright,
        )

    # 3. Inner Stained Glass Backing (Rectangular lower + Pointed arch head)
    z_glass = z_wall + depth * 0.72
    w_gl = half_w * 0.86
    h_gl = apex_h * 0.86
    cg_bot = _rgba(glass_tint[0] * 0.55, glass_tint[1] * 0.50, glass_tint[2] * 0.65)
    cg_mid = _rgba(glass_tint[0] * 0.92, glass_tint[1] * 0.85, glass_tint[2] * 0.95)
    cg_top = _rgba(min(1.0, glass_tint[0] * 1.15), min(1.0, glass_tint[1] * 1.10), min(1.0, glass_tint[2] * 1.15))

    # Lower glass rectangle subdivided vertically for vertex glow gradient
    y_mid_gl = 0.5 * (y_sill + y_spring)
    add_oriented_quad(
        mb_glass,
        (cx - w_gl, y_sill, z_glass), (cx + w_gl, y_sill, z_glass),
        (cx + w_gl, y_mid_gl, z_glass), (cx - w_gl, y_mid_gl, z_glass),
        (0.0, 0.0), (1.0, 0.0), (1.0, 0.5), (0.0, 0.5),
        cg_bot, cg_bot, cg_mid, cg_mid,
        (0.0, 0.0, -1.0),
    )
    add_oriented_quad(
        mb_glass,
        (cx - w_gl, y_mid_gl, z_glass), (cx + w_gl, y_mid_gl, z_glass),
        (cx + w_gl, y_spring, z_glass), (cx - w_gl, y_spring, z_glass),
        (0.0, 0.5), (1.0, 0.5), (1.0, 1.0), (0.0, 1.0),
        cg_mid, cg_mid, cg_top, cg_top,
        (0.0, 0.0, -1.0),
    )
    # Pointed arch glass head fan
    n_g_segs = 12
    p_center_gl = (cx, y_spring + h_gl * 0.35, z_glass)
    for i in range(n_g_segs):
        t0 = i / float(n_g_segs)
        t1 = (i + 1) / float(n_g_segs)
        gx0, gy0, _, _ = ogival_arch_point(cx, y_spring, w_gl, h_gl, t0)
        gx1, gy1, _, _ = ogival_arch_point(cx, y_spring, w_gl, h_gl, t1)
        add_oriented_tri(
            mb_glass,
            p_center_gl, (gx0, gy0, z_glass), (gx1, gy1, z_glass),
            (0.5, 0.5), (t0, 1.0), (t1, 1.0),
            cg_top, cg_mid, cg_mid,
            (0.0, 0.0, -1.0),
        )
    # Fill lower left & right corners of arch head
    add_oriented_tri(
        mb_glass,
        (cx - w_gl, y_spring, z_glass), p_center_gl, (cx + w_gl, y_spring, z_glass),
        (0.0, 0.5), (0.5, 0.5), (1.0, 0.5),
        cg_mid, cg_top, cg_mid,
        (0.0, 0.0, -1.0),
    )

    # 4. Stone Mullions, Sub-Lancet Arches & Quatrefoil Tracery in front of glass
    z_tr_f = z_glass - 0.18
    z_tr_b = z_glass + 0.02
    sub_w = w_gl / float(n_lancets)
    for k in range(1, n_lancets):
        mx = (cx - w_gl) + k * (2.0 * sub_w)
        add_box(
            mb_stone,
            mx, 0.5 * (y_sill + y_spring), 0.5 * (z_tr_f + z_tr_b),
            0.14, y_spring - y_sill, 0.20,
            c_s_dark, c_s_bright,
        )

    # Horizontal stone transoms with miniature pointed cusps
    for tr in range(1, n_transoms + 1):
        yt = y_sill + (y_spring - y_sill) * (tr / float(n_transoms + 1))
        add_box(
            mb_stone,
            cx, yt, 0.5 * (z_tr_f + z_tr_b),
            w_gl * 1.96, 0.14, 0.18,
            c_s_mid, c_s_bright,
        )

    # Sub-lancet arches at y_spring
    for k in range(n_lancets):
        scx = (cx - w_gl) + (k + 0.5) * (2.0 * sub_w)
        add_pointed_arch(
            mb_stone,
            scx, y_spring - 0.15, z_tr_f, z_tr_b,
            sub_w * 0.78, sub_w * 0.98,
            h_gl * 0.42, h_gl * 0.54,
            n_segs=8,
            c_inner=c_s_mid, c_outer=c_s_bright,
        )

    # Quatrefoil / Oculus ring in upper arch head
    r_oc = min(w_gl * 0.42, h_gl * 0.28)
    y_oc = y_spring + h_gl * 0.62
    n_oc = 12
    for i in range(n_oc):
        a0 = 2.0 * math.pi * i / float(n_oc)
        a1 = 2.0 * math.pi * (i + 1) / float(n_oc)
        # Quatrefoil cusp modulation
        c_mod0 = 1.0 - 0.18 * abs(math.sin(2.0 * a0))
        c_mod1 = 1.0 - 0.18 * abs(math.sin(2.0 * a1))
        r_in0, r_in1 = r_oc * 0.68 * c_mod0, r_oc * 0.68 * c_mod1
        r_out = r_oc * 1.08
        ix0, iy0 = cx + r_in0 * math.cos(a0), y_oc + r_in0 * math.sin(a0)
        ix1, iy1 = cx + r_in1 * math.cos(a1), y_oc + r_in1 * math.sin(a1)
        ox0, oy0 = cx + r_out * math.cos(a0), y_oc + r_out * math.sin(a0)
        ox1, oy1 = cx + r_out * math.cos(a1), y_oc + r_out * math.sin(a1)
        add_oriented_quad(
            mb_stone,
            (ix0, iy0, z_tr_f), (ox0, oy0, z_tr_f), (ox1, oy1, z_tr_f), (ix1, iy1, z_tr_f),
            (0, 0), (0.3, 0), (0.3, 0.3), (0, 0.3),
            c_s_mid, c_s_bright, c_s_bright, c_s_mid,
            (0.0, 0.0, -1.0),
        )

    # 5. Optional Crocketed Triangular Gablet above the window
    if with_gablet:
        add_gablet(
            mb_stone, mb_cartilage,
            cx,
            y_base=y_spring + apex_h * 0.35,
            y_apex=y_apex + apex_h * 0.62,
            z_front=z_wall - 0.32,
            z_back=z_wall + 0.02,
            half_w=half_w * 1.18,
            thickness=0.26,
            n_crockets=4,
            fill_tympanum=False,
            with_finial=True,
        )


def add_hooded_statue_in_niche(
    mb_stone,
    mb_cartilage,
    cx, y_pedestal, cz,
    scale=1.0,
    facing_dir=(0.0, 0.0, -1.0),
):
    """
    Builds a gothic statuary niche assembly:
    - Corbelled stone pedestal below
    - Stylized Hooded Gothic Bone-Saint Statue (robed body, cowled skull head, skeletal arms & staff)
    - Ornate miniature vaulted canopy with spirelet above
    """
    c_dark = stone_col(0.62)
    c_mid = stone_col(0.82)
    c_hi = stone_col(0.96)
    c_bone = cartilage_col(0.92)

    # 1. Corbelled pedestal base
    add_prism_pillar(
        mb_stone,
        cx, y_pedestal - 0.65 * scale, cz,
        cx, y_pedestal, cz,
        0.10 * scale, 0.10 * scale,
        0.34 * scale, 0.34 * scale,
        n_sides=6, rot_deg=30.0,
        c_bot=c_dark, c_top=c_hi,
    )

    # 2. Statue Robed Torso & Pleated Skirt
    h_statue = 1.85 * scale
    add_prism_pillar(
        mb_stone,
        cx, y_pedestal, cz,
        cx, y_pedestal + h_statue * 0.68, cz,
        0.26 * scale, 0.22 * scale,
        0.18 * scale, 0.16 * scale,
        n_sides=7, rot_deg=15.0,
        c_bot=c_dark, c_top=c_mid,
    )
    # Shoulder mantle & cowl
    add_prism_pillar(
        mb_stone,
        cx, y_pedestal + h_statue * 0.62, cz,
        cx, y_pedestal + h_statue * 0.84, cz,
        0.23 * scale, 0.20 * scale,
        0.15 * scale, 0.15 * scale,
        n_sides=6, rot_deg=0.0,
        c_bot=c_mid, c_top=c_hi,
    )
    # Hooded cowled skull head
    fx, _, fz = facing_dir
    add_prism_pillar(
        mb_stone,
        cx, y_pedestal + h_statue * 0.82, cz,
        cx + fx * 0.04 * scale, y_pedestal + h_statue * 1.02, cz + fz * 0.04 * scale,
        0.14 * scale, 0.15 * scale,
        0.04 * scale, 0.05 * scale,
        n_sides=6, rot_deg=30.0,
        c_bot=c_mid, c_top=c_hi,
    )
    # Inner skull face & vertical bone staff in front of torso
    if mb_cartilage is not None:
        add_prism_pillar(
            mb_cartilage,
            cx + fx * 0.09 * scale, y_pedestal + h_statue * 0.83, cz + fz * 0.09 * scale,
            cx + fx * 0.09 * scale, y_pedestal + h_statue * 0.95, cz + fz * 0.09 * scale,
            0.07 * scale, 0.07 * scale,
            0.05 * scale, 0.05 * scale,
            n_sides=5,
            c_bot=c_bone, c_top=c_bone,
        )
        # Staff / sword held vertically
        add_prism_pillar(
            mb_cartilage,
            cx + fx * 0.18 * scale, y_pedestal + 0.05 * scale, cz + fz * 0.18 * scale,
            cx + fx * 0.18 * scale, y_pedestal + h_statue * 1.08, cz + fz * 0.18 * scale,
            0.025 * scale, 0.025 * scale,
            0.020 * scale, 0.020 * scale,
            n_sides=4,
            c_bot=c_bone, c_top=c_bone,
        )

    # 3. Miniature Gothic Baldachin Canopy above statue
    y_can = y_pedestal + h_statue + 0.22 * scale
    add_pinnacle(
        mb_stone, mb_cartilage,
        cx, y_can, cz,
        base_w=0.62 * scale,
        total_h=1.45 * scale,
        n_sides=6,
        with_crockets=True,
        with_finial=True,
    )


def add_gargoyle(mb_stone, mb_cartilage, x0, y0, z0, dir_x, dir_z, scale=1.0):
    """Adds a projecting Gothic Gargoyle chimera on a corbel bracket with wings, snout, and fangs."""
    dlen = max(1e-5, math.hypot(dir_x, dir_z))
    dx, dz = dir_x / dlen, dir_z / dlen
    px, pz = -dz, dx  # perpendicular axis for wings

    c_lo = stone_col(0.65)
    c_hi = stone_col(0.92)
    c_bone = cartilage_col(0.94)

    # Corbel bracket under gargoyle
    add_prism_pillar(
        mb_stone,
        x0 + dx * 0.35 * scale, y0 - 0.55 * scale, z0 + dz * 0.35 * scale,
        x0 + dx * 0.75 * scale, y0, z0 + dz * 0.75 * scale,
        0.16 * scale, 0.16 * scale,
        0.28 * scale, 0.28 * scale,
        n_sides=4, rot_deg=45.0,
        c_bot=c_lo, c_top=c_hi,
    )
    # Hunched torso stretching outward
    x_chest = x0 + dx * 1.25 * scale
    y_chest = y0 + 0.38 * scale
    z_chest = z0 + dz * 1.25 * scale
    add_prism_pillar(
        mb_stone,
        x0 + dx * 0.25 * scale, y0 + 0.05 * scale, z0 + dz * 0.25 * scale,
        x_chest, y_chest, z_chest,
        0.28 * scale, 0.28 * scale,
        0.20 * scale, 0.20 * scale,
        n_sides=6,
        c_bot=c_lo, c_top=c_hi,
    )
    # Snout / skull head
    x_head = x0 + dx * 1.78 * scale
    y_head = y0 + 0.28 * scale
    z_head = z0 + dz * 1.78 * scale
    add_prism_pillar(
        mb_stone,
        x_chest, y_chest, z_chest,
        x_head, y_head, z_head,
        0.18 * scale, 0.18 * scale,
        0.08 * scale, 0.08 * scale,
        n_sides=5,
        c_bot=c_hi, c_top=c_lo,
    )
    # Bat-like stone wings on left and right of torso
    for w_side in (-1.0, 1.0):
        wx = x_chest + px * w_side * 0.85 * scale - dx * 0.25 * scale
        wy = y_chest + 0.65 * scale
        wz = z_chest + pz * w_side * 0.85 * scale - dz * 0.25 * scale
        add_oriented_tri(
            mb_stone,
            (x0 + dx * 0.35 * scale, y0 + 0.22 * scale, z0 + dz * 0.35 * scale),
            (x_chest, y_chest + 0.12 * scale, z_chest),
            (wx, wy, wz),
            (0, 0), (1, 0), (0.5, 1),
            c_lo, c_hi, c_hi,
            (px * w_side, 0.5, -0.3),
        )
        add_oriented_tri(
            mb_stone,
            (x_chest, y_chest + 0.12 * scale, z_chest),
            (x0 + dx * 0.35 * scale, y0 + 0.22 * scale, z0 + dz * 0.35 * scale),
            (wx, wy, wz),
            (1, 0), (0, 0), (0.5, 1),
            c_hi, c_lo, c_hi,
            (-px * w_side, 0.5, 0.3),
        )
    # Horns in cartilage
    if mb_cartilage is not None:
        for h_side in (-1.0, 1.0):
            add_prism_pillar(
                mb_cartilage,
                x_chest + px * h_side * 0.10 * scale, y_chest + 0.12 * scale, z_chest + pz * h_side * 0.10 * scale,
                x_chest + px * h_side * 0.28 * scale - dx * 0.25 * scale, y_chest + 0.58 * scale, z_chest + pz * h_side * 0.28 * scale - dz * 0.25 * scale,
                0.045 * scale, 0.045 * scale, 0.01 * scale, 0.01 * scale,
                n_sides=4,
                c_bot=c_bone, c_top=c_bone,
            )


def add_tube_along_curve(
    mb,
    pts,
    radii,
    n_sides=8,
    c_start=None,
    c_end=None,
    uv_scale=0.35,
    pulse_amp=0.08,
    pulse_freq=4.0,
    cap_ends=True,
):
    """Sweeps a 3D organic tubular mesh along a 3D polyline using parallel-transport frames."""
    if c_start is None:
        c_start = organ_col(0.62, 0.0)
    if c_end is None:
        c_end = organ_col(0.92, 1.0)

    pts_np = [np.asarray(p, dtype=np.float32) for p in pts]
    n_pts = len(pts_np)
    if n_pts < 2:
        return

    # Compute tangents
    tangents = []
    for i in range(n_pts):
        if i == 0:
            t = pts_np[1] - pts_np[0]
        elif i == n_pts - 1:
            t = pts_np[-1] - pts_np[-2]
        else:
            t = pts_np[i + 1] - pts_np[i - 1]
        t_len = float(np.linalg.norm(t))
        tangents.append(t / max(1e-6, t_len))

    # Initial normal frame
    t0 = tangents[0]
    up = np.array([0.0, 0.0, -1.0], dtype=np.float32)
    if abs(float(np.dot(t0, up))) > 0.9:
        up = np.array([1.0, 0.0, 0.0], dtype=np.float32)
    n0 = np.cross(t0, up)
    n0 = n0 / max(1e-6, float(np.linalg.norm(n0)))
    b0 = np.cross(t0, n0)

    rings = []
    cols = []
    v_coords = []
    arc_len = 0.0

    cur_n, cur_b = n0, b0
    for i in range(n_pts):
        if i > 0:
            arc_len += float(np.linalg.norm(pts_np[i] - pts_np[i - 1]))
            ti = tangents[i]
            # Project cur_n perpendicular to ti
            cur_n = cur_n - ti * float(np.dot(cur_n, ti))
            n_len = float(np.linalg.norm(cur_n))
            if n_len < 1e-5:
                cur_n = np.cross(ti, cur_b)
                n_len = max(1e-6, float(np.linalg.norm(cur_n)))
            cur_n = cur_n / n_len
            cur_b = np.cross(ti, cur_n)

        f = i / float(n_pts - 1)
        col = tuple(c_start[k] * (1.0 - f) + c_end[k] * f for k in range(4))
        cols.append(col)
        v_coords.append(arc_len * uv_scale)

        r_base = radii[i] * (1.0 + pulse_amp * math.sin(f * math.pi * pulse_freq))
        ring = []
        for s in range(n_sides):
            ang = 2.0 * math.pi * s / float(n_sides)
            # Slight vein-ridge modulation around circumference
            r_s = r_base * (1.0 + 0.08 * math.cos(3.0 * ang + f * 6.0))
            offset = cur_n * (r_s * math.cos(ang)) + cur_b * (r_s * math.sin(ang))
            ring.append((pts_np[i] + offset).tolist())
        rings.append(ring)

    for i in range(n_pts - 1):
        r0 = rings[i]
        r1 = rings[i + 1]
        c0 = cols[i]
        c1 = cols[i + 1]
        v0 = v_coords[i]
        v1 = v_coords[i + 1]
        mid_center = 0.5 * (pts_np[i] + pts_np[i + 1])
        for s in range(n_sides):
            ns = (s + 1) % n_sides
            p00, p10, p11, p01 = r0[s], r0[ns], r1[ns], r1[s]
            q_mid = 0.25 * (np.asarray(p00) + np.asarray(p10) + np.asarray(p11) + np.asarray(p01))
            out_dir = (q_mid - mid_center).tolist()
            u0 = s / float(n_sides)
            u1 = (s + 1) / float(n_sides)
            add_oriented_quad(
                mb,
                p00, p10, p11, p01,
                (u0, v0), (u1, v0), (u1, v1), (u0, v1),
                c0, c0, c1, c1,
                out_dir,
            )

    if cap_ends:
        # Start cap
        if radii[0] > 0.02:
            pc0 = pts_np[0].tolist()
            out0 = (-tangents[0]).tolist()
            for s in range(n_sides):
                ns = (s + 1) % n_sides
                add_oriented_tri(
                    mb, pc0, rings[0][ns], rings[0][s],
                    (0.5, 0.5), (1.0, 0.0), (0.0, 0.0),
                    cols[0], cols[0], cols[0], out0,
                )
        # End cap
        if radii[-1] > 0.02:
            pc1 = pts_np[-1].tolist()
            out1 = tangents[-1].tolist()
            for s in range(n_sides):
                ns = (s + 1) % n_sides
                add_oriented_tri(
                    mb, pc1, rings[-1][s], rings[-1][ns],
                    (0.5, 0.5), (0.0, 0.0), (1.0, 0.0),
                    cols[-1], cols[-1], cols[-1], out1,
                )


# ==============================================================================
# SECTION 1: ROCKY CASTLE ISLAND BASE, 14-STEP STAIRCASE & 4 BONE STREETLAMPS
# ==============================================================================

def _build_section1_island_and_staircase(
    mb_rock,
    mb_stone,
    mb_pipes,
    mb_glass,
    mb_cartilage,
    anchors,
):
    # 1. Jagged Calcified Cliff Foundation & Crags (Mat_DigestedCalcified)
    # Main island shelf under cathedral and flanking stairs (leaving stair channel clear)
    c_r_bot = rock_col(0.45)
    c_r_mid = rock_col(0.72)
    c_r_top = rock_col(0.94)

    # Left & Right rocky foundation ledges under the towers
    for side in (-1.0, 1.0):
        add_box(
            mb_rock,
            side * 12.2, -1.25, -0.5,
            13.2, 2.5, 11.0,
            c_r_bot, c_r_mid,
        )

    # Jagged crag clusters around the perimeter & front corners (Reference 2 silhouette)
    crag_specs = [
        # (cx, cz, rx_bot, rz_bot, h_top, tilt_x, tilt_z, n_sides)
        (-11.2, -18.2, 3.2, 2.8, 1.8, -0.6, -0.5, 6),
        ( 11.2, -18.2, 3.2, 2.8, 1.8,  0.6, -0.5, 6),
        (-14.8, -15.5, 3.8, 3.4, 3.4, -0.9, -0.4, 7),
        ( 14.8, -15.5, 3.8, 3.4, 3.4,  0.9, -0.4, 7),
        (-18.5, -12.0, 4.2, 3.6, 5.2, -1.2, -0.3, 6),
        ( 18.5, -12.0, 4.2, 3.6, 5.2,  1.2, -0.3, 6),
        (-21.5,  -7.5, 4.6, 4.0, 6.8, -1.5,  0.0, 7),
        ( 21.5,  -7.5, 4.6, 4.0, 6.8,  1.5,  0.0, 7),
        (-16.0,  -8.2, 3.5, 3.0, 2.6, -0.5, -0.4, 6),
        ( 16.0,  -8.2, 3.5, 3.0, 2.6,  0.5, -0.4, 6),
        (-20.2,  -2.5, 4.4, 4.2, 5.8, -1.1,  0.3, 6),
        ( 20.2,  -2.5, 4.4, 4.2, 5.8,  1.1,  0.3, 6),
        (-22.8,   3.0, 4.8, 4.4, 7.5, -1.4,  0.5, 7),
        ( 22.8,   3.0, 4.8, 4.4, 7.5,  1.4,  0.5, 7),
        ( -9.5, -14.2, 2.4, 2.2, 0.6, -0.3, -0.2, 5),
        (  9.5, -14.2, 2.4, 2.2, 0.6,  0.3, -0.2, 5),
        (-12.5, -11.0, 2.8, 2.6, 1.5, -0.4, -0.2, 6),
        ( 12.5, -11.0, 2.8, 2.6, 1.5,  0.4, -0.2, 6),
    ]
    for cx, cz, rxb, rzb, h_top, tx, tz, ns in crag_specs:
        y_base = -2.55
        y_mid = 0.5 * (y_base + h_top)
        # Stepped two-stage crag spire
        add_prism_pillar(
            mb_rock,
            cx, y_base, cz,
            cx + tx * 0.45, y_mid, cz + tz * 0.45,
            rxb, rzb, rxb * 0.68, rzb * 0.68,
            n_sides=ns, rot_deg=18.0,
            c_bot=c_r_bot, c_top=c_r_mid,
        )
        add_prism_pillar(
            mb_rock,
            cx + tx * 0.45, y_mid, cz + tz * 0.45,
            cx + tx, h_top, cz + tz,
            rxb * 0.68, rzb * 0.68, rxb * 0.12, rzb * 0.12,
            n_sides=ns, rot_deg=33.0,
            c_bot=c_r_mid, c_top=c_r_top,
        )
        # Secondary jagged shard next to main crag
        s_sign = -1.0 if cx < 0 else 1.0
        add_prism_pillar(
            mb_rock,
            cx + s_sign * rxb * 0.55, y_base, cz - 0.8,
            cx + s_sign * (rxb * 0.85 + 0.5), h_top * 0.65, cz - 1.2,
            rxb * 0.48, rzb * 0.48, 0.08, 0.08,
            n_sides=5, rot_deg=12.0,
            c_bot=c_r_bot, c_top=c_r_top,
        )

    # 2. 14-Step Grand Ceremonial Staircase (Z = -18.0 to -4.0, Y = -2.1 to 0.0)
    # Width 16.0m at bottom narrowing to 12.0m at landing Z = -4.0
    n_steps = 14
    z_start = -18.0
    z_end = -4.0
    y_start = -2.1
    y_end = 0.0
    dz_step = (z_end - z_start) / float(n_steps)  # 1.0m per step
    dy_step = (y_end - y_start) / float(n_steps)  # 0.15m per step

    c_st_bot = stone_col(0.64)
    c_st_mid = stone_col(0.80)
    c_st_top = stone_col(0.93)

    for i in range(n_steps):
        f0 = i / float(n_steps)
        f1 = (i + 1) / float(n_steps)
        z0 = z_start + i * dz_step
        z1 = z0 + dz_step + 0.06  # overlap slightly into next step
        y_bot = -2.45
        y_step_bot = y_start + i * dy_step
        y_step_top = y_start + (i + 1) * dy_step

        hw0 = 0.5 * (16.0 * (1.0 - f0) + 12.0 * f0)
        hw1 = 0.5 * (16.0 * (1.0 - f1) + 12.0 * f1)

        # Riser face (-Z)
        add_oriented_quad(
            mb_stone,
            (-hw0, y_step_bot, z0), (hw0, y_step_bot, z0),
            (hw0, y_step_top, z0 - 0.03), (-hw0, y_step_top, z0 - 0.03),
            (0.0, 0.0), (hw0 * 0.5, 0.0), (hw0 * 0.5, 0.2), (0.0, 0.2),
            c_st_bot, c_st_bot, c_st_top, c_st_top,
            (0.0, 0.0, -1.0),
        )
        # Tread face (+Y)
        add_oriented_quad(
            mb_stone,
            (-hw0, y_step_top, z0 - 0.03), (hw0, y_step_top, z0 - 0.03),
            (hw1, y_step_top, z1), (-hw1, y_step_top, z1),
            (0.0, 0.0), (hw0 * 0.5, 0.0), (hw1 * 0.5, 0.4), (0.0, 0.4),
            c_st_top, c_st_top, c_st_mid, c_st_mid,
            (0.0, 1.0, 0.0),
        )
        # Solid under-step foundation side walls
        add_oriented_quad(
            mb_stone,
            (-hw1, y_bot, z1), (-hw0, y_bot, z0),
            (-hw0, y_step_top, z0), (-hw1, y_step_top, z1),
            (0.0, 0.0), (0.4, 0.0), (0.4, 1.0), (0.0, 1.0),
            c_st_bot, c_st_bot, c_st_mid, c_st_mid,
            (-1.0, 0.0, -0.2),
        )
        add_oriented_quad(
            mb_stone,
            (hw0, y_bot, z0), (hw1, y_bot, z1),
            (hw1, y_step_top, z1), (hw0, y_step_top, z0),
            (0.0, 0.0), (0.4, 0.0), (0.4, 1.0), (0.0, 1.0),
            c_st_bot, c_st_bot, c_st_mid, c_st_mid,
            (1.0, 0.0, -0.2),
        )

    # Portal Threshold & Side Tower Plinth Aprons at Y = 0.0
    # Center walkthrough floor slab (Y = -0.45..0.0, Z = -4.2..+1.5, X = -6.5..+6.5)
    add_box(mb_stone, 0.0, -0.22, -1.35, 13.4, 0.44, 5.8, c_st_bot, c_st_top)
    # Left & Right Tower Plinths
    for side in (-1.0, 1.0):
        add_box(mb_stone, side * 12.2, -0.22, -0.8, 12.2, 0.44, 8.8, c_st_bot, c_st_top)

    anchors["Exterior_Portal_Threshold"] = [0.0, 0.0, 0.0]
    anchors["Exterior_Stair_Bottom"] = [0.0, -2.1, -18.0]

    # 3. Stepped Stone Cheek-Walls / Balustrades & 4 Gothic Bone Streetlamps
    lamp_positions = []
    for side in (-1.0, 1.0):
        # 7 stepped balustrade bays along each side of the staircase
        n_bays = 7
        for b in range(n_bays):
            fb0 = b / float(n_bays)
            fb1 = (b + 1) / float(n_bays)
            zb0 = z_start + fb0 * (z_end - z_start)
            zb1 = z_start + fb1 * (z_end - z_start)
            yb = y_start + fb1 * (y_end - y_start) + 0.95
            xb0 = side * (0.5 * (16.0 * (1.0 - fb0) + 12.0 * fb0) + 0.55)
            xb1 = side * (0.5 * (16.0 * (1.0 - fb1) + 12.0 * fb1) + 0.55)
            xm = 0.5 * (xb0 + xb1)
            zm = 0.5 * (zb0 + zb1)
            h_wall = yb - (-2.35)
            add_box(
                mb_stone,
                xm, 0.5 * (yb - 2.35), zm,
                0.92, h_wall, abs(zb1 - zb0) + 0.12,
                c_st_bot, c_st_mid,
            )
            # Molded coping slab on top of balustrade segment
            add_box(
                mb_stone,
                xm, yb + 0.10, zm,
                1.12, 0.20, abs(zb1 - zb0) + 0.24,
                c_st_mid, c_st_top,
            )
            # Recessed gothic blind lancet relief on outer & inner side of balustrade
            add_box(
                mb_stone,
                xm, yb - 0.45, zm,
                1.02, 0.65, abs(zb1 - zb0) * 0.55,
                c_st_bot, c_st_top,
            )

        # Two grand lamp pedestals per side (at bottom b=0 and upper-middle b=4)
        for p_idx, f_ped in enumerate((0.06, 0.68)):
            zp = z_start + f_ped * (z_end - z_start)
            yp = y_start + f_ped * (y_end - y_start) + 1.35
            xp = side * (0.5 * (16.0 * (1.0 - f_ped) + 12.0 * f_ped) + 0.68)
            add_box(mb_stone, xp, 0.5 * (yp - 2.4), zp, 1.45, yp + 2.4, 1.45, c_st_bot, c_st_top)
            add_box(mb_stone, xp, yp + 0.14, zp, 1.65, 0.28, 1.65, c_st_mid, c_st_top)
            lamp_positions.append((xp, yp + 0.28, zp, side, p_idx))

    # Build the 4 Gothic Bone Streetlamps / Candelabras (inspired by Catedral da Sé)
    c_pipe_lo = _rgba(0.32, 0.36, 0.38)
    c_pipe_hi = _rgba(0.68, 0.74, 0.78)
    c_glow_lo = _rgba(0.95, 0.65, 0.28)
    c_glow_hi = _rgba(1.00, 0.94, 0.62)

    for idx_l, (lx, ly, lz, side, p_idx) in enumerate(lamp_positions):
        # Ornate ossified stone base & capital
        add_prism_pillar(
            mb_stone,
            lx, ly, lz,
            lx, ly + 0.85, lz,
            0.48, 0.48, 0.24, 0.24,
            n_sides=8,
            c_bot=c_st_mid, c_top=c_st_top,
        )
        # Tall fluted streetlamp shaft (Mat_OrganPipes)
        h_post = 4.2
        add_prism_pillar(
            mb_pipes,
            lx, ly + 0.80, lz,
            lx, ly + h_post, lz,
            0.18, 0.18, 0.12, 0.12,
            n_sides=8,
            c_bot=c_pipe_lo, c_top=c_pipe_hi,
        )
        # Mid-shaft bone collar ring
        add_prism_pillar(
            mb_cartilage,
            lx, ly + 2.45, lz,
            lx, ly + 2.75, lz,
            0.26, 0.26, 0.22, 0.22,
            n_sides=8,
            c_bot=cartilage_col(0.82), c_top=cartilage_col(0.98),
        )
        # Upper branching candelabra crown at ly + h_post
        y_crown = ly + h_post
        add_prism_pillar(
            mb_stone,
            lx, y_crown - 0.15, lz,
            lx, y_crown + 0.30, lz,
            0.22, 0.22, 0.36, 0.36,
            n_sides=8,
            c_bot=c_st_mid, c_top=c_st_top,
        )

        # Central high lantern globe + 3 branching side globes
        branch_offsets = [
            (0.0, 0.95, 0.0, 0.34),
            (-0.68, 0.42, 0.0, 0.26),
            ( 0.68, 0.42, 0.0, 0.26),
            (0.0, 0.42, -0.68, 0.26),
        ]
        for bx, by, bz, gr in branch_offsets:
            gx, gy, gz = lx + bx, y_crown + by, lz + bz
            # Curved bone arm from crown to globe base
            add_prism_pillar(
                mb_cartilage,
                lx, y_crown + 0.10, lz,
                gx, gy - gr * 0.9, gz,
                0.07, 0.07, 0.05, 0.05,
                n_sides=5,
                c_bot=cartilage_col(0.80), c_top=cartilage_col(0.96),
            )
            # Glowing hexagonal lantern globe (Mat_StainedGlass)
            add_prism_pillar(
                mb_glass,
                gx, gy - gr * 0.85, gz,
                gx, gy, gz,
                gr * 0.55, gr * 0.55, gr, gr,
                n_sides=6,
                c_bot=c_glow_lo, c_top=c_glow_hi,
            )
            add_prism_pillar(
                mb_glass,
                gx, gy, gz,
                gx, gy + gr * 0.95, gz,
                gr, gr, gr * 0.35, gr * 0.35,
                n_sides=6,
                c_bot=c_glow_hi, c_top=c_glow_lo,
            )
            # Pointed bone finial cap on each lantern
            add_prism_pillar(
                mb_cartilage,
                gx, gy + gr * 0.90, gz,
                gx, gy + gr * 1.85, gz,
                gr * 0.45, gr * 0.45, 0.02, 0.02,
                n_sides=6,
                c_bot=cartilage_col(0.85), c_top=cartilage_col(1.0),
            )

        tag = f"Exterior_Streetlamp_{'L' if side < 0 else 'R'}{p_idx + 1}"
        anchors[tag] = [float(lx), float(y_crown + 0.6), float(lz)]


# ==============================================================================
# SECTION 2: 1ST STORY — TRIPLE-BAY FAÇADE, RECESSED OGIVAL PORTAL & BUTTRESSES
# ==============================================================================

def _build_section2_first_story_and_portal(
    mb_bricks,
    mb_stone,
    mb_glass,
    mb_door,
    mb_cartilage,
    anchors,
):
    c_br_lo = brick_col(0.58, 0.3)
    c_br_mid = brick_col(0.76, 0.1)
    c_br_hi = brick_col(0.88, 0.0)

    c_st_dark = stone_col(0.62)
    c_st_mid = stone_col(0.80)
    c_st_hi = stone_col(0.95)
    c_bone = cartilage_col(0.94)

    # ------------------------------------------------------------------
    # A. Main Triple-Bay Façade Walls (Leaving X in [-3.2, +3.2], Y in [0.0, 7.2] CLEAR!)
    # ------------------------------------------------------------------
    # Left & Right Portal Flanking Piers (X = -6.8..-3.35 and +3.35..+6.8, Y = 0.0..11.2, Z = -2.4..+0.8)
    for side in (-1.0, 1.0):
        add_box(
            mb_bricks,
            side * 5.08, 5.6, -0.8,
            3.45, 11.2, 3.2,
            c_br_lo, c_br_mid,
        )
    # Upper Portal Wall above walkthrough & tympanum (X = -3.4..+3.4, Y = 10.2..11.5, Z = -2.4..+0.8)
    add_box(
        mb_bricks,
        0.0, 10.85, -0.8,
        6.8, 1.3, 3.2,
        c_br_mid, c_br_hi,
    )

    # Left & Right Tower Ground-Floor 3D Massing (X in [-17.2, -6.8] and [+6.8, +17.2], Y = 0.0..11.2)
    # Framed around the deeply recessed Lancet Windows at X = ±12.0, Y = 2.2..8.8
    for side in (-1.0, 1.0):
        tcx = side * 12.0
        # Below window (Y = 0.0..2.3)
        add_box(mb_bricks, tcx, 1.15, 0.8, 10.4, 2.3, 6.0, c_br_lo, c_br_mid)
        # Above window (Y = 8.7..11.2)
        add_box(mb_bricks, tcx, 9.95, 0.8, 10.4, 2.5, 6.0, c_br_mid, c_br_hi)
        # Inner pier between portal and window (X = ±6.8..±9.9)
        add_box(mb_bricks, side * 8.35, 5.6, 0.8, 3.1, 11.2, 6.0, c_br_lo, c_br_mid)
        # Outer pier outside window (X = ±14.1..±17.2)
        add_box(mb_bricks, side * 15.65, 5.6, 0.8, 3.1, 11.2, 6.0, c_br_lo, c_br_mid)
        # Recessed window back wall at Z = +0.5
        add_box(mb_bricks, tcx, 5.5, 2.1, 4.6, 6.8, 3.2, c_br_lo, c_br_mid)

        # Ground-Floor Pointed Lancet Window at X = ±12.0, Y = 2.2..8.8, Z = -2.2
        add_gothic_window(
            mb_stone, mb_glass, mb_cartilage,
            cx=tcx,
            y_sill=2.2,
            y_spring=6.5,
            y_apex=8.8,
            z_wall=-2.2,
            half_w=2.05,
            depth=0.85,
            n_lancets=2,
            n_transoms=1,
            with_gablet=True,
            glass_tint=(0.82, 0.25, 0.38),
        )

    # ------------------------------------------------------------------
    # B. 6 Stepped Gothic Buttresses (Contrafortes) at X = ±17.5, ±12.0/±8.8, ±6.8
    # ------------------------------------------------------------------
    # Note: Since the tower lancet windows are centered at X = ±12.0, we place the grand
    # stepped buttresses at X = ±17.5, ±6.8, and twin window-flanking buttresses at X = ±9.4, ±14.6,
    # PLUS a prominent central-bay buttress pilaster at ±12.0 below the sill (Y = 0..2.1) and between stories!
    buttress_x_coords = [-17.5, -14.6, -9.4, -6.8, 6.8, 9.4, 14.6, 17.5]
    for bx in buttress_x_coords:
        is_major = abs(abs(bx) - 17.5) < 0.5 or abs(abs(bx) - 6.8) < 0.5
        bw = 1.45 if is_major else 1.05
        z_front_0 = -4.85 if is_major else -4.25
        z_back_b = -1.8

        # Stage 1 (Y = 0.0 .. 5.2)
        d1 = z_back_b - z_front_0
        add_box(mb_stone, bx, 2.6, 0.5 * (z_front_0 + z_back_b), bw, 5.2, d1, c_st_dark, c_st_mid)
        add_sloped_weathering(mb_stone, bx, 5.2, 5.75, z_front_0 - 0.08, z_back_b, bw * 1.08, bw * 0.92, c_st_mid, c_st_hi)

        # Stage 2 (Y = 5.75 .. 11.2)
        z_front_1 = z_front_0 + 0.55
        d2 = z_back_b - z_front_1
        add_box(mb_stone, bx, 8.48, 0.5 * (z_front_1 + z_back_b), bw * 0.92, 5.45, d2, c_st_mid, c_st_hi)
        add_sloped_weathering(mb_stone, bx, 11.2, 11.75, z_front_1 - 0.08, z_back_b, bw * 0.98, bw * 0.84, c_st_mid, c_st_hi)

        # Sculpted Saint Niche with Miniature Canopy on front face of each major buttress (Y = 2.4..5.2)
        if is_major:
            add_hooded_statue_in_niche(
                mb_stone, mb_cartilage,
                cx=bx,
                y_pedestal=2.2,
                cz=z_front_0 - 0.22,
                scale=0.92,
                facing_dir=(0.0, 0.0, -1.0),
            )

    # Buttress Plinth Pedestals at X = ±12.0 below the ground lancet window sills (Y = 0.0..2.0)
    for side in (-1.0, 1.0):
        add_box(mb_stone, side * 12.0, 1.0, -3.3, 1.6, 2.0, 2.4, c_st_dark, c_st_mid)

    # ------------------------------------------------------------------
    # C. Central Recessed Ogival Portal (X in [-6.5, +6.5], Z = -4.2 to -0.5)
    #    Clear walkable passage guaranteed at X in [-3.2, +3.2], Y in [0.0, 7.2], Z in [-4.5, +1.5]!
    # ------------------------------------------------------------------
    # 1. 6 Concentric Pointed Gothic Archivolts stepping inward from Z = -4.2 to Z = -0.5
    n_archivolts = 6
    for k in range(n_archivolts):
        f = k / float(n_archivolts - 1)
        z_f = -4.25 + f * 3.25
        z_b = z_f + 0.62
        w_in = 6.15 - f * 2.45     # From 6.15m down to 3.70m (leaves [-3.2, +3.2] 100% clear!)
        w_out = w_in + 0.46
        h_in = 5.85 - f * 2.35     # Springing at Y = 7.22 -> apex from 13.07 down to 10.72
        h_out = h_in + 0.52

        add_pointed_arch(
            mb_stone,
            cx=0.0,
            y_spring=7.22,
            z_front=z_f,
            z_back=z_b,
            w_inner=w_in,
            w_outer=w_out,
            h_inner=h_in,
            h_outer=h_out,
            n_segs=20,
            c_inner=stone_col(0.62 + 0.04 * (1.0 - f)),
            c_outer=stone_col(0.88 + 0.08 * (1.0 - f)),
        )

        # 2. Protruding Bone Teeth / Fangs (Mat_Cartilage) along inner & middle archivolts
        if k in (1, 3, 5):
            n_fangs = 14 if k == 5 else 16
            for fg in range(1, n_fangs):
                tf = fg / float(n_fangs)
                fx, fy, nx, ny = ogival_arch_point(0.0, 7.22, w_in + 0.05, h_in + 0.05, tf)
                # Keep strictly above Y = 7.65 so fang tips stay > 7.22
                if fy < 7.65:
                    continue
                fang_len = 0.38 if k == 5 else 0.32
                fz = 0.5 * (z_f + z_b)
                add_prism_pillar(
                    mb_cartilage,
                    fx, fy, fz,
                    fx - nx * fang_len, fy - ny * fang_len - 0.08, fz - 0.06,
                    0.075, 0.075, 0.012, 0.012,
                    n_sides=4, rot_deg=45.0,
                    c_bot=cartilage_col(0.78), c_top=cartilage_col(1.0),
                )

        # 3. 6 Jamb Colonnettes & Statuary Niches on each side (X = ±3.45 .. ±6.15)
        for side in (-1.0, 1.0):
            jx = side * (w_in + 0.16)
            jz = 0.5 * (z_f + z_b)
            # Molded plinth base (Y = 0.0 .. 1.45)
            add_prism_pillar(
                mb_stone,
                jx, 0.0, jz,
                jx, 1.45, jz,
                0.24, 0.26, 0.18, 0.20,
                n_sides=6,
                c_bot=c_st_dark, c_top=c_st_mid,
            )
            # Slender colonnette shaft (Y = 1.45 .. 6.85)
            add_prism_pillar(
                mb_stone,
                jx, 1.45, jz,
                jx, 6.85, jz,
                0.15, 0.16, 0.14, 0.15,
                n_sides=8,
                c_bot=c_st_dark, c_top=c_st_mid,
            )
            # Foliate/bone capital block (Y = 6.85 .. 7.25)
            add_prism_pillar(
                mb_stone,
                jx, 6.85, jz,
                jx, 7.25, jz,
                0.16, 0.16, 0.26, 0.26,
                n_sides=6,
                c_bot=c_st_mid, c_top=c_st_hi,
            )
            # Backing splay jamb wall behind colonnette
            add_box(
                mb_stone,
                jx + side * 0.22, 3.6, jz + 0.18,
                0.44, 7.2, 0.58,
                c_st_dark, c_st_mid,
            )
            # Hooded Gothic Bone-Statue in every jamb niche (6 on left, 6 on right!)
            niche_x = jx - side * 0.12
            niche_z = jz - 0.16
            add_hooded_statue_in_niche(
                mb_stone, mb_cartilage,
                cx=niche_x,
                y_pedestal=2.05 + 0.08 * (k % 2),
                cz=niche_z,
                scale=0.76,
                facing_dir=(-side * 0.55, 0.0, -0.83),
            )

    # 4. Sculpted Tympanum & Lintel above Y = 7.22 to Y = 10.75 at Z = -0.55
    # (Leaves Y = 0.0 to 7.22 completely open for walking through!)
    add_box(mb_stone, 0.0, 7.46, -0.55, 6.8, 0.48, 0.55, c_st_dark, c_st_hi)
    # Miniature blind arcade relief along the lintel beam
    for lk in range(-5, 6):
        add_prism_pillar(
            mb_stone,
            lk * 0.56, 7.24, -0.82,
            lk * 0.56, 7.68, -0.82,
            0.06, 0.06, 0.06, 0.06,
            n_sides=4,
            c_bot=c_st_mid, c_top=c_st_hi,
        )

    # Pointed Tympanum backing slab (Y = 7.65 .. 10.70, Z = -0.50)
    n_tym = 16
    p_tym_c = (0.0, 8.65, -0.50)
    for i in range(n_tym):
        t0 = i / float(n_tym)
        t1 = (i + 1) / float(n_tym)
        tx0, ty0, _, _ = ogival_arch_point(0.0, 7.20, 3.40, 3.55, t0)
        tx1, ty1, _, _ = ogival_arch_point(0.0, 7.20, 3.40, 3.55, t1)
        ty0 = max(7.65, ty0)
        ty1 = max(7.65, ty1)
        add_oriented_tri(
            mb_stone,
            p_tym_c, (tx0, ty0, -0.50), (tx1, ty1, -0.50),
            (0.5, 0.5), (t0, 1.0), (t1, 1.0),
            c_st_mid, c_st_dark, c_st_dark,
            (0.0, 0.0, -1.0),
        )
        # Back side of tympanum (+Z)
        add_oriented_tri(
            mb_stone,
            p_tym_c, (tx1, ty1, -0.32), (tx0, ty0, -0.32),
            (0.5, 0.5), (t1, 1.0), (t0, 1.0),
            c_st_dark, c_st_dark, c_st_dark,
            (0.0, 0.0, 1.0),
        )

    # High-relief central "Last Judgment / Ossified Seraph" sculpture on the Tympanum
    add_hooded_statue_in_niche(
        mb_stone, mb_cartilage,
        cx=0.0, y_pedestal=7.85, cz=-0.68,
        scale=0.95,
        facing_dir=(0.0, 0.0, -1.0),
    )
    # Radial bone ribs around the tympanum seraph
    for r_ang in range(-60, 65, 20):
        ar = math.radians(r_ang)
        sx, sy = math.sin(ar), math.cos(ar)
        add_prism_pillar(
            mb_cartilage,
            sx * 0.45, 8.75 + sy * 0.45, -0.65,
            sx * 1.65, 8.75 + sy * 1.45, -0.72,
            0.05, 0.05, 0.015, 0.015,
            n_sides=4,
            c_bot=c_bone, c_top=c_bone,
        )

    # 5. Monumental Double Gothic Doors (Mat_CathedralDoor) swung open at 75 degrees into +Z
    # Hinged at X = ±3.28, Z = -0.35, swinging into +Z so X stays outside [-3.2, +3.2]!
    c_dr_lo = _rgba(0.36, 0.22, 0.18)
    c_dr_mid = _rgba(0.54, 0.36, 0.28)
    c_dr_hi = _rgba(0.74, 0.52, 0.38)
    door_w = 2.95
    door_h = 7.15
    ang_open = math.radians(75.0)  # 75 degrees open into +Z (x goes from ±3.28 to ±4.04, clear of ±3.2!)
    for side in (-1.0, 1.0):
        hx = side * 3.28
        hz = -0.35
        tx = hx + side * door_w * math.cos(ang_open)
        tz = hz + door_w * math.sin(ang_open)
        nx_d = -side * math.sin(ang_open)
        nz_d = math.cos(ang_open)
        thk = 0.22

        # Subdivide door leaf into 4x6 Gothic coffered panels with raised stiles & rails
        n_cols_d = 3
        n_rows_d = 5
        for ic in range(n_cols_d):
            u0 = ic / float(n_cols_d)
            u1 = (ic + 1) / float(n_cols_d)
            x0 = hx + (tx - hx) * u0
            z0 = hz + (tz - hz) * u0
            x1 = hx + (tx - hx) * u1
            z1 = hz + (tz - hz) * u1
            xm = 0.5 * (x0 + x1)
            zm = 0.5 * (z0 + z1)

            for ir in range(n_rows_d):
                v0 = ir / float(n_rows_d)
                v1 = (ir + 1) / float(n_rows_d)
                y0 = door_h * v0
                y1 = door_h * v1
                ym = 0.5 * (y0 + y1)

                # Inner face of panel (facing portal walkway)
                add_oriented_quad(
                    mb_door,
                    (x0, y0, z0), (x1, y0, z1), (x1, y1, z1), (x0, y1, z0),
                    (u0, v0 * 2.0), (u1, v0 * 2.0), (u1, v1 * 2.0), (u0, v1 * 2.0),
                    c_dr_lo, c_dr_mid, c_dr_hi, c_dr_mid,
                    (nx_d, 0.0, -nz_d),
                )
                # Raised gothic diamond/lancet boss in center of each coffer panel
                bx_in = xm + nx_d * 0.08
                bz_in = zm - nz_d * 0.08
                add_oriented_tri(
                    mb_door,
                    (x0, y0, z0), (x1, y0, z1), (bx_in, ym, bz_in),
                    (0, 0), (1, 0), (0.5, 0.5),
                    c_dr_lo, c_dr_lo, c_dr_hi,
                    (nx_d, -0.2, -nz_d),
                )
                add_oriented_tri(
                    mb_door,
                    (x1, y0, z1), (x1, y1, z1), (bx_in, ym, bz_in),
                    (1, 0), (1, 1), (0.5, 0.5),
                    c_dr_lo, c_dr_mid, c_dr_hi,
                    (nx_d, 0.0, -nz_d),
                )
                add_oriented_tri(
                    mb_door,
                    (x1, y1, z1), (x0, y1, z0), (bx_in, ym, bz_in),
                    (1, 1), (0, 1), (0.5, 0.5),
                    c_dr_mid, c_dr_mid, c_dr_hi,
                    (nx_d, 0.2, -nz_d),
                )
                add_oriented_tri(
                    mb_door,
                    (x0, y1, z0), (x0, y0, z0), (bx_in, ym, bz_in),
                    (0, 1), (0, 0), (0.5, 0.5),
                    c_dr_mid, c_dr_lo, c_dr_hi,
                    (nx_d, 0.0, -nz_d),
                )

        # Back face & thickness rim of door
        p00 = (hx, 0.0, hz)
        p10 = (tx, 0.0, tz)
        p11 = (tx, door_h, tz)
        p01 = (hx, door_h, hz)
        p00_b = (hx + side * thk, 0.0, hz)
        p10_b = (tx + side * thk, 0.0, tz)
        p11_b = (tx + side * thk, door_h, tz)
        p01_b = (hx + side * thk, door_h, hz)
        add_oriented_quad(
            mb_door, p00_b, p10_b, p11_b, p01_b,
            (0, 0), (1, 0), (1, 2), (0, 2),
            c_dr_lo, c_dr_lo, c_dr_hi, c_dr_hi,
            (-nx_d, 0.0, nz_d),
        )
        add_oriented_quad(
            mb_door, p10, p10_b, p11_b, p11,
            (0, 0), (0.2, 0), (0.2, 2), (0, 2),
            c_dr_lo, c_dr_lo, c_dr_hi, c_dr_hi,
            (side * 0.3, 0.0, 1.0),
        )
        # Ornate bone/iron strap hinges across each door
        for yh in (1.2, 3.5, 5.8):
            mx = 0.5 * (hx + tx)
            mz = 0.5 * (hz + tz)
            add_prism_pillar(
                mb_cartilage,
                hx, yh, hz,
                mx, yh, mz,
                0.06, 0.06, 0.03, 0.03,
                n_sides=4,
                c_bot=c_bone, c_top=c_bone,
            )

    # 6. Steep Triangular Portal Gablet (Frontão do Portal) crowning the archivolts
    add_gablet(
        mb_stone, mb_cartilage,
        cx=0.0,
        y_base=7.6,
        y_apex=15.4,
        z_front=-4.42,
        z_back=-3.85,
        half_w=6.45,
        thickness=0.42,
        n_crockets=7,
        fill_tympanum=False,
        with_finial=True,
    )


# ==============================================================================
# SECTION 3: 2ND STORY — TRIFORIUM ARCADE, GREAT ROSE WINDOW, GABLE & LANCETS
# ==============================================================================

def _build_section3_second_story_and_rose_window(
    mb_bricks,
    mb_stone,
    mb_glass,
    mb_rose,
    mb_cartilage,
    anchors,
):
    c_br_lo = brick_col(0.62, 0.2)
    c_br_mid = brick_col(0.78, 0.1)
    c_br_hi = brick_col(0.90, 0.0)

    c_st_dark = stone_col(0.64)
    c_st_mid = stone_col(0.82)
    c_st_hi = stone_col(0.96)
    c_bone = cartilage_col(0.95)

    # ------------------------------------------------------------------
    # A. Ornate Horizontal Gothic Cornice & Blind Triforium Arcade (Y = 10.8..12.3)
    # ------------------------------------------------------------------
    # Lower and upper cornice ledges spanning X in [-17.6, +17.6]
    add_box(mb_stone, 0.0, 10.92, -2.55, 35.4, 0.32, 1.35, c_st_dark, c_st_hi)
    add_box(mb_stone, 0.0, 12.22, -2.65, 35.6, 0.36, 1.55, c_st_mid, c_st_hi)

    # 18 miniature pointed arches and colonnettes running across Y = 11.08..12.05
    n_trif = 18
    x_trif_span = 33.6
    bay_w = x_trif_span / float(n_trif)
    for i in range(n_trif + 1):
        cx_col = -0.5 * x_trif_span + i * bay_w
        add_prism_pillar(
            mb_stone,
            cx_col, 11.05, -2.68,
            cx_col, 11.82, -2.68,
            0.09, 0.09, 0.08, 0.08,
            n_sides=6,
            c_bot=c_st_dark, c_top=c_st_hi,
        )
        if i < n_trif:
            arch_cx = cx_col + 0.5 * bay_w
            add_pointed_arch(
                mb_stone,
                arch_cx, 11.75, -2.78, -2.42,
                bay_w * 0.38, bay_w * 0.52,
                0.28, 0.38,
                n_segs=6,
                c_inner=c_st_dark, c_outer=c_st_hi,
            )

    # ------------------------------------------------------------------
    # B. 2nd-Story Walls & Stepped Buttresses (Y = 11.2 .. 23.0)
    # ------------------------------------------------------------------
    # Central Nave Wall framing the Great Rose Window (X in [-6.8, +6.8], Y = 11.2..23.0, Z = -2.0..+1.2)
    add_box(mb_bricks, 0.0, 17.1, -0.4, 13.6, 11.8, 3.2, c_br_lo, c_br_hi)

    # Left & Right Tower 2nd-Story Walls (X in [-17.2, -6.8] and [+6.8, +17.2], Y = 11.2..23.0)
    for side in (-1.0, 1.0):
        tcx = side * 12.0
        # 3D tower box behind the window plane
        add_box(mb_bricks, tcx, 17.1, 1.1, 10.4, 11.8, 5.4, c_br_lo, c_br_mid)
        # Front wall piers around the tall double-lancet window (X = ±12.0, Y = 12.6..21.6)
        add_box(mb_bricks, side * 8.4, 17.1, -0.8, 3.2, 11.8, 2.8, c_br_lo, c_br_mid)
        add_box(mb_bricks, side * 15.6, 17.1, -0.8, 3.2, 11.8, 2.8, c_br_lo, c_br_mid)
        add_box(mb_bricks, tcx, 11.9, -0.8, 4.4, 1.4, 2.8, c_br_lo, c_br_mid)
        add_box(mb_bricks, tcx, 22.2, -0.8, 4.4, 1.6, 2.8, c_br_mid, c_br_hi)

        # Twin Towers 2nd-Story Tall Double-Lancet Windows (X = ±12.0, Y = 12.5..21.8)
        add_gothic_window(
            mb_stone, mb_glass, mb_cartilage,
            cx=tcx,
            y_sill=12.5,
            y_spring=18.8,
            y_apex=21.8,
            z_wall=-2.1,
            half_w=2.15,
            depth=0.90,
            n_lancets=2,
            n_transoms=2,
            with_gablet=True,
            glass_tint=(0.75, 0.28, 0.58),
        )

    # 2nd-Story Buttress Stages (Y = 11.75 .. 23.0)
    buttress_x_coords = [-17.5, -14.6, -9.4, -6.8, 6.8, 9.4, 14.6, 17.5]
    for bx in buttress_x_coords:
        is_major = abs(abs(bx) - 17.5) < 0.5 or abs(abs(bx) - 6.8) < 0.5
        bw = 1.25 if is_major else 0.92
        z_f2 = -3.85 if is_major else -3.35
        z_b2 = -1.8
        add_box(mb_stone, bx, 14.6, 0.5 * (z_f2 + z_b2), bw, 5.7, z_b2 - z_f2, c_st_dark, c_st_mid)
        add_sloped_weathering(mb_stone, bx, 17.45, 18.0, z_f2 - 0.08, z_b2, bw * 1.06, bw * 0.88, c_st_mid, c_st_hi)

        z_f3 = z_f2 + 0.48
        add_box(mb_stone, bx, 20.5, 0.5 * (z_f3 + z_b2), bw * 0.88, 5.0, z_b2 - z_f3, c_st_mid, c_st_hi)
        add_sloped_weathering(mb_stone, bx, 23.0, 23.55, z_f3 - 0.06, z_b2, bw * 0.94, bw * 0.78, c_st_mid, c_st_hi)

        # Corner buttress pinnacles flanking the 2nd-story windows
        if not is_major:
            add_pinnacle(
                mb_stone, mb_cartilage,
                bx, 23.5, 0.5 * (z_f3 + z_b2),
                base_w=0.82, total_h=4.2,
                n_sides=4, with_crockets=True, with_finial=True,
            )

    # ------------------------------------------------------------------
    # C. The Great Central Rose Window ("O Olho do Juízo", X=0, Y=17.2, Z=-2.2, R=4.2m)
    # ------------------------------------------------------------------
    rcx, rcy, rcz = 0.0, 17.2, -2.2
    r_rose = 4.2
    anchors["Exterior_Rose_Window_Center"] = [rcx, rcy, rcz]

    # Outer Pointed Arch Frame enclosing the Rose Window recess
    add_pointed_arch(
        mb_stone,
        cx=0.0, y_spring=16.5, z_front=-3.45, z_back=-2.0,
        w_inner=4.55, w_outer=5.25,
        h_inner=5.4, h_outer=6.2,
        n_segs=20,
        c_inner=c_st_dark, c_outer=c_st_hi,
    )

    # 1. 3 Concentric Stepped Circular Stone Moldings (Mat_OssifiedStone)
    n_rose_segs = 32
    for ring_idx in range(3):
        rf = ring_idx / 2.0
        z_rf = -3.35 + rf * 0.82
        z_rb = z_rf + 0.48
        r_in = r_rose * (1.02 - rf * 0.11)
        r_out = r_in + 0.38
        c_in = stone_col(0.62 + 0.08 * (1.0 - rf))
        c_out = stone_col(0.90 + 0.06 * (1.0 - rf))

        for i in range(n_rose_segs):
            a0 = 2.0 * math.pi * i / float(n_rose_segs)
            a1 = 2.0 * math.pi * (i + 1) / float(n_rose_segs)
            ca0, sa0 = math.cos(a0), math.sin(a0)
            ca1, sa1 = math.cos(a1), math.sin(a1)
            ix0, iy0 = rcx + r_in * ca0, rcy + r_in * sa0
            ix1, iy1 = rcx + r_in * ca1, rcy + r_in * sa1
            ox0, oy0 = rcx + r_out * ca0, rcy + r_out * sa0
            ox1, oy1 = rcx + r_out * ca1, rcy + r_out * sa1

            # Front ring face (-Z)
            add_oriented_quad(
                mb_stone,
                (ix0, iy0, z_rf), (ox0, oy0, z_rf), (ox1, oy1, z_rf), (ix1, iy1, z_rf),
                (0, 0), (0.4, 0), (0.4, 0.4), (0, 0.4),
                c_in, c_out, c_out, c_in,
                (0.0, 0.0, -1.0),
            )
            # Inner cylindrical soffit (facing inward toward center of rose window)
            add_oriented_quad(
                mb_stone,
                (ix0, iy0, z_rf), (ix1, iy1, z_rf), (ix1, iy1, z_rb), (ix0, iy0, z_rb),
                (0, 0), (0.4, 0), (0.4, 0.4), (0, 0.4),
                c_in, c_in, c_in, c_in,
                (-0.5 * (ca0 + ca1), -0.5 * (sa0 + sa1), -0.2),
            )

    # 2. Glowing Mat_RoseWindow Stained-Glass Disc at Z = -2.08
    z_disc = -2.08
    r_disc = r_rose * 0.90
    c_rw_center = _rgba(1.00, 0.88, 0.55)
    c_rw_mid = _rgba(0.95, 0.32, 0.42)
    c_rw_outer = _rgba(0.62, 0.22, 0.72)

    for i in range(n_rose_segs):
        a0 = 2.0 * math.pi * i / float(n_rose_segs)
        a1 = 2.0 * math.pi * (i + 1) / float(n_rose_segs)
        u0 = 0.5 + 0.48 * math.cos(a0)
        v0 = 0.5 + 0.48 * math.sin(a0)
        u1 = 0.5 + 0.48 * math.cos(a1)
        v1 = 0.5 + 0.48 * math.sin(a1)
        um0 = 0.5 + 0.24 * math.cos(a0)
        vm0 = 0.5 + 0.24 * math.sin(a0)
        um1 = 0.5 + 0.24 * math.cos(a1)
        vm1 = 0.5 + 0.24 * math.sin(a1)

        mx0, my0 = rcx + r_disc * 0.5 * math.cos(a0), rcy + r_disc * 0.5 * math.sin(a0)
        mx1, my1 = rcx + r_disc * 0.5 * math.cos(a1), rcy + r_disc * 0.5 * math.sin(a1)
        ox0, oy0 = rcx + r_disc * math.cos(a0), rcy + r_disc * math.sin(a0)
        ox1, oy1 = rcx + r_disc * math.cos(a1), rcy + r_disc * math.sin(a1)

        # Inner ring triangle
        add_oriented_tri(
            mb_rose,
            (rcx, rcy, z_disc), (mx0, my0, z_disc), (mx1, my1, z_disc),
            (0.5, 0.5), (um0, vm0), (um1, vm1),
            c_rw_center, c_rw_mid, c_rw_mid,
            (0.0, 0.0, -1.0),
        )
        # Outer ring quad
        add_oriented_quad(
            mb_rose,
            (mx0, my0, z_disc), (ox0, oy0, z_disc), (ox1, oy1, z_disc), (mx1, my1, z_disc),
            (um0, vm0), (u0, v0), (u1, v1), (um1, vm1),
            c_rw_mid, c_rw_outer, c_rw_outer, c_rw_mid,
            (0.0, 0.0, -1.0),
        )

    # 3. 3D Gothic Wheel Tracery in front of the glass (Z = -2.38)
    z_tr = -2.38
    # Central stone boss ring & cartilage iris spikes
    add_prism_pillar(
        mb_stone,
        rcx, rcy, z_disc,
        rcx, rcy, z_tr - 0.18,
        0.55, 0.55, 0.35, 0.35,
        n_sides=12,
        c_bot=c_st_mid, c_top=c_st_hi,
    )
    # Inner 12-sided stone hub ring (r = 1.05m) & Intermediate trefoil ring (r = 2.45m)
    for r_hub, thk_h in ((1.05, 0.18), (2.45, 0.16), (3.55, 0.20)):
        for i in range(24):
            a0 = 2.0 * math.pi * i / 24.0
            a1 = 2.0 * math.pi * (i + 1) / 24.0
            # Scalloped cusp modulation on outer ring
            cusp0 = 1.0 - (0.09 * abs(math.sin(6.0 * a0)) if r_hub > 3.0 else 0.0)
            cusp1 = 1.0 - (0.09 * abs(math.sin(6.0 * a1)) if r_hub > 3.0 else 0.0)
            r0 = r_hub * cusp0
            r1 = r_hub * cusp1
            ix0, iy0 = rcx + (r0 - thk_h) * math.cos(a0), rcy + (r0 - thk_h) * math.sin(a0)
            ix1, iy1 = rcx + (r1 - thk_h) * math.cos(a1), rcy + (r1 - thk_h) * math.sin(a1)
            ox0, oy0 = rcx + (r0 + thk_h) * math.cos(a0), rcy + (r0 + thk_h) * math.sin(a0)
            ox1, oy1 = rcx + (r1 + thk_h) * math.cos(a1), rcy + (r1 + thk_h) * math.sin(a1)
            add_oriented_quad(
                mb_stone,
                (ix0, iy0, z_tr), (ox0, oy0, z_tr), (ox1, oy1, z_tr), (ix1, iy1, z_tr),
                (0, 0), (0.3, 0), (0.3, 0.3), (0, 0.3),
                c_st_mid, c_st_hi, c_st_hi, c_st_mid,
                (0.0, 0.0, -1.0),
            )

    # 12 Radial Stone Spokes (Colonnettes) & 12 Secondary Outer Petal Spokes
    for sp in range(12):
        ang = 2.0 * math.pi * sp / 12.0
        ca, sa = math.cos(ang), math.sin(ang)
        pa_x, pa_y = -sa * 0.09, ca * 0.09
        x_in, y_in = rcx + 0.95 * ca, rcy + 0.95 * sa
        x_out, y_out = rcx + 3.65 * ca, rcy + 3.65 * sa
        add_oriented_quad(
            mb_stone,
            (x_in - pa_x, y_in - pa_y, z_tr - 0.04),
            (x_in + pa_x, y_in + pa_y, z_tr - 0.04),
            (x_out + pa_x, y_out + pa_y, z_tr - 0.04),
            (x_out - pa_x, y_out - pa_y, z_tr - 0.04),
            (0, 0), (0.2, 0), (0.2, 1.5), (0, 1.5),
            c_st_mid, c_st_hi, c_st_hi, c_st_mid,
            (0.0, 0.0, -1.0),
        )
        # Secondary intermediate spoke from r=2.45 to r=3.65
        ang_mid = ang + math.pi / 12.0
        cm, sm = math.cos(ang_mid), math.sin(ang_mid)
        pm_x, pm_y = -sm * 0.07, cm * 0.07
        xm_in, ym_in = rcx + 2.40 * cm, rcy + 2.40 * sm
        xm_out, ym_out = rcx + 3.65 * cm, rcy + 3.65 * sm
        add_oriented_quad(
            mb_stone,
            (xm_in - pm_x, ym_in - pm_y, z_tr),
            (xm_in + pm_x, ym_in + pm_y, z_tr),
            (xm_out + pm_x, ym_out + pm_y, z_tr),
            (xm_out - pm_x, ym_out - pm_y, z_tr),
            (0, 0), (0.2, 0), (0.2, 0.8), (0, 0.8),
            c_st_dark, c_st_mid, c_st_mid, c_st_dark,
            (0.0, 0.0, -1.0),
        )
        # Sharp bone cusp tooth pointing inward along the outer wheel
        add_prism_pillar(
            mb_cartilage,
            xm_out, ym_out, z_tr - 0.06,
            rcx + 3.05 * cm, rcy + 3.05 * sm, z_tr - 0.12,
            0.08, 0.08, 0.015, 0.015,
            n_sides=4,
            c_bot=c_bone, c_top=c_bone,
        )

    # Spandrel Trefoil Reliefs in the 4 corners around the Rose Window
    for sx in (-1.0, 1.0):
        for sy, y_sp in ((-1.0, 13.5), (1.0, 20.9)):
            add_prism_pillar(
                mb_stone,
                sx * 4.35, y_sp, -2.1,
                sx * 4.35, y_sp, -2.55,
                0.68, 0.68, 0.45, 0.45,
                n_sides=8,
                c_bot=c_st_dark, c_top=c_st_hi,
            )

    # ------------------------------------------------------------------
    # D. Central Triangular Gable (Frontão Central, X in [-7.0, +7.0], Y = 22.0..30.5)
    # ------------------------------------------------------------------
    # Horizontal gallery cornice at Y = 22.4
    add_box(mb_stone, 0.0, 22.4, -2.55, 14.4, 0.55, 1.6, c_st_mid, c_st_hi)

    # Steeply pitched central gable with tympanum, crocketed raking cornices, and tall finial
    add_gablet(
        mb_stone, mb_cartilage,
        cx=0.0,
        y_base=22.4,
        y_apex=30.5,
        z_front=-2.65,
        z_back=-1.45,
        half_w=7.0,
        thickness=0.52,
        n_crockets=9,
        fill_tympanum=True,
        with_finial=True,
    )
    # 7 Recessed Graduated Blind Lancet Arches on the Central Gable face
    for gk in range(-3, 4):
        gx = gk * 1.45
        gh = max(1.1, 4.4 - abs(gk) * 1.05)
        gy_mid = 22.6 + 0.5 * gh
        add_box(mb_stone, gx - 0.45, gy_mid, -2.52, 0.14, gh, 0.24, c_st_dark, c_st_hi)
        add_box(mb_stone, gx + 0.45, gy_mid, -2.52, 0.14, gh, 0.24, c_st_dark, c_st_hi)
        add_pointed_arch(
            mb_stone,
            gx, 22.6 + gh, -2.62, -2.38,
            0.36, 0.52, 0.42, 0.58,
            n_segs=6,
            c_inner=c_st_dark, c_outer=c_st_hi,
        )
    # Sculpted Circular Oculus Medallion Relief near top of Central Gable (Y = 27.6)
    add_prism_pillar(
        mb_stone,
        0.0, 27.6, -2.42,
        0.0, 27.6, -2.72,
        0.85, 0.85, 0.62, 0.62,
        n_sides=12,
        c_bot=c_st_dark, c_top=c_st_hi,
    )
    # Flanking Gable Pinnacles at X = ±6.8, Y = 22.6
    for side in (-1.0, 1.0):
        add_pinnacle(
            mb_stone, mb_cartilage,
            side * 6.8, 22.6, -2.35,
            base_w=1.15, total_h=5.8,
            n_sides=4, with_crockets=True, with_finial=True,
        )


# ==============================================================================
# SECTION 4 & 5: 3RD STORY OCTAGONAL BELFRIES, 16 PINNACLES & 4TH STORY TWIN SPIRES
# ==============================================================================

def _build_section4_and_5_belfries_and_spires(
    mb_bricks,
    mb_stone,
    mb_glass,
    mb_rose,
    mb_shingles,
    mb_cartilage,
    anchors,
):
    c_br_lo = brick_col(0.60, 0.2)
    c_br_hi = brick_col(0.88, 0.0)
    c_st_dark = stone_col(0.64)
    c_st_mid = stone_col(0.82)
    c_st_hi = stone_col(0.96)
    c_sh_lo = shingle_col(0.55)
    c_sh_hi = shingle_col(0.88)
    c_bone = cartilage_col(0.96)

    for side in (-1.0, 1.0):
        tcx = side * 12.0
        tcz = -0.2  # Center Z of twin towers

        # ------------------------------------------------------------------
        # 1. Heavy Corbelled Cornice, Crenellated Gallery & Gargoyles at Y = 23.0
        # ------------------------------------------------------------------
        add_box(mb_stone, tcx, 23.0, tcz, 10.6, 0.65, 8.6, c_st_dark, c_st_hi)
        add_box(mb_stone, tcx, 23.65, tcz, 11.0, 0.65, 9.0, c_st_mid, c_st_hi)

        # Crenellated gallery merlons around the 23.6m cornice
        for mx_off in np.linspace(-4.8, 4.8, 7):
            add_box(mb_stone, tcx + mx_off, 24.3, tcz - 4.3, 0.65, 0.65, 0.36, c_st_mid, c_st_hi)
            add_box(mb_stone, tcx + mx_off, 24.3, tcz + 4.3, 0.65, 0.65, 0.36, c_st_mid, c_st_hi)

        # Projecting Corner Gargoyles at Y = 23.5
        add_gargoyle(mb_stone, mb_cartilage, tcx - 5.1, 23.5, tcz - 4.1, -0.7, -1.0, scale=0.95)
        add_gargoyle(mb_stone, mb_cartilage, tcx + 5.1, 23.5, tcz - 4.1,  0.7, -1.0, scale=0.95)

        # ------------------------------------------------------------------
        # 2. 3rd Story — Octagonal Belfry Drum (Y = 23.5 to 35.0)
        # ------------------------------------------------------------------
        r_belfry = 3.95
        add_prism_pillar(
            mb_bricks,
            tcx, 23.5, tcz,
            tcx, 35.0, tcz,
            r_belfry, r_belfry,
            r_belfry * 0.96, r_belfry * 0.96,
            n_sides=8, rot_deg=22.5,
            c_bot=c_br_lo, c_top=c_br_hi,
        )
        # 8 Octagonal Corner Stone Pilasters & Horizontal Stringcourses
        for i in range(8):
            ang = math.radians(22.5 + i * 45.0)
            px = tcx + r_belfry * 0.98 * math.cos(ang)
            pz = tcz + r_belfry * 0.98 * math.sin(ang)
            add_prism_pillar(
                mb_stone,
                px, 23.5, pz,
                px, 35.2, pz,
                0.38, 0.38, 0.34, 0.34,
                n_sides=4, rot_deg=45.0,
                c_bot=c_st_dark, c_top=c_st_hi,
            )
        # Upper belfry cornice rings at Y = 31.4 and Y = 34.8
        for yc in (31.4, 34.8):
            add_prism_pillar(
                mb_stone,
                tcx, yc - 0.25, tcz,
                tcx, yc + 0.25, tcz,
                r_belfry * 1.08, r_belfry * 1.08,
                r_belfry * 1.08, r_belfry * 1.08,
                n_sides=8, rot_deg=22.5,
                c_bot=c_st_mid, c_top=c_st_hi,
            )

        # Tall Pointed Belfry Louvered/Glass Openings (Y = 24.4..31.0) on Front (-Z)
        belfry_z_front = tcz - r_belfry * math.cos(math.radians(22.5))
        add_gothic_window(
            mb_stone, mb_glass, mb_cartilage,
            cx=tcx,
            y_sill=24.4,
            y_spring=29.0,
            y_apex=31.0,
            z_wall=belfry_z_front + 0.15,
            half_w=1.42,
            depth=0.55,
            n_lancets=2,
            n_transoms=2,
            with_gablet=False,
            glass_tint=(0.68, 0.78, 0.92),
        )
        # Stone acoustic louvers (abajours) inside front belfry opening
        for yl in (25.4, 26.4, 27.4, 28.4):
            add_sloped_weathering(
                mb_stone,
                tcx, yl, yl + 0.32,
                belfry_z_front - 0.05, belfry_z_front + 0.35,
                2.55, 2.55,
                c_st_dark, c_st_hi,
            )

        # Circular Clock / Oculus Medallions at Y = 32.8 & Steep Triangular Gablets on all 4 cardinal sides
        cardinal_dirs = [
            (0.0, -1.0, 0.0),   # Front (-Z)
            (0.0,  1.0, 180.0), # Back (+Z)
            (-1.0, 0.0, 90.0),  # Left (-X)
            ( 1.0, 0.0, -90.0), # Right (+X)
        ]
        dist_card = r_belfry * math.cos(math.radians(22.5))
        for dx, dz, _ in cardinal_dirs:
            ox = tcx + dx * (dist_card + 0.05)
            oz = tcz + dz * (dist_card + 0.05)
            oy = 32.85

            # Oculus stone ring & glowing center
            add_prism_pillar(
                mb_stone,
                ox - dx * 0.15, oy - 1.05, oz - dz * 0.15,
                ox + dx * 0.32, oy + 1.05, oz + dz * 0.32,
                1.05 if abs(dz) > 0.5 else 0.25,
                1.05 if abs(dx) > 0.5 else 0.25,
                1.05 if abs(dz) > 0.5 else 0.25,
                1.05 if abs(dx) > 0.5 else 0.25,
                n_sides=12,
                c_bot=c_st_dark, c_top=c_st_hi,
            )
            if abs(dz) > 0.5:
                # Extra crisp 12-segment circular oculus & clock-wheel on front and back
                z_oc = oz + dz * 0.28
                for k in range(12):
                    a0 = 2.0 * math.pi * k / 12.0
                    a1 = 2.0 * math.pi * (k + 1) / 12.0
                    add_oriented_tri(
                        mb_rose,
                        (ox, oy, z_oc),
                        (ox + 0.92 * math.cos(a0), oy + 0.92 * math.sin(a0), z_oc),
                        (ox + 0.92 * math.cos(a1), oy + 0.92 * math.sin(a1), z_oc),
                        (0.5, 0.5), (0.0, 0.0), (1.0, 0.0),
                        _rgba(1.0, 0.92, 0.6), _rgba(0.9, 0.4, 0.35), _rgba(0.9, 0.4, 0.35),
                        (0.0, 0.0, dz),
                    )
                    add_oriented_quad(
                        mb_stone,
                        (ox + 0.85 * math.cos(a0), oy + 0.85 * math.sin(a0), z_oc + dz * 0.06),
                        (ox + 1.22 * math.cos(a0), oy + 1.22 * math.sin(a0), z_oc + dz * 0.06),
                        (ox + 1.22 * math.cos(a1), oy + 1.22 * math.sin(a1), z_oc + dz * 0.06),
                        (ox + 0.85 * math.cos(a1), oy + 0.85 * math.sin(a1), z_oc + dz * 0.06),
                        (0, 0), (0.3, 0), (0.3, 0.3), (0, 0.3),
                        c_st_mid, c_st_hi, c_st_hi, c_st_mid,
                        (0.0, 0.0, dz),
                    )
                # Steep triangular gablet above front/back oculus (Y = 33.2..37.8)
                add_gablet(
                    mb_stone, mb_cartilage,
                    cx=ox,
                    y_base=33.0,
                    y_apex=37.8,
                    z_front=oz + dz * 0.38 if dz < 0 else oz - 0.10,
                    z_back=oz + 0.10 if dz < 0 else oz + dz * 0.38,
                    half_w=2.15,
                    thickness=0.30,
                    n_crockets=5,
                    fill_tympanum=True,
                    with_finial=True,
                )
            else:
                # Side gablets on -X and +X faces (Y = 33.0..37.5)
                add_prism_pillar(
                    mb_stone,
                    ox + dx * 0.20, 33.0, oz,
                    ox + dx * 0.20, 37.5, oz,
                    0.35, 1.95, 0.12, 0.12,
                    n_sides=4, rot_deg=0.0,
                    c_bot=c_st_mid, c_top=c_st_hi,
                )

        # ------------------------------------------------------------------
        # 3. Forest of 16 Crocketed Gothic Pinnacles (8 per tower) + Flying Buttresses
        # ------------------------------------------------------------------
        for sx in (-1.0, 1.0):
            for sz in (-1.0, 1.0):
                # Outer lower corner pinnacle (Y = 23.8 .. 32.5)
                px_out = tcx + sx * 4.65
                pz_out = tcz + sz * 3.85
                add_pinnacle(
                    mb_stone, mb_cartilage,
                    px_out, 23.8, pz_out,
                    base_w=1.18, total_h=8.6,
                    n_sides=4, with_crockets=True, with_finial=True,
                )
                # Inner upper corner pinnacle (Y = 28.5 .. 38.2)
                px_in = tcx + sx * 3.55
                pz_in = tcz + sz * 3.15
                add_pinnacle(
                    mb_stone, mb_cartilage,
                    px_in, 28.5, pz_in,
                    base_w=1.02, total_h=9.6,
                    n_sides=6, with_crockets=True, with_finial=True,
                )
                # Miniature Flying-Buttress Flyer Arch connecting outer pinnacle to belfry drum
                add_prism_pillar(
                    mb_stone,
                    px_out, 28.2, pz_out,
                    tcx + sx * 2.8, 30.8, tcz + sz * 2.4,
                    0.22, 0.22, 0.22, 0.22,
                    n_sides=4,
                    c_bot=c_st_mid, c_top=c_st_hi,
                )

        # ------------------------------------------------------------------
        # 4. 4th Story — The Soaring 8-Sided Pointed Spire (Y = 35.0 to 48.5)
        # ------------------------------------------------------------------
        y_sp_base = 35.0
        y_sp_top = 48.5
        r_sp_base = 3.55
        r_sp_top = 0.22
        n_bands = 9

        for b in range(n_bands):
            f0 = b / float(n_bands)
            f1 = (b + 1) / float(n_bands)
            yb0 = y_sp_base + f0 * (y_sp_top - y_sp_base)
            yb1 = y_sp_base + f1 * (y_sp_top - y_sp_base)
            rb0 = r_sp_base * (1.0 - f0) + r_sp_top * f0
            rb1 = r_sp_base * (1.0 - f1) + r_sp_top * f1

            c_b0 = shingle_col(0.56 + 0.28 * f0 + (0.05 if b % 2 == 0 else -0.04))
            c_b1 = shingle_col(0.56 + 0.28 * f1)
            add_prism_pillar(
                mb_shingles,
                tcx, yb0, tcz,
                tcx, yb1, tcz,
                rb0, rb0, rb1, rb1,
                n_sides=8, rot_deg=22.5,
                c_bot=c_b0, c_top=c_b1,
                cap_top=(b == n_bands - 1),
            )
            # Decorative stone collar band every 3 bands
            if b in (2, 5, 7):
                add_prism_pillar(
                    mb_stone,
                    tcx, yb1 - 0.14, tcz,
                    tcx, yb1 + 0.14, tcz,
                    rb1 * 1.07, rb1 * 1.07, rb1 * 1.05, rb1 * 1.05,
                    n_sides=8, rot_deg=22.5,
                    c_bot=c_st_mid, c_top=c_st_hi,
                )

        # 8 Raised Stone Ribs & Crockets along the octagonal ridges of each spire
        for r_idx in range(8):
            ang = math.radians(22.5 + r_idx * 45.0)
            ca, sa = math.cos(ang), math.sin(ang)
            add_prism_pillar(
                mb_stone,
                tcx + r_sp_base * ca, y_sp_base, tcz + r_sp_base * sa,
                tcx + r_sp_top * ca, y_sp_top, tcz + r_sp_top * sa,
                0.15, 0.15, 0.05, 0.05,
                n_sides=4, rot_deg=45.0,
                c_bot=c_st_mid, c_top=c_st_hi,
            )
            # Projecting crocket knobs along each rib
            for cr in range(1, 9):
                fc = cr / 9.5
                yc = y_sp_base + fc * (y_sp_top - y_sp_base)
                rc = r_sp_base * (1.0 - fc) + r_sp_top * fc
                add_prism_pillar(
                    mb_stone,
                    tcx + rc * ca, yc, tcz + rc * sa,
                    tcx + (rc + 0.28 * (1.0 - 0.5 * fc)) * ca, yc + 0.26, tcz + (rc + 0.28 * (1.0 - 0.5 * fc)) * sa,
                    0.11, 0.11, 0.025, 0.025,
                    n_sides=4, rot_deg=45.0,
                    c_bot=c_st_mid, c_top=c_st_hi,
                )

        # Dormer Lucarnes at the base of the spire (Y = 36.2..40.2) on front & sides
        for d_ang in (0.0, 90.0, 180.0, 270.0):
            ar = math.radians(d_ang)
            dx, dz = math.sin(ar), -math.cos(ar)
            lx = tcx + dx * 2.55
            lz = tcz + dz * 2.55
            add_pinnacle(
                mb_stone, mb_cartilage,
                lx, 36.0, lz,
                base_w=0.92, total_h=4.2,
                n_sides=4, with_crockets=True, with_finial=True,
            )

        # ------------------------------------------------------------------
        # 5. Giant Ornate Gothic Bone Cross / Finial at Y = 48.5 .. 51.6
        # ------------------------------------------------------------------
        # Ossified orb boss
        add_prism_pillar(
            mb_stone,
            tcx, 48.35, tcz,
            tcx, 49.05, tcz,
            0.36, 0.36, 0.24, 0.24,
            n_sides=8,
            c_bot=c_st_mid, c_top=c_st_hi,
        )
        # Main vertical bone cross shaft (Y = 48.8 .. 51.6)
        add_prism_pillar(
            mb_cartilage,
            tcx, 48.8, tcz,
            tcx, 51.6, tcz,
            0.16, 0.16, 0.03, 0.03,
            n_sides=6,
            c_bot=cartilage_col(0.82), c_top=c_bone,
        )
        # Horizontal & depth crossbars at Y = 50.1
        add_box(mb_cartilage, tcx, 50.1, tcz, 1.85, 0.18, 0.18, cartilage_col(0.85), c_bone)
        add_box(mb_cartilage, tcx, 50.1, tcz, 0.18, 0.18, 1.45, cartilage_col(0.85), c_bone)
        # Celtic/Gothic ring around cross intersection
        for kr in range(8):
            a0 = 2.0 * math.pi * kr / 8.0
            a1 = 2.0 * math.pi * (kr + 1) / 8.0
            add_prism_pillar(
                mb_cartilage,
                tcx + 0.52 * math.cos(a0), 50.1 + 0.52 * math.sin(a0), tcz,
                tcx + 0.52 * math.cos(a1), 50.1 + 0.52 * math.sin(a1), tcz,
                0.05, 0.05, 0.05, 0.05,
                n_sides=4,
                c_bot=c_bone, c_top=c_bone,
            )

        spire_tag = "Exterior_Left_Spire_Tip" if side < 0 else "Exterior_Right_Spire_Tip"
        anchors[spire_tag] = [float(tcx), 51.6, float(tcz)]


# ==============================================================================
# SECTION 6: CENTRAL DARK CASTLE KEEP (Y = 24..56, Z = +6) & 8 WRITHING TENDRILS
# ==============================================================================

def _build_section6_castle_keep_and_tendrils(
    mb_bricks,
    mb_stone,
    mb_glass,
    mb_shingles,
    mb_organs,
    mb_cartilage,
    anchors,
):
    c_br_lo = brick_col(0.52, 0.35)
    c_br_mid = brick_col(0.72, 0.15)
    c_br_hi = brick_col(0.86, 0.0)
    c_st_dark = stone_col(0.60)
    c_st_mid = stone_col(0.80)
    c_st_hi = stone_col(0.95)
    c_sh_lo = shingle_col(0.50)
    c_sh_hi = shingle_col(0.85)
    c_bone = cartilage_col(0.95)

    # ------------------------------------------------------------------
    # A. Colossal Dark Gothic Citadel Keep (X = 0, Z = +6.0, Y = 24.0 to 56.0)
    #    Strictly >= Y = 24.0 so it never blocks the interior nave floor!
    # ------------------------------------------------------------------
    kcx, kcz = 0.0, 6.0

    # 1. Corbelled Structural Bridge Soffit at Y = 24.0 .. 25.8
    add_prism_pillar(
        mb_stone,
        kcx, 24.0, kcz,
        kcx, 25.8, kcz,
        4.8, 3.4, 6.4, 4.6,
        n_sides=8, rot_deg=22.5,
        c_bot=c_st_dark, c_top=c_st_mid,
        cap_bottom=True,
    )

    # 2. Main Citadel Keep Body (Y = 25.8 .. 44.0)
    add_prism_pillar(
        mb_bricks,
        kcx, 25.8, kcz,
        kcx, 44.0, kcz,
        6.2, 4.5, 5.8, 4.2,
        n_sides=8, rot_deg=22.5,
        c_bot=c_br_lo, c_top=c_br_hi,
    )

    # 8 Vertical Ribbed Gothic Buttress Piers along the Keep walls (Y = 25.5 .. 44.5)
    for i in range(8):
        ang = math.radians(22.5 + i * 45.0)
        ca, sa = math.cos(ang), math.sin(ang)
        add_prism_pillar(
            mb_stone,
            kcx + 6.1 * ca, 25.5, kcz + 4.4 * sa,
            kcx + 5.7 * ca, 44.2, kcz + 4.1 * sa,
            0.62, 0.62, 0.48, 0.48,
            n_sides=4, rot_deg=45.0,
            c_bot=c_st_dark, c_top=c_st_hi,
        )

    # Tall Narrow Slit-Lancet Windows on the Keep front face (Z ~ +1.8)
    for kx in (-2.4, 0.0, 2.4):
        add_gothic_window(
            mb_stone, mb_glass, mb_cartilage,
            cx=kx,
            y_sill=29.5,
            y_spring=39.2,
            y_apex=41.5,
            z_wall=1.95,
            half_w=0.78 if kx != 0.0 else 1.05,
            depth=0.55,
            n_lancets=1 if kx != 0.0 else 2,
            n_transoms=2,
            with_gablet=True,
            glass_tint=(0.95, 0.25, 0.30),
        )

    # 3. Overhanging Machicolations & Crenellated Battlements at Y = 43.2 .. 45.8
    # Corbelled machicolation brackets around perimeter
    n_mach = 24
    for m in range(n_mach):
        ang = 2.0 * math.pi * m / float(n_mach)
        mx = kcx + 5.95 * math.cos(ang)
        mz = kcz + 4.35 * math.sin(ang)
        add_prism_pillar(
            mb_stone,
            mx, 42.6, mz,
            mx + 0.45 * math.cos(ang), 44.2, mz + 0.45 * math.sin(ang),
            0.18, 0.18, 0.34, 0.34,
            n_sides=4,
            c_bot=c_st_dark, c_top=c_st_hi,
        )
        # Battlement merlon above each bracket
        if m % 2 == 0:
            add_box(
                mb_stone,
                mx + 0.42 * math.cos(ang), 45.1, mz + 0.42 * math.sin(ang),
                0.65, 1.15, 0.65,
                c_st_mid, c_st_hi,
            )

    # Heavy Battlement Parapet Ring at Y = 44.0 .. 44.7
    add_prism_pillar(
        mb_stone,
        kcx, 43.8, kcz,
        kcx, 44.7, kcz,
        6.45, 4.75, 6.55, 4.85,
        n_sides=8, rot_deg=22.5,
        c_bot=c_st_mid, c_top=c_st_hi,
    )

    # 4. Upper Crooked Citadel Turret reaching Y = 56.0
    # Octagonal lantern drum (Y = 44.5 .. 49.8)
    add_prism_pillar(
        mb_bricks,
        kcx, 44.5, kcz,
        kcx + 0.18, 49.8, kcz - 0.10,
        3.35, 2.85, 2.95, 2.55,
        n_sides=8, rot_deg=22.5,
        c_bot=c_br_mid, c_top=c_br_hi,
    )
    add_prism_pillar(
        mb_stone,
        kcx + 0.18, 49.5, kcz - 0.10,
        kcx + 0.22, 50.1, kcz - 0.12,
        3.25, 2.85, 3.35, 2.95,
        n_sides=8, rot_deg=22.5,
        c_bot=c_st_mid, c_top=c_st_hi,
    )
    # Glowing upper lantern slit windows (Y = 45.5 .. 49.0)
    for lx_off in (-1.1, 0.0, 1.1):
        add_box(
            mb_glass,
            kcx + lx_off, 47.2, kcz - 2.75,
            0.48, 3.2, 0.22,
            _rgba(0.95, 0.22, 0.32), _rgba(1.0, 0.78, 0.42),
        )

    # Crooked Multi-Stage Witch-Spire Roof on Central Keep (Y = 50.0 .. 55.2) + Finial to 56.0
    keep_roof_stages = [
        (50.0, 51.8, 0.22, 0.42, 3.20, 1.85),
        (51.8, 53.6, 0.42, 0.72, 1.85, 0.72),
        (53.6, 55.2, 0.72, 0.52, 0.72, 0.10),
    ]
    for y0, y1, ox0, ox1, r0, r1 in keep_roof_stages:
        add_prism_pillar(
            mb_shingles,
            kcx + ox0, y0, kcz - 0.15,
            kcx + ox1, y1, kcz - 0.25,
            r0, r0 * 0.88, r1, r1 * 0.88,
            n_sides=8, rot_deg=22.5,
            c_bot=c_sh_lo, c_top=c_sh_hi,
        )
    # Bone Finial at Y = 55.0 .. 56.2
    add_prism_pillar(
        mb_cartilage,
        kcx + 0.52, 55.0, kcz - 0.25,
        kcx + 0.58, 56.2, kcz - 0.28,
        0.14, 0.14, 0.02, 0.02,
        n_sides=5,
        c_bot=cartilage_col(0.82), c_top=c_bone,
    )
    anchors["Exterior_Central_Keep_Crown"] = [kcx + 0.58, 56.0, kcz - 0.25]

    # 5. 2 Flanking Crooked Bartizan Witch-Spires (X = ±6.6, Z = +5.2, Y = 34.5 .. 49.8)
    for side in (-1.0, 1.0):
        bx = side * 6.6
        bz = 5.2
        # Corbelled stone cone base (Y = 34.5 .. 37.5)
        add_prism_pillar(
            mb_stone,
            bx * 0.88, 34.5, bz,
            bx, 37.5, bz,
            0.35, 0.35, 1.95, 1.95,
            n_sides=8,
            c_bot=c_st_dark, c_top=c_st_hi,
        )
        # Cylindrical turret body (Y = 37.5 .. 43.5)
        add_prism_pillar(
            mb_bricks,
            bx, 37.5, bz,
            bx + side * 0.22, 43.5, bz,
            1.85, 1.85, 1.72, 1.72,
            n_sides=8,
            c_bot=c_br_lo, c_top=c_br_hi,
        )
        # Machicolated turret collar
        add_prism_pillar(
            mb_stone,
            bx + side * 0.20, 43.3, bz,
            bx + side * 0.25, 43.9, bz,
            2.05, 2.05, 2.10, 2.10,
            n_sides=8,
            c_bot=c_st_mid, c_top=c_st_hi,
        )
        # Crooked pointed witch-hat roof (Y = 43.8 .. 49.2)
        add_prism_pillar(
            mb_shingles,
            bx + side * 0.25, 43.8, bz,
            bx + side * 0.65, 46.8, bz - 0.15,
            2.02, 2.02, 0.85, 0.85,
            n_sides=8,
            c_bot=c_sh_lo, c_top=c_sh_hi,
        )
        add_prism_pillar(
            mb_shingles,
            bx + side * 0.65, 46.8, bz - 0.15,
            bx + side * 1.05, 49.4, bz - 0.25,
            0.85, 0.85, 0.06, 0.06,
            n_sides=8,
            c_bot=c_sh_lo, c_top=c_sh_hi,
        )
        add_prism_pillar(
            mb_cartilage,
            bx + side * 1.05, 49.3, bz - 0.25,
            bx + side * 1.18, 50.4, bz - 0.28,
            0.09, 0.09, 0.015, 0.015,
            n_sides=4,
            c_bot=c_bone, c_top=c_bone,
        )

    # ------------------------------------------------------------------
    # B. 8 Writhing Visceral Tendrils & Dead Vein-Branches (Mat_HellOrgans)
    #    Matching Reference 2's silhouette around the base, towers, and sky!
    # ------------------------------------------------------------------
    tendril_specs = [
        # 1. Front-Left Crag & Stair Flanking Tendril
        (
            [
                (-19.5, -2.4, -15.5),
                (-16.2,  0.2, -14.2),
                (-13.5,  1.8, -11.5),
                (-10.8,  1.2,  -8.5),
                ( -8.8,  2.8,  -5.6),
                ( -7.2,  5.5,  -4.6),
                ( -6.8,  8.4,  -3.8),
            ],
            [0.95, 0.82, 0.68, 0.58, 0.48, 0.35, 0.12],
        ),
        # 2. Front-Right Crag & Stair Flanking Tendril
        (
            [
                ( 19.5, -2.4, -15.5),
                ( 16.4,  0.4, -13.8),
                ( 13.8,  2.1, -11.0),
                ( 11.2,  1.4,  -8.2),
                (  9.0,  3.1,  -5.4),
                (  7.4,  6.0,  -4.5),
                (  6.9,  8.8,  -3.8),
            ],
            [0.95, 0.82, 0.68, 0.58, 0.48, 0.35, 0.12],
        ),
        # 3. Left Tower Buttress Climber
        (
            [
                (-21.0, -2.2, -6.5),
                (-18.8,  2.5, -5.2),
                (-17.8,  8.2, -4.6),
                (-16.2, 14.0, -3.8),
                (-17.4, 19.8, -3.4),
                (-15.8, 25.5, -2.6),
                (-14.2, 30.2, -1.8),
            ],
            [1.05, 0.88, 0.74, 0.62, 0.48, 0.32, 0.10],
        ),
        # 4. Right Tower Buttress Climber
        (
            [
                ( 21.0, -2.2, -6.5),
                ( 18.8,  2.8, -5.2),
                ( 17.6,  8.6, -4.6),
                ( 16.0, 14.5, -3.8),
                ( 17.2, 20.2, -3.4),
                ( 15.6, 25.8, -2.6),
                ( 14.0, 30.5, -1.8),
            ],
            [1.05, 0.88, 0.74, 0.62, 0.48, 0.32, 0.10],
        ),
        # 5. High Left Skyward Vein-Branch (Arching out from Left Tower / Keep)
        (
            [
                ( -6.2, 28.0,  3.8),
                ( -9.5, 31.5,  2.2),
                (-14.5, 34.8,  0.8),
                (-18.8, 38.5, -0.2),
                (-22.2, 42.8, -0.8),
                (-24.5, 46.2, -1.2),
            ],
            [0.85, 0.68, 0.52, 0.38, 0.22, 0.06],
        ),
        # 6. High Right Skyward Vein-Branch (Arching out from Right Tower / Keep)
        (
            [
                (  6.2, 28.0,  3.8),
                (  9.8, 31.8,  2.2),
                ( 14.8, 35.2,  0.8),
                ( 19.0, 38.8, -0.2),
                ( 22.5, 43.2, -0.8),
                ( 24.8, 46.5, -1.2),
            ],
            [0.85, 0.68, 0.52, 0.38, 0.22, 0.06],
        ),
        # 7. Upper-Left Citadel Crown Tendril (Reaching toward sky between Left Spire & Keep)
        (
            [
                ( -2.8, 38.5,  4.8),
                ( -5.6, 42.5,  3.6),
                ( -8.4, 46.8,  2.4),
                ( -9.8, 51.2,  1.6),
                ( -8.2, 54.5,  1.2),
            ],
            [0.72, 0.56, 0.40, 0.24, 0.06],
        ),
        # 8. Upper-Right Citadel Crown Tendril (Reaching toward sky between Right Spire & Keep)
        (
            [
                (  2.8, 38.5,  4.8),
                (  5.8, 42.8,  3.6),
                (  8.6, 47.2,  2.4),
                ( 10.0, 51.5,  1.6),
                (  8.5, 54.8,  1.2),
            ],
            [0.72, 0.56, 0.40, 0.24, 0.06],
        ),
    ]

    for t_idx, (pts, rads) in enumerate(tendril_specs):
        add_tube_along_curve(
            mb_organs,
            pts,
            rads,
            n_sides=8,
            c_start=organ_col(0.58, 0.0),
            c_end=organ_col(0.94, 1.0),
            pulse_amp=0.10,
            pulse_freq=5.0,
        )
        # Forked secondary arterial offshoots & bone thorns on each tendril
        for sub_i in (2, 4):
            if sub_i >= len(pts):
                continue
            p_base = pts[sub_i]
            r_b = rads[sub_i] * 0.68
            sx = -1.0 if p_base[0] < 0 else 1.0
            p_mid = (p_base[0] + sx * 2.2, p_base[1] + 2.4, p_base[2] - 0.8)
            p_tip = (p_base[0] + sx * 3.8, p_base[1] + 4.8, p_base[2] - 1.4)
            add_tube_along_curve(
                mb_organs,
                [p_base, p_mid, p_tip],
                [r_b, r_b * 0.52, 0.04],
                n_sides=6,
                c_start=organ_col(0.68, 0.3),
                c_end=organ_col(0.96, 1.0),
            )
            # Calcified bone thorn at offshoot tip
            add_prism_pillar(
                mb_cartilage,
                p_mid[0], p_mid[1], p_mid[2],
                p_tip[0] + sx * 0.6, p_tip[1] + 0.9, p_tip[2] - 0.3,
                0.12, 0.12, 0.015, 0.015,
                n_sides=4,
                c_bot=cartilage_col(0.80), c_top=c_bone,
            )


# ==============================================================================
# PUBLIC ENTRY POINT
# ==============================================================================

def build_cathedral_exterior() -> tuple[list[MeshBuilder], dict[str, list[float]]]:
    """
    Builds the complete 3D Exterior Façade, Twin Gothic Spires, Dark Castle Keep,
    Grand Staircase, and Organic Tendrils of the Boss Cathedral.

    Returns:
        (meshes, anchors):
            meshes: list[MeshBuilder]
            anchors: dict[str, list[float]]
    """
    mb_rock = MeshBuilder("Exterior_Cliff_Foundation", "Mat_DigestedCalcified")
    mb_stairs = MeshBuilder("Exterior_Grand_Staircase", "Mat_OssifiedStone")
    mb_pipes = MeshBuilder("Exterior_Streetlamp_Shafts", "Mat_OrganPipes")
    mb_bricks_facade = MeshBuilder("Exterior_Facade_FleshBricks", "Mat_FleshBricks")
    mb_stone_facade = MeshBuilder("Exterior_Facade_OssifiedStone", "Mat_OssifiedStone")
    mb_glass = MeshBuilder("Exterior_Lancet_StainedGlass", "Mat_StainedGlass")
    mb_rose = MeshBuilder("Exterior_RoseWindow_Glass", "Mat_RoseWindow")
    mb_door = MeshBuilder("Exterior_Cathedral_Doors", "Mat_CathedralDoor")
    mb_belfry_stone = MeshBuilder("Exterior_Belfries_And_Pinnacles", "Mat_OssifiedStone")
    mb_spires = MeshBuilder("Exterior_Twin_Spire_Roofs", "Mat_SpireShingles")
    mb_keep_bricks = MeshBuilder("Exterior_CastleKeep_Walls", "Mat_FleshBricks")
    mb_keep_stone = MeshBuilder("Exterior_CastleKeep_Stonework", "Mat_OssifiedStone")
    mb_organs = MeshBuilder("Exterior_Visceral_Tendrils", "Mat_HellOrgans")
    mb_cartilage = MeshBuilder("Exterior_Bone_Spikes_And_Finials", "Mat_Cartilage")

    anchors: dict[str, list[float]] = {}

    # 1. Rocky Castle Island Base & 14-Step Grand Staircase + 4 Streetlamps
    _build_section1_island_and_staircase(
        mb_rock=mb_rock,
        mb_stone=mb_stairs,
        mb_pipes=mb_pipes,
        mb_glass=mb_glass,
        mb_cartilage=mb_cartilage,
        anchors=anchors,
    )

    # 2. 1st Story — Triple-Bay Façade, Recessed Ogival Portal, Doors & Ground Lancets
    _build_section2_first_story_and_portal(
        mb_bricks=mb_bricks_facade,
        mb_stone=mb_stone_facade,
        mb_glass=mb_glass,
        mb_door=mb_door,
        mb_cartilage=mb_cartilage,
        anchors=anchors,
    )

    # 3. 2nd Story — Triforium Arcade, Great Rose Window, Central Gable & Tower Lancets
    _build_section3_second_story_and_rose_window(
        mb_bricks=mb_bricks_facade,
        mb_stone=mb_stone_facade,
        mb_glass=mb_glass,
        mb_rose=mb_rose,
        mb_cartilage=mb_cartilage,
        anchors=anchors,
    )

    # 4 & 5. 3rd Story Octagonal Belfries, 16 Pinnacles & 4th Story Soaring Twin Spires
    _build_section4_and_5_belfries_and_spires(
        mb_bricks=mb_bricks_facade,
        mb_stone=mb_belfry_stone,
        mb_glass=mb_glass,
        mb_rose=mb_rose,
        mb_shingles=mb_spires,
        mb_cartilage=mb_cartilage,
        anchors=anchors,
    )

    # 6. Central Dark Castle Keep (Y = 24..56, Z = +6) & 8 Writhing Tendrils
    _build_section6_castle_keep_and_tendrils(
        mb_bricks=mb_keep_bricks,
        mb_stone=mb_keep_stone,
        mb_glass=mb_glass,
        mb_shingles=mb_spires,
        mb_organs=mb_organs,
        mb_cartilage=mb_cartilage,
        anchors=anchors,
    )

    meshes = [
        mb_rock,
        mb_stairs,
        mb_pipes,
        mb_bricks_facade,
        mb_stone_facade,
        mb_glass,
        mb_rose,
        mb_door,
        mb_belfry_stone,
        mb_spires,
        mb_keep_bricks,
        mb_keep_stone,
        mb_organs,
        mb_cartilage,
    ]
    return meshes, anchors


if __name__ == "__main__":
    built_meshes, built_anchors = build_cathedral_exterior()
    tot_tris = sum(len(m.indices) // 3 for m in built_meshes)
    tot_verts = sum(len(m.positions) for m in built_meshes)
    print(f"Cathedral Exterior built: {len(built_meshes)} meshes, {tot_verts} verts, {tot_tris} tris")
    for m in built_meshes:
        print(f"  - {m.name} ({m.material_name}): {len(m.positions)} verts, {len(m.indices)//3} tris")
    print("Anchors:", built_anchors)
