import os
import math
import numpy as np

from retro_textures import generate_all_textures
from glb_exporter import MeshBuilder, export_glb
from preview_render import load_glb, render_view


# ---------------------------------------------------------------------------
# Organic Noise & Signed Distance Helpers
# ---------------------------------------------------------------------------

def organic_noise(x, z):
    """Deterministic smooth multi-frequency organic warp for stomach tissue."""
    return (
        0.45 * math.sin(x * 0.28 + z * 0.22)
        + 0.32 * math.cos(x * 0.62 - z * 0.54 + 0.9)
        + 0.23 * math.sin(x * 1.25 + z * 1.12 - 0.6)
    )


def sd_ellipse(x, z, cx, cz, rx, rz):
    dx = (x - cx) / rx
    dz = (z - cz) / rz
    return (math.sqrt(dx * dx + dz * dz) - 1.0) * min(rx, rz)


def sd_capsule_2d(x, z, ax, az, bx, bz, r):
    px, pz = x - ax, z - az
    dx, dz = bx - ax, bz - az
    denom = dx * dx + dz * dz
    t = max(0.0, min(1.0, (px * dx + pz * dz) / denom)) if denom > 1e-8 else 0.0
    cx, cz = ax + t * dx, az + t * dz
    return math.hypot(x - cx, z - cz) - r


def smooth_min(a, b, k=1.2):
    h = max(k - abs(a - b), 0.0) / k
    return min(a, b) - h * h * k * 0.25


def angular_diff(a, b):
    return math.atan2(math.sin(a - b), math.cos(a - b))


def outer_dome_radius(theta):
    """
    Colossal stomach dome outer contour (~62m wide in X by ~50m deep in Z).
    Includes smooth anatomical fundus/greater-curvature warp and clean door framing
    around West Entry (-25.8, -2.5) and East Exit (26.8, 0.5).
    """
    rx = 28.6
    rz = 23.2
    base = 1.0 / math.sqrt((math.cos(theta) / rx) ** 2 + (math.sin(theta) / rz) ** 2)
    warp = (
        1.10 * math.sin(3.0 * theta + 0.45)
        + 0.68 * math.cos(5.0 * theta - 0.95)
        + 0.32 * math.sin(8.0 * theta + 0.25)
    )
    r = base + warp

    # Smoothly guide wall contour through West Entry (-26.1, -2.5) and East Exit (27.1, 0.5)
    th_west = math.atan2(-2.5, -25.8)
    dw = abs(angular_diff(theta, th_west))
    if dw < 0.28:
        w_t = 0.5 * (1.0 + math.cos(math.pi * dw / 0.28))
        r = r * (1.0 - w_t * 0.75) + 26.35 * (w_t * 0.75)

    th_east = math.atan2(0.5, 26.8)
    de = abs(angular_diff(theta, th_east))
    if de < 0.28:
        e_t = 0.5 * (1.0 + math.cos(math.pi * de / 0.28))
        r = r * (1.0 - e_t * 0.75) + 27.15 * (e_t * 0.75)

    return r


# 7 Main Walkable Islands Specification: (cx, cz, rx, rz)
ISLAND_SPECS = [
    (-20.5, -2.5, 5.6, 4.8),   # 0: West Entry Island
    (  0.0,  1.0, 9.6, 8.0),   # 1: Central Heart Arena (Main Combat Hub)
    ( -5.5, 14.2, 5.8, 4.4),   # 2: North Ulcer Sanctuary
    ( 11.5, 13.0, 5.4, 4.4),   # 3: Northeast Rib Bastion
    ( -6.5, -12.5, 5.2, 4.0),  # 4: South Acid-Edge Island
    ( 10.5, -11.0, 5.4, 4.2),  # 5: Southeast Tendon Island
    ( 21.5,  0.5, 5.8, 5.0),   # 6: East Exit Boss Gate Island
]


def island_sdf(x, z):
    """
    Signed distance to the 7 walkable flesh islands.
    <= 0 means walkable flesh floor; > 0 drops into the colossal boiling acid lake.
    """
    w = organic_noise(x * 0.65, z * 0.65) * 0.45
    wx = x + 0.38 * math.sin(z * 0.42)
    wz = z + 0.38 * math.cos(x * 0.38)

    # 1. West Entry Island + neck reaching West Entry Flesh Door (-25.8, -2.5)
    d_west = sd_ellipse(wx, wz, -20.5, -2.5, 5.6, 4.8)
    d_west_neck = sd_capsule_2d(wx, wz, -20.5, -2.5, -26.2, -2.5, 2.5)
    d_west = smooth_min(d_west, d_west_neck, k=1.0)

    # 2. Central Heart Arena (Main Combat Hub)
    d_center = sd_ellipse(wx, wz, 0.0, 1.0, 9.6, 8.0)

    # 3. North Ulcer Sanctuary
    d_north = sd_ellipse(wx, wz, -5.5, 14.2, 5.8, 4.4)

    # 4. Northeast Rib Bastion
    d_ne = sd_ellipse(wx, wz, 11.5, 13.0, 5.4, 4.4)

    # 5. South Acid-Edge Island
    d_south = sd_ellipse(wx, wz, -6.5, -12.5, 5.2, 4.0)

    # 6. Southeast Tendon Island
    d_se = sd_ellipse(wx, wz, 10.5, -11.0, 5.4, 4.2)

    # 7. East Exit Boss Gate Island + neck reaching East Exit Flesh Door (26.8, 0.5)
    d_east = sd_ellipse(wx, wz, 21.5, 0.5, 5.8, 5.0)
    d_east_neck = sd_capsule_2d(wx, wz, 21.5, 0.5, 27.1, 0.5, 2.7)
    d_east = smooth_min(d_east, d_east_neck, k=1.0)

    d = min(d_west, d_center, d_north, d_ne, d_south, d_se, d_east) + w * 0.48

    # Ensure clear boiling acid bays around the 2 independent floating cartilage islands
    # so they float visibly surrounded by acid
    d_float1_bay = 3.55 - math.hypot(x - (-12.5), z - 7.5)
    d_float2_bay = 3.35 - math.hypot(x - 5.0, z - (-12.0))
    d = max(d, d_float1_bay, d_float2_bay)

    return d


def terrain_height_and_color(x, z):
    """
    Returns (y, rgba) for Dome_Floor_Walkable.
    Features raised island plateaus (y ~ 0.22..0.28) sloping down to the shoreline
    where (-0.42 <= y <= 0.18) is stained with a corroded yellow-green bile/acid rim
    (0.72, 1.0, 0.38, 1.0).
    """
    d = island_sdf(x, z)
    rugae = (
        0.045 * math.sin(x * 1.15 + organic_noise(x, z))
        + 0.038 * math.cos(z * 1.30 - x * 0.45)
    )

    if d <= 0.0:
        # Walkable fleshy island surface: gentle shoreline shelf (-1.5 <= d <= 0) in [-0.42, 0.18]
        # rising smoothly to plateau at y ~ 0.25 in the island interior
        shelf_t = max(0.0, min(1.0, -d / 1.15))
        plateau_t = max(0.0, min(1.0, (-d - 0.85) / 1.35))
        shelf_s = shelf_t * shelf_t * (3.0 - 2.0 * shelf_t)
        plateau_s = plateau_t * plateau_t * (3.0 - 2.0 * plateau_t)
        y = -0.14 + 0.28 * shelf_s + 0.12 * plateau_s + rugae * (0.40 + 0.35 * shelf_s)

        # Visceral crimson-flesh shading with subtle dark arena depression tint
        edge_t = 1.0 - shelf_s
        arena_r = math.hypot(x, z - 1.0)
        center_tint = 1.0 - 0.12 * math.exp(-(arena_r ** 2) / 36.0)
        r = (0.98 - 0.22 * (edge_t ** 1.4)) * center_tint
        g = (0.86 + 0.12 * (edge_t ** 1.4)) * center_tint
        b = (0.84 - 0.32 * (edge_t ** 1.4)) * center_tint
    elif d < 1.35:
        # Steep visceral cliff dropping into the boiling acid lake (acid at y = -0.42)
        t = d / 1.35
        s = t * t * (3.0 - 2.0 * t)
        y = -0.14 * (1.0 - s) - 1.08 * s + rugae * (1.0 - t) * 0.35
        r = 0.58 * (1.0 - 0.55 * t)
        g = 0.52 * (1.0 - 0.20 * t)
        b = 0.40 * (1.0 - 0.60 * t)
    else:
        # Submerged basin bottom under the boiling acid lake
        y = -1.08 + 0.09 * organic_noise(x * 1.1, z * 1.1)
        r, g, b = 0.15, 0.38, 0.14

    # Corroded Acid Waterline Mucus Rim: where -0.42 <= y <= 0.18, blend toward (0.72, 1.0, 0.38, 1.0)
    if -0.42 <= y <= 0.18:
        band_pos = (y - (-0.42)) / 0.60  # 0 at -0.42, 1 at +0.18
        rim_bell = math.sin(max(0.0, min(1.0, band_pos)) * math.pi)
        stain_w = 0.55 + 0.42 * (rim_bell ** 0.65)
        r = r * (1.0 - stain_w) + 0.72 * stain_w
        g = g * (1.0 - stain_w) + 1.00 * stain_w
        b = b * (1.0 - stain_w) + 0.38 * stain_w

    return y, (r, g, b, 1.0)


