import math
import numpy as np

try:
    from .glb_exporter import MeshBuilder
except ImportError:
    from glb_exporter import MeshBuilder


# ==============================================================================
# GEOMETRY & WINDING HELPERS (Guaranteed Outward / Inward Normals)
# ==============================================================================

def _v3(p):
    return np.array([float(p[0]), float(p[1]), float(p[2])], dtype=np.float32)


def add_tri_checked(mb, p0, p1, p2, expected_normal, uv0=(0.0, 0.0), uv1=(1.0, 0.0), uv2=(0.5, 1.0),
                    c0=(1.0, 1.0, 1.0, 1.0), c1=None, c2=None):
    """Adds a flat triangle whose winding is automatically oriented to match expected_normal."""
    if c1 is None:
        c1 = c0
    if c2 is None:
        c2 = c0
    v0, v1, v2 = _v3(p0), _v3(p1), _v3(p2)
    n = np.cross(v1 - v0, v2 - v0)
    if np.dot(n, _v3(expected_normal)) >= 0.0:
        mb.add_flat_triangle(p0, p1, p2, uv0, uv1, uv2, c0, c1, c2)
    else:
        mb.add_flat_triangle(p0, p2, p1, uv0, uv2, uv1, c0, c2, c1)


def add_quad_checked(mb, p0, p1, p2, p3, expected_normal,
                     uv0=(0.0, 0.0), uv1=(1.0, 0.0), uv2=(1.0, 1.0), uv3=(0.0, 1.0),
                     c0=(1.0, 1.0, 1.0, 1.0), c1=None, c2=None, c3=None):
    """Adds a flat quad (p0->p1->p2->p3) guaranteed to face expected_normal."""
    if c1 is None:
        c1 = c0
    if c2 is None:
        c2 = c1
    if c3 is None:
        c3 = c0
    v0, v1, v2 = _v3(p0), _v3(p1), _v3(p2)
    n = np.cross(v1 - v0, v2 - v0)
    if np.dot(n, _v3(expected_normal)) >= 0.0:
        mb.add_flat_quad(p0, p1, p2, p3, uv0, uv1, uv2, uv3, c0, c1, c2, c3)
    else:
        mb.add_flat_quad(p0, p3, p2, p1, uv0, uv3, uv2, uv1, c0, c3, c2, c1)


def add_box_minmax(mb, x0, y0, z0, x1, y1, z1, c_bot, c_top=None, uv_scale=0.35,
                   faces=("px", "nx", "py", "ny", "pz", "nz")):
    """Adds an axis-aligned box with outward-facing normals and vertical vertex color gradient."""
    if c_top is None:
        c_top = c_bot
    if x0 > x1:
        x0, x1 = x1, x0
    if y0 > y1:
        y0, y1 = y1, y0
    if z0 > z1:
        z0, z1 = z1, z0

    dx = (x1 - x0) * uv_scale
    dy = (y1 - y0) * uv_scale
    dz = (z1 - z0) * uv_scale

    if "pz" in faces:
        add_quad_checked(
            mb,
            (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1),
            (0.0, 0.0, 1.0),
            (0.0, 0.0), (dx, 0.0), (dx, dy), (0.0, dy),
            c_bot, c_bot, c_top, c_top,
        )
    if "nz" in faces:
        add_quad_checked(
            mb,
            (x1, y0, z0), (x0, y0, z0), (x0, y1, z0), (x1, y1, z0),
            (0.0, 0.0, -1.0),
            (0.0, 0.0), (dx, 0.0), (dx, dy), (0.0, dy),
            c_bot, c_bot, c_top, c_top,
        )
    if "px" in faces:
        add_quad_checked(
            mb,
            (x1, y0, z1), (x1, y0, z0), (x1, y1, z0), (x1, y1, z1),
            (1.0, 0.0, 0.0),
            (0.0, 0.0), (dz, 0.0), (dz, dy), (0.0, dy),
            c_bot, c_bot, c_top, c_top,
        )
    if "nx" in faces:
        add_quad_checked(
            mb,
            (x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0),
            (-1.0, 0.0, 0.0),
            (0.0, 0.0), (dz, 0.0), (dz, dy), (0.0, dy),
            c_bot, c_bot, c_top, c_top,
        )
    if "py" in faces:
        add_quad_checked(
            mb,
            (x0, y1, z1), (x1, y1, z1), (x1, y1, z0), (x0, y1, z0),
            (0.0, 1.0, 0.0),
            (0.0, 0.0), (dx, 0.0), (dx, dz), (0.0, dz),
            c_top, c_top, c_top, c_top,
        )
    if "ny" in faces:
        add_quad_checked(
            mb,
            (x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1),
            (0.0, -1.0, 0.0),
            (0.0, 0.0), (dx, 0.0), (dx, dz), (0.0, dz),
            c_bot, c_bot, c_bot, c_bot,
        )


def add_cylinder_y(mb, cx, cz, y0, y1, r0, r1, segments=8, c_bot=(0.8, 0.8, 0.8, 1.0), c_top=None,
                   cap_top=True, cap_bottom=False, theta_offset=0.0, sx=1.0, sz=1.0, uv_scale=0.4):
    """Adds a vertical prism/cylinder/frustum with outward normals."""
    if c_top is None:
        c_top = c_bot
    dy = abs(y1 - y0) * uv_scale
    for i in range(segments):
        t0 = theta_offset + (2.0 * math.pi * i) / segments
        t1 = theta_offset + (2.0 * math.pi * (i + 1)) / segments
        tm = 0.5 * (t0 + t1)

        x0_b = cx + r0 * sx * math.cos(t0)
        z0_b = cz + r0 * sz * math.sin(t0)
        x1_b = cx + r0 * sx * math.cos(t1)
        z1_b = cz + r0 * sz * math.sin(t1)

        x0_t = cx + r1 * sx * math.cos(t0)
        z0_t = cz + r1 * sz * math.sin(t0)
        x1_t = cx + r1 * sx * math.cos(t1)
        z1_t = cz + r1 * sz * math.sin(t1)

        u0 = float(i) / segments
        u1 = float(i + 1) / segments
        out_n = (math.cos(tm), 0.15 * (r0 - r1), math.sin(tm))

        add_quad_checked(
            mb,
            (x0_b, y0, z0_b), (x1_b, y0, z1_b), (x1_t, y1, z1_t), (x0_t, y1, z0_t),
            out_n,
            (u0, 0.0), (u1, 0.0), (u1, dy), (u0, dy),
            c_bot, c_bot, c_top, c_top,
        )

        if cap_top and r1 > 1e-4:
            add_tri_checked(
                mb,
                (cx, y1, cz), (x0_t, y1, z0_t), (x1_t, y1, z1_t),
                (0.0, 1.0, 0.0),
                (0.5, 0.5),
                (0.5 + 0.5 * math.cos(t0), 0.5 + 0.5 * math.sin(t0)),
                (0.5 + 0.5 * math.cos(t1), 0.5 + 0.5 * math.sin(t1)),
                c_top, c_top, c_top,
            )
        if cap_bottom and r0 > 1e-4:
            add_tri_checked(
                mb,
                (cx, y0, cz), (x1_b, y0, z1_b), (x0_b, y0, z0_b),
                (0.0, -1.0, 0.0),
                (0.5, 0.5),
                (0.5 + 0.5 * math.cos(t1), 0.5 + 0.5 * math.sin(t1)),
                (0.5 + 0.5 * math.cos(t0), 0.5 + 0.5 * math.sin(t0)),
                c_bot, c_bot, c_bot,
            )


def add_extruded_tube(mb, path_pts, radii, segments=6, colors=None, cap_ends=True):
    """Extrudes a tubular mesh along a 3D polyline with guaranteed outward normals."""
    n_pts = len(path_pts)
    if n_pts < 2:
        return
    pts = [ _v3(p) for p in path_pts ]
    if isinstance(radii, (int, float)):
        radii = [float(radii)] * n_pts
    if colors is None:
        colors = [(0.8, 0.8, 0.8, 1.0)] * n_pts
    elif isinstance(colors[0], (int, float)):
        colors = [colors] * n_pts

    rings = []
    tangents = []
    for i in range(n_pts):
        if i == 0:
            t = pts[1] - pts[0]
        elif i == n_pts - 1:
            t = pts[-1] - pts[-2]
        else:
            t = pts[i + 1] - pts[i - 1]
        tn = np.linalg.norm(t)
        t = t / (tn if tn > 1e-6 else 1.0)
        tangents.append(t)

        up = np.array([0.0, 1.0, 0.0], dtype=np.float32)
        if abs(np.dot(t, up)) > 0.92:
            up = np.array([1.0, 0.0, 0.0], dtype=np.float32)
        b1 = np.cross(t, up)
        b1 /= np.linalg.norm(b1)
        b2 = np.cross(t, b1)
        b2 /= np.linalg.norm(b2)

        ring = []
        r = radii[i]
        for s in range(segments):
            ang = (2.0 * math.pi * s) / segments
            radial = math.cos(ang) * b1 + math.sin(ang) * b2
            ring.append((pts[i] + r * radial, radial))
        rings.append(ring)

    for i in range(n_pts - 1):
        c0 = colors[i]
        c1 = colors[i + 1]
        v0 = float(i) * 0.4
        v1 = float(i + 1) * 0.4
        for s in range(segments):
            sn = (s + 1) % segments
            p00, r00 = rings[i][s]
            p10, r10 = rings[i][sn]
            p11, r11 = rings[i + 1][sn]
            p01, r01 = rings[i + 1][s]
            out_n = r00 + r10 + r11 + r01
            u0 = float(s) / segments
            u1 = float(s + 1) / segments
            add_quad_checked(
                mb, p00, p10, p11, p01, out_n,
                (u0, v0), (u1, v0), (u1, v1), (u0, v1),
                c0, c0, c1, c1,
            )

    if cap_ends:
        if radii[0] > 1e-4:
            c0 = colors[0]
            for s in range(segments):
                sn = (s + 1) % segments
                add_tri_checked(
                    mb, pts[0], rings[0][sn][0], rings[0][s][0], -tangents[0],
                    (0.5, 0.5), (1.0, 0.0), (0.0, 1.0), c0, c0, c0
                )
        if radii[-1] > 1e-4:
            c1 = colors[-1]
            for s in range(segments):
                sn = (s + 1) % segments
                add_tri_checked(
                    mb, pts[-1], rings[-1][s][0], rings[-1][sn][0], tangents[-1],
                    (0.5, 0.5), (0.0, 0.0), (1.0, 1.0), c1, c1, c1
                )


