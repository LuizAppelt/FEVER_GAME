import os
import math
import numpy as np

from retro_textures import generate_all_textures
from glb_exporter import MeshBuilder, export_glb
from preview_render import load_glb, render_view


EYE_PIVOT = (0.0, 3.88, 0.24)
UPPER_JAW_PIVOT = (0.0, 2.40, 0.42)
LOWER_JAW_PIVOT = (0.0, 0.06, 0.12)


def organic_warp(x, y, z):
    return (
        0.45 * math.sin(x * 1.9 + y * 1.4 + z * 1.1)
        + 0.32 * math.cos(x * 3.2 - y * 2.5 + 0.8)
        + 0.23 * math.sin(y * 4.4 + z * 2.8 - 0.5)
    )


def add_oriented_quad(mb, p00, p10, p11, p01, uv00, uv10, uv11, uv01, c00, c10, c11, c01, outward_hint):
    """Emits a flat quad oriented so its normal aligns with outward_hint."""
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
    p0 = np.asarray(p0, dtype=np.float32)
    p1 = np.asarray(p1, dtype=np.float32)
    p2 = np.asarray(p2, dtype=np.float32)
    n = np.cross(p1 - p0, p2 - p0)
    if float(np.dot(n, np.asarray(outward_hint, dtype=np.float32))) >= 0.0:
        mb.add_flat_triangle(p0, p1, p2, uv0, uv1, uv2, c0, c1, c2)
    else:
        mb.add_flat_triangle(p0, p2, p1, uv0, uv2, uv1, c0, c2, c1)


def _ao_color(bright, warm=0.0):
    return (
        min(1.0, bright * (1.02 + 0.08 * warm)),
        min(1.0, bright * 0.76),
        min(1.0, bright * 0.78),
        1.0,
    )