def clamp_to_wall_footprint(x, z, margin=0.35):
    """Clamps (x, z) cleanly inside outer_dome_radius(theta) + margin so no triangles poke out."""
    r = math.hypot(x, z)
    if r < 1e-6:
        return x, z, r, 25.0
    th = math.atan2(z, x)
    r_wall = outer_dome_radius(th)
    r_max = r_wall + margin
    if r > r_max:
        scale = r_max / r
        return x * scale, z * scale, r_max, r_wall
    return x, z, r, r_wall


# ---------------------------------------------------------------------------
# 1. Walkable Islands & Boiling Acid Lake
# ---------------------------------------------------------------------------

def build_terrain_and_acid():
    """Builds Dome_Floor_Walkable (128x108 grid) and Dome_Acid_Lake (at y = -0.42)."""
    floor_mb = MeshBuilder("Dome_Floor_Walkable", "mat_flesh_floor")
    acid_mb = MeshBuilder("Dome_Acid_Lake", "mat_acid")

    nx, nz = 128, 108
    x_min, x_max = -31.0, 31.0
    z_min, z_max = -24.5, 25.5
    xs = np.linspace(x_min, x_max, nx)
    zs = np.linspace(z_min, z_max, nz)

    vert_idx = np.full((nx, nz), -1, dtype=np.int32)
    uv_scale = 0.24

    for ix, gx in enumerate(xs):
        for iz, gz in enumerate(zs):
            r_raw = math.hypot(gx, gz)
            th = math.atan2(gz, gx)
            r_wall = outer_dome_radius(th)
            if r_raw <= r_wall + 1.35:
                x, z, r, r_w = clamp_to_wall_footprint(float(gx), float(gz), margin=0.35)
                y, col = terrain_height_and_color(x, z)
                wall_prox = max(0.0, min(1.0, (r - (r_w - 1.6)) / 2.6))
                col = (
                    col[0] * (1.0 - 0.50 * wall_prox),
                    col[1] * (1.0 - 0.50 * wall_prox),
                    col[2] * (1.0 - 0.50 * wall_prox),
                    1.0,
                )
                u = x * uv_scale + 0.15 * math.sin(z * 0.35)
                v = z * uv_scale + 0.15 * math.cos(x * 0.32)
                idx = floor_mb.add_vertex((x, y, z), (0.0, 1.0, 0.0), (u, v), col)
                vert_idx[ix, iz] = idx

    for ix in range(nx - 1):
        for iz in range(nz - 1):
            i00 = vert_idx[ix, iz]
            i01 = vert_idx[ix, iz + 1]
            i11 = vert_idx[ix + 1, iz + 1]
            i10 = vert_idx[ix + 1, iz]
            if i00 >= 0 and i01 >= 0 and i11 >= 0:
                floor_mb.add_tri_indices(i00, i01, i11)
            if i00 >= 0 and i11 >= 0 and i10 >= 0:
                floor_mb.add_tri_indices(i00, i11, i10)

    floor_mb.recompute_smooth_normals()

    # Colossal Boiling Acid Lake at y = -0.42
    anx, anz = 92, 78
    axs = np.linspace(x_min, x_max, anx)
    azs = np.linspace(z_min, z_max, anz)
    a_idx = np.full((anx, anz), -1, dtype=np.int32)
    acid_uv_scale = 0.21

    for ix, gx in enumerate(axs):
        for iz, gz in enumerate(azs):
            r_raw = math.hypot(gx, gz)
            th = math.atan2(gz, gx)
            r_wall = outer_dome_radius(th)
            if r_raw <= r_wall + 1.05:
                x, z, _, _ = clamp_to_wall_footprint(float(gx), float(gz), margin=0.08)
                d = island_sdf(x, z)
                y_acid = -0.42 + 0.022 * math.sin(x * 0.85 + z * 0.75)
                shore_glow = max(0.0, min(1.0, 1.0 - abs(d - 0.55) / 2.1))
                col = (
                    0.74 + 0.26 * shore_glow,
                    0.92 + 0.08 * shore_glow,
                    0.68 + 0.26 * shore_glow,
                    1.0,
                )
                idx = acid_mb.add_vertex(
                    (x, y_acid, z),
                    (0.0, 1.0, 0.0),
                    (x * acid_uv_scale, z * acid_uv_scale),
                    col,
                )
                a_idx[ix, iz] = idx

    for ix in range(anx - 1):
        for iz in range(anz - 1):
            i00 = a_idx[ix, iz]
            i01 = a_idx[ix, iz + 1]
            i11 = a_idx[ix + 1, iz + 1]
            i10 = a_idx[ix + 1, iz]
            if i00 >= 0 and i01 >= 0 and i11 >= 0:
                acid_mb.add_tri_indices(i00, i01, i11)
            if i00 >= 0 and i11 >= 0 and i10 >= 0:
                acid_mb.add_tri_indices(i00, i11, i10)

    acid_mb.recompute_smooth_normals()
    return floor_mb, acid_mb


# ---------------------------------------------------------------------------
# 2. Three Types of Walkable Island Connections
# ---------------------------------------------------------------------------

def add_cartilage_bridge(mb, p_start, p_end, width=3.2, arch_height=0.30, n_len=12):
    """Adds a wide ribbed cartilage bridge spanning over the acid lake."""
    sx, sz = p_start
    ex, ez = p_end
    dx, dz = ex - sx, ez - sz
    length = math.hypot(dx, dz)
    fwd = np.array([dx / length, 0.0, dz / length], dtype=np.float32)
    right = np.array([fwd[2], 0.0, -fwd[0]], dtype=np.float32)

    hw = width * 0.5
    profile = [
        (-hw - 0.22, -0.38),
        (-hw - 0.14,  0.16),
        (-hw + 0.22,  0.28),
        (-hw + 0.46,  0.05),
        (0.0,         0.08),
        ( hw - 0.46,  0.05),
        ( hw - 0.22,  0.28),
        ( hw + 0.14,  0.16),
        ( hw + 0.22, -0.38),
        (0.0,        -0.62),
    ]
    n_prof = len(profile)
    rings = []

    for i in range(n_len + 1):
        t = i / float(n_len)
        arch = 0.16 + arch_height * math.sin(t * math.pi)
        rib = 0.05 * math.sin(t * 7.0 * math.pi)
        flare = 1.0 + 0.20 * ((2.0 * t - 1.0) ** 4)
        base_pos = np.array([sx + dx * t, arch, sz + dz * t], dtype=np.float32)

        ring_pts = []
        ring_cols = []
        end_blood = (2.0 * t - 1.0) ** 4

        for px, py in profile:
            world_p = (
                base_pos
                + right * (px * flare)
                + np.array([0.0, py + (rib if py > 0.02 else 0.0), 0.0], dtype=np.float32)
            )
            acid_tint = max(0.0, min(1.0, (-0.08 - py) / 0.50))
            r = (1.0 - 0.30 * acid_tint) * (0.96 - 0.16 * end_blood)
            g = (0.94 + 0.06 * acid_tint) * (0.92 - 0.48 * end_blood)
            b = (0.90 - 0.45 * acid_tint) * (0.90 - 0.48 * end_blood)
            ring_pts.append(world_p)
            ring_cols.append((r, g, b, 1.0))

        rings.append((ring_pts, ring_cols))

    for i in range(n_len):
        pts0, cols0 = rings[i]
        pts1, cols1 = rings[i + 1]
        v0 = i / float(n_len) * 3.2
        v1 = (i + 1) / float(n_len) * 3.2
        for k in range(n_prof):
            kn = (k + 1) % n_prof
            u0 = k / float(n_prof) * 2.0
            u1 = (k + 1) / float(n_prof) * 2.0
            mb.add_flat_quad(
                pts0[k], pts1[k], pts1[kn], pts0[kn],
                (u0, v0), (u0, v1), (u1, v1), (u1, v0),
                cols0[k], cols1[k], cols1[kn], cols0[kn],
            )