def add_pointed_gothic_arch(mb, p_start, p_end, y_spring, y_apex, arch_thickness, wall_depth,
                            segments_per_half=7, c_base=(0.74, 0.70, 0.65, 1.0), c_apex=(0.88, 0.84, 0.78, 1.0)):
    """
    Builds a 3D pointed gothic arch (extrados, intrados, and both side archivolts)
    between horizontal endpoints p_start=(x0, z0) and p_end=(x1, z1).
    """
    x0, z0 = float(p_start[0]), float(p_start[1])
    x1, z1 = float(p_end[0]), float(p_end[1])
    dx = x1 - x0
    dz = z1 - z0
    span = math.hypot(dx, dz)
    if span < 1e-4:
        return
    ux, uz = dx / span, dz / span
    # Perpendicular horizontal normal
    nx, nz = -uz, ux
    half_d = 0.5 * wall_depth
    rise = y_apex - y_spring

    def _arch_curve(side_sign, s_frac):
        """
        side_sign = -1 for left half (from start to mid), +1 for right half (from end to mid).
        s_frac in [0, 1] where 0 is springing and 1 is pointed apex.
        """
        ang = s_frac * (math.pi * 0.5)
        # Pointed gothic profile: sin(ang) vertical rise, (1 - cos^1.25) horizontal travel toward center
        u_dist = 0.5 * span * (1.0 - math.pow(max(0.0, math.cos(ang)), 1.15))
        y_in = y_spring + rise * math.sin(ang)
        y_out = y_in + arch_thickness * (0.45 + 0.55 * math.sin(ang))
        u_in = u_dist if side_sign < 0 else (span - u_dist)
        inward_push = arch_thickness * 0.75 * math.cos(ang)
        u_out = (u_dist - inward_push) if side_sign < 0 else (span - u_dist + inward_push)
        return u_in, y_in, u_out, y_out

    for side_sign in (-1, 1):
        for i in range(segments_per_half):
            t0 = float(i) / segments_per_half
            t1 = float(i + 1) / segments_per_half
            u_in0, y_in0, u_out0, y_out0 = _arch_curve(side_sign, t0)
            u_in1, y_in1, u_out1, y_out1 = _arch_curve(side_sign, t1)

            c0 = tuple(c_base[k] * (1.0 - t0) + c_apex[k] * t0 for k in range(4))
            c1 = tuple(c_base[k] * (1.0 - t1) + c_apex[k] * t1 for k in range(4))

            # 3D points on front (+n) and back (-n) faces
            def _pt(u, y, n_offset):
                return (x0 + ux * u + nx * n_offset, y, z0 + uz * u + nz * n_offset)

            in0_f = _pt(u_in0, y_in0, half_d)
            in1_f = _pt(u_in1, y_in1, half_d)
            out1_f = _pt(u_out1, y_out1, half_d)
            out0_f = _pt(u_out0, y_out0, half_d)

            in0_b = _pt(u_in0, y_in0, -half_d)
            in1_b = _pt(u_in1, y_in1, -half_d)
            out1_b = _pt(u_out1, y_out1, -half_d)
            out0_b = _pt(u_out0, y_out0, -half_d)

            # Front archivolt face (+n)
            add_quad_checked(
                mb, in0_f, in1_f, out1_f, out0_f, (nx, 0.0, nz),
                (t0, 0.0), (t1, 0.0), (t1, 1.0), (t0, 1.0), c0, c1, c1, c0
            )
            # Back archivolt face (-n)
            add_quad_checked(
                mb, in1_b, in0_b, out0_b, out1_b, (-nx, 0.0, -nz),
                (t1, 0.0), (t0, 0.0), (t0, 1.0), (t1, 1.0), c1, c0, c0, c1
            )
            # Intrados / underside soffit (facing down & toward arch center)
            inward_u = -side_sign
            soffit_n = (ux * inward_u, -0.8, uz * inward_u)
            add_quad_checked(
                mb, in0_b, in0_f, in1_f, in1_b, soffit_n,
                (0.0, t0), (1.0, t0), (1.0, t1), (0.0, t1), c0, c0, c1, c1
            )
            # Extrados / top surface (facing up & outward)
            extrados_n = (-ux * inward_u, 0.9, -uz * inward_u)
            add_quad_checked(
                mb, out0_f, out0_b, out1_b, out1_f, extrados_n,
                (0.0, t0), (1.0, t0), (1.0, t1), (0.0, t1), c0, c0, c1, c1
            )


# ==============================================================================
# 1. FLOOR, RITUAL MEDALLION & BOILING ACID CHANNELS
# ==============================================================================

def _build_floors_medallion_and_acid(meshes):
    mb_floor = MeshBuilder("Cathedral_Floor_Walkable", "Mat_CathedralFloor")
    mb_medallion = MeshBuilder("Cathedral_Floor_Medallion", "Mat_RoseWindow")
    mb_acid = MeshBuilder("Cathedral_Interior_Acid_Channels", "Mat_GastricAcid")

    # 1A. Entrance Portal Threshold Floor (Z in [-4.5, +1.0], X in [-3.4, +3.4])
    for iz in range(6):
        z0 = -4.5 + iz * (5.5 / 6.0)
        z1 = -4.5 + (iz + 1) * (5.5 / 6.0)
        for ix in range(4):
            x0 = -3.4 + ix * (6.8 / 4.0)
            x1 = -3.4 + (ix + 1) * (6.8 / 4.0)
            c_thresh = (0.72, 0.64, 0.58, 1.0)
            add_quad_checked(
                mb_floor,
                (x0, 0.0, z0), (x0, 0.0, z1), (x1, 0.0, z1), (x1, 0.0, z0),
                (0.0, 1.0, 0.0),
                (x0 * 0.25, z0 * 0.25), (x0 * 0.25, z1 * 0.25),
                (x1 * 0.25, z1 * 0.25), (x1 * 0.25, z0 * 0.25),
                c_thresh,
            )

    # 1B. Walkable Nave & Side Aisle Floor Grid (Z in [1.0, 41.0], X in [-13.2, +13.2])
    # Outer side aisles X in [-15.8, -13.2] and [+13.2, +15.8] are recessed Boiling Acid Channels,
    # bridged by stone walkways at each bay Z = 1, 9, 17, 25, 33, 41.
    pier_zs = [1.0, 9.0, 17.0, 25.0, 33.0, 41.0]

    def _floor_vertex_color(x, z):
        # Warm central processional carpet runner X in [-3.5, +3.5]
        abs_x = abs(x)
        if abs_x <= 3.5:
            runner_t = 1.0 - (abs_x / 3.5) * 0.22
            r, g, b = 0.88 * runner_t, 0.72 * runner_t, 0.64 * runner_t
        elif abs_x <= 9.5:
            t = (abs_x - 3.5) / 6.0
            r = 0.76 - 0.14 * t
            g = 0.71 - 0.13 * t
            b = 0.67 - 0.11 * t
        else:
            # Side aisles: darker stone with subtle green-amber reflection near acid channels
            t = min(1.0, (abs_x - 9.5) / 3.7)
            r = 0.56 - 0.10 * t
            g = 0.55 - 0.04 * t
            b = 0.50 - 0.12 * t

        # Subtle darkening near column bases (X = ±9.5, Z in pier_zs)
        for pz in pier_zs:
            for px in (-9.5, 9.5):
                d = math.hypot(x - px, z - pz)
                if d < 2.6:
                    ao = 0.55 + 0.45 * (d / 2.6)
                    r *= ao
                    g *= ao
                    b *= ao

        # Warm ritual glow near central medallion (0, 22.0)
        d_med = math.hypot(x, z - 22.0)
        if 6.2 <= d_med <= 9.0:
            glow = (1.0 - abs(d_med - 7.2) / 1.8) * 0.12
            r = min(1.0, r + glow)
            g = min(1.0, g + glow * 0.6)

        return (float(r), float(g), float(b), 1.0)

    nx_tiles = 24
    nz_tiles = 40
    for iz in range(nz_tiles):
        z0 = 1.0 + (40.0 * iz) / nz_tiles
        z1 = 1.0 + (40.0 * (iz + 1)) / nz_tiles
        for ix in range(nx_tiles):
            x0 = -13.2 + (26.4 * ix) / nx_tiles
            x1 = -13.2 + (26.4 * (ix + 1)) / nx_tiles
            c00 = _floor_vertex_color(x0, z0)
            c01 = _floor_vertex_color(x0, z1)
            c11 = _floor_vertex_color(x1, z1)
            c10 = _floor_vertex_color(x1, z0)
            add_quad_checked(
                mb_floor,
                (x0, 0.0, z0), (x0, 0.0, z1), (x1, 0.0, z1), (x1, 0.0, z0),
                (0.0, 1.0, 0.0),
                (x0 * 0.25, z0 * 0.25), (x0 * 0.25, z1 * 0.25),
                (x1 * 0.25, z1 * 0.25), (x1 * 0.25, z0 * 0.25),
                c00, c01, c11, c10,
            )

    # Outer wall ledge strips (X in [-16.0, -15.8] and [+15.8, +16.0]) and bay bridges across acid moats
    for side in (-1.0, 1.0):
        # Wall strip
        xa = -16.0 if side < 0 else 15.8
        xb = -15.8 if side < 0 else 16.0
        add_box_minmax(mb_floor, xa, -0.6, 1.0, xb, 0.02, 41.0,
                       (0.38, 0.35, 0.33, 1.0), (0.48, 0.45, 0.42, 1.0), faces=("py", "px", "nx"))
        # Stone walkways bridging the acid channels at each bay (Z = 1, 9, 17, 25, 33, 41)
        ca = -15.8 if side < 0 else 13.2
        cb = -13.2 if side < 0 else 15.8
        for bz in pier_zs:
            z_lo = max(1.0, bz - 0.85)
            z_hi = min(41.0, bz + 0.85)
            add_box_minmax(
                mb_floor, ca, -0.18, z_lo, cb, 0.03, z_hi,
                (0.45, 0.44, 0.40, 1.0), (0.64, 0.60, 0.56, 1.0),
                faces=("py", "pz", "nz")
            )

    # 1C. Central Boss Ritual Rose Medallion at (0, 0.03, 22.0), radius 6.5m
    med_cx, med_cy, med_cz = 0.0, 0.03, 22.0
    med_radius = 6.5
    med_rings = [0.0, 1.1, 2.3, 3.6, 4.9, 5.9, 6.5]
    med_segs = 32
    for r_idx in range(len(med_rings) - 1):
        r_in = med_rings[r_idx]
        r_out = med_rings[r_idx + 1]
        for s in range(med_segs):
            a0 = (2.0 * math.pi * s) / med_segs
            a1 = (2.0 * math.pi * (s + 1)) / med_segs
            # UV mapped cleanly into [0, 1] circle so Mat_RoseWindow radial art aligns
            uv_c = (0.5, 0.5)
            uv0_in = (0.5 + 0.5 * (r_in / med_radius) * math.cos(a0),
                      0.5 + 0.5 * (r_in / med_radius) * math.sin(a0))
            uv1_in = (0.5 + 0.5 * (r_in / med_radius) * math.cos(a1),
                      0.5 + 0.5 * (r_in / med_radius) * math.sin(a1))
            uv1_out = (0.5 + 0.5 * (r_out / med_radius) * math.cos(a1),
                       0.5 + 0.5 * (r_out / med_radius) * math.sin(a1))
            uv0_out = (0.5 + 0.5 * (r_out / med_radius) * math.cos(a0),
                       0.5 + 0.5 * (r_out / med_radius) * math.sin(a0))

            p0_in = (med_cx + r_in * math.cos(a0), med_cy, med_cz + r_in * math.sin(a0))
            p1_in = (med_cx + r_in * math.cos(a1), med_cy, med_cz + r_in * math.sin(a1))
            p1_out = (med_cx + r_out * math.cos(a1), med_cy, med_cz + r_out * math.sin(a1))
            p0_out = (med_cx + r_out * math.cos(a0), med_cy, med_cz + r_out * math.sin(a0))

            # Subtle pulsing crimson-gold vertex tint across rings
            petal_pulse = 0.90 + 0.10 * math.cos(8.0 * a0)
            c_in = (1.0 * petal_pulse, 0.86 * petal_pulse, 0.82 * petal_pulse, 1.0)
            c_out = (0.94 * petal_pulse, 0.78 * petal_pulse, 0.74 * petal_pulse, 1.0)

            if r_idx == 0:
                add_tri_checked(
                    mb_medallion,
                    (med_cx, med_cy + 0.01, med_cz), p0_out, p1_out,
                    (0.0, 1.0, 0.0),
                    uv_c, uv0_out, uv1_out,
                    (1.0, 0.95, 0.90, 1.0), c_out, c_out,
                )
            else:
                add_quad_checked(
                    mb_medallion,
                    p0_in, p0_out, p1_out, p1_in,
                    (0.0, 1.0, 0.0),
                    uv0_in, uv0_out, uv1_out, uv1_in,
                    c_in, c_out, c_out, c_in,
                )

    # 1D. Elevated Sanctuary & Polygonal Apse Floor (Z in [43.5, 56.0], Y = 1.25)
    # 5-sided polygonal apse contour narrows from X = ±16.0 at Z = 41.0 to X = ±8.0 at Z = 56.0.
    def _apse_half_width(z):
        if z <= 41.0:
            return 16.0
        elif z <= 49.0:
            # 16.0 -> 14.0
            return 16.0 - 2.0 * ((z - 41.0) / 8.0)
        else:
            # 14.0 -> 8.0 at Z=56.0
            return 14.0 - 6.0 * ((z - 49.0) / 7.0)

    # Lower apse margin floor between Z=41.0 and Z=43.5 outside the central steps
    z_step_bands = np.linspace(41.0, 55.8, 16)
    for iz in range(len(z_step_bands) - 1):
        za, zb = float(z_step_bands[iz]), float(z_step_bands[iz + 1])
        # Sanctuary platform starts at Z=43.5 (and steps occupy Z=41.0..43.5)
        y_plat = 1.25 if za >= 43.4 else 0.0
        hw_a = max(4.0, _apse_half_width(za) - 1.8)
        hw_b = max(4.0, _apse_half_width(zb) - 1.8)
        nx_ap = 12
        for ix in range(nx_ap):
            fa0 = -1.0 + (2.0 * ix) / nx_ap
            fa1 = -1.0 + (2.0 * (ix + 1)) / nx_ap
            xa0, xa1 = fa0 * hw_a, fa1 * hw_a
            xb0, xb1 = fa0 * hw_b, fa1 * hw_b
            # Warm altar glow near (0, 47.5)
            d_alt = math.hypot(0.5 * (xa0 + xa1), 0.5 * (za + zb) - 47.5)
            warm = max(0.0, 1.0 - d_alt / 10.0)
            c_sanct = (0.72 + 0.18 * warm, 0.62 + 0.10 * warm, 0.58 + 0.06 * warm, 1.0)
            add_quad_checked(
                mb_floor,
                (xa0, y_plat, za), (xb0, y_plat, zb), (xb1, y_plat, zb), (xa1, y_plat, za),
                (0.0, 1.0, 0.0),
                (xa0 * 0.25, za * 0.25), (xb0 * 0.25, zb * 0.25),
                (xb1 * 0.25, zb * 0.25), (xa1 * 0.25, za * 0.25),
                c_sanct,
            )

    # 1E. Recessed Boiling Acid Channels (Y = -0.55) along Side Aisles & Apse Perimeter
    acid_y = -0.55
    for side in (-1.0, 1.0):
        x_outer = -15.8 if side < 0 else 13.2
        x_inner = -13.2 if side < 0 else 15.8
        nz_acid = 48
        nx_acid = 4
        for iz in range(nz_acid):
            z0 = 1.2 + (39.6 * iz) / nz_acid
            z1 = 1.2 + (39.6 * (iz + 1)) / nz_acid
            for ix in range(nx_acid):
                x0 = x_outer + (x_inner - x_outer) * (ix / float(nx_acid))
                x1 = x_outer + (x_inner - x_outer) * ((ix + 1) / float(nx_acid))

                def _acid_pt(xx, zz):
                    ripple = 0.045 * math.sin(zz * 1.3 + xx * 2.1) * math.cos(zz * 0.7 - xx * 1.5)
                    return (xx, acid_y + ripple, zz)

                def _acid_col(xx, zz):
                    glow = 0.5 + 0.5 * math.sin(zz * 1.1 + xx * 1.7)
                    return (0.55 + 0.35 * glow, 0.85 + 0.15 * glow, 0.25 + 0.20 * glow, 1.0)

                add_quad_checked(
                    mb_acid,
                    _acid_pt(x0, z0), _acid_pt(x0, z1), _acid_pt(x1, z1), _acid_pt(x1, z0),
                    (0.0, 1.0, 0.0),
                    (x0 * 0.4, z0 * 0.4), (x0 * 0.4, z1 * 0.4),
                    (x1 * 0.4, z1 * 0.4), (x1 * 0.4, z0 * 0.4),
                    _acid_col(x0, z0), _acid_col(x0, z1), _acid_col(x1, z1), _acid_col(x1, z0),
                )

        # Bubbling acid blisters along the moat surface
        for b_idx in range(14):
            bz = 3.0 + b_idx * 2.75
            bx = -14.5 + 0.35 * math.sin(b_idx * 2.3) if side < 0 else 14.5 + 0.35 * math.cos(b_idx * 1.9)
            br = 0.22 + 0.10 * (b_idx % 3)
            add_cylinder_y(
                mb_acid, bx, bz, acid_y - 0.02, acid_y + 0.14, br, br * 0.35,
                segments=6, c_bot=(0.62, 0.88, 0.25, 1.0), c_top=(0.95, 1.0, 0.55, 1.0), cap_top=True
            )

    # Apse perimeter acid moat flanking the sanctuary platform (Z = 41.0..54.5)
    for side in (-1.0, 1.0):
        for iz in range(12):
            za = 41.0 + iz * (13.2 / 12.0)
            zb = 41.0 + (iz + 1) * (13.2 / 12.0)
            hwa_out = _apse_half_width(za) - 0.3
            hwa_in = _apse_half_width(za) - 1.75
            hwb_out = _apse_half_width(zb) - 0.3
            hwb_in = _apse_half_width(zb) - 1.75
            p0 = (side * hwa_in, acid_y, za)
            p1 = (side * hwb_in, acid_y, zb)
            p2 = (side * hwb_out, acid_y, zb)
            p3 = (side * hwa_out, acid_y, za)
            add_quad_checked(
                mb_acid, p0, p1, p2, p3, (0.0, 1.0, 0.0),
                (0.0, za * 0.4), (0.0, zb * 0.4), (1.0, zb * 0.4), (1.0, za * 0.4),
                (0.68, 0.92, 0.32, 1.0), (0.82, 0.98, 0.44, 1.0),
                (0.75, 0.94, 0.36, 1.0), (0.64, 0.88, 0.28, 1.0),
            )

    meshes.extend([mb_floor, mb_medallion, mb_acid])


