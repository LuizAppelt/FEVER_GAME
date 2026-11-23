import math
import os
import sys
import numpy as np

TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ASSETS = os.path.abspath(os.path.join(TOOLS_DIR, ".."))
TEX_DIR = os.path.join(PROJECT_ASSETS, "Textures")
GLB_OUT = os.path.join(PROJECT_ASSETS, "Cross_Razor_Greatsword.glb")

sys.path.insert(0, TOOLS_DIR)
from glb_exporter import MeshBuilder, export_glb
from retro_textures import fbm_periodic, apply_bayer_palette


def make_smooth_cross_tex(path):
    size = 128
    n1 = fbm_periodic(size, base_period=6, octaves=3, seed=8101)
    n2 = fbm_periodic(size, base_period=16, octaves=2, seed=8102)
    val = np.clip(0.65 * n1 + 0.35 * n2, 0.0, 1.0)
    c_low = np.array([42, 44, 48], dtype=np.float32)
    c_mid = np.array([124, 128, 136], dtype=np.float32)
    c_hi = np.array([228, 232, 238], dtype=np.float32)
    pal = [[38, 40, 44], [78, 82, 88], [124, 128, 136], [176, 180, 190], [230, 234, 240]]

    m_low = val < 0.5
    t1 = np.clip(val / 0.5, 0, 1)[..., None]
    t2 = np.clip((val - 0.5) / 0.5, 0, 1)[..., None]
    rgb = np.where(m_low[..., None], c_low * (1 - t1) + c_mid * t1, c_mid * (1 - t2) + c_hi * t2)
    img = apply_bayer_palette(rgb, pal, dither_strength=12.0)
    img.save(path)


def make_clean_razor_tex(path):
    size = 128
    n1 = fbm_periodic(size, base_period=8, octaves=3, seed=8201)
    yy, xx = np.meshgrid(np.linspace(0, 1, size, endpoint=False),
                         np.linspace(0, 1, size, endpoint=False), indexing="ij")
    brush = 0.5 + 0.5 * np.sin(yy * math.pi * 56 + n1 * 1.4)
    edge_dist = np.minimum(xx, 1.0 - xx)
    bevel = np.clip(1.0 - edge_dist / 0.14, 0.0, 1.0) ** 1.3
    val = np.clip(0.45 * brush + 0.30 * n1 + 0.25 * (1.0 - np.abs(yy - 0.5)), 0.0, 1.0)

    c_body = np.array([148, 152, 160], dtype=np.float32)
    c_mid = np.array([208, 212, 220], dtype=np.float32)
    c_edge = np.array([248, 250, 254], dtype=np.float32)
    pal = [[132, 136, 144], [166, 170, 178], [198, 202, 210], [226, 230, 236], [248, 250, 254]]

    base = c_body * (1 - val[..., None]) + c_mid * val[..., None]
    rgb = base * (1 - bevel[..., None] * 0.72) + c_edge * (bevel[..., None] * 0.72)
    img = apply_bayer_palette(rgb, pal, dither_strength=10.0)
    img.save(path)


def add_quad_strip(mb, ring_a, ring_b, colors_a, colors_b, u_scale=1.0, v0=0.0, v1=1.0):
    n = len(ring_a)
    for i in range(n):
        j = (i + 1) % n
        u0 = (i / float(n)) * u_scale
        u1 = ((i + 1) / float(n)) * u_scale
        mb.add_flat_quad(ring_a[i], ring_a[j], ring_b[j], ring_b[i],
                         (u0, v0), (u1, v0), (u1, v1), (u0, v1),
                         colors_a[i], colors_a[j], colors_b[j], colors_b[i])