def add_tendon_bridge(mb, p_start, p_end, width=2.1, arch_height=0.26, n_len=12):
    """Adds a narrow stretched sinewy tendon/muscle bridge with taut longitudinal fibers."""
    sx, sz = p_start
    ex, ez = p_end
    dx, dz = ex - sx, ez - sz
    length = math.hypot(dx, dz)
    fwd = np.array([dx / length, 0.0, dz / length], dtype=np.float32)
    right = np.array([fwd[2], 0.0, -fwd[0]], dtype=np.float32)

    hw = width * 0.5
    # Sinewy multi-bundle cross section (2 outer tendon cords + striated center muscle deck)
    profile = [
        (-hw - 0.12, -0.28),
        (-hw - 0.05,  0.14),
        (-hw + 0.18,  0.22),
        (-hw + 0.36,  0.04),
        (0.0,         0.09),
        ( hw - 0.36,  0.04),
        ( hw - 0.18,  0.22),
        ( hw + 0.05,  0.14),
        ( hw + 0.12, -0.28),
        (0.0,        -0.46),
    ]
    n_prof = len(profile)
    rings = []

    for i in range(n_len + 1):
        t = i / float(n_len)
        # Taut catenary/arch with flared muscular roots at island attachments
        arch = 0.16 + arch_height * math.sin(t * math.pi)
        neck_pinch = 0.86 + 0.32 * ((2.0 * t - 1.0) ** 2)
        sinew_pulse = 0.03 * math.sin(t * 4.0 * math.pi)
        base_pos = np.array([sx + dx * t, arch, sz + dz * t], dtype=np.float32)

        ring_pts = []
        ring_cols = []
        tension_highlight = math.sin(t * math.pi)

        for px, py in profile:
            world_p = (
                base_pos
                + right * (px * neck_pinch)
                + np.array([0.0, py + sinew_pulse, 0.0], dtype=np.float32)
            )
            acid_glow = max(0.0, min(1.0, (-0.08 - py) / 0.42))
            r = 0.78 + 0.22 * tension_highlight - 0.18 * acid_glow
            g = 0.52 + 0.20 * tension_highlight + 0.38 * acid_glow
            b = 0.54 + 0.16 * tension_highlight - 0.20 * acid_glow
            ring_pts.append(world_p)
            ring_cols.append((r, g, b, 1.0))

        rings.append((ring_pts, ring_cols))

    for i in range(n_len):
        pts0, cols0 = rings[i]
        pts1, cols1 = rings[i + 1]
        v0 = i / float(n_len) * 3.0
        v1 = (i + 1) / float(n_len) * 3.0
        for k in range(n_prof):
            kn = (k + 1) % n_prof
            u0 = k / float(n_prof) * 2.0
            u1 = (k + 1) / float(n_prof) * 2.0
            mb.add_flat_quad(
                pts0[k], pts1[k], pts1[kn], pts0[kn],
                (u0, v0), (u0, v1), (u1, v1), (u1, v0),
                cols0[k], cols1[k], cols1[kn], cols0[kn],
            )


def add_calcified_mound(mb, cx, cy, cz, rx, rz, height=0.26, seed=1.0, n_rad=14):
    """
    Adds a porous chyme-corroded calcified cartilage platform or stepping stone
    with acid-etched lower rim and walkable flat top.
    """
    prof = [
        (1.14, -0.52, (0.45, 0.78, 0.32, 1.0)),          # submerged acid-stained base
        (1.04, -0.18, (0.68, 0.96, 0.42, 1.0)),          # corroded waterline rim
        (0.95,  height * 0.65, (0.86, 0.84, 0.76, 1.0)), # calcified upper bevel
        (0.76,  height, (0.98, 0.95, 0.88, 1.0)),        # flat walkable top rim
    ]
    rings = []
    for r_scale, y_off, col in prof:
        pts = []
        cols = []
        for i in range(n_rad):
            th = (i / float(n_rad)) * 2.0 * math.pi
            wobble = (
                1.0
                + 0.10 * math.sin(3.0 * th + seed)
                + 0.07 * math.cos(5.0 * th - seed * 1.3)
            )
            px = cx + rx * r_scale * wobble * math.cos(th)
            pz = cz + rz * r_scale * wobble * math.sin(th)
            pts.append([px, cy + y_off, pz])
            cols.append(col)
        rings.append((pts, cols))

    for k in range(len(rings) - 1):
        p0, c0 = rings[k]
        p1, c1 = rings[k + 1]
        v0 = k / float(len(rings) - 1) * 1.4
        v1 = (k + 1) / float(len(rings) - 1) * 1.4
        for i in range(n_rad):
            inext = (i + 1) % n_rad
            u0 = i / float(n_rad) * 2.5
            u1 = (i + 1) / float(n_rad) * 2.5
            mb.add_flat_quad(
                p0[i], p1[i], p1[inext], p0[inext],
                (u0, v0), (u0, v1), (u1, v1), (u1, v0),
                c0[i], c1[i], c1[inext], c0[inext],
            )

    top_center = [cx, cy + height + 0.04, cz]
    pt, ct = rings[-1]
    for i in range(n_rad):
        inext = (i + 1) % n_rad
        th0 = (i / float(n_rad)) * 2.0 * math.pi
        th1 = (inext / float(n_rad)) * 2.0 * math.pi
        uv_c = (0.5, 0.5)
        uv0 = (0.5 + 0.45 * math.cos(th0), 0.5 + 0.45 * math.sin(th0))
        uv1 = (0.5 + 0.45 * math.cos(th1), 0.5 + 0.45 * math.sin(th1))
        mb.add_flat_triangle(
            top_center, pt[inext], pt[i],
            uv_c, uv1, uv0,
            (1.0, 0.98, 0.92, 1.0), ct[inext], ct[i],
        )


def build_island_connections():
    """
    Builds the 3 types of walkable island connections:
    1. Dome_Bridges_Cartilage (5 wide ribbed cartilage bridges)
    2. Dome_Bridges_Tendon (4 narrow stretched muscle/tendon bridges)
    3. Dome_Stepping_Stones_Calcified + Dome_Floating_Island_01 + Dome_Floating_Island_02
    """
    cartilage_mb = MeshBuilder("Dome_Bridges_Cartilage", "mat_cartilage")
    tendon_mb = MeshBuilder("Dome_Bridges_Tendon", "mat_hell_organs")
    stepping_mb = MeshBuilder("Dome_Stepping_Stones_Calcified", "mat_digested_calcified")
    float_01_mb = MeshBuilder("Dome_Floating_Island_01", "mat_digested_calcified", translation=(-12.5, -0.15, 7.5))
    float_02_mb = MeshBuilder("Dome_Floating_Island_02", "mat_digested_calcified", translation=(5.0, -0.15, -12.0))

    # 1. Wide Ribbed Cartilage Bridges (width 3.0 - 3.4m)
    cartilage_spans = [
        ((-15.5, -2.0), (-8.8,  0.2), 3.4, 0.32),  # West Entry <-> Central Arena
        ((  8.8,  0.8), (16.2,  0.6), 3.4, 0.32),  # Central Arena <-> East Exit
        (( -3.2,  8.0), (-4.8, 10.4), 3.1, 0.25),  # Central Arena <-> North Sanctuary
        (( -0.2, 14.2), ( 6.6, 13.4), 3.2, 0.30),  # North Sanctuary <-> Northeast Bastion
        (( 14.2,  9.5), (19.0,  4.5), 3.1, 0.30),  # Northeast Bastion <-> East Exit
    ]
    for p0, p1, w, ah in cartilage_spans:
        add_cartilage_bridge(cartilage_mb, p0, p1, width=w, arch_height=ah, n_len=12)

    # 2. Narrow Stretched Tendon / Muscle Bridges (width 2.1m)
    tendon_spans = [
        ((-16.5, -6.0), (-10.8, -11.2), 2.1, 0.30),  # West Entry <-> South Island
        (( -3.0, -9.2), ( -2.2,  -6.2), 2.1, 0.24),  # South Island <-> Central Arena
        ((  4.8, -5.8), (  7.5,  -8.5), 2.1, 0.25),  # Central Arena <-> Southeast Island
        (( 14.5, -8.5), ( 19.2,  -3.5), 2.1, 0.30),  # Southeast Island <-> East Exit
    ]
    for p0, p1, w, ah in tendon_spans:
        add_tendon_bridge(tendon_mb, p0, p1, width=w, arch_height=ah, n_len=12)

    # 3. Two Independent Floating Cartilage Islands (centered local pivots)
    add_calcified_mound(float_01_mb, 0.0, 0.0, 0.0, rx=2.65, rz=2.45, height=0.34, seed=1.7, n_rad=16)
    add_calcified_mound(float_02_mb, 0.0, 0.0, 0.0, rx=2.55, rz=2.35, height=0.34, seed=3.1, n_rad=16)

    # Submerged Calcified Stepping Stones bridging gaps to the Floating Islands
    stepping_specs = [
        # West Entry <-> Floating Island 01 (-12.5, 7.5) <-> North Ulcer Sanctuary & Central Arena
        (-16.2,  3.2, 1.18, 1.02, 0.16, 1.1),
        (-14.6,  5.2, 1.12, 0.98, 0.15, 2.3),
        (-10.2, 10.1, 1.15, 1.00, 0.16, 3.4),
        ( -8.6, 11.6, 1.10, 0.96, 0.17, 4.2),
        ( -9.6,  5.8, 1.22, 1.05, 0.16, 5.1),
        # South Island (-6.5, -12.5) <-> Floating Island 02 (5.0, -12.0) <-> Southeast Island (10.5, -11.0)
        ( -1.6, -12.4, 1.18, 1.04, 0.16, 6.2),
        (  1.2, -12.2, 1.25, 1.08, 0.17, 7.4),
        (  8.0, -11.6, 1.12, 0.98, 0.16, 8.5),
    ]
    for sx, sz, srx, srz, sh, sseed in stepping_specs:
        add_calcified_mound(stepping_mb, sx, 0.0, sz, rx=srx, rz=srz, height=sh, seed=sseed, n_rad=12)

    return cartilage_mb, tendon_mb, stepping_mb, float_01_mb, float_02_mb