# ==============================================================================
# 2. THE GRAND COLONNADE — 12 COMPOUND GOTHIC PIERS & POINTED NAVE ARCADE
# ==============================================================================

def _build_single_compound_pier(mb, cx, cz, include_nave_shafts=True):
    """
    Builds a richly detailed Compound Gothic Pier at (cx, cz):
    - Stepped octagonal plinth base (Y = 0.0..1.4)
    - Central octagonal core pier + 4 attached half-columns/shafts (Y = 1.4..11.5)
    - Horizontal shaft rings at mid-height (Y = 5.0 and Y = 8.4)
    - Flared Gothic Foliate/Vertebra Capital (Y = 11.5..12.8)
    - Upper triple vaulting shafts rising from capital to vault springing (Y = 12.8..22.0)
    """
    c_dark = (0.48, 0.44, 0.41, 1.0)
    c_mid = (0.72, 0.68, 0.63, 1.0)
    c_light = (0.86, 0.82, 0.76, 1.0)
    c_cap = (0.92, 0.88, 0.82, 1.0)

    # Stepped Octagonal Plinth Base (Y = 0.0..1.4)
    add_cylinder_y(mb, cx, cz, 0.0, 0.45, 1.42, 1.36, segments=8,
                   c_bot=c_dark, c_top=c_mid, cap_top=True, theta_offset=math.pi / 8.0)
    add_cylinder_y(mb, cx, cz, 0.45, 0.95, 1.24, 1.12, segments=8,
                   c_bot=c_mid, c_top=c_light, cap_top=True, theta_offset=math.pi / 8.0)
    add_cylinder_y(mb, cx, cz, 0.95, 1.40, 1.06, 0.92, segments=8,
                   c_bot=c_light, c_top=c_mid, cap_top=True, theta_offset=math.pi / 8.0)

    # Central Core Pier (Y = 1.4..11.5) wrapped by horizontal shaft rings
    add_cylinder_y(mb, cx, cz, 1.40, 11.50, 0.78, 0.76, segments=8,
                   c_bot=c_mid, c_top=c_light, cap_top=False, theta_offset=math.pi / 8.0)

    # 4 Attached Colonnette Shafts (N, S, E, W) + 4 Diagonal Flute Shafts
    shaft_offsets = [
        (0.76, 0.0, 0.34, 6),
        (-0.76, 0.0, 0.34, 6),
        (0.0, 0.76, 0.34, 6),
        (0.0, -0.76, 0.34, 6),
        (0.52, 0.52, 0.20, 4),
        (-0.52, 0.52, 0.20, 4),
        (0.52, -0.52, 0.20, 4),
        (-0.52, -0.52, 0.20, 4),
    ]
    for ox, oz, sr, n_seg in shaft_offsets:
        sx, sz = cx + ox, cz + oz
        # Individual colonnette base moulding
        add_cylinder_y(mb, sx, sz, 0.95, 1.45, sr * 1.32, sr * 1.05, segments=n_seg,
                       c_bot=c_dark, c_top=c_light, cap_top=False)
        add_cylinder_y(mb, sx, sz, 1.45, 11.50, sr, sr * 0.98, segments=n_seg,
                       c_bot=c_mid, c_top=c_light, cap_top=False)
        # Flared colonnette bell capital (Y = 11.5..12.15)
        add_cylinder_y(mb, sx, sz, 11.50, 12.15, sr * 0.98, sr * 1.48, segments=n_seg,
                       c_bot=c_mid, c_top=c_cap, cap_top=True)

    # Horizontal Annular Shaft Rings (Banding rings at Y = 4.9..5.15 and Y = 8.3..8.55, plus necking at 11.35..11.55)
    for ry in (4.92, 8.32, 11.38):
        add_cylinder_y(mb, cx, cz, ry, ry + 0.22, 1.16, 1.16, segments=8,
                       c_bot=c_dark, c_top=c_cap, cap_top=True, cap_bottom=False, theta_offset=math.pi / 8.0)

    # Richly Flared Gothic Foliate / Vertebra Capital (Y = 11.5..12.8)
    add_cylinder_y(mb, cx, cz, 11.50, 12.05, 0.88, 1.28, segments=8,
                   c_bot=c_mid, c_top=c_light, cap_top=False, theta_offset=math.pi / 8.0)
    add_cylinder_y(mb, cx, cz, 12.05, 12.48, 1.28, 1.48, segments=8,
                   c_bot=c_light, c_top=c_cap, cap_top=False, theta_offset=math.pi / 8.0)
    # Molded Square/Octagonal Abacus Slab atop the capital (Y = 12.48..12.80)
    add_box_minmax(mb, cx - 1.36, 12.48, cz - 1.36, cx + 1.36, 12.80, cz + 1.36,
                   c_mid, c_cap, faces=("px", "nx", "py", "ny", "pz", "nz"))

    # Upper Engaged Triple Vaulting Shafts rising toward Nave High Vaults (Y = 12.8..22.0)
    if include_nave_shafts:
        inward_sign = -1.0 if cx > 0 else 1.0
        vx = cx + inward_sign * 0.72
        for dz_off, vr in ((0.0, 0.24), (-0.32, 0.16), (0.32, 0.16)):
            add_cylinder_y(mb, vx, cz + dz_off, 12.80, 22.00, vr, vr * 0.95, segments=5,
                           c_bot=c_mid, c_top=c_light, cap_top=False)
        # Upper vaulting capital at Y = 21.4..22.0
        add_box_minmax(mb, vx - 0.38, 21.45, cz - 0.58, vx + 0.38, 22.00, cz + 0.58,
                       c_mid, c_cap)