def build_smooth_diamond_tip(mb, center, axis_dir, width, length, thick, col_outer, col_inner, hollow=True):
    cx, cy, _ = center
    ax, ay = axis_dir
    alen = math.hypot(ax, ay)
    ax, ay = ax / alen, ay / alen
    px, py = -ay, ax

    pts_outer = [
        (cx - ax * length * 0.08, cy - ay * length * 0.08),
        (cx + px * width + ax * length * 0.28, cy + py * width + ay * length * 0.28),
        (cx + ax * length * 1.00, cy + ay * length * 1.00),
        (cx - px * width + ax * length * 0.28, cy - py * width + ay * length * 0.28),
    ]
    pts_inner = [
        (cx + ax * length * 0.12, cy + ay * length * 0.12),
        (cx + px * width * 0.44 + ax * length * 0.30, cy + py * width * 0.44 + ay * length * 0.30),
        (cx + ax * length * 0.56, cy + ay * length * 0.56),
        (cx - px * width * 0.44 + ax * length * 0.30, cy - py * width * 0.44 + ay * length * 0.30),
    ]

    for sign_z in (1.0, -1.0):
        z_out = sign_z * thick * 0.28
        z_ridge = sign_z * thick * 0.90
        for i in range(4):
            j = (i + 1) % 4
            ox0, oy0 = pts_outer[i]
            ox1, oy1 = pts_outer[j]
            ix0, iy0 = pts_inner[i]
            ix1, iy1 = pts_inner[j]
            if sign_z > 0:
                mb.add_flat_quad((ox0, oy0, z_out), (ox1, oy1, z_out),
                                 (ix1, iy1, z_ridge), (ix0, iy0, z_ridge),
                                 (0.1, 0.1), (0.9, 0.1), (0.7, 0.9), (0.3, 0.9),
                                 col_outer, col_outer, col_inner, col_inner)
            else:
                mb.add_flat_quad((ox1, oy1, z_out), (ox0, oy0, z_out),
                                 (ix0, iy0, z_ridge), (ix1, iy1, z_ridge),
                                 (0.9, 0.1), (0.1, 0.1), (0.3, 0.9), (0.7, 0.9),
                                 col_outer, col_outer, col_inner, col_inner)

    for i in range(4):
        j = (i + 1) % 4
        ox0, oy0 = pts_outer[i]
        ox1, oy1 = pts_outer[j]
        z0 = thick * 0.28
        mb.add_flat_quad((ox0, oy0, -z0), (ox1, oy1, -z0),
                         (ox1, oy1, z0), (ox0, oy0, z0),
                         (0, 0), (1, 0), (1, 0.4), (0, 0.4),
                         col_outer, col_outer, col_outer, col_outer)
    if hollow:
        for i in range(4):
            j = (i + 1) % 4
            ix0, iy0 = pts_inner[i]
            ix1, iy1 = pts_inner[j]
            z0 = thick * 0.90
            mb.add_flat_quad((ix1, iy1, -z0), (ix0, iy0, -z0),
                             (ix0, iy0, z0), (ix1, iy1, z0),
                             (0, 0), (1, 0), (1, 0.4), (0, 0.4),
                             col_inner, col_inner, col_inner, col_inner)


def octagon_ring_xz(cx, cy, rx, rz):
    return [(cx + math.cos(k / 8.0 * 2.0 * math.pi) * rx, cy, math.sin(k / 8.0 * 2.0 * math.pi) * rz) for k in range(8)]


def octagon_ring_yz(px, py, ry, rz):
    return [(px, py + math.cos(k / 8.0 * 2.0 * math.pi) * ry, math.sin(k / 8.0 * 2.0 * math.pi) * rz) for k in range(8)]