def build_door_flesh_frame():
    """
    Builds Door_Flesh_Frame as one cohesive organic monolith of muscle and flesh:
    lower recessed maw throat with pointed inner silhouette + seamless upper pointed-almond eye cavity.
    """
    mb = MeshBuilder("Door_Flesh_Frame", "mat_flesh_wall")

    # ------------------------------------------------------------------
    # 1. Lower Mouth Archway & Side Pillars (y = -0.06 .. 2.86)
    # ------------------------------------------------------------------
    u_pts = []
    n_jamb = 5
    n_arch = 12
    for i in range(n_jamb):
        t = i / float(n_jamb)
        u_pts.append((-1.15 + 0.05 * t, -0.04 + t * 1.44))
    for i in range(n_arch + 1):
        a = math.pi * (1.0 - i / float(n_arch))
        u_pts.append((1.10 * math.cos(a), 1.40 + 0.78 * math.sin(a)))
    for i in range(1, n_jamb + 1):
        t = i / float(n_jamb)
        u_pts.append((1.10 + 0.05 * t, 1.40 - t * 1.44))

    n_u = len(u_pts)
    rings = []
    ring_specs = [
        # (z, inner_scale_xy, out_push, bright)
        (-1.35, 0.72, 0.00, 0.08),
        (-0.72, 0.86, 0.00, 0.24),
        ( 0.05, 0.96, 0.00, 0.56),
        ( 0.56, 1.00, 0.02, 0.92),
        ( 0.82, 1.00, 0.48, 1.00),   # thick cheek muscles
        ( 0.38, 1.00, 1.02, 0.82),   # outer front shoulder
        (-0.45, 1.00, 1.05, 0.56),   # outer side wall
        (-1.35, 1.00, 1.02, 0.28),   # outer back edge
    ]

    for rz, s_xy, push, bright in ring_specs:
        row_pos = []
        row_col = []
        for idx_u, (ux, uy) in enumerate(u_pts):
            dx = ux
            dy = max(0.0, uy - 0.55)
            dlen = max(1e-5, math.hypot(dx, dy))
            nx, ny = dx / dlen, dy / dlen
            if uy < 0.72:
                nx = -1.0 if ux < 0.0 else 1.0
                ny = 0.0

            bx = ux * s_xy + nx * push
            by = uy * (s_xy if push == 0.0 else 1.0) + ny * push * (0.72 if uy > 1.35 else 0.0)

            # Seamless flush junction into upper eye half around y = 2.86
            if push > 0.5 and uy > 1.35:
                by = min(2.86, max(by, 2.74))
            if push > 0.5:
                bx = max(-2.16, min(2.16, bx))

            # Side wall stays cleanly aligned across bottom & top halves
            wiggle = organic_warp(bx, by, rz) * (0.07 if push > 0.1 else 0.04)
            rib = 0.04 * math.sin(idx_u * 1.15) if push > 0.1 else 0.0
            px = bx + nx * (wiggle + rib)
            py = by + ny * (wiggle * 0.35)
            pz = rz + wiggle * 0.40

            if uy <= 0.0 and push < 0.6:
                py = min(py, -0.06)

            row_pos.append([px, py, pz])
            shade = bright * (0.88 + 0.12 * math.sin(idx_u * 1.3))
            row_col.append(_ao_color(shade))
        rings.append((row_pos, row_col))

    for rk in range(len(rings) - 1):
        p_a, c_a = rings[rk]
        p_b, c_b = rings[rk + 1]
        v0 = rk / float(len(rings) - 1) * 1.8
        v1 = (rk + 1) / float(len(rings) - 1) * 1.8
        for iu in range(n_u - 1):
            u0 = iu / float(n_u - 1) * 3.0
            u1 = (iu + 1) / float(n_u - 1) * 3.0
            mid = 0.25 * (np.array(p_a[iu]) + p_a[iu + 1] + np.array(p_b[iu]) + p_b[iu + 1])
            if rk < 3:
                hint = np.array([-mid[0], 0.95 - mid[1], 0.35], dtype=np.float32)
            elif rk == 3:
                hint = np.array([mid[0] * 0.35, (mid[1] - 0.95) * 0.35, 1.0], dtype=np.float32)
            else:
                hint = np.array([mid[0], mid[1] - 0.95, 0.25 if rk == 4 else -0.2], dtype=np.float32)
            add_oriented_quad(
                mb,
                p_a[iu], p_a[iu + 1], p_b[iu + 1], p_b[iu],
                (u0, v0), (u1, v0), (u1, v1), (u0, v1),
                c_a[iu], c_a[iu + 1], c_b[iu + 1], c_b[iu],
                hint,
            )

    throat_back_pts, _ = rings[0]
    back_center = np.array([0.0, 0.85, -1.42], dtype=np.float32)
    dark_c = _ao_color(0.06)
    for iu in range(n_u - 1):
        add_oriented_tri(
            mb,
            back_center, throat_back_pts[iu], throat_back_pts[iu + 1],
            (0.5, 0.5), (0.3, 0.6), (0.7, 0.6),
            dark_c, _ao_color(0.09), _ao_color(0.09),
            (0.0, 0.0, 1.0),
        )

    portal_pts = [
        (-0.34, -0.05, -1.32),
        (-0.28,  0.48, -1.32),
        ( 0.00,  0.92, -1.32),
        ( 0.28,  0.48, -1.32),
        ( 0.34, -0.05, -1.32),
    ]
    p_bot = np.array([0.0, -0.05, -1.32], dtype=np.float32)
    void_c = (0.015, 0.006, 0.010, 1.0)
    for ip in range(len(portal_pts) - 1):
        add_oriented_tri(
            mb,
            p_bot, portal_pts[ip], portal_pts[ip + 1],
            (0.5, 0.5), (0.5, 0.6), (0.6, 0.5),
            void_c, void_c, void_c,
            (0.0, 0.0, 1.0),
        )

    left_back = np.array([-1.55, 1.1, -1.35], dtype=np.float32)
    right_back = np.array([1.55, 1.1, -1.35], dtype=np.float32)
    back_outer_pts, back_outer_cols = rings[-1]
    for iu in range(n_u - 1):
        mid = 0.5 * (np.array(back_outer_pts[iu]) + np.array(back_outer_pts[iu + 1]))
        anchor = left_back if mid[0] < 0 else right_back
        if abs(mid[0]) < 1.15 and mid[1] > 1.8:
            anchor = np.array([0.0, 2.2, -1.35], dtype=np.float32)
        add_oriented_tri(
            mb,
            anchor, back_outer_pts[iu], back_outer_pts[iu + 1],
            (0.5, 0.5), (0.6, 0.5), (0.5, 0.6),
            _ao_color(0.18), back_outer_cols[iu], back_outer_cols[iu + 1],
            (0.0, 0.0, -1.0),
        )

    # ------------------------------------------------------------------
    # 2. Upper Eye Socket & Muscular Brow Frame (y = 2.66 .. 5.45)
    # ------------------------------------------------------------------
    ex, ey, ez = EYE_PIVOT
    n_ang = 32
    thetas = np.linspace(0.0, 2.0 * math.pi, n_ang, endpoint=False)

    eye_rings = []
    for k_spec in range(7):
        pts = []
        cols = []
        for i, th in enumerate(thetas):
            ct = math.cos(th)
            st = math.sin(th)
            taper_top = 1.0 - 0.18 * max(0.0, st)
            ix = 1.02 * ct * taper_top
            iy = 0.94 * st + 0.24 * (max(0.0, st) ** 1.7)

            # High-exponent superellipse gives straight side alignment down into the lower pillars
            p_exp = 5.0
            super_r = (abs(ct) ** p_exp + abs(st) ** p_exp) ** (-1.0 / p_exp)
            ox = ct * super_r * 2.16
            if st >= 0.0:
                oy = ey + st * super_r * 1.56 + 0.08 * (1.0 - abs(ct))
            else:
                # Overlap flush down to y = 2.62 so there is zero horizontal seam or waist pinch
                oy = ey + st * super_r * 1.26

            if k_spec == 0:
                px, py, pz, bright = ex + ix * 0.45, ey + iy * 0.45, ez - 0.98, 0.06
            elif k_spec == 1:
                px, py, pz, bright = ex + ix * 0.96, ey + iy * 0.96, ez - 0.30, 0.24
            elif k_spec == 2:
                px, py, pz, bright = ex + ix, ey + iy, ez + 0.44, 0.72
            elif k_spec == 3:
                blend = 0.46
                px = (ex + ix) * (1.0 - blend) + ox * blend
                py = (ey + iy) * (1.0 - blend) + oy * blend
                fold = 0.08 * math.sin(9.0 * th + 0.4) + 0.06 * organic_warp(px, py, ez + 0.74)
                pz = ez + 0.76 + fold
                bright = 1.00
            elif k_spec == 4:
                px, py = ox, oy
                pz = ez + 0.34 + 0.06 * organic_warp(ox, oy, 0.5)
                bright = 0.84
            elif k_spec == 5:
                px, py, pz, bright = ox, oy, -0.45, 0.54
            else:
                px, py, pz, bright = ox, oy, -1.35, 0.28

            pts.append([px, py, pz])
            cols.append(_ao_color(bright * (0.90 + 0.10 * math.cos(7.0 * th))))
        eye_rings.append((pts, cols))

    for rk in range(len(eye_rings) - 1):
        p_a, c_a = eye_rings[rk]
        p_b, c_b = eye_rings[rk + 1]
        v0 = rk / float(len(eye_rings) - 1) * 1.6
        v1 = (rk + 1) / float(len(eye_rings) - 1) * 1.6
        for i in range(n_ang):
            j = (i + 1) % n_ang
            u0 = i / float(n_ang) * 3.0
            u1 = (i + 1) / float(n_ang) * 3.0
            mid = 0.25 * (np.array(p_a[i]) + p_a[j] + np.array(p_b[i]) + p_b[j])
            if rk <= 1:
                hint = np.array([ex - mid[0], ey - mid[1], 0.45], dtype=np.float32)
            elif rk <= 3:
                hint = np.array([(mid[0] - ex) * 0.4, (mid[1] - ey) * 0.4, 1.0], dtype=np.float32)
            else:
                hint = np.array([mid[0] - ex, mid[1] - ey, -0.15], dtype=np.float32)
            add_oriented_quad(
                mb,
                p_a[i], p_a[j], p_b[j], p_b[i],
                (u0, v0), (u1, v0), (u1, v1), (u0, v1),
                c_a[i], c_a[j], c_b[j], c_b[i],
                hint,
            )

    cup_pts, _ = eye_rings[0]
    cup_center = np.array([ex, ey, ez - 1.08], dtype=np.float32)
    for i in range(n_ang):
        j = (i + 1) % n_ang
        add_oriented_tri(
            mb,
            cup_center, cup_pts[i], cup_pts[j],
            (0.5, 0.5), (0.6, 0.5), (0.5, 0.6),
            _ao_color(0.04), _ao_color(0.07), _ao_color(0.07),
            (0.0, 0.0, 1.0),
        )

    back_eye_pts, back_eye_cols = eye_rings[-1]
    back_eye_center = np.array([ex, ey, -1.35], dtype=np.float32)
    for i in range(n_ang):
        j = (i + 1) % n_ang
        add_oriented_tri(
            mb,
            back_eye_center, back_eye_pts[i], back_eye_pts[j],
            (0.5, 0.5), (0.6, 0.5), (0.5, 0.6),
            _ao_color(0.18), back_eye_cols[i], back_eye_cols[j],
            (0.0, 0.0, -1.0),
        )

    return mb