# ---------------------------------------------------------------------------
# 3 & 5. Colossal Cutaway Stomach Dome Wall + Dual-Layer Downward Wall Blood
# ---------------------------------------------------------------------------

def build_dome_walls_and_blood():
    """
    Builds:
    - Dome_Wall_Flesh_Back (z >= -9.5, rises 10.5m to 13.8m with -3.8m inward dome overhang)
    - Dome_Wall_Cutaway_Front (z < -9.5, drops to 0.85m for unobstructed isometric view)
    - Dome_Wall_Blood_Drips (mat_blood_drips, r - 0.14, u_tiles = 18.0, dv = -float(y) * v_scale)
    - Dome_Wall_Blood_Waves (mat_blood_waves, r - 0.20, u_tiles = 12.0, dv = -float(y) * v_scale)
    Leaves clean archway framing recesses at West Entry (-25.8, -2.5) and East Exit (26.8, 0.5).
    """
    back_mb = MeshBuilder("Dome_Wall_Flesh_Back", "mat_flesh_wall")
    front_mb = MeshBuilder("Dome_Wall_Cutaway_Front", "mat_flesh_wall")
    drips_mb = MeshBuilder("Dome_Wall_Blood_Drips", "mat_blood_drips")
    waves_mb = MeshBuilder("Dome_Wall_Blood_Waves", "mat_blood_waves")

    n_seg = 96
    n_inner_rings = 12
    n_outer_rings = 5
    total_rings = n_inner_rings + n_outer_rings

    def is_front_cutaway(z_val):
        return z_val < -9.5

    door_centers = [(-25.8, -2.5), (26.8, 0.5)]

    thetas = np.linspace(0.0, 2.0 * math.pi, n_seg, endpoint=False)
    rings_pos = np.zeros((n_seg, total_rings, 3), dtype=np.float32)
    rings_col = np.zeros((n_seg, total_rings, 4), dtype=np.float32)
    drip_pos = np.zeros((n_seg, n_inner_rings, 3), dtype=np.float32)
    wave_pos = np.zeros((n_seg, n_inner_rings, 3), dtype=np.float32)

    for i, th in enumerate(thetas):
        r_base = outer_dome_radius(float(th))
        cx = math.cos(th)
        cz = math.sin(th)
        x_est = r_base * cx
        z_est = r_base * cz

        # Smooth transition from tall enclosing dome to front dome arch (cut open dynamically in Unity by BG3 shader)
        cut_t = max(0.0, min(1.0, (-6.8 - z_est) / 6.5))
        cut_smooth = cut_t * cut_t * (3.0 - 2.0 * cut_t)

        back_h = (
            15.2
            + 1.15 * math.sin(3.0 * th + 0.35)
            + 0.50 * math.cos(6.0 * th - 0.4)
        )
        front_h = 13.4 + 0.65 * math.sin(5.0 * th)
        h_top = back_h * (1.0 - cut_smooth) + front_h * cut_smooth

        # Flatten rugae and form a clean recessed archway frame at West Entry & East Exit
        door_dist = min(math.hypot(x_est - dx, z_est - dz) for dx, dz in door_centers)
        door_frame = max(0.0, min(1.0, 1.0 - door_dist / 4.2))

        buttress = 0.52 * math.sin(18.0 * th + 0.35 * math.cos(4.0 * th)) * (1.0 - 0.85 * door_frame)

        for k in range(total_rings):
            if k < n_inner_rings:
                t = k / float(n_inner_rings - 1)
                y = -1.15 + (h_top + 1.15) * (t ** 0.84)
                rugae_amp = (0.55 * (1.0 - 0.45 * cut_smooth)) * (1.0 - 0.85 * door_frame)
                rugae_wave = math.sin(t * 4.5 * 2.0 * math.pi + 0.65 * math.sin(5.0 * th))

                # Full 360-degree inward dome overhang curving overhead like a stomach vault
                overhang_t = max(0.0, (t - 0.30) / 0.70)
                overhang = -(r_base * 0.76) * (overhang_t ** 1.48)

                # Slight recessed archway pocket below y = 4.6m at West Entry / East Exit
                arch_recess = 0.65 * door_frame * max(0.0, min(1.0, (4.8 - y) / 2.5))

                r = (
                    r_base
                    + overhang
                    + arch_recess
                    + buttress * math.sin(t * math.pi)
                    - rugae_amp * rugae_wave
                )

                acid_bounce = math.exp(-((y - (-0.42)) ** 2) / 0.65) * 0.58
                crevice = 0.5 + 0.5 * rugae_wave
                ao = (0.28 + 0.56 * (t ** 0.62) + 0.18 * crevice) * (1.0 - 0.28 * arch_recess)
                col = (
                    min(1.0, ao * 1.02 + 0.06 * acid_bounce),
                    min(1.0, ao * 0.76 + 0.50 * acid_bounce),
                    min(1.0, ao * 0.76 + 0.06 * acid_bounce),
                    1.0,
                )
                drip_pos[i, k] = [(r - 0.14) * cx, y, (r - 0.14) * cz]
                wave_pos[i, k] = [(r - 0.20) * cx, y, (r - 0.20) * cz]
            else:
                ot = (k - n_inner_rings + 1) / float(n_outer_rings)
                t_mirror = 1.0 - ot
                y = -1.15 + (h_top + 1.15) * (t_mirror ** 0.84)
                overhang_t = max(0.0, (t_mirror - 0.30) / 0.70)
                overhang = -(r_base * 0.76) * (overhang_t ** 1.48)
                r = r_base + overhang + 1.85 + 0.35 * math.sin(ot * math.pi)
                shade = 0.22 + 0.18 * (1.0 - ot)
                col = (shade * 1.15, shade * 0.36, shade * 0.42, 1.0)

            rings_pos[i, k] = [r * cx, y, r * cz]
            rings_col[i, k] = col

    for i in range(n_seg):
        j = (i + 1) % n_seg
        z_mid = 0.5 * (rings_pos[i, 0, 2] + rings_pos[j, 0, 2])
        x_mid = 0.5 * (rings_pos[i, 0, 0] + rings_pos[j, 0, 0])
        is_front = is_front_cutaway(z_mid)
        target_mb = front_mb if is_front else back_mb

        u0 = i / float(n_seg) * 26.0
        u1 = (i + 1) / float(n_seg) * 26.0

        for k in range(total_rings - 1):
            v0 = k / float(n_inner_rings - 1) * 3.5
            v1 = (k + 1) / float(n_inner_rings - 1) * 3.5

            p00 = rings_pos[i, k]
            p10 = rings_pos[j, k]
            p11 = rings_pos[j, k + 1]
            p01 = rings_pos[i, k + 1]

            c00 = rings_col[i, k]
            c10 = rings_col[j, k]
            c11 = rings_col[j, k + 1]
            c01 = rings_col[i, k + 1]

            target_mb.add_flat_quad(
                p00, p10, p11, p01,
                (u0, v0), (u1, v0), (u1, v1), (u0, v1),
                c00, c10, c11, c01,
            )

        if not is_front:
            near_door = any(math.hypot(x_mid - dx, z_mid - dz) < 3.6 for dx, dz in door_centers)
            blood_layers = (
                (drips_mb, drip_pos, 18.0, 0.28, (1.0, 0.92, 0.92, 1.0)),
                (waves_mb, wave_pos, 12.0, 0.22, (0.95, 0.88, 0.88, 1.0)),
            )
            for k in range(1, n_inner_rings - 2):
                y_avg = 0.25 * (
                    drip_pos[i, k][1] + drip_pos[j, k][1]
                    + drip_pos[j, k + 1][1] + drip_pos[i, k + 1][1]
                )
                if near_door and y_avg < 5.2:
                    continue

                for layer_mb, pos_arr, u_tiles, v_scale, dc0 in blood_layers:
                    lp00 = pos_arr[i, k]
                    lp10 = pos_arr[j, k]
                    lp11 = pos_arr[j, k + 1]
                    lp01 = pos_arr[i, k + 1]

                    du0 = i / float(n_seg) * u_tiles
                    du1 = (i + 1) / float(n_seg) * u_tiles
                    layer_mb.add_flat_quad(
                        lp00, lp10, lp11, lp01,
                        (du0, -float(lp00[1]) * v_scale),
                        (du1, -float(lp10[1]) * v_scale),
                        (du1, -float(lp11[1]) * v_scale),
                        (du0, -float(lp01[1]) * v_scale),
                        dc0, dc0, dc0, dc0,
                    )

    # Add muscular archway collars around West Entry (-25.8, -2.5) and East Exit (26.8, 0.5)
    add_door_arch_collar(back_mb, center=(-25.8, 0.0, -2.5), facing=(1.0, 0.0, 0.08), radius_w=2.9, height=4.4)
    add_door_arch_collar(back_mb, center=( 26.8, 0.0,  0.5), facing=(-1.0, 0.0, -0.04), radius_w=3.1, height=4.6)

    return back_mb, front_mb, drips_mb, waves_mb