def _build_colonnade_and_arches(meshes):
    mb = MeshBuilder("Cathedral_Nave_Columns_And_Arches", "Mat_OssifiedStone")

    pier_zs = [1.0, 9.0, 17.0, 25.0, 33.0, 41.0]
    c_stone_dark = (0.50, 0.46, 0.42, 1.0)
    c_stone_mid = (0.74, 0.70, 0.65, 1.0)
    c_stone_light = (0.89, 0.85, 0.79, 1.0)

    # 2A. 12 Compound Gothic Piers (6 Left at X = -9.5, 6 Right at X = +9.5)
    for px in (-9.5, 9.5):
        for pz in pier_zs:
            _build_single_compound_pier(mb, px, pz, include_nave_shafts=True)

    # 2B. Longitudinal Pointed Nave Arches (Y = 12.8..17.2) across all 5 bays (8.0m each)
    # Multi-order stepped gothic archivolts (inner order + outer wider order)
    for px in (-9.5, 9.5):
        for i in range(len(pier_zs) - 1):
            z_a = pier_zs[i] + 0.85
            z_b = pier_zs[i + 1] - 0.85
            # Inner archivolt order
            add_pointed_gothic_arch(
                mb, (px, z_a), (px, z_b),
                y_spring=12.80, y_apex=16.85, arch_thickness=0.55, wall_depth=0.68,
                segments_per_half=5, c_base=c_stone_mid, c_apex=c_stone_light
            )
            # Outer moulded archivolt order
            add_pointed_gothic_arch(
                mb, (px, z_a - 0.25), (px, z_b + 0.25),
                y_spring=12.80, y_apex=17.25, arch_thickness=0.58, wall_depth=1.18,
                segments_per_half=5, c_base=c_stone_dark, c_apex=c_stone_mid
            )

    # Horizontal Stringcourse Cornice above the Nave Arcade (Y = 17.3..17.65, Z = 1..41)
    for px in (-9.5, 9.5):
        add_box_minmax(mb, px - 0.72, 17.28, 1.0, px + 0.72, 17.68, 41.0,
                       c_stone_dark, c_stone_light)

    # 2C. Transverse Side-Aisle Pointed Arches spanning from X = ±9.5 to outer walls X = ±16.0
    for pz in pier_zs:
        # Left aisle transverse arch (X = -15.6 to -10.2)
        add_pointed_gothic_arch(
            mb, (-15.6, pz), (-10.2, pz),
            y_spring=9.80, y_apex=13.20, arch_thickness=0.48, wall_depth=0.62,
            segments_per_half=5, c_base=c_stone_mid, c_apex=c_stone_light
        )
        # Right aisle transverse arch (X = +10.2 to +15.6)
        add_pointed_gothic_arch(
            mb, (10.2, pz), (15.6, pz),
            y_spring=9.80, y_apex=13.20, arch_thickness=0.48, wall_depth=0.62,
            segments_per_half=5, c_base=c_stone_mid, c_apex=c_stone_light
        )

    # Diagonal Side-Aisle Groin Vault Ribs across each side-aisle bay
    for side in (-1.0, 1.0):
        x_in = side * 9.8
        x_out = side * 15.6
        for i in range(len(pier_zs) - 1):
            z0 = pier_zs[i]
            z1 = pier_zs[i + 1]
            add_pointed_gothic_arch(
                mb, (x_in, z0), (x_out, z1),
                y_spring=9.80, y_apex=13.65, arch_thickness=0.34, wall_depth=0.34,
                segments_per_half=4, c_base=c_stone_dark, c_apex=c_stone_light
            )
            add_pointed_gothic_arch(
                mb, (x_out, z0), (x_in, z1),
                y_spring=9.80, y_apex=13.65, arch_thickness=0.34, wall_depth=0.34,
                segments_per_half=4, c_base=c_stone_dark, c_apex=c_stone_light
            )

    # 2D. Octagonal Carved Stone Border Ring around the Central Boss Ritual Medallion (0, 0.04, 22.0)
    med_cx, med_cz = 0.0, 22.0
    r_inner, r_outer = 6.32, 6.98
    border_segs = 16
    for s in range(border_segs):
        a0 = (2.0 * math.pi * s) / border_segs
        a1 = (2.0 * math.pi * (s + 1)) / border_segs
        am = 0.5 * (a0 + a1)
        x0_i, z0_i = med_cx + r_inner * math.cos(a0), med_cz + r_inner * math.sin(a0)
        x1_i, z1_i = med_cx + r_inner * math.cos(a1), med_cz + r_inner * math.sin(a1)
        x0_o, z0_o = med_cx + r_outer * math.cos(a0), med_cz + r_outer * math.sin(a0)
        x1_o, z1_o = med_cx + r_outer * math.cos(a1), med_cz + r_outer * math.sin(a1)

        # Top bevel face
        add_quad_checked(
            mb,
            (x0_i, 0.09, z0_i), (x0_o, 0.07, z0_o), (x1_o, 0.07, z1_o), (x1_i, 0.09, z1_i),
            (0.0, 1.0, 0.0),
            (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0),
            c_stone_light, c_stone_mid, c_stone_mid, c_stone_light,
        )
        # Outer rim curb
        add_quad_checked(
            mb,
            (x0_o, 0.0, z0_o), (x1_o, 0.0, z1_o), (x1_o, 0.07, z1_o), (x0_o, 0.07, z0_o),
            (math.cos(am), 0.2, math.sin(am)),
            (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0),
            c_stone_dark, c_stone_dark, c_stone_mid, c_stone_mid,
        )
        # 8 Octagonal Corner Boss Studs around the medallion ring
        if s % 2 == 0:
            bx = med_cx + 6.65 * math.cos(a0)
            bz = med_cz + 6.65 * math.sin(a0)
            add_cylinder_y(mb, bx, bz, 0.0, 0.16, 0.36, 0.26, segments=6,
                           c_bot=c_stone_mid, c_top=c_stone_light, cap_top=True)

    # 2E. Stone Curb Walls & Recessed Trench Walls for Side-Aisle Acid Channels
    for side in (-1.0, 1.0):
        x_in_curb = -13.2 if side < 0 else 13.2
        x_out_curb = -15.8 if side < 0 else 15.8
        curb_half = 0.18
        for cx_curb in (x_in_curb, x_out_curb):
            add_box_minmax(
                mb, cx_curb - curb_half, -0.60, 1.0, cx_curb + curb_half, 0.18, 41.0,
                c_stone_dark, c_stone_mid, faces=("px", "nx", "py", "pz", "nz")
            )

    meshes.append(mb)


# ==============================================================================
# 3. 3-TIER GOTHIC INTERIOR WALLS, TRIFORIUM, CLERESTORY & VOLUMETRIC GOD-RAYS
# ==============================================================================

def _build_three_tier_side_wall(mb_flesh, mb_stone, mb_glass, side_sign):
    """
    Builds one full 40m side wall (Z = 1..41, Y = 0..24) at X = side_sign * 16.0:
    - Tier 1 (Y = 0..10m): Engaged wall half-columns, blind gothic arcading, recessed side-aisle lancet windows.
    - Tier 2 (Y = 10..15m): Recessed Triforium Gallery behind pointed trefoil colonnette arches & gothic balustrades.
    - Tier 3 (Y = 15..24m): Clerestory Double-Lancet + Rose Oculi Stained Glass Windows framed by mullions & hood moulds.
    """
    wall_x = side_sign * 16.0
    inward_nx = -side_sign  # +1 for Left wall (X=-16), -1 for Right wall (X=+16)
    inward_dir = (inward_nx, 0.0, 0.0)

    c_flesh_bot = (0.58, 0.28, 0.28, 1.0)
    c_flesh_mid = (0.72, 0.36, 0.35, 1.0)
    c_flesh_top = (0.82, 0.44, 0.42, 1.0)
    c_stone_dark = (0.48, 0.44, 0.40, 1.0)
    c_stone_mid = (0.74, 0.70, 0.64, 1.0)
    c_stone_light = (0.90, 0.86, 0.80, 1.0)
    c_glass_bot = (0.85, 0.35, 0.40, 1.0)
    c_glass_top = (1.00, 0.82, 0.55, 1.0)

    pier_zs = [1.0, 9.0, 17.0, 25.0, 33.0, 41.0]

    # Engaged Wall Half-Columns at every bay boundary (Z = 1, 9, 17, 25, 33, 41)
    for pz in pier_zs:
        col_x = wall_x + inward_nx * 0.35
        add_cylinder_y(mb_stone, col_x, pz, 0.0, 1.1, 0.65, 0.56, segments=6,
                       c_bot=c_stone_dark, c_top=c_stone_mid, cap_top=True)
        add_cylinder_y(mb_stone, col_x, pz, 1.1, 9.8, 0.46, 0.44, segments=6,
                       c_bot=c_stone_mid, c_top=c_stone_light, cap_top=True)
        add_cylinder_y(mb_stone, col_x, pz, 9.8, 22.5, 0.32, 0.30, segments=6,
                       c_bot=c_stone_mid, c_top=c_stone_light, cap_top=True)

    # Continuous Horizontal Stringcourses separating Tier 1 (Y=10), Tier 2 (Y=15), and Roof Cornice (Y=24)
    for sy in (2.1, 10.0, 15.0, 23.8):
        x_a = min(wall_x, wall_x + inward_nx * 0.55)
        x_b = max(wall_x, wall_x + inward_nx * 0.55)
        add_box_minmax(mb_stone, x_a, sy - 0.18, 1.0, x_b, sy + 0.18, 41.0,
                       c_stone_dark, c_stone_light)

    # Build each of the 5 Bays (8.0m wide each)
    for bay_idx in range(5):
        z0 = pier_zs[bay_idx]
        z1 = pier_zs[bay_idx + 1]
        zm = 0.5 * (z0 + z1)

        # --- TIER 1: Ground Aisle Wall (Y = 0..10m) ---
        # Lower dado masonry wall (Y = 0..2.2m)
        add_quad_checked(
            mb_flesh,
            (wall_x, 0.0, z0), (wall_x, 0.0, z1), (wall_x, 2.2, z1), (wall_x, 2.2, z0),
            inward_dir,
            (0.0, 0.0), (2.0, 0.0), (2.0, 0.6), (0.0, 0.6),
            c_flesh_bot, c_flesh_bot, c_flesh_mid, c_flesh_mid,
        )
        # Side-aisle masonry piers flanking the central lancet window (Y = 2.2..10.0m)
        win_z0, win_z1 = zm - 1.35, zm + 1.35
        for za, zb in ((z0, win_z0), (win_z1, z1)):
            add_quad_checked(
                mb_flesh,
                (wall_x, 2.2, za), (wall_x, 2.2, zb), (wall_x, 10.0, zb), (wall_x, 10.0, za),
                inward_dir,
                (0.0, 0.0), (1.0, 0.0), (1.0, 2.0), (0.0, 2.0),
                c_flesh_mid, c_flesh_mid, c_flesh_top, c_flesh_top,
            )
        # Spandrel above ground lancet window (Y = 8.4..10.0m)
        add_quad_checked(
            mb_flesh,
            (wall_x, 8.4, win_z0), (wall_x, 8.4, win_z1), (wall_x, 10.0, win_z1), (wall_x, 10.0, win_z0),
            inward_dir,
            (0.0, 0.0), (1.0, 0.0), (1.0, 0.5), (0.0, 0.5),
            c_flesh_mid, c_flesh_mid, c_flesh_top, c_flesh_top,
        )

        # Blind Gothic Arcading (2 flanking blind arches + 1 central lancet window arch)
        for arch_za, arch_zb, y_ap in ((z0 + 0.45, win_z0 - 0.1, 7.6),
                                       (win_z0, win_z1, 8.5),
                                       (win_z1 + 0.1, z1 - 0.45, 7.6)):
            arch_x = wall_x + inward_nx * 0.18
            add_pointed_gothic_arch(
                mb_stone, (arch_x, arch_za), (arch_x, arch_zb),
                y_spring=6.2, y_apex=y_ap, arch_thickness=0.28, wall_depth=0.36,
                segments_per_half=5, c_base=c_stone_mid, c_apex=c_stone_light
            )
            # Jamb shafts for the arch
            for jz in (arch_za, arch_zb):
                add_cylinder_y(mb_stone, arch_x, jz, 2.1, 6.2, 0.14, 0.13, segments=4,
                               c_bot=c_stone_dark, c_top=c_stone_light, cap_top=False)

        # Recessed Side-Aisle Stained Glass Lancet Window (Y = 2.2..8.4m)
        gx = wall_x - inward_nx * 0.15
        add_quad_checked(
            mb_glass,
            (gx, 2.2, win_z0), (gx, 2.2, win_z1), (gx, 6.4, win_z1), (gx, 6.4, win_z0),
            inward_dir,
            (0.0, 0.0), (1.0, 0.0), (1.0, 0.75), (0.0, 0.75),
            c_glass_bot, c_glass_bot, c_glass_top, c_glass_top,
        )
        add_tri_checked(
            mb_glass,
            (gx, 6.4, win_z0), (gx, 6.4, win_z1), (gx, 8.45, zm),
            inward_dir,
            (0.0, 0.75), (1.0, 0.75), (0.5, 1.0),
            c_glass_top, c_glass_top, (1.0, 0.95, 0.75, 1.0),
        )

        # Sacristy Carved Gothic Door in Bay 2 (Z = 17..25, zm = 21.0)
        if bay_idx == 2:
            door_x = wall_x + inward_nx * 0.08
            add_pointed_gothic_arch(
                mb_stone, (door_x, zm - 1.15), (door_x, zm + 1.15),
                y_spring=1.4, y_apex=2.35, arch_thickness=0.24, wall_depth=0.32,
                segments_per_half=4, c_base=c_stone_dark, c_apex=c_stone_light
            )

        # --- TIER 2: Triforium Gallery (Y = 10.0..15.0m) ---
        # Recessed dark gallery backing wall & floor
        trif_back_x = wall_x - inward_nx * 1.15
        add_quad_checked(
            mb_flesh,
            (trif_back_x, 10.0, z0), (trif_back_x, 10.0, z1),
            (trif_back_x, 15.0, z1), (trif_back_x, 15.0, z0),
            inward_dir,
            (0.0, 0.0), (2.0, 0.0), (2.0, 1.0), (0.0, 1.0),
            (0.32, 0.16, 0.18, 1.0), (0.32, 0.16, 0.18, 1.0),
            (0.45, 0.22, 0.24, 1.0), (0.45, 0.22, 0.24, 1.0),
        )
        # Gothic Balustrade Parapet at the bottom of the Triforium (Y = 10.0..11.15)
        bal_x = wall_x + inward_nx * 0.16
        add_box_minmax(
            mb_stone,
            min(wall_x, bal_x), 10.0, z0 + 0.3, max(wall_x, bal_x), 11.15, z1 - 0.3,
            c_stone_dark, c_stone_mid
        )
        # 4 Pointed Trefoil Colonnette Arches per Bay in the Triforium (Y = 11.15..14.7)
        n_trif = 4
        span_trif = (z1 - z0 - 0.8) / n_trif
        for it in range(n_trif):
            tza = z0 + 0.4 + it * span_trif
            tzb = tza + span_trif
            add_cylinder_y(mb_stone, bal_x, tza, 11.15, 13.5, 0.13, 0.12, segments=4,
                           c_bot=c_stone_mid, c_top=c_stone_light, cap_top=False)
            if it == n_trif - 1:
                add_cylinder_y(mb_stone, bal_x, tzb, 11.15, 13.5, 0.13, 0.12, segments=4,
                               c_bot=c_stone_mid, c_top=c_stone_light, cap_top=False)
            add_pointed_gothic_arch(
                mb_stone, (bal_x, tza), (bal_x, tzb),
                y_spring=13.5, y_apex=14.65, arch_thickness=0.22, wall_depth=0.28,
                segments_per_half=3, c_base=c_stone_mid, c_apex=c_stone_light
            )

        # --- TIER 3: Clerestory Double-Lancet + Rose Oculi Stained Glass Windows (Y = 15.0..24.0m) ---
        # Outer clerestory masonry surrounds
        cl_z0, cl_z1 = z0 + 1.15, z1 - 1.15
        for za, zb in ((z0, cl_z0), (cl_z1, z1)):
            add_quad_checked(
                mb_flesh,
                (wall_x, 15.0, za), (wall_x, 15.0, zb), (wall_x, 23.8, zb), (wall_x, 23.8, za),
                inward_dir,
                (0.0, 0.0), (0.6, 0.0), (0.6, 2.0), (0.0, 2.0),
                c_flesh_mid, c_flesh_mid, c_flesh_top, c_flesh_top,
            )
        # Double Lancet Window Panes (Left Lancet + Right Lancet, Y = 15.4..21.2m)
        lancet_pairs = [(cl_z0, zm - 0.12), (zm + 0.12, cl_z1)]
        for lza, lzb in lancet_pairs:
            lzm = 0.5 * (lza + lzb)
            add_quad_checked(
                mb_glass,
                (gx, 15.4, lza), (gx, 15.4, lzb), (gx, 19.6, lzb), (gx, 19.6, lza),
                inward_dir,
                (0.0, 0.0), (1.0, 0.0), (1.0, 0.75), (0.0, 0.75),
                c_glass_bot, c_glass_bot, c_glass_top, c_glass_top,
            )
            add_tri_checked(
                mb_glass,
                (gx, 19.6, lza), (gx, 19.6, lzb), (gx, 21.1, lzm),
                inward_dir,
                (0.0, 0.75), (1.0, 0.75), (0.5, 1.0),
                c_glass_top, c_glass_top, (1.0, 0.95, 0.80, 1.0),
            )
            # Stone lancet arch hood
            add_pointed_gothic_arch(
                mb_stone, (bal_x, lza), (bal_x, lzb),
                y_spring=19.6, y_apex=21.15, arch_thickness=0.22, wall_depth=0.30,
                segments_per_half=4, c_base=c_stone_mid, c_apex=c_stone_light
            )

        # Central & Side Stone Mullions for the Clerestory Window
        for mz in (cl_z0, zm, cl_z1):
            add_cylinder_y(mb_stone, bal_x, mz, 15.2, 20.6, 0.15, 0.14, segments=6,
                           c_bot=c_stone_mid, c_top=c_stone_light, cap_top=True)

        # Rose Oculus Window above the Double Lancets (centered at Y = 22.15, Z = zm, radius = 1.15m)
        oc_y, oc_r = 22.15, 1.15
        oc_segs = 12
        for s in range(oc_segs):
            a0 = (2.0 * math.pi * s) / oc_segs
            a1 = (2.0 * math.pi * (s + 1)) / oc_segs
            p0 = (gx, oc_y + oc_r * math.sin(a0), zm + oc_r * math.cos(a0))
            p1 = (gx, oc_y + oc_r * math.sin(a1), zm + oc_r * math.cos(a1))
            add_tri_checked(
                mb_glass,
                (gx, oc_y, zm), p0, p1,
                inward_dir,
                (0.5, 0.5),
                (0.5 + 0.5 * math.cos(a0), 0.5 + 0.5 * math.sin(a0)),
                (0.5 + 0.5 * math.cos(a1), 0.5 + 0.5 * math.sin(a1)),
                (1.0, 0.95, 0.80, 1.0), c_glass_top, c_glass_bot,
            )
        # Outer Enclosing Clerestory Hood Mould Arch (Y = 19.8..23.5m)
        add_pointed_gothic_arch(
            mb_stone, (bal_x, cl_z0), (bal_x, cl_z1),
            y_spring=19.8, y_apex=23.45, arch_thickness=0.32, wall_depth=0.38,
            segments_per_half=6, c_base=c_stone_mid, c_apex=c_stone_light
        )

    # Upper Nave Arcade Spandrel & Clerestory Inner Wall (at X = side_sign * 9.5, Y = 17.6..22.2)
    nave_wall_x = side_sign * 9.5
    add_quad_checked(
        mb_flesh,
        (nave_wall_x, 17.6, 1.0), (nave_wall_x, 17.6, 41.0),
        (nave_wall_x, 22.2, 41.0), (nave_wall_x, 22.2, 1.0),
        inward_dir,
        (0.0, 0.0), (8.0, 0.0), (8.0, 1.2), (0.0, 1.2),
        c_flesh_mid, c_flesh_mid, c_flesh_top, c_flesh_top,
    )