def _add_tube_strand(mb, pts, radii, n_sides=6, u_repeat=2.0, col_ends=(0.55, 0.90)):
    """Extrudes an organic muscle tendon or vein cord along a 3D polyline."""
    n_pts = len(pts)
    rings = []
    for k in range(n_pts):
        p = np.asarray(pts[k], dtype=np.float32)
        if k == 0:
            t_dir = np.asarray(pts[1], dtype=np.float32) - p
        elif k == n_pts - 1:
            t_dir = p - np.asarray(pts[k - 1], dtype=np.float32)
        else:
            t_dir = np.asarray(pts[k + 1], dtype=np.float32) - np.asarray(pts[k - 1], dtype=np.float32)
        t_dir /= max(1e-6, float(np.linalg.norm(t_dir)))
        ref = np.array([0.0, 0.0, 1.0], dtype=np.float32) if abs(t_dir[2]) < 0.9 else np.array([1.0, 0.0, 0.0], dtype=np.float32)
        b1 = np.cross(t_dir, ref)
        b1 /= max(1e-6, float(np.linalg.norm(b1)))
        b2 = np.cross(t_dir, b1)
        r = radii[k]
        ring = [p + (b1 * math.cos(a) + b2 * math.sin(a)) * r for a in np.linspace(0.0, 2.0 * math.pi, n_sides, endpoint=False)]
        rings.append(ring)

    c0, c1 = col_ends
    for k in range(n_pts - 1):
        t0 = k / float(n_pts - 1)
        t1 = (k + 1) / float(n_pts - 1)
        col_a = _ao_color(c0 + (c1 - c0) * math.sin(t0 * math.pi))
        col_b = _ao_color(c0 + (c1 - c0) * math.sin(t1 * math.pi))
        for s in range(n_sides):
            sn = (s + 1) % n_sides
            p00 = rings[k][s]
            p10 = rings[k][sn]
            p11 = rings[k + 1][sn]
            p01 = rings[k + 1][s]
            center = 0.5 * (np.asarray(pts[k]) + np.asarray(pts[k + 1]))
            mid = 0.25 * (p00 + p10 + p11 + p01)
            add_oriented_quad(
                mb, p00, p10, p11, p01,
                (s / float(n_sides), t0 * u_repeat),
                ((s + 1) / float(n_sides), t0 * u_repeat),
                ((s + 1) / float(n_sides), t1 * u_repeat),
                (s / float(n_sides), t1 * u_repeat),
                col_a, col_a, col_b, col_b,
                mid - center,
            )