def add_door_arch_collar(mb, center, facing, radius_w=3.0, height=4.5):
    """Adds a thick ribbed muscular archway frame recessed into the dome wall for the Flesh Eye Doors."""
    cx, cy, cz = center
    fx, _, fz = facing
    fl = math.hypot(fx, fz)
    fwd = np.array([fx / fl, 0.0, fz / fl], dtype=np.float32)
    up = np.array([0.0, 1.0, 0.0], dtype=np.float32)
    right = np.cross(up, fwd)
    right /= np.linalg.norm(right)
    c_base = np.array([cx, cy, cz], dtype=np.float32)

    n_arc = 14
    n_tube = 8
    tube_r = 0.72
    rings = []

    for i in range(n_arc + 1):
        t = i / float(n_arc)
        ang = t * math.pi  # 0 to pi arch from right floor to left floor
        spine_x = math.cos(ang) * radius_w
        spine_y = math.sin(ang) * height
        spine_pt = c_base + right * spine_x + up * spine_y + fwd * 0.25

        # Radial normal in the arch plane
        r_dir = right * math.cos(ang) + up * math.sin(ang)
        r_dir /= np.linalg.norm(r_dir)

        lobe = 1.0 + 0.16 * math.sin(t * 7.0 * math.pi)
        pts = []
        cols = []
        for k in range(n_tube):
            phi = (k / float(n_tube)) * 2.0 * math.pi
            p = spine_pt + (r_dir * math.cos(phi) + fwd * math.sin(phi)) * (tube_r * lobe)
            shade = 0.52 + 0.44 * (0.5 + 0.5 * math.sin(phi))
            pts.append(p)
            cols.append((min(1.0, shade * 1.05), shade * 0.74, shade * 0.74, 1.0))
        rings.append((pts, cols))

    for i in range(n_arc):
        p0, c0 = rings[i]
        p1, c1 = rings[i + 1]
        v0 = i / float(n_arc) * 3.0
        v1 = (i + 1) / float(n_arc) * 3.0
        for k in range(n_tube):
            kn = (k + 1) % n_tube
            u0 = k / float(n_tube) * 2.0
            u1 = (k + 1) / float(n_tube) * 2.0
            mb.add_flat_quad(
                p0[k], p1[k], p1[kn], p0[kn],
                (u0, v0), (u0, v1), (u1, v1), (u1, v0),
                c0[k], c1[k], c1[kn], c0[kn],
            )


# ---------------------------------------------------------------------------
# 4. Colossal Dome Rib Arches & Overhanging Veins
# ---------------------------------------------------------------------------

def add_swept_organ_tube(mb, spine_pts, radii, n_sides=8, v_tiles=4.0, color_fn=None):
    """Sweeps a closed tubular organ mesh along 3D spine_pts with varying radii."""
    n_spine = len(spine_pts)
    rings = []
    up_ref = np.array([0.0, 1.0, 0.0], dtype=np.float32)

    for i in range(n_spine):
        t = i / float(n_spine - 1)
        p = np.array(spine_pts[i], dtype=np.float32)
        if i == 0:
            tang = np.array(spine_pts[1], dtype=np.float32) - p
        elif i == n_spine - 1:
            tang = p - np.array(spine_pts[i - 1], dtype=np.float32)
        else:
            tang = np.array(spine_pts[i + 1], dtype=np.float32) - np.array(spine_pts[i - 1], dtype=np.float32)
        tl = np.linalg.norm(tang)
        tang = tang / tl if tl > 1e-8 else np.array([0.0, 1.0, 0.0], dtype=np.float32)

        ref = up_ref if abs(np.dot(tang, up_ref)) < 0.92 else np.array([1.0, 0.0, 0.0], dtype=np.float32)
        b1 = np.cross(tang, ref)
        b1 /= np.linalg.norm(b1)
        b2 = np.cross(tang, b1)
        b2 /= np.linalg.norm(b2)

        rad = radii[i]
        pts = []
        cols = []
        for k in range(n_sides):
            phi = (k / float(n_sides)) * 2.0 * math.pi
            rib_bulge = 1.0 + 0.12 * math.cos(3.0 * phi)
            offset = (b1 * math.cos(phi) + b2 * math.sin(phi)) * (rad * rib_bulge)
            wp = p + offset
            if color_fn is not None:
                col = color_fn(t, phi, wp)
            else:
                sh = 0.55 + 0.42 * (0.5 + 0.5 * math.sin(phi))
                col = (min(1.0, sh * 1.06), sh * 0.72, sh * 0.74, 1.0)
            pts.append(wp)
            cols.append(col)
        rings.append((pts, cols))

    for i in range(n_spine - 1):
        p0, c0 = rings[i]
        p1, c1 = rings[i + 1]
        v0 = (i / float(n_spine - 1)) * v_tiles
        v1 = ((i + 1) / float(n_spine - 1)) * v_tiles
        for k in range(n_sides):
            kn = (k + 1) % n_sides
            u0 = k / float(n_sides) * 2.0
            u1 = (k + 1) / float(n_sides) * 2.0
            mb.add_flat_quad(
                p0[k], p1[k], p1[kn], p0[kn],
                (u0, v0), (u0, v1), (u1, v1), (u1, v0),
                c0[k], c1[k], c1[kn], c0[kn],
            )


def build_rib_arches_and_veins():
    """
    Builds Dome_Rib_Arches (mat_hell_organs):
    - 9 massive curved muscular rib-buttresses climbing the inner back/side dome walls
      and curving overhead toward the dome crown
    - 4 visceral vein cords spanning across the dome & acid chasms
    """
    mb = MeshBuilder("Dome_Rib_Arches", "mat_hell_organs")

    # 9 massive curved muscular rib-buttresses along the back & side dome arc
    rib_angles = np.linspace(0.11 * math.pi, 0.89 * math.pi, 9)
    n_steps = 11

    for idx_rib, th in enumerate(rib_angles):
        r_wall = outer_dome_radius(float(th))
        cx = math.cos(th)
        cz = math.sin(th)
        spine = []
        radii = []
        h_max = 12.6 + 1.0 * math.sin(3.0 * th + 0.4)

        for s in range(n_steps):
            t = s / float(n_steps - 1)
            # Climb wall and curve dramatically inward overhead toward dome crown
            y = -0.85 + (h_max + 1.6) * (t ** 0.82)
            inward_reach = 0.35 + 7.2 * (max(0.0, t - 0.25) / 0.75) ** 1.65
            r_cur = r_wall - inward_reach
            # Segmented muscular nodes along each colossal rib arch
            node_swell = 1.0 + 0.22 * math.sin(t * 6.0 * math.pi)
            rad = (1.18 * (1.0 - 0.42 * t) + 0.25 * math.sin(t * math.pi)) * node_swell
            spine.append([r_cur * cx, y, r_cur * cz])
            radii.append(rad)

        def rib_col(t_val, phi, wp):
            lit = 0.46 + 0.50 * (t_val ** 0.55) * (0.65 + 0.35 * math.sin(phi))
            return (min(1.0, lit * 1.12), min(1.0, lit * 0.68), min(1.0, lit * 0.70), 1.0)

        add_swept_organ_tube(mb, spine, radii, n_sides=8, v_tiles=5.0, color_fn=rib_col)

    # 4 Visceral Vein Cords spanning high overhead (y = 5.8..8.8m) along the back dome canopy
    # and low across acid chasms alongside bridges
    vein_specs = [
        # High overarching arterial festoons anchored between upper back dome ribs
        ((-23.5, 6.4,  8.2), ( -6.5, 8.2, 18.8), 0.48, -1.65, 3.2),
        (( -5.5, 8.2, 18.8), ( 19.5, 6.5, 11.2), 0.46, -1.55, 3.0),
        # Low-slung taut visceral arteries spanning across acid channels near bridges
        ((-16.0, 0.08, -0.4), ( -8.4, 0.16,  2.1), 0.36, -0.42, 0.6),
        ((  8.4, 0.16,  2.3), ( 16.5, 0.08,  2.1), 0.36, -0.42, 0.6),
    ]
    for (ax, ay, az), (bx, by, bz), base_r, arch_dy, curve_z in vein_specs:
        n_v = 12
        v_spine = []
        v_radii = []
        for s in range(n_v):
            t = s / float(n_v - 1)
            arc = math.sin(t * math.pi)
            vx = ax + (bx - ax) * t + 0.40 * math.sin(t * 2.0 * math.pi)
            vy = ay + (by - ay) * t + arch_dy * arc
            vz = az + (bz - az) * t + curve_z * arc
            v_spine.append([vx, vy, vz])
            flare = 1.0 + 0.50 * ((2.0 * t - 1.0) ** 4)
            pulse = 1.0 + 0.16 * math.sin(t * 5.0 * math.pi)
            v_radii.append(base_r * flare * pulse)
        add_swept_organ_tube(mb, v_spine, v_radii, n_sides=7, v_tiles=4.0)

    return mb