def build_inverted_half_cross(mb, cross_scale=0.50, y_shift=0.28):
    c_dark = (0.52, 0.54, 0.58, 1.0)
    c_mid = (0.80, 0.82, 0.86, 1.0)
    c_hi = (0.98, 0.99, 1.00, 1.0)
    ring_cols = [c_hi, c_mid, c_mid, c_dark, c_dark, c_mid, c_mid, c_hi]

    guard_arch = -0.04
    for sign_x in (1.0, -1.0):
        arm_steps = [
            (0.03, 0.00, 0.056, 0.042),
            (0.14, guard_arch * 0.25, 0.046, 0.036),
            (0.28, guard_arch * 0.65, 0.042, 0.032),
            (0.41, guard_arch * 1.00, 0.046, 0.034),
        ]
        rings = [octagon_ring_yz(sign_x * ax, ay, ry, rz) for ax, ay, ry, rz in arm_steps]
        for i in range(len(rings) - 1):
            if sign_x > 0:
                add_quad_strip(mb, rings[i], rings[i + 1], ring_cols, ring_cols, 1.0, i * 0.25, (i + 1) * 0.25)
            else:
                add_quad_strip(mb, rings[i + 1], rings[i], ring_cols, ring_cols, 1.0, i * 0.25, (i + 1) * 0.25)

        build_smooth_diamond_tip(mb, (sign_x * 0.41, guard_arch, 0.0), (sign_x, guard_arch * 0.25),
                                 width=0.095, length=0.22, thick=0.038,
                                 col_outer=c_mid, col_inner=c_hi, hollow=True)

    hub_b = octagon_ring_xz(0, -0.06, 0.068, 0.050)
    hub_t = octagon_ring_xz(0,  0.06, 0.068, 0.050)
    add_quad_strip(mb, hub_b, hub_t, ring_cols, ring_cols, 1.0, 0, 0.3)

    handle_prof = [
        (-1.12, 0.014, 0.014),
        (-0.92, 0.032, 0.028),
        (-0.62, 0.038, 0.032),
        (-0.26, 0.046, 0.036),
        (0.00,  0.060, 0.044),
    ]
    for i in range(len(handle_prof) - 1):
        r0 = octagon_ring_xz(0, handle_prof[i][0], handle_prof[i][1], handle_prof[i][2])
        r1 = octagon_ring_xz(0, handle_prof[i + 1][0], handle_prof[i + 1][1], handle_prof[i + 1][2])
        add_quad_strip(mb, r0, r1, ring_cols, ring_cols, 1.0, i * 0.25, (i + 1) * 0.25)

    cap_b = octagon_ring_xz(0, -1.16, 0.004, 0.004)
    cap_t = octagon_ring_xz(0, -1.12, 0.034, 0.030)
    add_quad_strip(mb, cap_b, cap_t, ring_cols, ring_cols, 1.0, 0, 0.2)

    up_prof = [
        (0.00, 0.056, 0.042),
        (0.14, 0.046, 0.034),
        (0.24, 0.040, 0.030),
    ]
    for i in range(len(up_prof) - 1):
        r0 = octagon_ring_xz(0, up_prof[i][0], up_prof[i][1], up_prof[i][2])
        r1 = octagon_ring_xz(0, up_prof[i + 1][0], up_prof[i + 1][1], up_prof[i + 1][2])
        add_quad_strip(mb, r0, r1, ring_cols, ring_cols, 1.0, 0, 0.3)

    build_smooth_diamond_tip(mb, (0.0, 0.24, 0.0), (0.0, 1.0),
                             width=0.095, length=0.22, thick=0.040,
                             col_outer=c_mid, col_inner=c_hi, hollow=True)

    mb.positions = [[p[0] * cross_scale, p[1] * cross_scale + y_shift, p[2] * cross_scale] for p in mb.positions]
    mb.recompute_smooth_normals()