def build_door_tendons():
    """Creates sinewy flesh tendrils inside the eye socket and vein cords hugging the cheek walls."""
    mb = MeshBuilder("Door_Tendons", "mat_hell_organs")
    _, _, ez = EYE_PIVOT

    # Tattered muscular strands hugging upper/side curves of the eye socket without blocking the iris
    strand_configs = [
        (-0.74, 4.72, -0.88, 3.60, 0.068),
        (-0.56, 4.86, -0.74, 4.08, 0.056),
        (-0.26, 4.98, -0.44, 4.52, 0.048),
        ( 0.26, 4.98,  0.44, 4.52, 0.048),
        ( 0.56, 4.86,  0.74, 4.08, 0.058),
        ( 0.74, 4.72,  0.88, 3.60, 0.070),
        (-0.85, 3.55, -0.58, 3.04, 0.060),
        ( 0.85, 3.55,  0.58, 3.04, 0.060),
    ]
    for idx, (x0, y0, x1, y1, r_base) in enumerate(strand_configs):
        pts = []
        rads = []
        for s in range(6):
            t = s / 5.0
            sx = x0 + (x1 - x0) * t + 0.05 * math.sin(t * math.pi + idx)
            sy = y0 + (y1 - y0) * t
            sz = ez + 0.42 + 0.14 * math.sin(t * math.pi)
            pts.append((sx, sy, sz))
            rads.append(r_base * (1.20 - 0.65 * math.sin(t * math.pi)))
        _add_tube_strand(mb, pts, rads, n_sides=5, u_repeat=1.5, col_ends=(0.50, 0.92))

    # Twisted muscular cheek cords embedded snugly into the pillar surface (tapered at both ends)
    for side in (-1.0, 1.0):
        for offset, x_pos in enumerate((1.56, 1.90)):
            pts = []
            rads = []
            for s in range(9):
                t = s / 8.0
                sy = 0.05 + t * 4.85
                sx = side * x_pos + side * 0.10 * math.sin(t * 5.2 + offset * 1.4)
                sz = 0.72 + 0.08 * math.sin(t * 3.8 + offset)
                pts.append((sx, sy, sz))
                rads.append(0.085 * (0.45 + 0.55 * math.sin(t * math.pi)))
            _add_tube_strand(mb, pts, rads, n_sides=6, u_repeat=2.4, col_ends=(0.45, 0.90))

    return mb