# ---------------------------------------------------------------------------
# 6. Pustule Clusters, Mucosal Pillars, Acid Crater Vents & 28 Acid Bubbles
# ---------------------------------------------------------------------------

def add_pustule_blister(mb, lx, ly, lz, radius, seed=1.0):
    """Adds a swollen, bulbous low-poly pustule sphere/dome with an inflamed flesh collar."""
    n_rad = 10
    rings = [
        (radius * 1.18, ly - radius * 0.15, (0.55, 0.18, 0.22, 1.0)),  # inflamed crimson root collar
        (radius * 1.02, ly + radius * 0.22, (0.85, 0.45, 0.35, 1.0)),  # swollen blister wall
        (radius * 0.80, ly + radius * 0.64, (0.98, 0.92, 0.55, 1.0)),  # glowing toxic pus shoulder
        (radius * 0.42, ly + radius * 0.94, (1.00, 1.00, 0.78, 1.0)),  # bright pustule crown
    ]
    ring_pts = []
    for k, (rad, yy, col) in enumerate(rings):
        pts = []
        uvs = []
        for i in range(n_rad):
            th = (i / float(n_rad)) * 2.0 * math.pi
            wob = 1.0 + 0.08 * math.sin(3.0 * th + seed) + 0.05 * math.cos(4.0 * th - seed)
            pts.append([lx + rad * wob * math.cos(th), yy, lz + rad * wob * math.sin(th)])
            r_uv = 0.46 * (1.0 - k / float(len(rings)))
            uvs.append((0.5 + r_uv * math.cos(th), 0.5 + r_uv * math.sin(th)))
        ring_pts.append((pts, uvs, col))

    for k in range(len(ring_pts) - 1):
        p0, u0, c0 = ring_pts[k]
        p1, u1, c1 = ring_pts[k + 1]
        for i in range(n_rad):
            inext = (i + 1) % n_rad
            mb.add_flat_quad(
                p0[i], p1[i], p1[inext], p0[inext],
                u0[i], u1[i], u1[inext], u0[inext],
                c0, c1, c1, c0,
            )

    top_p = [lx, ly + radius * 1.08, lz]
    pt, ut, ct = ring_pts[-1]
    for i in range(n_rad):
        inext = (i + 1) % n_rad
        mb.add_flat_triangle(
            top_p, pt[inext], pt[i],
            (0.5, 0.5), ut[inext], ut[i],
            (1.0, 1.0, 0.85, 1.0), ct, ct,
        )


def build_pustule_clusters():
    """
    Builds Dome_Pustule_Clusters_01..06 (mat_pustule), each with a centered translation pivot
    and 5-6 bulbous pustule spheres/domes on island edges and wall bases.
    """
    cluster_centers = [
        ("Dome_Pustule_Clusters_01", (-18.2, 0.22,   1.4)),  # West Entry north rim
        ("Dome_Pustule_Clusters_02", ( -6.8, 0.24,   5.0)),  # Central Heart Arena NW rim
        ("Dome_Pustule_Clusters_03", (  6.6, 0.24,   5.2)),  # Central Heart Arena NE rim
        ("Dome_Pustule_Clusters_04", ( -8.8, 0.22,  15.6)),  # North Ulcer Sanctuary wall base
        ("Dome_Pustule_Clusters_05", ( 14.2, 0.22,  14.6)),  # Northeast Rib Bastion wall base
        ("Dome_Pustule_Clusters_06", ( 11.6, 0.22, -13.2)),  # Southeast Tendon Island south rim
    ]
    sub_offsets = [
        ( 0.00,  0.00, 0.00, 0.92),
        (-0.95, -0.04, 0.56, 0.68),
        ( 0.88, -0.03, 0.60, 0.72),
        ( 0.52, -0.05, -0.82, 0.62),
        (-0.68, -0.06, -0.74, 0.58),
        ( 0.15,  0.38,  0.26, 0.52),
    ]
    clusters = []
    for idx, (name, center) in enumerate(cluster_centers):
        mb = MeshBuilder(name, "mat_pustule", translation=center)
        for s_idx, (ox, oy, oz, rad) in enumerate(sub_offsets):
            ang = idx * 1.1 + s_idx * 0.35
            rx = ox * math.cos(ang) - oz * math.sin(ang)
            rz = ox * math.sin(ang) + oz * math.cos(ang)
            add_pustule_blister(mb, rx, oy, rz, radius=rad, seed=idx * 3.1 + s_idx)
        clusters.append(mb)
    return clusters


def add_twisted_pillar(mb, cx, cz, base_y=0.0, height=2.6, r_base=0.90):
    """Adds a gnarled muscular mucosal cover pillar with clear isometric silhouette."""
    n_rad = 10
    n_h = 6
    rings = []

    for ih in range(n_h + 1):
        t = ih / float(n_h)
        y = base_y + t * height
        rad = r_base * (1.28 * (1.0 - t) ** 2 + 0.58 + 0.16 * math.sin(t * 2.2 * math.pi))
        twist = t * 0.80
        pts = []
        cols = []
        for ir in range(n_rad):
            th = (ir / float(n_rad)) * 2.0 * math.pi + twist
            rib = 1.0 + 0.16 * math.cos(5.0 * (ir / float(n_rad)) * 2.0 * math.pi)
            rx = rad * rib * math.cos(th)
            rz = rad * rib * math.sin(th)
            pts.append([cx + rx, y, cz + rz])
            ao = 0.58 + 0.48 * (t ** 0.55) * (0.88 + 0.12 * math.cos(5.0 * th))
            cols.append((min(1.0, ao * 1.08), min(1.0, ao * 0.86), min(1.0, ao * 0.84), 1.0))
        rings.append((pts, cols))

    for ih in range(n_h):
        p0, c0 = rings[ih]
        p1, c1 = rings[ih + 1]
        v0 = ih / float(n_h) * 2.2
        v1 = (ih + 1) / float(n_h) * 2.2
        for ir in range(n_rad):
            irn = (ir + 1) % n_rad
            u0 = ir / float(n_rad) * 2.0
            u1 = (ir + 1) / float(n_rad) * 2.0
            mb.add_flat_quad(
                p0[ir], p1[ir], p1[irn], p0[irn],
                (u0, v0), (u0, v1), (u1, v1), (u1, v0),
                c0[ir], c1[ir], c1[irn], c0[irn],
            )

    top_center = [cx, base_y + height + 0.22, cz]
    pt, ct = rings[-1]
    for ir in range(n_rad):
        irn = (ir + 1) % n_rad
        mb.add_flat_triangle(
            top_center, pt[irn], pt[ir],
            (0.5, 0.5), (0.8, 0.5), (0.5, 0.8),
            (1.0, 0.82, 0.80, 1.0), ct[irn], ct[ir],
        )