def build_double_edged_razor(mb, half_w=0.25, half_h=0.24, body_t=0.015, edge_t=0.002,
                             taper_top=1.0, pointy_tip=False):
    stations = [
        (-1.00, 0.82, 0.000),
        (-0.88, 0.82, 0.000),
        (-0.87, 1.00, 0.000),
        (-0.80, 1.00, 0.250),
        (-0.66, 1.00, 0.250),
        (-0.62, 1.00, 0.115),
        (-0.44, 1.00, 0.195),
        (-0.32, 1.00, 0.115),
        (-0.14, 1.00, 0.240),
        (0.00,  1.00, 0.255),
        (0.14,  1.00, 0.240),
        (0.32,  1.00, 0.115),
        (0.44,  1.00, 0.195),
        (0.62,  1.00, 0.115),
        (0.66,  1.00, 0.250),
        (0.80,  1.00, 0.250),
        (0.87,  1.00, 0.000),
        (0.88,  0.82, 0.000),
        (1.00,  0.82, 0.000),
    ]
    if pointy_tip:
        stations[-3] = (0.85, 0.90, 0.000)
        stations[-2] = (0.94, 0.42, 0.000)
        stations[-1] = (1.14, 0.02, 0.000)

    c_body = (0.88, 0.90, 0.94, 1.0)
    c_bevel = (0.98, 0.99, 1.00, 1.0)
    c_hole = (0.64, 0.68, 0.74, 1.0)

    def get_x(ny, omult):
        t_up = (ny + 1.0) * 0.5
        w_scale = 1.0 * (1.0 - t_up) + taper_top * t_up
        x_out = half_w * w_scale * omult
        x_bev = max(0.01, x_out - 0.042 * w_scale)
        return x_out, x_bev

    for idx in range(len(stations) - 1):
        ny0, om0, hw0 = stations[idx]
        ny1, om1, hw1 = stations[idx + 1]
        y0, y1 = ny0 * half_h, ny1 * half_h
        xout0, xbev0 = get_x(ny0, om0)
        xout1, xbev1 = get_x(ny1, om1)
        xh0, xh1 = half_w * hw0, half_w * hw1
        u_v0, u_v1 = (ny0 + 1.0) * 0.5, (ny1 + 1.0) * 0.5

        for side in (1.0, -1.0):
            for zsign in (1.0, -1.0):
                zb, ze = zsign * body_t, zsign * edge_t
                p00, p01 = (side * xh0, y0, zb), (side * xbev0, y0, zb)
                p11, p10 = (side * xbev1, y1, zb), (side * xh1, y1, zb)
                if (side > 0) == (zsign > 0):
                    mb.add_flat_quad(p00, p01, p11, p10,
                                     (xh0 / half_w, u_v0), (xbev0 / half_w, u_v0),
                                     (xbev1 / half_w, u_v1), (xh1 / half_w, u_v1),
                                     c_hole, c_body, c_body, c_hole)
                else:
                    mb.add_flat_quad(p01, p00, p10, p11,
                                     (xbev0 / half_w, u_v0), (xh0 / half_w, u_v0),
                                     (xh1 / half_w, u_v1), (xbev1 / half_w, u_v1),
                                     c_body, c_hole, c_hole, c_body)

                b00, b01 = (side * xbev0, y0, zb), (side * xout0, y0, ze)
                b11, b10 = (side * xout1, y1, ze), (side * xbev1, y1, zb)
                if (side > 0) == (zsign > 0):
                    mb.add_flat_quad(b00, b01, b11, b10,
                                     (0.85, u_v0), (1.0, u_v0), (1.0, u_v1), (0.85, u_v1),
                                     c_body, c_bevel, c_bevel, c_body)
                else:
                    mb.add_flat_quad(b01, b00, b10, b11,
                                     (1.0, u_v0), (0.85, u_v0), (0.85, u_v1), (1.0, u_v1),
                                     c_bevel, c_body, c_body, c_bevel)

            e_f0, e_b0 = (side * xout0, y0, edge_t), (side * xout0, y0, -edge_t)
            e_b1, e_f1 = (side * xout1, y1, -edge_t), (side * xout1, y1, edge_t)
            if side > 0:
                mb.add_flat_quad(e_f0, e_b0, e_b1, e_f1, (0, 0), (0.1, 0), (0.1, 1), (0, 1), c_bevel, c_bevel, c_bevel, c_bevel)
            else:
                mb.add_flat_quad(e_b0, e_f0, e_f1, e_b1, (0, 0), (0.1, 0), (0.1, 1), (0, 1), c_bevel, c_bevel, c_bevel, c_bevel)

            if hw0 > 0.001 or hw1 > 0.001:
                h_f0, h_b0 = (side * xh0, y0, body_t), (side * xh0, y0, -body_t)
                h_b1, h_f1 = (side * xh1, y1, -body_t), (side * xh1, y1, body_t)
                if side > 0:
                    mb.add_flat_quad(h_b0, h_f0, h_f1, h_b1, (0, 0), (0.2, 0), (0.2, 1), (0, 1), c_hole, c_hole, c_hole, c_hole)
                else:
                    mb.add_flat_quad(h_f0, h_b0, h_b1, h_f1, (0, 0), (0.2, 0), (0.2, 1), (0, 1), c_hole, c_hole, c_hole, c_hole)

    bx0, _ = get_x(-1.0, stations[0][1])
    tx0, _ = get_x(stations[-1][0], stations[-1][1])
    mb.add_flat_quad((-bx0, -half_h, body_t), (bx0, -half_h, body_t),
                     (bx0, -half_h, -body_t), (-bx0, -half_h, -body_t),
                     (0, 0), (1, 0), (1, 0.2), (0, 0.2), c_hole, c_hole, c_hole, c_hole)
    top_y = stations[-1][0] * half_h
    mb.add_flat_quad((-tx0, top_y, -body_t), (tx0, top_y, -body_t),
                     (tx0, top_y, body_t), (-tx0, top_y, body_t),
                     (0, 0), (1, 0), (1, 0.2), (0, 0.2), c_bevel, c_bevel, c_bevel, c_bevel)