def build_eyeball():
    """Builds Door_Eyeball centered at local (0,0,0) facing +Z with pivot at EYE_PIVOT."""
    mb = MeshBuilder("Door_Eyeball", "mat_eyeball", translation=EYE_PIVOT)
    r_eye = 0.76
    n_lat = 18
    n_lon = 28

    def eye_uv(nx, ny, nz):
        if nz >= 0.0:
            u = 0.5 + 0.48 * nx
            v = 0.5 - 0.48 * ny
        else:
            d = max(1e-5, math.hypot(nx, ny))
            u = 0.5 + 0.48 * (nx / d)
            v = 0.5 - 0.48 * (ny / d)
        return (u, v)

    grid = []
    for ilat in range(n_lat + 1):
        phi = -0.5 * math.pi + (ilat / float(n_lat)) * math.pi
        cp = math.cos(phi)
        sp = math.sin(phi)
        row = []
        for ilon in range(n_lon):
            th = (ilon / float(n_lon)) * 2.0 * math.pi
            nx = cp * math.sin(th)
            ny = sp
            nz = cp * math.cos(th)
            pos = (nx * r_eye, ny * r_eye, nz * r_eye)
            uv = eye_uv(nx, ny, nz)
            shade = 0.80 + 0.20 * max(0.0, nz)
            col = (shade, shade * 0.97, shade * 0.98, 1.0)
            row.append((pos, uv, col))
        grid.append(row)

    for ilat in range(n_lat):
        for ilon in range(n_lon):
            jlon = (ilon + 1) % n_lon
            p00, uv00, c00 = grid[ilat][ilon]
            p10, uv10, c10 = grid[ilat][jlon]
            p11, uv11, c11 = grid[ilat + 1][jlon]
            p01, uv01, c01 = grid[ilat + 1][ilon]
            mid = 0.25 * (np.array(p00) + p10 + p11 + p01)
            add_oriented_quad(mb, p00, p10, p11, p01, uv00, uv10, uv11, uv01, c00, c10, c11, c01, mid)

    return mb


def build_eyelid(name, lat_min_deg, lat_max_deg, is_upper):
    """
    Builds a thickened 3D fleshy eyelid shell pivoted around EYE_PIVOT
    so rotating around local X opens/closes cleanly over Door_Eyeball.
    """
    mb = MeshBuilder(name, "mat_flesh_wall", translation=EYE_PIVOT)
    r_in = 0.80
    r_out = 0.87
    n_lat = 6
    n_lon = 14
    lon_min = math.radians(-76.0)
    lon_max = math.radians(76.0)
    lat_a = math.radians(lat_min_deg)
    lat_b = math.radians(lat_max_deg)

    def surf_pt(rad, lat, lon):
        cp = math.cos(lat)
        return np.array([
            rad * cp * math.sin(lon),
            rad * math.sin(lat),
            rad * cp * math.cos(lon),
        ], dtype=np.float32)

    outer_grid = []
    inner_grid = []
    for ilat in range(n_lat + 1):
        t_lat = ilat / float(n_lat)
        lat = lat_a + (lat_b - lat_a) * t_lat
        rim_t = (1.0 - t_lat) if is_upper else t_lat
        bright = 0.68 + 0.32 * (1.0 - 0.45 * rim_t)
        row_o = []
        row_i = []
        for ilon in range(n_lon + 1):
            t_lon = ilon / float(n_lon)
            lon = lon_min + (lon_max - lon_min) * t_lon
            row_o.append((surf_pt(r_out, lat, lon), (t_lon * 1.4, t_lat * 0.9), _ao_color(bright, warm=0.25 * rim_t)))
            row_i.append((surf_pt(r_in, lat, lon), (t_lon * 1.4, t_lat * 0.9), _ao_color(bright * 0.55)))
        outer_grid.append(row_o)
        inner_grid.append(row_i)

    for ilat in range(n_lat):
        for ilon in range(n_lon):
            po00, uo00, co00 = outer_grid[ilat][ilon]
            po10, uo10, co10 = outer_grid[ilat][ilon + 1]
            po11, uo11, co11 = outer_grid[ilat + 1][ilon + 1]
            po01, uo01, co01 = outer_grid[ilat + 1][ilon]
            mid_o = 0.25 * (po00 + po10 + po11 + po01)
            add_oriented_quad(mb, po00, po10, po11, po01, uo00, uo10, uo11, uo01, co00, co10, co11, co01, mid_o)

            pi00, ui00, ci00 = inner_grid[ilat][ilon]
            pi10, ui10, ci10 = inner_grid[ilat][ilon + 1]
            pi11, ui11, ci11 = inner_grid[ilat + 1][ilon + 1]
            pi01, ui01, ci01 = inner_grid[ilat + 1][ilon]
            mid_i = 0.25 * (pi00 + pi10 + pi11 + pi01)
            add_oriented_quad(mb, pi00, pi10, pi11, pi01, ui00, ui10, ui11, ui01, ci00, ci10, ci11, ci01, -mid_i)

    lip_idx = 0 if is_upper else n_lat
    lip_dir = np.array([0.0, -1.0 if is_upper else 1.0, 0.2], dtype=np.float32)
    for ilon in range(n_lon):
        po0, uo0, co0 = outer_grid[lip_idx][ilon]
        po1, uo1, co1 = outer_grid[lip_idx][ilon + 1]
        pi0, ui0, ci0 = inner_grid[lip_idx][ilon]
        pi1, ui1, ci1 = inner_grid[lip_idx][ilon + 1]
        add_oriented_quad(mb, pi0, pi1, po1, po0, ui0, ui1, uo1, uo0, ci0, ci1, co1, co0, lip_dir)

    return mb