def _build_walls_windows_and_godrays(meshes):
    mb_flesh = MeshBuilder("Cathedral_Walls_FleshMasonry", "Mat_FleshBricks")
    mb_stone = MeshBuilder("Cathedral_Walls_StoneTracery", "Mat_OssifiedStone")
    mb_right_cutaway = MeshBuilder("Cathedral_Wall_Right_Cutaway", "Mat_OssifiedStone")
    mb_glass = MeshBuilder("Cathedral_StainedGlass_Windows", "Mat_StainedGlass")
    mb_godrays = MeshBuilder("Cathedral_GodRays", "Mat_BloodWaves")

    c_flesh_bot = (0.58, 0.28, 0.28, 1.0)
    c_flesh_top = (0.80, 0.42, 0.40, 1.0)
    c_stone_dark = (0.48, 0.44, 0.40, 1.0)
    c_stone_mid = (0.74, 0.70, 0.64, 1.0)
    c_stone_light = (0.90, 0.86, 0.80, 1.0)
    c_glass_bot = (0.86, 0.32, 0.38, 1.0)
    c_glass_top = (1.00, 0.84, 0.56, 1.0)

    # 3A. North / Back-Left Wall (X = -16.0): ultra-detailed in permanent meshes
    _build_three_tier_side_wall(mb_flesh, mb_stone, mb_glass, side_sign=-1.0)

    # 3B. South / Front-Right Wall (X = +16.0): put into Cathedral_Wall_Right_Cutaway
    # so the isometric camera (-X, -Z -> +X, +Z) can fade/hide it without blocking the arena!
    _build_three_tier_side_wall(mb_right_cutaway, mb_right_cutaway, mb_glass, side_sign=1.0)

    # Low ruined stone wall plinth along X = +16.0 (Y = 0.0..1.35) in permanent mb_stone
    # so even when Cathedral_Wall_Right_Cutaway is hidden, the right border is grounded!
    add_box_minmax(mb_stone, 15.75, 0.0, 1.0, 16.25, 1.35, 41.0, c_stone_dark, c_stone_mid)

    # 3C. Interior Back of the West / Front Façade (Z = +1.0, X in [-16.0, +16.0], facing +Z)
    # CRITICAL: Leave Main Portal Opening clear (X in [-3.2, +3.2], Y in [0.0, 7.2], Z in [-4.5, +1.5])!
    inward_pz = (0.0, 0.0, 1.0)
    # Left & Right lower wall panels flanking the clear Main Portal Opening (X in [-16.0, -3.2] and [+3.2, +16.0], Y = 0..10.5)
    for xa, xb in ((-16.0, -3.25), (3.25, 16.0)):
        add_quad_checked(
            mb_flesh,
            (xa, 0.0, 1.0), (xb, 0.0, 1.0), (xb, 10.5, 1.0), (xa, 10.5, 1.0),
            inward_pz,
            (0.0, 0.0), (3.0, 0.0), (3.0, 2.5), (0.0, 2.5),
            c_flesh_bot, c_flesh_bot, c_flesh_top, c_flesh_top,
        )
    # Upper West Façade Wall above the portal (X in [-16.0, +16.0], Y = 7.4..26.5)
    add_quad_checked(
        mb_flesh,
        (-16.0, 7.4, 1.0), (16.0, 7.4, 1.0), (16.0, 22.0, 1.0), (-16.0, 22.0, 1.0),
        inward_pz,
        (0.0, 0.0), (6.0, 0.0), (6.0, 3.0), (0.0, 3.0),
        c_flesh_bot, c_flesh_bot, c_flesh_top, c_flesh_top,
    )
    add_tri_checked(
        mb_flesh,
        (-9.5, 22.0, 1.0), (9.5, 22.0, 1.0), (0.0, 27.5, 1.0),
        inward_pz,
        (0.0, 0.0), (4.0, 0.0), (2.0, 1.5),
        c_flesh_top, c_flesh_top, c_flesh_top,
    )

    # Grand Interior Portal Arch & Jambs framing the clear entrance (X in [-3.2, +3.2], Y = 0.0..7.2)
    for px_jamb in (-3.45, 3.45):
        add_box_minmax(mb_stone, px_jamb - 0.25, 0.0, -1.2, px_jamb + 0.25, 7.25, 1.45,
                       c_stone_dark, c_stone_light)
    # Stone lintel & tympanum arch above Y = 7.25 so Y in [0.0, 7.2] is 100% unobstructed
    add_box_minmax(mb_stone, -3.7, 7.22, -1.2, 3.7, 7.65, 1.50, c_stone_mid, c_stone_light)
    add_pointed_gothic_arch(
        mb_stone, (-3.55, 1.25), (3.55, 1.25),
        y_spring=7.25, y_apex=10.6, arch_thickness=0.55, wall_depth=0.75,
        segments_per_half=7, c_base=c_stone_mid, c_apex=c_stone_light
    )

    # Interior West Rose Window (centered at X = 0, Y = 16.5, Z = 1.12, radius = 4.2m)
    wr_y, wr_z, wr_r = 16.5, 1.12, 4.2
    wr_segs = 16
    for s in range(wr_segs):
        a0 = (2.0 * math.pi * s) / wr_segs
        a1 = (2.0 * math.pi * (s + 1)) / wr_segs
        p0 = (wr_r * math.cos(a0), wr_y + wr_r * math.sin(a0), wr_z)
        p1 = (wr_r * math.cos(a1), wr_y + wr_r * math.sin(a1), wr_z)
        add_tri_checked(
            mb_glass,
            (0.0, wr_y, wr_z), p0, p1,
            inward_pz,
            (0.5, 0.5),
            (0.5 + 0.5 * math.cos(a0), 0.5 + 0.5 * math.sin(a0)),
            (0.5 + 0.5 * math.cos(a1), 0.5 + 0.5 * math.sin(a1)),
            (1.0, 0.92, 0.78, 1.0), c_glass_top, c_glass_bot,
        )
        # Stone tracery spoke & outer rim segment
        add_box_minmax(
            mb_stone,
            min(p0[0], p1[0]) - 0.15, min(p0[1], p1[1]) - 0.15, 1.05,
            max(p0[0], p1[0]) + 0.15, max(p0[1], p1[1]) + 0.15, 1.38,
            c_stone_mid, c_stone_light
        )

    # 3D. 5-Sided Polygonal Gothic Apse Walls & Tall Lancet Windows (Z = 41.0..56.0)
    apse_pts = [
        (-16.0, 41.0),
        (-14.0, 49.0),
        (-8.0, 56.0),
        (8.0, 56.0),
        (14.0, 49.0),
        (16.0, 41.0),
    ]
    # Engaged Apse Angle Columns at all 6 polygonal vertices
    for vx, vz in apse_pts:
        add_cylinder_y(mb_stone, vx, vz, 0.0, 24.0, 0.58, 0.48, segments=8,
                       c_bot=c_stone_dark, c_top=c_stone_light, cap_top=True)

    for seg_i in range(len(apse_pts) - 1):
        x0, z0 = apse_pts[seg_i]
        x1, z1 = apse_pts[seg_i + 1]
        xm, zm = 0.5 * (x0 + x1), 0.5 * (z0 + z1)
        # Vector pointing inward toward (0, 46.0)
        in_dir = (-xm, 0.0, 46.0 - zm)

        # Lower apse masonry dado (Y = 0..4.2m) & full height masonry wall
        add_quad_checked(
            mb_flesh,
            (x0, 0.0, z0), (x1, 0.0, z1), (x1, 24.5, z1), (x0, 24.5, z0),
            in_dir,
            (0.0, 0.0), (3.0, 0.0), (3.0, 5.0), (0.0, 5.0),
            c_flesh_bot, c_flesh_bot, c_flesh_top, c_flesh_top,
        )
        # Pointed Apse Wall Arch at Y = 20.5..24.2m
        add_pointed_gothic_arch(
            mb_stone, (x0, z0), (x1, z1),
            y_spring=20.2, y_apex=24.0, arch_thickness=0.42, wall_depth=0.55,
            segments_per_half=6, c_base=c_stone_mid, c_apex=c_stone_light
        )

        # Tall Glowing Stained-Glass Lancet Windows in the 4 angled Apse walls
        # (Seg 2 is behind the Colossal Pipe Organ, so we place high clerestory rose & lancet glow above/around it)
        for w_frac0, w_frac1 in ((0.18, 0.44), (0.56, 0.82)):
            wx0 = x0 + (x1 - x0) * w_frac0
            wz0 = z0 + (z1 - z0) * w_frac0
            wx1 = x0 + (x1 - x0) * w_frac1
            wz1 = z0 + (z1 - z0) * w_frac1
            wxm, wzm = 0.5 * (wx0 + wx1), 0.5 * (wz0 + wz1)
            # Offset slightly inward so window sits proud of wall plane
            in_norm = _v3(in_dir) / np.linalg.norm(_v3(in_dir))
            ox, oz = float(in_norm[0]) * 0.14, float(in_norm[2]) * 0.14
            y_win_bot = 4.5 if seg_i != 2 else 16.5
            y_win_spring = 18.8 if seg_i != 2 else 21.0
            y_win_apex = 21.4 if seg_i != 2 else 23.2

            add_quad_checked(
                mb_glass,
                (wx0 + ox, y_win_bot, wz0 + oz), (wx1 + ox, y_win_bot, wz1 + oz),
                (wx1 + ox, y_win_spring, wz1 + oz), (wx0 + ox, y_win_spring, wz0 + oz),
                in_dir,
                (0.0, 0.0), (1.0, 0.0), (1.0, 0.8), (0.0, 0.8),
                c_glass_bot, c_glass_bot, c_glass_top, c_glass_top,
            )
            add_tri_checked(
                mb_glass,
                (wx0 + ox, y_win_spring, wz0 + oz), (wx1 + ox, y_win_spring, wz1 + oz),
                (wxm + ox, y_win_apex, wzm + oz),
                in_dir,
                (0.0, 0.8), (1.0, 0.8), (0.5, 1.0),
                c_glass_top, c_glass_top, (1.0, 0.96, 0.82, 1.0),
            )
            add_pointed_gothic_arch(
                mb_stone, (wx0 + ox, wz0 + oz), (wx1 + ox, wz1 + oz),
                y_spring=y_win_spring, y_apex=y_win_apex + 0.15, arch_thickness=0.24, wall_depth=0.35,
                segments_per_half=4, c_base=c_stone_mid, c_apex=c_stone_light
            )

    # 3E. Volumetric God-Ray Light Shafts (Cathedral_GodRays, Mat_BloodWaves)
    # Slanted translucent beams streaming down from the North-Left Clerestory windows,
    # Side-Aisle Lancets, and Apse Lancets toward the Nave & Sanctuary floor!
    c_ray_top = (1.00, 0.68, 0.52, 0.85)
    c_ray_bot = (0.75, 0.18, 0.22, 0.18)

    def _add_volumetric_beam(p_top_a, p_top_b, p_bot_a, p_bot_b):
        # Crossed double-sided ribbons so beams are visible from isometric & side angles
        for normal_hint in ((0.0, 0.4, -1.0), (0.0, -0.4, 1.0), (-0.7, 0.5, 0.0)):
            add_quad_checked(
                mb_godrays,
                p_top_a, p_top_b, p_bot_b, p_bot_a,
                normal_hint,
                (0.0, 1.0), (1.0, 1.0), (1.0, 0.0), (0.0, 0.0),
                c_ray_top, c_ray_top, c_ray_bot, c_ray_bot,
            )

    # 5 Clerestory God-Ray Fans from X = -15.6, Y = 19.5 down to Nave floor X = +2.5, Y = 0.08
    for bay_idx in range(5):
        zm = 5.0 + bay_idx * 8.0
        _add_volumetric_beam(
            (-15.5, 20.5, zm - 1.6), (-15.5, 20.5, zm + 1.6),
            (-1.5, 0.08, zm - 2.2), (3.2, 0.08, zm + 2.8)
        )
        _add_volumetric_beam(
            (-15.5, 17.5, zm - 1.2), (-15.5, 21.5, zm + 1.2),
            (-3.2, 0.08, zm + 0.5), (1.8, 0.08, zm + 2.5)
        )
        # Side-aisle lower lancet god-ray
        _add_volumetric_beam(
            (-15.6, 6.8, zm - 0.9), (-15.6, 6.8, zm + 0.9),
            (-10.5, 0.08, zm - 0.4), (-10.2, 0.08, zm + 1.5)
        )

    # 2 Dramatic Apse Sanctuary God-Rays streaming onto the High Altar at (0, 1.3, 47.5)
    _add_volumetric_beam(
        (-13.8, 18.5, 45.0), (-11.2, 18.5, 51.0),
        (-2.2, 1.30, 46.2), (2.2, 1.30, 49.0)
    )
    _add_volumetric_beam(
        (-10.5, 19.5, 52.0), (-6.5, 19.5, 55.2),
        (-1.8, 1.30, 47.0), (2.5, 1.30, 50.2)
    )

    meshes.extend([mb_flesh, mb_stone, mb_right_cutaway, mb_glass, mb_godrays])