def main():
    os.makedirs(TEX_DIR, exist_ok=True)
    tex_cross = os.path.join(TEX_DIR, "tex_cross_razor_hilt.png")
    tex_razor = os.path.join(TEX_DIR, "tex_razor_steel.png")
    make_smooth_cross_tex(tex_cross)
    make_clean_razor_tex(tex_razor)

    # Pivot shifted +0.28m so origin (0,0,0) lies squarely on the two-handed cross grip
    y_shift = 0.28
    mb_cross = MeshBuilder("Cross_Hilt_Smooth", "Mat_Cross_Razor_Hilt")
    build_inverted_half_cross(mb_cross, cross_scale=0.50, y_shift=y_shift)

    base_y = 0.46 * 0.50 + 0.31 + y_shift
    slots = [
        ("Razor_01_Base", base_y + 0.00, 0.25, 0.24, 1.0, False),
        ("Razor_02_Mid",  base_y + 0.60, 0.24, 0.24, 1.0, False),
        ("Razor_03_Tip",  base_y + 1.20, 0.22, 0.25, 0.92, True),
    ]
    blades = []
    for name, y_pos, hw, hh, taper, pointy in slots:
        mb_r = MeshBuilder(name, "Mat_Razor_Steel", translation=[0.0, y_pos, 0.0])
        build_double_edged_razor(mb_r, half_w=hw, half_h=hh, taper_top=taper, pointy_tip=pointy)
        blades.append(mb_r)

    mat_specs = {
        "Mat_Cross_Razor_Hilt": {
            "image_path": tex_cross,
            "base_color": [1.0, 1.0, 1.0, 1.0],
            "metallic": 0.70,
            "roughness": 0.30,
            "double_sided": True,
        },
        "Mat_Razor_Steel": {
            "image_path": tex_razor,
            "base_color": [1.0, 1.0, 1.0, 1.0],
            "metallic": 0.85,
            "roughness": 0.20,
            "double_sided": True,
        },
    }
    info = export_glb(GLB_OUT, [mb_cross] + blades, mat_specs, root_name="Cross_Razor_Greatsword")
    print(f"Exported approved Cross Razor Greatsword: {info}")


if __name__ == "__main__":
    main()