def build_upper_gum():
    """Swollen protruding horseshoe upper gum arch pivoted at UPPER_JAW_PIVOT."""
    mb = MeshBuilder("Door_Upper_Gum", "mat_flesh_wall", translation=UPPER_JAW_PIVOT)
    n_arc = 18

    prof_pts = [
        (-0.34,  0.18, 0.35),
        ( 0.04, -0.02, 0.65),
        ( 0.42, -0.10, 0.90),
        ( 0.62,  0.04, 1.00),
        ( 0.58,  0.26, 0.96),
        ( 0.32,  0.42, 0.84),
        (-0.10,  0.40, 0.58),
        (-0.34,  0.18, 0.35),
    ]

    grid = []
    for ia in range(n_arc + 1):
        t = ia / float(n_arc)
        ang = math.radians(-62.0 + 124.0 * t)
        cx = 1.08 * math.sin(ang)
        cz = 0.44 * (math.cos(ang) - 0.35)
        bump = 0.055 * abs(math.sin(t * 6.0 * math.pi))
        fwd_x, fwd_z = math.sin(ang), math.cos(ang)

        row = []
        for ip in range(len(prof_pts)):
            pz_off, py, bright = prof_pts[ip]
            p = np.array([
                cx + fwd_x * (pz_off + bump),
                py + 0.08 * math.cos(ang) - 0.02,
                cz + fwd_z * (pz_off + bump),
            ], dtype=np.float32)
            uv = (t * 2.2, ip / float(len(prof_pts) - 1) * 1.1)
            row.append((p, uv, _ao_color(bright, warm=0.35)))
        grid.append(row)

    for ia in range(n_arc):
        for ip in range(len(prof_pts) - 1):
            p00, u00, c00 = grid[ia][ip]
            p10, u10, c10 = grid[ia + 1][ip]
            p11, u11, c11 = grid[ia + 1][ip + 1]
            p01, u01, c01 = grid[ia][ip + 1]
            mid = 0.25 * (p00 + p10 + p11 + p01)
            center = np.array([mid[0] * 0.55, 0.14, -0.15], dtype=np.float32)
            add_oriented_quad(mb, p00, p10, p11, p01, u00, u10, u11, u01, c00, c10, c11, c01, mid - center)

    return mb