# ==============================================================================
# 4. HIGH RIBBED GROIN VAULTS (CUTAWAY) & 5 HANGING BONE/ORGAN CHANDELIERS
# ==============================================================================

def _build_vaults_and_chandeliers(meshes, anchors):
    mb_ceiling = MeshBuilder("Cathedral_Ceiling_Cutaway", "Mat_OssifiedStone")
    mb_chand_metal = MeshBuilder("Cathedral_Chandeliers", "Mat_OrganPipes")
    mb_chand_glass = MeshBuilder("Cathedral_Chandeliers_LanternGlass", "Mat_StainedGlass")
    mb_chand_bone = MeshBuilder("Cathedral_Bone_Horns_And_Cartilage", "Mat_Cartilage")

    c_rib_base = (0.64, 0.60, 0.55, 1.0)
    c_rib_apex = (0.90, 0.86, 0.80, 1.0)
    c_web_low = (0.38, 0.34, 0.32, 1.0)
    c_web_high = (0.54, 0.49, 0.45, 1.0)

    pier_zs = [1.0, 9.0, 17.0, 25.0, 33.0, 41.0]

    # 4A. Transverse Pointed Vault Ribs at every Bay Line (Z = 1, 9, 17, 25, 33, 41)
    for pz in pier_zs:
        add_pointed_gothic_arch(
            mb_ceiling, (-9.5, pz), (9.5, pz),
            y_spring=22.0, y_apex=27.2, arch_thickness=0.52, wall_depth=0.58,
            segments_per_half=8, c_base=c_rib_base, c_apex=c_rib_apex
        )

    # 4B. 5 Nave Bays of Quadripartite + Tierceron Ribbed Groin Vaults & Keystone Bosses
    for bay_idx in range(5):
        z0 = pier_zs[bay_idx]
        z1 = pier_zs[bay_idx + 1]
        zm = 0.5 * (z0 + z1)

        # Diagonal Groin Vault Ribs crossing (X=-9.5, z0) <-> (X=+9.5, z1) and (X=+9.5, z0) <-> (X=-9.5, z1)
        add_pointed_gothic_arch(
            mb_ceiling, (-9.5, z0), (9.5, z1),
            y_spring=22.0, y_apex=27.45, arch_thickness=0.44, wall_depth=0.44,
            segments_per_half=8, c_base=c_rib_base, c_apex=c_rib_apex
        )
        add_pointed_gothic_arch(
            mb_ceiling, (9.5, z0), (-9.5, z1),
            y_spring=22.0, y_apex=27.45, arch_thickness=0.44, wall_depth=0.44,
            segments_per_half=8, c_base=c_rib_base, c_apex=c_rib_apex
        )
        # Longitudinal Central Ridge Rib at Y = 27.3..27.6
        add_box_minmax(mb_ceiling, -0.24, 27.15, z0, 0.24, 27.60, z1, c_rib_base, c_rib_apex)

        # Sculpted Central Keystone Boss at (0.0, 26.85, zm)
        add_cylinder_y(mb_ceiling, 0.0, zm, 26.55, 27.55, 0.72, 0.92, segments=8,
                       c_bot=c_rib_apex, c_top=c_rib_base, cap_top=True, cap_bottom=True)
        add_cylinder_y(mb_ceiling, 0.0, zm, 26.15, 26.55, 0.32, 0.68, segments=8,
                       c_bot=c_rib_apex, c_top=c_rib_apex, cap_bottom=True)

        # High Vault Webbing Shell (facing downward -Y into the Nave)
        add_quad_checked(
            mb_ceiling,
            (-9.5, 23.5, z0), (-9.5, 23.5, z1), (0.0, 27.4, z1), (0.0, 27.4, z0),
            (0.3, -1.0, 0.0),
            (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0),
            c_web_low, c_web_low, c_web_high, c_web_high,
        )
        add_quad_checked(
            mb_ceiling,
            (0.0, 27.4, z0), (0.0, 27.4, z1), (9.5, 23.5, z1), (9.5, 23.5, z0),
            (-0.3, -1.0, 0.0),
            (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0),
            c_web_high, c_web_high, c_web_low, c_web_low,
        )

    # 4C. Polygonal Apse Radial Vault Ribs meeting at Sanctuary Keystone Boss (0, 27.2, 47.5)
    apse_pts = [(-16.0, 41.0), (-14.0, 49.0), (-8.0, 56.0), (8.0, 56.0), (14.0, 49.0), (16.0, 41.0)]
    for vx, vz in apse_pts:
        add_pointed_gothic_arch(
            mb_ceiling, (vx, vz), (-vx if abs(vx) > 1e-3 else 0.0, 95.0 - vz if vz < 48.0 else 41.0),
            y_spring=22.0, y_apex=27.3, arch_thickness=0.42, wall_depth=0.42,
            segments_per_half=6, c_base=c_rib_base, c_apex=c_rib_apex
        )
    add_cylinder_y(mb_ceiling, 0.0, 47.5, 26.4, 27.5, 0.85, 1.05, segments=8,
                   c_bot=c_rib_apex, c_top=c_rib_base, cap_top=True, cap_bottom=True)

    # 4D. 5 Ornate Gothic Bone & Organ Chandeliers suspended at Z = 5, 13, 21, 29, 37 down to Y = 9.5
    c_brass_dark = (0.52, 0.42, 0.30, 1.0)
    c_brass_bright = (0.88, 0.76, 0.54, 1.0)
    c_bone = (0.92, 0.87, 0.76, 1.0)
    c_bone_dark = (0.68, 0.62, 0.52, 1.0)
    c_flame = (1.00, 0.88, 0.45, 1.0)

    for idx in range(5):
        cz = 5.0 + idx * 8.0
        cy = 9.5
        anchors[f"Chandelier_Anchor_0{idx + 1}"] = [0.0, cy, cz]

        # Vertical suspension chain / rod from Keystone Boss (Y = 26.2) down to Y = 9.5
        add_cylinder_y(mb_chand_metal, 0.0, cz, cy + 2.2, 26.25, 0.07, 0.07, segments=6,
                       c_bot=c_brass_bright, c_top=c_brass_dark, cap_top=False)
        # Upper suspension crown at Y = 11.7..12.3
        add_cylinder_y(mb_chand_metal, 0.0, cz, cy + 2.1, cy + 2.7, 0.55, 0.18, segments=8,
                       c_bot=c_brass_bright, c_top=c_brass_dark, cap_top=True, cap_bottom=True)

        # Central hub & bone spire pendant at (0, 9.5, cz)
        add_cylinder_y(mb_chand_metal, 0.0, cz, cy - 0.45, cy + 0.85, 0.42, 0.52, segments=8,
                       c_bot=c_brass_dark, c_top=c_brass_bright, cap_top=True, cap_bottom=True)
        add_cylinder_y(mb_chand_bone, 0.0, cz, cy - 1.35, cy - 0.45, 0.04, 0.36, segments=6,
                       c_bot=c_bone, c_top=c_bone_dark, cap_top=True)

        # Outer Spoked Wheel Ring (radius = 2.35m) & Inner Tier Ring (radius = 1.25m)
        wheel_segs = 8
        for ring_r, ring_y, ring_h in ((2.35, cy, 0.28), (1.25, cy + 0.85, 0.22)):
            pts_ring = []
            for s in range(wheel_segs + 1):
                ang = (2.0 * math.pi * s) / wheel_segs
                pts_ring.append((ring_r * math.cos(ang), ring_y, cz + ring_r * math.sin(ang)))
            add_extruded_tube(mb_chand_metal, pts_ring, ring_h * 0.5, segments=6,
                              colors=c_brass_bright, cap_ends=False)

        # 8 Diagonal Suspension Chains, Radial Spokes, Curved Bone Ribs & 12 Glowing Pipe-Lantern Turrets
        for s in range(wheel_segs):
            ang = (2.0 * math.pi * s) / wheel_segs
            lx = 2.35 * math.cos(ang)
            lz = cz + 2.35 * math.sin(ang)

            # Turret Organ-Pipe Resonator & Glowing Stained-Glass Lantern at each wheel vertex
            add_cylinder_y(mb_chand_metal, lx, lz, cy - 0.35, cy + 0.95, 0.16, 0.20, segments=6,
                           c_bot=c_brass_dark, c_top=c_brass_bright, cap_top=True, cap_bottom=True)
            add_cylinder_y(mb_chand_glass, lx, lz, cy + 0.15, cy + 0.72, 0.21, 0.21, segments=6,
                           c_bot=(1.0, 0.65, 0.30, 1.0), c_top=c_flame, cap_top=True, cap_bottom=True)
            # Spiky bone finial atop each turret
            add_cylinder_y(mb_chand_bone, lx, lz, cy + 0.95, cy + 1.55, 0.15, 0.02, segments=5,
                           c_bot=c_bone_dark, c_top=c_bone, cap_top=False)

            if s % 2 == 0:
                # Radial spoke & diagonal chain up to crown
                add_extruded_tube(
                    mb_chand_metal,
                    [(0.0, cy + 0.1, cz), (lx, cy, lz), (0.0, cy + 2.25, cz)],
                    [0.06, 0.05, 0.04], segments=5, colors=c_brass_dark, cap_ends=False
                )
                # Curved Gothic Bone Rib arching under the wheel
                add_extruded_tube(
                    mb_chand_bone,
                    [(0.0, cy - 0.85, cz), (0.55 * lx, cy - 0.55, cz + 0.55 * (lz - cz)), (lx, cy - 0.25, lz)],
                    [0.07, 0.09, 0.05], segments=5, colors=c_bone, cap_ends=True
                )

    meshes.extend([mb_ceiling, mb_chand_metal, mb_chand_glass, mb_chand_bone])
    return mb_chand_glass, mb_chand_bone