def add_ulcer_crater(vent_mb, acid_pool_mb, cx, cz, base_y=-0.42, scale=1.0, seed=1.0):
    """Adds an organic asymmetric gastric ulcer crater oozing glowing green acid."""
    n_rad = 12
    prof = [
        (1.72 * scale, -0.55, (0.32, 0.25, 0.22, 1.0)),
        (1.25 * scale,  0.22 * scale, (0.78, 0.46, 0.42, 1.0)),
        (0.94 * scale,  0.74 * scale, (1.00, 0.72, 0.65, 1.0)),
        (0.66 * scale,  0.60 * scale, (0.78, 0.90, 0.55, 1.0)),
        (0.45 * scale,  0.18 * scale, (0.35, 0.72, 0.28, 1.0)),
    ]
    rings = []
    for rad, y_off, col in prof:
        pts = []
        cols = []
        for i in range(n_rad):
            th = (i / float(n_rad)) * 2.0 * math.pi
            wobble = 1.0 + 0.14 * math.sin(3.0 * th + seed) + 0.09 * math.cos(5.0 * th - seed * 0.7)
            pts.append([cx + rad * wobble * math.cos(th), base_y + y_off, cz + rad * wobble * math.sin(th)])
            cols.append(col)
        rings.append((pts, cols))

    for k in range(len(rings) - 1):
        p0, c0 = rings[k]
        p1, c1 = rings[k + 1]
        v0 = k / float(len(rings) - 1) * 1.2
        v1 = (k + 1) / float(len(rings) - 1) * 1.2
        for i in range(n_rad):
            inext = (i + 1) % n_rad
            u0 = i / float(n_rad) * 2.0
            u1 = (i + 1) / float(n_rad) * 2.0
            vent_mb.add_flat_quad(
                p0[i], p1[i], p1[inext], p0[inext],
                (u0, v0), (u0, v1), (u1, v1), (u1, v0),
                c0[i], c1[i], c1[inext], c0[inext],
            )

    y_pool = base_y + 0.46 * scale
    pool_center = [cx, y_pool, cz]
    rim_pts = []
    rim_uvs = []
    for i in range(n_rad):
        th = (i / float(n_rad)) * 2.0 * math.pi
        wobble = 1.0 + 0.14 * math.sin(3.0 * th + seed) + 0.09 * math.cos(5.0 * th - seed * 0.7)
        rim_pts.append([cx + 0.70 * scale * wobble * math.cos(th), y_pool, cz + 0.70 * scale * wobble * math.sin(th)])
        rim_uvs.append((0.5 + 0.42 * math.cos(th), 0.5 + 0.42 * math.sin(th)))
    for i in range(n_rad):
        inext = (i + 1) % n_rad
        acid_pool_mb.add_flat_triangle(
            pool_center, rim_pts[inext], rim_pts[i],
            (0.5, 0.5), rim_uvs[inext], rim_uvs[i],
            (1.0, 1.0, 0.9, 1.0), (0.85, 1.0, 0.75, 1.0), (0.85, 1.0, 0.75, 1.0),
        )


def add_calcified_horn(mb, base_pos, tip_offset, base_r=0.52):
    """Adds a dark curved calcified/necrotic flesh spire (biological, NOT skeleton bone)."""
    bx, by, bz = base_pos
    tx, ty, tz = tip_offset
    n_rad = 6
    n_seg = 4
    rings = []

    for s in range(n_seg + 1):
        t = s / float(n_seg)
        cx = bx + tx * t + 0.18 * math.sin(t * math.pi)
        cy = by + ty * t
        cz = bz + tz * t
        r = base_r * ((1.0 - t) ** 0.75) + 0.05
        pts = []
        cols = []
        for i in range(n_rad):
            th = (i / float(n_rad)) * 2.0 * math.pi + t * 0.4
            pts.append([cx + r * math.cos(th), cy, cz + r * math.sin(th)])
            b = 0.48 + 0.52 * t
            cols.append((b, b * 0.85, b * 0.92, 1.0))
        rings.append((pts, cols))

    for s in range(n_seg):
        p0, c0 = rings[s]
        p1, c1 = rings[s + 1]
        for i in range(n_rad):
            inext = (i + 1) % n_rad
            mb.add_flat_quad(
                p0[i], p1[i], p1[inext], p0[inext],
                (i / n_rad, s / n_seg), (i / n_rad, (s + 1) / n_seg),
                ((i + 1) / n_rad, (s + 1) / n_seg), ((i + 1) / n_rad, s / n_seg),
                c0[i], c1[i], c1[inext], c0[inext],
            )
    tip_pt = [bx + tx, by + ty + 0.14, bz + tz]
    pt, ct = rings[-1]
    for i in range(n_rad):
        inext = (i + 1) % n_rad
        mb.add_flat_triangle(
            tip_pt, pt[inext], pt[i],
            (0.5, 1.0), (0.0, 0.0), (1.0, 0.0),
            (1.0, 0.85, 0.90, 1.0), ct[inext], ct[i],
        )


def add_lowpoly_dome(mb, cx, cy, cz, radius, color=(1.0, 1.0, 1.0, 1.0)):
    """Adds a crisp low-poly bubbling dome for acid bubbles."""
    n_rad = 8
    rings = [
        (radius * 1.0, cy - radius * 0.15, (color[0] * 0.75, color[1] * 0.80, color[2] * 0.75, 1.0)),
        (radius * 0.82, cy + radius * 0.45, color),
        (radius * 0.42, cy + radius * 0.82, (min(1.0, color[0] * 1.15), min(1.0, color[1] * 1.15), color[2], 1.0)),
    ]
    ring_pts = []
    for rad, yy, col in rings:
        pts = []
        for i in range(n_rad):
            th = (i / float(n_rad)) * 2.0 * math.pi
            pts.append([cx + rad * math.cos(th), yy, cz + rad * math.sin(th)])
        ring_pts.append((pts, col))

    for k in range(len(ring_pts) - 1):
        p0, c0 = ring_pts[k]
        p1, c1 = ring_pts[k + 1]
        for i in range(n_rad):
            inext = (i + 1) % n_rad
            mb.add_flat_quad(
                p0[i], p1[i], p1[inext], p0[inext],
                (i / n_rad, k * 0.5), (i / n_rad, (k + 1) * 0.5),
                ((i + 1) / n_rad, (k + 1) * 0.5), ((i + 1) / n_rad, k * 0.5),
                c0, c1, c1, c0,
            )

    top_p = [cx, cy + radius * 0.98, cz]
    pt, ct = ring_pts[-1]
    for i in range(n_rad):
        inext = (i + 1) % n_rad
        mb.add_flat_triangle(
            top_p, pt[inext], pt[i],
            (0.5, 0.5), (0.8, 0.5), (0.5, 0.8),
            ct, ct, ct,
        )


def build_pillars_vents_and_bubbles():
    """
    Builds:
    - 6 Mucosal Pillars (Dome_Mucosal_Pillar_01..06, mat_flesh_wall) with centered pivots
    - 5 Ulcer Crater Vents + 4 Island Geyser Mounds (Dome_Acid_Vents, Dome_Acid_Crater_Pools)
    - 28 Acid Bubbles (Dome_Acid_Bubble_00..27, mat_acid) with centered pivots
    """
    pillar_specs = [
        ("Dome_Mucosal_Pillar_01", ( -4.6, 0.05, -1.8), 2.75, 0.92),  # Central Arena SW
        ("Dome_Mucosal_Pillar_02", (  4.8, 0.05,  3.4), 2.85, 0.94),  # Central Arena NE
        ("Dome_Mucosal_Pillar_03", (-21.5, 0.05, -5.0), 2.55, 0.84),  # West Entry Island
        ("Dome_Mucosal_Pillar_04", ( -2.4, 0.05, 15.6), 2.70, 0.88),  # North Ulcer Sanctuary
        ("Dome_Mucosal_Pillar_05", ( 13.4, 0.05, 11.2), 2.65, 0.86),  # Northeast Rib Bastion
        ("Dome_Mucosal_Pillar_06", ( 22.6, 0.05,  3.6), 2.80, 0.90),  # East Exit Island
    ]
    pillar_meshes = []
    for name, pos, h, rb in pillar_specs:
        pmb = MeshBuilder(name, "mat_flesh_wall", translation=pos)
        add_twisted_pillar(pmb, 0.0, 0.0, base_y=-0.15, height=h, r_base=rb)
        pillar_meshes.append(pmb)

    vents_mb = MeshBuilder("Dome_Acid_Vents", "mat_hell_organs")
    crater_pools_mb = MeshBuilder("Dome_Acid_Crater_Pools", "mat_acid")

    # 5 Acid Crater Vents matching Acid_Vent_Anchor_01..05
    acid_vent_locs = [
        (-12.0,  -6.5, -0.42, 1.25, 1.1),  # Acid_Vent_Anchor_01
        (  0.5,  11.2, -0.42, 1.20, 2.4),  # Acid_Vent_Anchor_02
        (  6.5,   7.8, -0.42, 1.18, 3.7),  # Acid_Vent_Anchor_03
        ( -1.5, -10.5, -0.42, 1.22, 4.9),  # Acid_Vent_Anchor_04
        ( 15.5,  -4.0, -0.42, 1.25, 6.2),  # Acid_Vent_Anchor_05
    ]
    for vx, vz, by, vsc, vsd in acid_vent_locs:
        add_ulcer_crater(vents_mb, crater_pools_mb, vx, vz, base_y=by, scale=vsc, seed=vsd)

    # 4 Warning Geyser Mounds on the main islands matching Island_Geyser_Anchor_01..04
    island_geyser_locs = [
        (-3.5,  2.5, 0.20, 0.72, 7.1),   # Island_Geyser_Anchor_01 (Central Arena)
        ( 4.2, -1.2, 0.20, 0.72, 8.3),   # Island_Geyser_Anchor_02 (Central Arena)
        (-5.5, 14.2, 0.18, 0.70, 9.5),   # Island_Geyser_Anchor_03 (North Sanctuary)
        (11.5, 13.0, 0.18, 0.70, 10.7),  # Island_Geyser_Anchor_04 (Northeast Bastion)
    ]
    for gx, gz, by, gsc, gsd in island_geyser_locs:
        add_ulcer_crater(vents_mb, crater_pools_mb, gx, gz, base_y=by, scale=gsc, seed=gsd)

    # Biological Calcified Hell-Flesh Spires along back/side wall bases (zero skeletons!)
    horn_specs = [
        ((-23.5, 0.1,  9.5), ( 1.8, 3.6, -1.2), 0.68),
        ((-16.5, 0.1, 16.5), ( 1.4, 3.9, -1.5), 0.72),
        (( -9.5, 0.1, 19.2), ( 0.6, 4.1, -1.6), 0.74),
        ((  2.5, 0.1, 19.8), (-0.4, 4.0, -1.6), 0.70),
        (( 17.8, 0.1, 16.2), (-1.5, 3.8, -1.4), 0.72),
        (( 24.2, 0.1,  8.5), (-1.8, 3.5, -0.8), 0.66),
    ]
    for base, tip, br in horn_specs:
        add_calcified_horn(vents_mb, base, tip, base_r=br)

    # 28 Individual Effervescent Acid Bubbles (Dome_Acid_Bubble_00..27) across the acid lake
    rng = np.random.default_rng(2026)
    bubble_meshes = []
    placed = 0
    attempts = 0
    while placed < 28 and attempts < 900:
        attempts += 1
        bx = float(rng.uniform(-25.5, 25.5))
        bz = float(rng.uniform(-19.5, 19.5))
        r = math.hypot(bx, bz)
        if r > outer_dome_radius(math.atan2(bz, bx)) - 2.2:
            continue
        if island_sdf(bx, bz) < 1.15:
            continue
        # Keep clear of floating islands
        if math.hypot(bx - (-12.5), bz - 7.5) < 3.4:
            continue
        if math.hypot(bx - 5.0, bz - (-12.0)) < 3.3:
            continue

        rad = float(rng.uniform(0.30, 0.62))
        bmb = MeshBuilder(f"Dome_Acid_Bubble_{placed:02d}", "mat_acid", translation=(bx, -0.42, bz))
        add_lowpoly_dome(bmb, 0.0, 0.0, 0.0, rad, color=(0.95, 1.0, 0.85, 1.0))
        bubble_meshes.append(bmb)
        placed += 1

    return pillar_meshes, vents_mb, crater_pools_mb, bubble_meshes