def _add_tooth_prism(mb, cx, cz, width, height, depth, yaw_deg, tilt_x_deg, notch_seed):
    """Builds a sculpted asymmetric 6-sided PS1 horror tooth hanging downward cleanly."""
    n_sides = 6
    n_y = 4
    yaw = math.radians(yaw_deg)
    tilt = math.radians(tilt_x_deg)
    cy_cos, cy_sin = math.cos(yaw), math.sin(yaw)
    tx_cos, tx_sin = math.cos(tilt), math.sin(tilt)

    rings = []
    ring_centers = []
    for iy in range(n_y + 1):
        t = iy / float(n_y)
        rx = 0.5 * width * (0.94 + 0.14 * math.sin(t * math.pi) - 0.12 * (t ** 2))
        rz = 0.5 * depth * (1.0 - 0.50 * (t ** 1.35))
        ring = []
        for s in range(n_sides):
            ang = (s / float(n_sides)) * 2.0 * math.pi
            lx = rx * math.cos(ang)
            lz = rz * math.sin(ang)
            ly = 0.04 - t * height
            if iy == n_y:
                ly += 0.04 * math.sin(s * 2.1 + notch_seed)
            ty = ly * tx_cos - lz * tx_sin
            tz = ly * tx_sin + lz * tx_cos
            wx = cx + lx * cy_cos + tz * cy_sin
            wz = cz - lx * cy_sin + tz * cy_cos
            wy = ty
            u = s / float(n_sides)
            v = 0.04 + 0.92 * t
            shade = 0.74 + 0.26 * t
            col = (shade, shade * (0.78 + 0.22 * t), shade * (0.74 + 0.26 * t), 1.0)
            ring.append((np.array([wx, wy, wz], dtype=np.float32), (u, v), col))
        rings.append(ring)
        ring_centers.append(np.mean([r[0] for r in ring], axis=0))

    for iy in range(n_y):
        c_mid = 0.5 * (ring_centers[iy] + ring_centers[iy + 1])
        for s in range(n_sides):
            sn = (s + 1) % n_sides
            p00, u00, c00 = rings[iy][s]
            p10, u10, c10 = rings[iy][sn]
            p11, u11, c11 = rings[iy + 1][sn]
            p01, u01, c01 = rings[iy + 1][s]
            mid = 0.25 * (p00 + p10 + p11 + p01)
            add_oriented_quad(mb, p00, p10, p11, p01, u00, u10, u11, u01, c00, c10, c11, c01, mid - c_mid)

    bot_ring = rings[-1]
    bot_center = ring_centers[-1] - np.array([0.0, 0.015, 0.0], dtype=np.float32)
    for s in range(n_sides):
        sn = (s + 1) % n_sides
        p0, u0, c0 = bot_ring[s]
        p1, u1, c1 = bot_ring[sn]
        add_oriented_tri(mb, bot_center, p0, p1, (0.5, 0.96), u0, u1, c0, c0, c1, (0.0, -1.0, 0.15))


def build_upper_teeth():
    """6 chunky crooked yellow-bloodstained teeth tucked clearly inside the mouth arch."""
    mb = MeshBuilder("Door_Upper_Teeth", "mat_teeth", translation=UPPER_JAW_PIVOT)
    teeth_specs = [
        (-0.88, 0.22, 0.29, 0.56, 0.25, -30.0, 11.0, 0.4),
        (-0.54, 0.42, 0.34, 0.65, 0.28, -14.0,  8.0, 1.3),
        (-0.19, 0.52, 0.39, 0.76, 0.30,  -4.0,  6.0, 2.2),
        ( 0.20, 0.52, 0.38, 0.73, 0.30,   5.0,  7.0, 3.5),
        ( 0.55, 0.41, 0.33, 0.63, 0.28,  15.0,  8.0, 4.1),
        ( 0.89, 0.21, 0.28, 0.55, 0.24,  32.0, 11.0, 5.2),
    ]
    for spec in teeth_specs:
        _add_tooth_prism(mb, *spec)
    return mb


def build_lower_tongue_jaw():
    """Protruding fleshy tongue / threshold lip at bottom of mouth, pivoted at LOWER_JAW_PIVOT."""
    mb = MeshBuilder("Door_Lower_Jaw", "mat_flesh_wall", translation=LOWER_JAW_PIVOT)
    nx = 10
    nz = 7
    top_grid = []
    bot_grid = []

    for iz in range(nz + 1):
        tz = iz / float(nz)
        z = -0.35 + tz * 1.62
        half_w = 1.02 * (1.0 - 0.32 * (tz ** 1.8))
        row_t = []
        row_b = []
        for ix in range(nx + 1):
            tx = (ix / float(nx)) * 2.0 - 1.0
            x = tx * half_w
            z_cur = z - 0.24 * (tx * tx) * tz
            edge_drop = 1.0 - (tx * tx) ** 1.6
            groove = -0.045 * math.exp(-(tx / 0.22) ** 2) * (1.0 - 0.4 * tz)
            tip_taper = (1.0 - 0.55 * (tz ** 2.2))
            y_top = (0.04 + 0.16 * edge_drop * tip_taper) + groove
            y_bot = -0.08
            bright = 0.65 + 0.35 * (0.4 + 0.6 * tz) * (0.6 + 0.4 * edge_drop)
            row_t.append((np.array([x, y_top, z_cur], dtype=np.float32), ((tx + 1.0) * 0.9, tz * 1.4), _ao_color(bright, warm=0.45)))
            row_b.append((np.array([x, y_bot, z_cur], dtype=np.float32), ((tx + 1.0) * 0.9, tz * 1.4), _ao_color(0.28)))
        top_grid.append(row_t)
        bot_grid.append(row_b)

    for iz in range(nz):
        for ix in range(nx):
            pt00, ut00, ct00 = top_grid[iz][ix]
            pt10, ut10, ct10 = top_grid[iz][ix + 1]
            pt11, ut11, ct11 = top_grid[iz + 1][ix + 1]
            pt01, ut01, ct01 = top_grid[iz + 1][ix]
            add_oriented_quad(mb, pt00, pt10, pt11, pt01, ut00, ut10, ut11, ut01, ct00, ct10, ct11, ct01, (0.0, 1.0, 0.15))

            pb00, ub00, cb00 = bot_grid[iz][ix]
            pb10, ub10, cb10 = bot_grid[iz][ix + 1]
            pb11, ub11, cb11 = bot_grid[iz + 1][ix + 1]
            pb01, ub01, cb01 = bot_grid[iz + 1][ix]
            add_oriented_quad(mb, pb00, pb10, pb11, pb01, ub00, ub10, ub11, ub01, cb00, cb10, cb11, cb01, (0.0, -1.0, 0.0))

    for ix in range(nx):
        pt0, ut0, ct0 = top_grid[-1][ix]
        pt1, ut1, ct1 = top_grid[-1][ix + 1]
        pb0, ub0, cb0 = bot_grid[-1][ix]
        pb1, ub1, cb1 = bot_grid[-1][ix + 1]
        add_oriented_quad(mb, pb0, pb1, pt1, pt0, ub0, ub1, ut1, ut0, cb0, cb1, ct1, ct0, (0.0, 0.2, 1.0))

    for iz in range(nz):
        for side_idx, hint_x in ((0, -1.0), (nx, 1.0)):
            pt0, ut0, ct0 = top_grid[iz][side_idx]
            pt1, ut1, ct1 = top_grid[iz + 1][side_idx]
            pb0, ub0, cb0 = bot_grid[iz][side_idx]
            pb1, ub1, cb1 = bot_grid[iz + 1][side_idx]
            add_oriented_quad(mb, pb0, pb1, pt1, pt0, ub0, ub1, ut1, ut0, cb0, cb1, ct1, ct0, (hint_x, 0.2, 0.0))

    return mb