# ==============================================================================
# 5. HIGH ALTAR SANCTUARY, CHOIR STALLS, BOSS THRONE & COLOSSAL PIPE ORGAN
# ==============================================================================

def _build_sanctuary_altar_and_organ(meshes, anchors, mb_glass_shared, mb_bone_shared):
    mb_sanct_stone = MeshBuilder("Cathedral_Sanctuary_And_Altar_Stone", "Mat_OssifiedStone")
    mb_wood = MeshBuilder("Cathedral_Woodwork_Choir_And_OrganCase", "Mat_CathedralDoor")
    mb_pipes = MeshBuilder("Cathedral_Colossal_Organ_Pipes", "Mat_OrganPipes")
    mb_viscera = MeshBuilder("Cathedral_Visceral_Growths_And_Bellows", "Mat_HellOrgans")

    c_stone_dark = (0.48, 0.44, 0.40, 1.0)
    c_stone_mid = (0.74, 0.70, 0.64, 1.0)
    c_stone_light = (0.90, 0.86, 0.80, 1.0)
    c_wood_dark = (0.34, 0.20, 0.15, 1.0)
    c_wood_mid = (0.52, 0.32, 0.23, 1.0)
    c_wood_light = (0.68, 0.44, 0.32, 1.0)
    c_pipe_base = (0.58, 0.52, 0.44, 1.0)
    c_pipe_mid = (0.84, 0.78, 0.68, 1.0)
    c_pipe_top = (0.96, 0.90, 0.78, 1.0)
    c_flesh_dark = (0.55, 0.14, 0.18, 1.0)
    c_flesh_bright = (0.92, 0.28, 0.32, 1.0)
    c_bone = (0.92, 0.87, 0.76, 1.0)
    c_bone_dark = (0.66, 0.60, 0.50, 1.0)

    # 5A. 5-Step Polygonal Sanctuary Dais rising from Y = 0.0 at Z = 41.0 to Y = 1.25 at Z = 43.5
    for step_i in range(5):
        y_bot = step_i * 0.25
        y_top = (step_i + 1) * 0.25
        z_front = 41.0 + step_i * 0.50
        z_back = 43.65
        half_w = 13.0 - step_i * 0.25
        add_box_minmax(
            mb_sanct_stone, -half_w, y_bot, z_front, half_w, y_top, z_back,
            c_stone_dark, c_stone_light, faces=("py", "nz", "px", "nx")
        )

    # Sanctuary Platform Perimeter Retaining Wall (Z = 43.5..55.5, Y = -0.6..1.25)
    for side in (-1.0, 1.0):
        pts_curb = [
            (side * 12.2, 0.6, 43.5),
            (side * 11.4, 0.6, 49.0),
            (side * 6.8, 0.6, 55.2),
        ]
        add_extruded_tube(mb_sanct_stone, pts_curb, 0.68, segments=6, colors=c_stone_mid, cap_ends=True)

    # 5B. Carved Gothic Choir Stalls flanking the Sanctuary (X = ±7.8..±10.6, Z = 44.0..50.2)
    for side in (-1.0, 1.0):
        x_front = side * 7.8
        x_back = side * 9.8
        xa, xb = min(x_front, x_back), max(x_front, x_back)
        # Front kneeler desk & bench
        add_box_minmax(mb_wood, xa, 1.25, 44.0, xb, 2.15, 50.2, c_wood_dark, c_wood_mid)
        # Tall carved gothic back panel & overhanging canopy (Y = 1.25..5.4)
        back_x0 = min(x_back, x_back + side * 0.55)
        back_x1 = max(x_back, x_back + side * 0.55)
        add_box_minmax(mb_wood, back_x0, 1.25, 44.0, back_x1, 5.20, 50.2, c_wood_dark, c_wood_light)
        can_x0 = min(x_front, x_back + side * 0.55)
        can_x1 = max(x_front, x_back + side * 0.55)
        add_box_minmax(mb_wood, can_x0, 5.10, 43.9, can_x1, 5.55, 50.3, c_wood_mid, c_wood_light)
        # 6 Carved Gothic Finial Pinnacles crowning each Choir Stall bank
        for ip in range(6):
            pz = 44.2 + ip * 1.15
            add_cylinder_y(mb_wood, x_back + side * 0.25, pz, 5.55, 6.75, 0.22, 0.03, segments=6,
                           c_bot=c_wood_mid, c_top=c_wood_light, cap_top=False)

    # Sacristy Carved Wood Side Doors in Bay 2 (Z = 21.0, X = ±15.85)
    for side in (-1.0, 1.0):
        dx0 = -16.05 if side < 0 else 15.75
        dx1 = -15.75 if side < 0 else 16.05
        add_box_minmax(mb_wood, dx0, 0.0, 19.95, dx1, 2.15, 22.05, c_wood_dark, c_wood_light)

    # 5C. 6 Burning Bone Braziers / Candelabras (Brazier_Anchor_01..06)
    brazier_coords = [
        (-5.8, 0.0, 40.6),
        (5.8, 0.0, 40.6),
        (-5.5, 1.25, 44.2),
        (5.5, 1.25, 44.2),
        (-4.5, 1.25, 48.2),
        (4.5, 1.25, 48.2),
    ]
    for b_idx, (bx, by, bz) in enumerate(brazier_coords):
        anchors[f"Brazier_Anchor_0{b_idx + 1}"] = [bx, by + 2.65, bz]
        # Stepped octagonal bone-stone pedestal & fluted shaft
        add_cylinder_y(mb_sanct_stone, bx, bz, by, by + 0.55, 0.62, 0.45, segments=8,
                       c_bot=c_stone_dark, c_top=c_stone_mid, cap_top=True)
        add_cylinder_y(mb_sanct_stone, bx, bz, by + 0.55, by + 2.05, 0.24, 0.20, segments=6,
                       c_bot=c_stone_mid, c_top=c_stone_light, cap_top=True)
        # Flared Brazier Fire Basin (Y = by+2.05 .. by+2.55)
        add_cylinder_y(mb_sanct_stone, bx, bz, by + 2.05, by + 2.55, 0.25, 0.68, segments=8,
                       c_bot=c_stone_dark, c_top=c_stone_light, cap_top=True)
        # Glowing Emissive Flame Core
        add_cylinder_y(mb_glass_shared, bx, bz, by + 2.45, by + 3.25, 0.46, 0.06, segments=6,
                       c_bot=(1.0, 0.55, 0.20, 1.0), c_top=(1.0, 0.95, 0.60, 1.0), cap_top=False)
        # 4 Curved Bone Claws gripping the brazier basin
        for c_ang in (0.0, 0.5 * math.pi, math.pi, 1.5 * math.pi):
            add_extruded_tube(
                mb_bone_shared,
                [
                    (bx + 0.22 * math.cos(c_ang), by + 1.85, bz + 0.22 * math.sin(c_ang)),
                    (bx + 0.72 * math.cos(c_ang), by + 2.35, bz + 0.72 * math.sin(c_ang)),
                    (bx + 0.56 * math.cos(c_ang), by + 2.85, bz + 0.56 * math.sin(c_ang)),
                ],
                [0.07, 0.08, 0.02], segments=5, colors=c_bone, cap_ends=True
            )

    # 5D. The Sacrificial High Altar, Boss Reliquary Throne & Towering Gothic Baldachin at (0, 1.25, 47.5)
    alt_y = 1.25
    alt_z = 47.5
    # 2-step Altar Footpace Platform
    add_box_minmax(mb_sanct_stone, -3.4, alt_y, alt_z - 2.2, 3.4, alt_y + 0.36, alt_z + 2.4,
                   c_stone_dark, c_stone_mid)
    # Sacrificial Stone Altar Mensa Table
    add_box_minmax(mb_sanct_stone, -2.2, alt_y + 0.36, alt_z - 0.95, 2.2, alt_y + 1.65, alt_z + 0.95,
                   c_stone_mid, c_stone_light)
    # Crimson Visceral Flesh Altar Cloth draped over the table
    add_box_minmax(mb_viscera, -1.85, alt_y + 0.42, alt_z - 1.02, 1.85, alt_y + 1.70, alt_z + 1.02,
                   c_flesh_dark, c_flesh_bright)

    # Boss Reliquary Throne rising directly behind the Altar at (0, 1.61, 49.2)
    add_box_minmax(mb_sanct_stone, -1.45, alt_y + 0.36, 48.6, 1.45, alt_y + 2.6, 49.8,
                   c_stone_dark, c_stone_mid)
    add_box_minmax(mb_sanct_stone, -1.25, alt_y + 2.6, 49.2, 1.25, alt_y + 6.4, 49.85,
                   c_stone_mid, c_stone_light)
    # Boss Throne Fangs & Altar Horns (Mat_Cartilage)
    for side in (-1.0, 1.0):
        # Altar corner horns
        for hz in (alt_z - 0.85, alt_z + 0.85):
            add_extruded_tube(
                mb_bone_shared,
                [
                    (side * 2.05, alt_y + 1.60, hz),
                    (side * 2.45, alt_y + 2.15, hz),
                    (side * 2.20, alt_y + 2.75, hz),
                ],
                [0.14, 0.10, 0.02], segments=6, colors=c_bone, cap_ends=True
            )
        # Towering Boss Throne Rib-Fangs
        for f_idx in range(3):
            fy = alt_y + 3.2 + f_idx * 1.1
            add_extruded_tube(
                mb_bone_shared,
                [
                    (side * 1.20, fy, 49.5),
                    (side * 2.15, fy + 0.55, 49.1),
                    (side * 1.65, fy + 1.25, 48.6),
                ],
                [0.16, 0.11, 0.02], segments=6, colors=c_bone, cap_ends=True
            )

    # Towering 4-Pillar Gothic Baldachin / Canopy over the High Altar (reaching Y = 14.0)
    bald_pillars = [(-2.8, 45.6), (2.8, 45.6), (-2.8, 49.6), (2.8, 49.6)]
    for bpx, bpz in bald_pillars:
        add_cylinder_y(mb_sanct_stone, bpx, bpz, alt_y + 0.36, 8.2, 0.34, 0.30, segments=8,
                       c_bot=c_stone_mid, c_top=c_stone_light, cap_top=True)
        # Pinnacle turret atop each Baldachin corner pillar (Y = 8.2..11.2)
        add_cylinder_y(mb_sanct_stone, bpx, bpz, 8.2, 11.2, 0.38, 0.04, segments=6,
                       c_bot=c_stone_light, c_top=c_stone_mid, cap_top=False)

    # 4 Pointed Canopy Arches connecting the Baldachin pillars (Y = 8.2..10.4)
    for p_a, p_b in ((bald_pillars[0], bald_pillars[1]),
                     (bald_pillars[2], bald_pillars[3]),
                     (bald_pillars[0], bald_pillars[2]),
                     (bald_pillars[1], bald_pillars[3])):
        add_pointed_gothic_arch(
            mb_sanct_stone, p_a, p_b,
            y_spring=8.0, y_apex=10.35, arch_thickness=0.36, wall_depth=0.42,
            segments_per_half=5, c_base=c_stone_mid, c_apex=c_stone_light
        )

    # Curved Ossified Rib Canopy & Central Spire reaching Y = 14.0
    for bpx, bpz in bald_pillars:
        add_extruded_tube(
            mb_bone_shared,
            [
                (bpx, 8.4, bpz),
                (bpx * 0.55, 11.2, alt_z + (bpz - alt_z) * 0.55),
                (0.0, 12.6, alt_z),
            ],
            [0.22, 0.16, 0.10], segments=6, colors=c_bone, cap_ends=True
        )
    add_cylinder_y(mb_bone_shared, 0.0, alt_z, 11.8, 14.0, 0.45, 0.03, segments=8,
                   c_bot=c_bone_dark, c_top=c_bone, cap_top=False)

    # 5E. THE COLOSSAL PIPE ORGAN OF TRACHEAS ("Órgão Monumental do Boss")
    # Built against the rear apse wall (Z = 52.5..55.5, X in [-10.0, +10.0], Y = 1.25..25.0)
    # Stone & Carved Wood Organ Base Podium & Console (Y = 1.25..6.2)
    add_box_minmax(mb_sanct_stone, -9.8, 1.25, 52.5, 9.8, 3.2, 55.4, c_stone_dark, c_stone_mid)
    add_box_minmax(mb_wood, -9.4, 3.2, 52.6, 9.4, 6.2, 55.2, c_wood_dark, c_wood_mid)
    # Organist Console Keyboard Desk at (0, 1.25..2.8, 51.4..52.5)
    add_box_minmax(mb_wood, -1.8, 1.25, 51.3, 1.8, 2.75, 52.5, c_wood_dark, c_wood_light)

    # 3 Towering Pipe Turrets (Central Tower at X=0 reaching Y=25.0; Side Towers at X=±7.2 reaching Y=22.0)
    # and 2 Connecting Flats (X in [-5.4, -2.0] and [+2.0, +5.4], reaching Y=17.5)
    turret_specs = [
        (0.0, 52.8, 2.1, 6.2, 21.8, 25.0),     # Central Grand Turret (reaches Y = 25.0)
        (-7.2, 53.0, 1.85, 6.2, 19.2, 22.0),   # Left Pipe Turret (reaches Y = 22.0)
        (7.2, 53.0, 1.85, 6.2, 19.2, 22.0),    # Right Pipe Turret (reaches Y = 22.0)
    ]
    for tx, tz, tr, ty_base, ty_cornice, ty_spire in turret_specs:
        # Lower turret corbel cone
        add_cylinder_y(mb_wood, tx, tz, ty_base - 1.4, ty_base, tr * 0.45, tr, segments=8,
                       c_bot=c_wood_dark, c_top=c_wood_mid, cap_top=True)
        # Wooden backing column behind pipes
        add_box_minmax(mb_wood, tx - tr * 0.85, ty_base, tz + 0.2, tx + tr * 0.85, ty_cornice, 55.3,
                       c_wood_dark, c_wood_mid)
        # Upper carved Gothic Turret Crown & Spire reaching ty_spire (25.0 / 22.0)
        add_cylinder_y(mb_wood, tx, tz, ty_cornice, ty_cornice + 1.1, tr * 1.08, tr * 1.18, segments=8,
                       c_bot=c_wood_mid, c_top=c_wood_light, cap_top=True, cap_bottom=True)
        add_cylinder_y(mb_wood, tx, tz, ty_cornice + 1.1, ty_spire, tr * 0.92, 0.06, segments=8,
                       c_bot=c_wood_light, c_top=c_wood_dark, cap_top=False)
        # Bone Cresting spikes around turret crown
        for s in range(6):
            ang = math.pi + (math.pi * s) / 5.0
            sx = tx + tr * 1.12 * math.cos(ang)
            sz = tz + tr * 1.12 * math.sin(ang)
            add_cylinder_y(mb_bone_shared, sx, sz, ty_cornice + 1.0, ty_cornice + 2.3, 0.14, 0.02,
                           segments=5, c_bot=c_bone_dark, c_top=c_bone, cap_top=False)

    # 2 Connecting Wooden Pipe Chest Flats (Left: X in [-5.4, -2.0], Right: X in [+2.0, +5.4])
    for side in (-1.0, 1.0):
        fx0, fx1 = (2.0, 5.4) if side > 0 else (-5.4, -2.0)
        add_box_minmax(mb_wood, fx0, 6.2, 53.3, fx1, 16.8, 55.2, c_wood_dark, c_wood_mid)
        add_box_minmax(mb_wood, fx0 - 0.1, 16.5, 52.8, fx1 + 0.1, 17.6, 55.3, c_wood_mid, c_wood_light)

    # 38 Individual 3D Cylindrical Organ Pipes (Mat_OrganPipes):
    # - 8 giant pipes in Central Turret semicircle
    # - 7 tall pipes in Left Turret semicircle
    # - 7 tall pipes in Right Turret semicircle
    # - 8 graduated V-rank pipes in Left Flat
    # - 8 graduated V-rank pipes in Right Flat
    # Total = 8 + 7 + 7 + 8 + 8 = 38 3D pipes!
    def _add_organ_pipe(px, pz, y_boot, y_top, radius):
        y_mouth = y_boot + 0.85
        # Conical pipe foot/boot
        add_cylinder_y(mb_pipes, px, pz, y_boot, y_mouth, radius * 0.38, radius * 1.06, segments=8,
                       c_bot=c_pipe_base, c_top=c_pipe_mid, cap_top=False)
        # Main cylindrical resonator body
        add_cylinder_y(mb_pipes, px, pz, y_mouth, y_top - 0.45, radius, radius, segments=8,
                       c_bot=c_pipe_mid, c_top=c_pipe_top, cap_top=False)
        # Flared trachea-horn crown at the top of the pipe
        add_cylinder_y(mb_pipes, px, pz, y_top - 0.45, y_top, radius, radius * 1.28, segments=8,
                       c_bot=c_pipe_top, c_top=c_pipe_base, cap_top=True)

    # Central Turret: 8 colossal pipes (reaching Y = 21.8)
    for i in range(8):
        frac = i / 7.0
        ang = math.pi * (1.12 + 0.76 * frac)  # facing -Z into the cathedral
        px = 0.0 + 1.65 * math.cos(ang)
        pz = 52.8 + 1.45 * math.sin(ang)
        center_factor = 1.0 - abs(frac - 0.5) * 1.1
        p_top = 18.8 + 3.0 * center_factor
        p_rad = 0.21 + 0.08 * center_factor
        _add_organ_pipe(px, pz, 6.2, p_top, p_rad)

    # Left & Right Turrets: 7 pipes each (reaching Y = 19.2)
    for tx in (-7.2, 7.2):
        for i in range(7):
            frac = i / 6.0
            ang = math.pi * (1.12 + 0.76 * frac)
            px = tx + 1.42 * math.cos(ang)
            pz = 53.0 + 1.25 * math.sin(ang)
            center_factor = 1.0 - abs(frac - 0.5) * 1.1
            p_top = 16.5 + 2.7 * center_factor
            p_rad = 0.18 + 0.07 * center_factor
            _add_organ_pipe(px, pz, 6.2, p_top, p_rad)

    # Left & Right Connecting Flats: 8 graduated V-rank pipes each (16 pipes)
    for side in (-1.0, 1.0):
        fx0, fx1 = (2.15, 5.25) if side > 0 else (-5.25, -2.15)
        for i in range(8):
            frac = i / 7.0
            px = fx0 + (fx1 - fx0) * frac
            pz = 53.05
            # V-shape rank: tallest on outer edges, shortest in the middle
            v_factor = abs(frac - 0.5) * 2.0
            p_top = 13.4 + 3.1 * v_factor
            p_rad = 0.14 + 0.05 * v_factor
            _add_organ_pipe(px, pz, 6.2, p_top, p_rad)

    # 5F. Pulsating Arterial Bellows & Fleshy Trachea Tubes feeding the Pipe Organ (Mat_HellOrgans)
    for side in (-1.0, 1.0):
        # 3 Huge Pulsating Visceral Bellows Sacs flanking the organ at X = ±9.6..±11.5
        for s_idx in range(3):
            sx = side * (9.5 + s_idx * 0.7)
            sz = 51.8 + s_idx * 1.1
            sy = 1.25 + s_idx * 1.4
            add_cylinder_y(
                mb_viscera, sx, sz, sy, sy + 2.8, 0.85, 1.15, segments=8,
                c_bot=c_flesh_dark, c_top=c_flesh_bright, cap_top=True
            )
            add_cylinder_y(
                mb_viscera, sx, sz, sy + 2.8, sy + 3.9, 1.15, 0.35, segments=8,
                c_bot=c_flesh_bright, c_top=c_flesh_dark, cap_top=True
            )

        # Coiling Trachea / Arterial Tubes pumping from the Bellows into the Organ Windchests
        trachea_paths = [
            [
                (side * 10.2, 2.2, 52.0),
                (side * 9.8, 5.2, 51.6),
                (side * 8.2, 8.5, 52.4),
                (side * 7.2, 10.5, 53.2),
            ],
            [
                (side * 10.8, 3.8, 53.2),
                (side * 9.2, 9.2, 52.8),
                (side * 5.8, 13.2, 52.9),
                (side * 3.5, 15.0, 53.4),
            ],
            [
                (side * 9.4, 2.0, 51.4),
                (side * 6.2, 4.4, 51.8),
                (side * 2.8, 7.8, 52.2),
                (side * 1.2, 11.5, 52.8),
            ],
        ]
        for t_path in trachea_paths:
            add_extruded_tube(
                mb_viscera, t_path, [0.42, 0.36, 0.32, 0.26], segments=7,
                colors=[c_flesh_dark, c_flesh_bright, c_flesh_bright, c_flesh_dark], cap_ends=True
            )

    # 5G. Organic Visceral Tendrils coiling around several Nave Column Bases
    coiled_piers = [(-9.5, 9.0), (9.5, 17.0), (-9.5, 25.0), (9.5, 33.0), (-9.5, 41.0), (9.5, 41.0)]
    for cpx, cpz in coiled_piers:
        coil_pts = []
        coil_rads = []
        n_coil = 14
        for k in range(n_coil):
            t = k / float(n_coil - 1)
            ang = t * 3.2 * math.pi + (1.2 if cpx > 0 else 0.0)
            rad = 1.48 - 0.42 * t
            coil_pts.append((cpx + rad * math.cos(ang), 0.08 + t * 5.4, cpz + rad * math.sin(ang)))
            coil_rads.append(0.28 * (1.0 - 0.65 * t))
        add_extruded_tube(mb_viscera, coil_pts, coil_rads, segments=6, colors=c_flesh_bright, cap_ends=True)

    meshes.extend([mb_sanct_stone, mb_wood, mb_pipes, mb_viscera])


# ==============================================================================
# PUBLIC ENTRYPOINT
# ==============================================================================

def build_cathedral_interior() -> tuple[list[MeshBuilder], dict[str, list[float]]]:
    """
    Builds the complete 3D Interior Boss Arena of the Gothic Body-Horror Cathedral.
    Returns (meshes, anchors).
    """
    meshes: list[MeshBuilder] = []
    anchors: dict[str, list[float]] = {
        "Portal_Entry_Anchor": [0.0, 0.0, 0.0],
        "Nave_Center_Anchor": [0.0, 0.05, 22.0],
        "Boss_Altar_Anchor": [0.0, 1.25, 47.5],
        "Sanctuary_Steps_Anchor": [0.0, 0.0, 41.0],
        "Pipe_Organ_Anchor": [0.0, 6.2, 53.0],
    }

    _build_floors_medallion_and_acid(meshes)
    _build_colonnade_and_arches(meshes)
    _build_walls_windows_and_godrays(meshes)
    mb_glass_shared, mb_bone_shared = _build_vaults_and_chandeliers(meshes, anchors)
    _build_sanctuary_altar_and_organ(meshes, anchors, mb_glass_shared, mb_bone_shared)

    return meshes, anchors