# ---------------------------------------------------------------------------
# Main Build & Preview Entrypoint
# ---------------------------------------------------------------------------

def build_stomach_main_dome(project_root):
    tex_dir = os.path.join(project_root, "Textures")
    tex_paths = generate_all_textures(tex_dir)

    materials_spec = {
        "mat_flesh_floor": {
            "image_path": tex_paths["tex_flesh_floor.png"],
            "roughness": 0.72,
            "metallic": 0.0,
            "double_sided": False,
        },
        "mat_flesh_wall": {
            "image_path": tex_paths["tex_flesh_wall.png"],
            "roughness": 0.78,
            "metallic": 0.0,
            "double_sided": False,
        },
        "mat_acid": {
            "image_path": tex_paths["tex_acid.png"],
            "roughness": 0.22,
            "metallic": 0.0,
            "emissive_factor": [0.75, 1.0, 0.30],
            "use_emissive_texture": True,
            "double_sided": False,
        },
        "mat_cartilage": {
            "image_path": tex_paths["tex_cartilage.png"],
            "roughness": 0.65,
            "metallic": 0.0,
            "double_sided": False,
        },
        "mat_hell_organs": {
            "image_path": tex_paths["tex_hell_organs.png"],
            "roughness": 0.82,
            "metallic": 0.0,
            "double_sided": False,
        },
        "mat_blood_drips": {
            "image_path": tex_paths["tex_blood_drips.png"],
            "roughness": 0.25,
            "metallic": 0.0,
            "alpha_mode": "MASK",
            "alpha_cutoff": 0.45,
            "double_sided": False,
        },
        "mat_blood_waves": {
            "image_path": tex_paths["tex_blood_waves.png"],
            "roughness": 0.25,
            "metallic": 0.0,
            "alpha_mode": "MASK",
            "alpha_cutoff": 0.45,
            "double_sided": False,
        },
        "mat_pustule": {
            "image_path": tex_paths.get("tex_pustule.png", tex_paths["tex_hell_organs.png"]),
            "roughness": 0.42,
            "metallic": 0.0,
            "emissive_factor": [0.42, 0.36, 0.10],
            "use_emissive_texture": True,
            "double_sided": False,
        },
        "mat_digested_calcified": {
            "image_path": tex_paths.get("tex_digested_calcified.png", tex_paths["tex_cartilage.png"]),
            "roughness": 0.70,
            "metallic": 0.0,
            "double_sided": False,
        },
    }

    floor_mb, acid_mb = build_terrain_and_acid()
    cartilage_mb, tendon_mb, stepping_mb, float_01_mb, float_02_mb = build_island_connections()
    wall_back_mb, wall_front_mb, blood_drips_mb, blood_waves_mb = build_dome_walls_and_blood()
    rib_arches_mb = build_rib_arches_and_veins()
    pustule_clusters = build_pustule_clusters()
    pillar_meshes, vents_mb, crater_pools_mb, bubble_meshes = build_pillars_vents_and_bubbles()

    meshes = (
        [
            floor_mb,
            acid_mb,
            crater_pools_mb,
            cartilage_mb,
            tendon_mb,
            stepping_mb,
            float_01_mb,
            float_02_mb,
            wall_back_mb,
            wall_front_mb,
            blood_drips_mb,
            blood_waves_mb,
            rib_arches_mb,
            vents_mb,
        ]
        + pustule_clusters
        + pillar_meshes
        + bubble_meshes
    )

    anchors = [
        ("Entry_Flesh_Door_Anchor", (-25.2, 0.18, -2.5)),
        ("Exit_Flesh_Door_Anchor", (26.2, 0.18, 0.5)),
        ("Island_Geyser_Anchor_01", (-3.5, 0.28, 2.5)),
        ("Island_Geyser_Anchor_02", (4.2, 0.28, -1.2)),
        ("Island_Geyser_Anchor_03", (-5.5, 0.25, 14.2)),
        ("Island_Geyser_Anchor_04", (11.5, 0.25, 13.0)),
        ("Acid_Vent_Anchor_01", (-12.0, 0.12, -6.5)),
        ("Acid_Vent_Anchor_02", (0.5, 0.12, 11.2)),
        ("Acid_Vent_Anchor_03", (6.5, 0.12, 7.8)),
        ("Acid_Vent_Anchor_04", (-1.5, 0.12, -10.5)),
        ("Acid_Vent_Anchor_05", (15.5, 0.12, -4.0)),
        ("Dome_Blood_Drip_Anchor_01", (-10.0, 7.5, 4.0)),
        ("Dome_Blood_Drip_Anchor_02", (0.0, 8.2, 6.0)),
        ("Dome_Blood_Drip_Anchor_03", (10.0, 7.5, 4.0)),
    ]

    glb_path = os.path.join(project_root, "Stomach_Main_Dome.glb")
    stats = export_glb(glb_path, meshes, materials_spec, root_name="Stomach_Main_Dome", anchors=anchors)

    # Render verification previews
    preview_dir = os.path.join(project_root, "Preview")
    os.makedirs(preview_dir, exist_ok=True)
    gltf, loaded_meshes = load_glb(glb_path)
    print(
        f"Validated Stomach_Main_Dome.glb! "
        f"NodesWithMesh={len(loaded_meshes)}, Materials={len(gltf['materials'])}, Textures={len(gltf['textures'])}"
    )

    views = [
        ("preview_dome_iso_sw.png", (-33.0, 36.0, -34.0), (0.0, 1.0, 1.5), 48.0),
        ("preview_dome_iso_se.png", ( 33.0, 36.0, -34.0), (0.0, 1.0, 1.5), 48.0),
        ("preview_dome_topdown.png", (0.0, 62.0, -1.0), (0.0, 0.0, 1.0), 50.0),
    ]
    for fname, cam, tgt, fov in views:
        im = render_view(loaded_meshes, cam, tgt, width=840, height=630, fov_deg=fov)
        out_p = os.path.join(preview_dir, fname)
        im.save(out_p)
        print("Saved preview:", out_p)

    return stats


if __name__ == "__main__":
    root = r"C:\Users\user\IdeaProjects\Game final\Assets\PilotChamber"
    stats = build_stomach_main_dome(root)
    print("GLB Export Stats:", stats)