def build_flesh_eye_door(project_root):
    tex_dir = os.path.join(project_root, "Textures")
    tex_paths = generate_all_textures(tex_dir)

    materials_spec = {
        "mat_flesh_wall": {
            "image_path": tex_paths["tex_flesh_wall.png"],
            "roughness": 0.78,
            "metallic": 0.0,
            "double_sided": False,
        },
        "mat_hell_organs": {
            "image_path": tex_paths["tex_hell_organs.png"],
            "roughness": 0.82,
            "metallic": 0.0,
            "double_sided": False,
        },
        "mat_teeth": {
            "image_path": tex_paths["tex_teeth.png"],
            "roughness": 0.55,
            "metallic": 0.0,
            "double_sided": False,
        },
        "mat_eyeball": {
            "image_path": tex_paths["tex_eyeball.png"],
            "roughness": 0.18,
            "metallic": 0.0,
            "double_sided": False,
        },
    }

    meshes = [
        build_door_flesh_frame(),
        build_door_tendons(),
        build_eyeball(),
        build_eyelid("Door_Eyelid_Upper", 28.0, 108.0, is_upper=True),
        build_eyelid("Door_Eyelid_Lower", -98.0, -24.0, is_upper=False),
        build_upper_gum(),
        build_upper_teeth(),
        build_lower_tongue_jaw(),
    ]

    glb_path = os.path.join(project_root, "Flesh_Eye_Door.glb")
    stats = export_glb(glb_path, meshes, materials_spec, root_name="Flesh_Eye_Door")
    return stats, glb_path


if __name__ == "__main__":
    project_root = r"C:\Users\user\IdeaProjects\Game final\Assets\PilotChamber"
    stats, glb_path = build_flesh_eye_door(project_root)
    print("Door GLB Export Stats:", stats)

    preview_dir = os.path.join(project_root, "Preview")
    os.makedirs(preview_dir, exist_ok=True)
    gltf, meshes = load_glb(glb_path)
    print(f"Validated Door GLB! NodesWithMesh={len(meshes)}, Materials={len(gltf['materials'])}, Textures={len(gltf['textures'])}")

    views = [
        ("preview_door_front.png", (0.0, 2.75, 7.6), (0.0, 2.65, 0.2), 42.0, (0.25, 0.55, 0.80)),
        ("preview_door_iso.png",   (-5.4, 4.4, 6.2), (0.0, 2.55, 0.2), 42.0, (-0.35, 0.65, 0.70)),
    ]
    for fname, cam, tgt, fov, ldir in views:
        im = render_view(meshes, cam, tgt, width=720, height=540, fov_deg=fov, light_dir=ldir)
        out_p = os.path.join(preview_dir, fname)
        im.save(out_p)
        print("Saved preview:", out_p)
