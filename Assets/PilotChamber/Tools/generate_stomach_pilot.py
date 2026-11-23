import os
import math
import numpy as np

from retro_textures import generate_all_textures
from glb_exporter import MeshBuilder, export_glb


# ---------------------------------------------------------------------------
# Organic Noise & Signed Distance Helpers
# ---------------------------------------------------------------------------

def organic_noise(x, z):
    """Deterministic smooth multi-frequency organic warp for stomach tissue."""
    return (
        0.45 * math.sin(x * 0.42 + z * 0.31)
        + 0.30 * math.cos(x * 0.85 - z * 0.72 + 1.1)
        + 0.25 * math.sin(x * 1.65 + z * 1.45 - 0.7)
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


def outer_wall_radius(theta):
    """Organic stomach cavity outer radius around (0, 0) as a function of angle."""
    rx = 13.8
    rz = 11.4
    base = 1.0 / math.sqrt((math.cos(theta) / rx) ** 2 + (math.sin(theta) / rz) ** 2)
    warp = (
        0.75 * math.sin(3.0 * theta + 0.5)
        + 0.45 * math.cos(5.0 * theta - 1.1)
        + 0.25 * math.sin(8.0 * theta + 0.2)
    )
    return base + warp


def island_sdf(x, z):
    """
    Signed distance to walkable stomach floor islands.
    <= 0 means walkable flesh floor; > 0 drops into the gastric acid moat.
    """
    w = organic_noise(x * 0.7, z * 0.7) * 0.42
    wx = x + 0.32 * math.sin(z * 0.55)
    wz = z + 0.32 * math.cos(x * 0.52)

    # Zone A: North-West Entry Vestibule (connected up to NW sphincter)
    d_vestibule = sd_ellipse(wx, wz, -8.2, 6.2, 3.1, 2.7)
    d_entry_neck = sd_capsule_2d(wx, wz, -8.2, 6.2, -9.2, 9.4, 1.8)
    d_a = smooth_min(d_vestibule, d_entry_neck, k=0.9)

    # Zone B: Central Stomach Combat Arena
    d_center = sd_ellipse(wx, wz, -0.4, -1.3, 5.5, 4.6)

    # Zone C: East-North-East Acidic Gland Alcove (connected to NE Exit sphincter)
    d_east = sd_ellipse(wx, wz, 8.8, 3.0, 3.0, 2.8)
    d_exit_neck = sd_capsule_2d(wx, wz, 8.8, 3.0, 11.2, 5.4, 1.8)
    d_c = smooth_min(d_east, d_exit_neck, k=0.9)

    # Zone D: South-West Stepping Islet (risky dash/jump target over acid)
    d_islet = sd_ellipse(wx, wz, -7.8, -6.4, 2.0, 1.7)

    d = min(d_a, d_center, d_c, d_islet)
    return d + w * 0.50


def terrain_height_and_color(x, z):
    """Returns (y, rgba) for the walkable floor & sunken acid basin terrain."""
    d = island_sdf(x, z)
    rugae = 0.055 * math.sin(x * 1.4 + organic_noise(x, z)) + 0.045 * math.cos(z * 1.6 - x * 0.5)

    if d <= 0.0:
        # Walkable fleshy surface (flat enough for crisp Hades/PoE2 navigation)
        y = 0.0 + rugae * 0.75
        edge_t = max(0.0, min(1.0, (d + 1.4) / 1.4))
        # Slight center arena blood stain / depression tint
        arena_r = math.hypot(x + 0.2, z + 1.2)
        center_tint = 1.0 - 0.14 * math.exp(-(arena_r ** 2) / 14.0)
        r = (1.0 - 0.35 * (edge_t ** 2)) * center_tint
        g = (0.88 + 0.12 * (edge_t ** 2)) * center_tint
        b = (0.88 - 0.45 * (edge_t ** 2)) * center_tint
    elif d < 1.15:
        # Steep visceral cliff dropping into the acid basin
        t = d / 1.15
        s = t * t * (3.0 - 2.0 * t)
        y = -0.95 * s + rugae * (1.0 - t) * 0.4
        acid_glow = math.exp(-((y - (-0.38)) ** 2) / 0.06)
        r = 0.58 * (1.0 - 0.6 * t) + 0.05 * acid_glow
        g = 0.45 * (1.0 - 0.2 * t) + 0.58 * acid_glow
        b = 0.45 * (1.0 - 0.65 * t) + 0.05 * acid_glow
    else:
        # Submerged basin bottom under the glowing green acid
        y = -0.95 + 0.08 * organic_noise(x * 1.2, z * 1.2)
        r, g, b = 0.16, 0.38, 0.14

    return y, (r, g, b, 1.0)


def clamp_to_wall_footprint(x, z, margin=0.25):
    """Clamps (x, z) cleanly inside outer_wall_radius(theta) + margin so no triangles poke out."""
    r = math.hypot(x, z)
    if r < 1e-6:
        return x, z, r, 10.0
    th = math.atan2(z, x)
    r_wall = outer_wall_radius(th)
    r_max = r_wall + margin
    if r > r_max:
        scale = r_max / r
        return x * scale, z * scale, r_max, r_wall
    return x, z, r, r_wall


# ---------------------------------------------------------------------------
# Geometry Builders
# ---------------------------------------------------------------------------

def build_terrain_and_acid():
    """Builds Floor_Flesh_Walkable and Acid_Lake clamped strictly inside outer wall."""
    floor_mb = MeshBuilder("Floor_Flesh_Walkable", "mat_flesh_floor")
    acid_mb = MeshBuilder("Acid_Lake", "mat_acid")

    # 1. Walkable flesh & sunken basin terrain grid
    nx, nz = 48, 42
    x_min, x_max = -15.5, 15.5
    z_min, z_max = -13.5, 13.5
    xs = np.linspace(x_min, x_max, nx)
    zs = np.linspace(z_min, z_max, nz)

    vert_idx = np.full((nx, nz), -1, dtype=np.int32)
    uv_scale = 0.27

    for ix, gx in enumerate(xs):
        for iz, gz in enumerate(zs):
            r_raw = math.hypot(gx, gz)
            th = math.atan2(gz, gx)
            r_wall = outer_wall_radius(th)
            # Only keep grid points inside or near the wall inner edge, clamping fringe vertices into wall thickness
            if r_raw <= r_wall + 1.05:
                x, z, r, r_w = clamp_to_wall_footprint(float(gx), float(gz), margin=0.28)
                y, col = terrain_height_and_color(x, z)
                wall_prox = max(0.0, min(1.0, (r - (r_w - 1.2)) / 2.2))
                col = (
                    col[0] * (1.0 - 0.55 * wall_prox),
                    col[1] * (1.0 - 0.55 * wall_prox),
                    col[2] * (1.0 - 0.55 * wall_prox),
                    1.0,
                )
                # Subtle organic UV warp so floor texture flows organically instead of rigid tiling
                u = x * uv_scale + 0.14 * math.sin(z * 0.42)
                v = z * uv_scale + 0.14 * math.cos(x * 0.38)
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

    # 2. Gastric Acid Lake plane at Y = -0.38m, clamped strictly inside wall thickness (margin = +0.10)
    anx, anz = 42, 36
    axs = np.linspace(x_min, x_max, anx)
    azs = np.linspace(z_min, z_max, anz)
    a_idx = np.full((anx, anz), -1, dtype=np.int32)
    acid_uv_scale = 0.24

    for ix, gx in enumerate(axs):
        for iz, gz in enumerate(azs):
            r_raw = math.hypot(gx, gz)
            th = math.atan2(gz, gx)
            r_wall = outer_wall_radius(th)
            if r_raw <= r_wall + 0.85:
                x, z, _, _ = clamp_to_wall_footprint(float(gx), float(gz), margin=0.05)
                d = island_sdf(x, z)
                y_acid = -0.38 + 0.02 * math.sin(x * 1.1 + z * 0.9)
                shore_glow = max(0.0, min(1.0, 1.0 - abs(d - 0.5) / 1.8))
                col = (
                    0.75 + 0.25 * shore_glow,
                    0.92 + 0.08 * shore_glow,
                    0.70 + 0.25 * shore_glow,
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


def build_perimeter_walls():
    """
    Builds Wall_Flesh_Back (tall dramatic U-shaped ribbed stomach walls with overhang),
    Wall_Cutaway_Front (low sliced-open solid diorama rim for unobstructed isometric view),
    and Wall_Blood_Drips (inward-offset cutout blood streams oozing down the back walls).
    """
    back_mb = MeshBuilder("Wall_Flesh_Back", "mat_flesh_wall")
    front_mb = MeshBuilder("Wall_Cutaway_Front", "mat_flesh_wall")
    drips_mb = MeshBuilder("Wall_Blood_Drips", "mat_blood_drips")
    waves_mb = MeshBuilder("Wall_Blood_Waves", "mat_blood_waves")

    n_seg = 72
    n_inner_rings = 11  # 0..10 inner face curving up into an enclosing stomach dome
    n_outer_rings = 5   # 11..15 outer dome shell curving back down to skirt base
    total_rings = n_inner_rings + n_outer_rings

    def is_front_cutaway(z_val):
        return z_val < -4.2

    thetas = np.linspace(0.0, 2.0 * math.pi, n_seg, endpoint=False)
    rings_pos = np.zeros((n_seg, total_rings, 3), dtype=np.float32)
    rings_col = np.zeros((n_seg, total_rings, 4), dtype=np.float32)
    drip_pos = np.zeros((n_seg, n_inner_rings, 3), dtype=np.float32)
    wave_pos = np.zeros((n_seg, n_inner_rings, 3), dtype=np.float32)

    for i, th in enumerate(thetas):
        r_base = outer_wall_radius(float(th))
        cx = math.cos(th)
        cz = math.sin(th)
        z_est = r_base * cz

        cut_t = max(0.0, min(1.0, (-3.0 - z_est) / 3.8))
        cut_smooth = cut_t * cut_t * (3.0 - 2.0 * cut_t)

        # Full 360-degree enclosing stomach dome (BG3 camera-to-player shader cutout opens the view)
        back_h = 10.4 + 0.75 * math.sin(3.0 * th + 0.4) + 0.35 * math.cos(7.0 * th)
        front_h = 9.2 + 0.45 * math.sin(5.0 * th)
        h_top = back_h * (1.0 - cut_smooth) + front_h * cut_smooth

        buttress = 0.35 * math.sin(14.0 * th + 0.3 * math.cos(4.0 * th))

        for k in range(total_rings):
            if k < n_inner_rings:
                t = k / float(n_inner_rings - 1)
                # Smooth dome height curve reaching h_top at the crown
                y = -1.05 + (h_top + 1.05) * (t ** 0.84)
                rugae_amp = 0.38 * (1.0 - 0.45 * cut_smooth)
                rugae_wave = math.sin(t * 4.0 * 2.0 * math.pi + 0.6 * math.sin(4.0 * th))
                dome_t = max(0.0, (t - 0.30) / 0.70)
                overhang = -(r_base * 0.78) * (dome_t ** 1.48)
                r = r_base + overhang + buttress * math.sin(t * math.pi) - rugae_amp * rugae_wave

                acid_bounce = math.exp(-((y - (-0.38)) ** 2) / 0.45) * 0.55
                crevice = 0.5 + 0.5 * rugae_wave
                ao = 0.30 + 0.55 * (t ** 0.65) + 0.18 * crevice
                col = (
                    min(1.0, ao * 1.02 + 0.05 * acid_bounce),
                    min(1.0, ao * 0.78 + 0.48 * acid_bounce),
                    min(1.0, ao * 0.78 + 0.05 * acid_bounce),
                    1.0,
                )
                drip_pos[i, k] = [(r - 0.12) * cx, y, (r - 0.12) * cz]
                wave_pos[i, k] = [(r - 0.17) * cx, y, (r - 0.17) * cz]
            else:
                # Outer dome shell curving gracefully back down to the floor skirt
                ot = (k - n_inner_rings + 1) / float(n_outer_rings)  # 1/5 .. 1.0
                t_mirror = 1.0 - ot
                y = -1.05 + (h_top + 1.05) * (t_mirror ** 0.84)
                dome_t = max(0.0, (t_mirror - 0.30) / 0.70)
                overhang = -(r_base * 0.78) * (dome_t ** 1.48)
                r = r_base + overhang + 1.25 + 0.25 * math.sin(ot * math.pi)
                shade = 0.24 + 0.18 * (1.0 - ot)
                col = (shade * 1.15, shade * 0.36, shade * 0.42, 1.0)

            rings_pos[i, k] = [r * cx, y, r * cz]
            rings_col[i, k] = col

    sphincter_holes = [(-9.4, 9.3), (11.1, 5.4)]

    for i in range(n_seg):
        j = (i + 1) % n_seg
        z_mid = 0.5 * (rings_pos[i, 0, 2] + rings_pos[j, 0, 2])
        x_mid = 0.5 * (rings_pos[i, 0, 0] + rings_pos[j, 0, 0])
        is_front = is_front_cutaway(z_mid)
        target_mb = front_mb if is_front else back_mb

        u0 = i / float(n_seg) * 18.0
        u1 = (i + 1) / float(n_seg) * 18.0

        for k in range(total_rings - 1):
            v0 = k / float(n_inner_rings - 1) * 2.5
            v1 = (k + 1) / float(n_inner_rings - 1) * 2.5

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
            near_hole = any(math.hypot(x_mid - sx, z_mid - sz) < 2.25 for sx, sz in sphincter_holes)
            blood_layers = (
                (drips_mb, drip_pos, 12.0, 0.34, (1.0, 0.92, 0.92, 1.0)),
                (waves_mb, wave_pos, 9.0, 0.26, (0.95, 0.88, 0.88, 1.0)),
            )
            for k in range(1, n_inner_rings - 2):
                y_avg = 0.25 * (
                    drip_pos[i, k][1] + drip_pos[j, k][1]
                    + drip_pos[j, k + 1][1] + drip_pos[i, k + 1][1]
                )
                if near_hole and y_avg < 2.85:
                    continue

                for layer_mb, pos_arr, u_tiles, v_scale, dc0 in blood_layers:
                    lp00 = pos_arr[i, k]
                    lp10 = pos_arr[j, k]
                    lp11 = pos_arr[j, k + 1]
                    lp01 = pos_arr[i, k + 1]

                    du0 = i / float(n_seg) * u_tiles
                    du1 = (i + 1) / float(n_seg) * u_tiles
                    # Negated Y in glTF UV so top-of-PNG (v=0) sits above bottom-of-PNG (v=1)
                    # in both glTF viewers and Unity glTFast (v_unity = 1 - v_gltf).
                    layer_mb.add_flat_quad(
                        lp00, lp10, lp11, lp01,
                        (du0, -float(lp00[1]) * v_scale),
                        (du1, -float(lp10[1]) * v_scale),
                        (du1, -float(lp11[1]) * v_scale),
                        (du0, -float(lp01[1]) * v_scale),
                        dc0, dc0, dc0, dc0,
                    )

    return back_mb, front_mb, drips_mb, waves_mb


def add_cartilage_bridge(mb, p_start, p_end, width=2.9, arch_height=0.22, n_len=8):
    """Adds a solid ribbed cartilage & tendon bridge spanning over the acid moat."""
    sx, sz = p_start
    ex, ez = p_end
    dx, dz = ex - sx, ez - sz
    length = math.hypot(dx, dz)
    fwd = np.array([dx / length, 0.0, dz / length], dtype=np.float32)
    right = np.array([fwd[2], 0.0, -fwd[0]], dtype=np.float32)

    hw = width * 0.5
    profile = [
        (-hw - 0.18, -0.35),
        (-hw - 0.12,  0.12),
        (-hw + 0.18,  0.22),
        (-hw + 0.38,  0.02),
        (0.0,         0.04),
        ( hw - 0.38,  0.02),
        ( hw - 0.18,  0.22),
        ( hw + 0.12,  0.12),
        ( hw + 0.18, -0.35),
        (0.0,        -0.55),
    ]
    n_prof = len(profile)
    rings = []

    for i in range(n_len + 1):
        t = i / float(n_len)
        arch = arch_height * math.sin(t * math.pi)
        rib = 0.04 * math.sin(t * 5.0 * math.pi)
        flare = 1.0 + 0.18 * ((2.0 * t - 1.0) ** 4)
        base_pos = np.array([sx + dx * t, arch, sz + dz * t], dtype=np.float32)

        ring_pts = []
        ring_cols = []
        end_blood = (2.0 * t - 1.0) ** 4

        for px, py in profile:
            world_p = base_pos + right * (px * flare) + np.array([0.0, py + (rib if py > 0.1 else 0.0), 0.0], dtype=np.float32)
            acid_tint = max(0.0, min(1.0, (-0.1 - py) / 0.45))
            r = (1.0 - 0.35 * acid_tint) * (0.95 - 0.15 * end_blood)
            g = (0.94 + 0.06 * acid_tint) * (0.92 - 0.50 * end_blood)
            b = (0.90 - 0.45 * acid_tint) * (0.90 - 0.50 * end_blood)
            ring_pts.append(world_p)
            ring_cols.append((r, g, b, 1.0))

        rings.append((ring_pts, ring_cols))

    for i in range(n_len):
        pts0, cols0 = rings[i]
        pts1, cols1 = rings[i + 1]
        v0 = i / float(n_len) * 2.5
        v1 = (i + 1) / float(n_len) * 2.5
        for k in range(n_prof):
            kn = (k + 1) % n_prof
            u0 = k / float(n_prof) * 2.0
            u1 = (k + 1) / float(n_prof) * 2.0
            mb.add_flat_quad(
                pts0[k], pts1[k], pts1[kn], pts0[kn],
                (u0, v0), (u0, v1), (u1, v1), (u1, v0),
                cols0[k], cols1[k], cols1[kn], cols0[kn],
            )


def build_bridges():
    mb = MeshBuilder("Bridges_Cartilage", "mat_cartilage")
    add_cartilage_bridge(mb, (-6.4, 4.3), (-3.3, 1.5), width=2.9, arch_height=0.25, n_len=8)
    add_cartilage_bridge(mb, (4.1, 0.6), (6.9, 1.9), width=2.7, arch_height=0.22, n_len=8)
    return mb


def build_sphincter_gate(name, center, facing_dir, scale=1.0):
    """
    Builds an organic, round-lobed muscular stomach sphincter portal (NO eyes, NO teeth),
    with local pivot at floor level (cx, 0, cz) so OrganPulse scales cleanly in place.
    """
    cx, cy, cz = center
    mb = MeshBuilder(name, "mat_flesh_wall", translation=(cx, 0.0, cz))
    fx, _, fz = facing_dir
    fl = math.hypot(fx, fz)
    fwd = np.array([fx / fl, 0.0, fz / fl], dtype=np.float32)
    up = np.array([0.0, 1.0, 0.0], dtype=np.float32)
    right = np.cross(up, fwd)
    right /= np.linalg.norm(right)

    c_vec = np.array([0.0, cy, 0.0], dtype=np.float32)

    n_rad = 24
    # Smooth thick bulbous muscle rings (no sharp spikes)
    axial_steps = [
        (-1.8 * scale, 0.58, 0.06),   # deep pitch-black digestive throat
        (-1.1 * scale, 0.78, 0.22),   # ribbed throat interior
        (-0.55 * scale, 0.66, 0.48),  # inner contracted sphincter valve
        (-0.05 * scale, 1.02, 0.85),  # thick inner fleshy lip
        ( 0.42 * scale, 1.32, 1.00),  # main outer visceral sphincter ring
        ( 0.55 * scale, 1.65, 0.80),  # rounded muscle collar
        ( 0.05 * scale, 1.95, 0.45),  # embedded seam into stomach wall
    ]

    rings = []
    for s_off, r_sc, bright in axial_steps:
        pts = []
        cols = []
        for i in range(n_rad):
            th = (i / float(n_rad)) * 2.0 * math.pi
            # Smooth anatomical 6-lobe sphincter folds (gentle sinusoidal creases)
            pucker = 1.0 + 0.08 * math.cos(6.0 * th) + 0.05 * math.sin(3.0 * th + 0.4)
            rad = (1.32 * scale) * r_sc * pucker
            rx = rad * 1.15 * math.cos(th)
            ry = rad * 0.92 * math.sin(th)
            p = c_vec + fwd * s_off + right * rx + up * ry
            crease_sh = 0.84 + 0.16 * math.cos(6.0 * th)
            b = bright * crease_sh
            pts.append(p)
            cols.append((b, b * 0.78, b * 0.78, 1.0))
        rings.append((pts, cols))

    for k in range(len(rings) - 1):
        pts0, cols0 = rings[k]
        pts1, cols1 = rings[k + 1]
        v0 = k / float(len(rings) - 1) * 1.8
        v1 = (k + 1) / float(len(rings) - 1) * 1.8
        for i in range(n_rad):
            inext = (i + 1) % n_rad
            u0 = i / float(n_rad) * 3.0
            u1 = (i + 1) / float(n_rad) * 3.0
            mb.add_flat_quad(
                pts0[i], pts1[i], pts1[inext], pts0[inext],
                (u0, v0), (u0, v1), (u1, v1), (u1, v0),
                cols0[i], cols1[i], cols1[inext], cols0[inext],
            )

    center_back = c_vec + fwd * (-1.9 * scale)
    pts0, _ = rings[0]
    for i in range(n_rad):
        inext = (i + 1) % n_rad
        mb.add_flat_triangle(
            center_back, pts0[inext], pts0[i],
            (0.5, 0.5), (0.6, 0.5), (0.5, 0.6),
            (0.05, 0.02, 0.03, 1.0), (0.08, 0.03, 0.04, 1.0), (0.08, 0.03, 0.04, 1.0),
        )

    # Fleshy mucosal floor ramp leading smoothly onto the chamber floor
    ramp_len = 2.4 * scale
    ramp_w = 1.40 * scale
    n_rl = 5
    ramp_rows = []
    for ir in range(n_rl + 1):
        t = ir / float(n_rl)
        s = 0.0 + t * ramp_len
        y_top = (0.32 * scale) * (1.0 - t) - 0.08 * t + 0.05 * math.sin(t * math.pi)
        w_cur = ramp_w * (1.0 - 0.12 * t)
        row = [
            c_vec * np.array([1, 0, 1]) + fwd * s + right * (-w_cur - 0.25) + np.array([0, -0.35, 0]),
            c_vec * np.array([1, 0, 1]) + fwd * s + right * (-w_cur) + np.array([0, y_top - 0.06, 0]),
            c_vec * np.array([1, 0, 1]) + fwd * s + np.array([0, y_top + 0.05, 0]),
            c_vec * np.array([1, 0, 1]) + fwd * s + right * (w_cur) + np.array([0, y_top - 0.06, 0]),
            c_vec * np.array([1, 0, 1]) + fwd * s + right * (w_cur + 0.25) + np.array([0, -0.35, 0]),
        ]
        ramp_rows.append(row)

    for ir in range(n_rl):
        r0 = ramp_rows[ir]
        r1 = ramp_rows[ir + 1]
        b0 = 0.72 + 0.28 * (ir / n_rl)
        b1 = 0.72 + 0.28 * ((ir + 1) / n_rl)
        c0 = (b0, b0 * 0.84, b0 * 0.84, 1.0)
        c1 = (b1, b1 * 0.84, b1 * 0.84, 1.0)
        for ic in range(len(r0) - 1):
            mb.add_flat_quad(
                r0[ic], r1[ic], r1[ic + 1], r0[ic + 1],
                (ic * 0.4, ir * 0.5), (ic * 0.4, (ir + 1) * 0.5),
                ((ic + 1) * 0.4, (ir + 1) * 0.5), ((ic + 1) * 0.4, ir * 0.5),
                c0, c1, c1, c0,
            )

    return mb


def add_twisted_pillar(mb, cx, cz, base_y=-0.12, height=2.3, r_base=0.82):
    """Adds a gnarled muscular mucosal cover pillar with clear isometric silhouette & highlights."""
    n_rad = 10
    n_h = 6
    rings = []

    for ih in range(n_h + 1):
        t = ih / float(n_h)
        y = base_y + t * height
        rad = r_base * (1.28 * (1.0 - t) ** 2 + 0.58 + 0.16 * math.sin(t * 2.2 * math.pi))
        twist = t * 0.75
        pts = []
        cols = []
        for ir in range(n_rad):
            th = (ir / float(n_rad)) * 2.0 * math.pi + twist
            rib = 1.0 + 0.16 * math.cos(5.0 * (ir / float(n_rad)) * 2.0 * math.pi)
            rx = rad * rib * math.cos(th)
            rz = rad * rib * math.sin(th)
            pts.append([cx + rx, y, cz + rz])
            # Brighter warm flesh highlights on upper half so pillar reads clearly against floor
            ao = 0.58 + 0.48 * (t ** 0.55) * (0.88 + 0.12 * math.cos(5.0 * th))
            cols.append((min(1.0, ao * 1.08), min(1.0, ao * 0.86), min(1.0, ao * 0.84), 1.0))
        rings.append((pts, cols))

    for ih in range(n_h):
        p0, c0 = rings[ih]
        p1, c1 = rings[ih + 1]
        v0 = ih / float(n_h) * 2.0
        v1 = (ih + 1) / float(n_h) * 2.0
        for ir in range(n_rad):
            irn = (ir + 1) % n_rad
            u0 = ir / float(n_rad) * 2.0
            u1 = (ir + 1) / float(n_rad) * 2.0
            mb.add_flat_quad(
                p0[ir], p1[ir], p1[irn], p0[irn],
                (u0, v0), (u0, v1), (u1, v1), (u1, v0),
                c0[ir], c1[ir], c1[irn], c0[irn],
            )

    top_center = [cx, base_y + height + 0.20, cz]
    pt, ct = rings[-1]
    for ir in range(n_rad):
        irn = (ir + 1) % n_rad
        mb.add_flat_triangle(
            top_center, pt[irn], pt[ir],
            (0.5, 0.5), (0.8, 0.5), (0.5, 0.8),
            (1.0, 0.82, 0.80, 1.0), ct[irn], ct[ir],
        )


def add_ulcer_crater(vent_mb, acid_pool_mb, cx, cz, scale=1.0, seed=1):
    """Adds an organic asymmetric gastric ulcer crater (CARNE.jpg style) oozing glowing green acid."""
    n_rad = 12
    prof = [
        (1.68 * scale, -0.55, (0.32, 0.25, 0.22, 1.0)),
        (1.22 * scale,  0.22 * scale, (0.78, 0.46, 0.42, 1.0)),
        (0.92 * scale,  0.72 * scale, (1.00, 0.72, 0.65, 1.0)),
        (0.65 * scale,  0.60 * scale, (0.78, 0.90, 0.55, 1.0)),
        (0.45 * scale,  0.18 * scale, (0.35, 0.72, 0.28, 1.0)),
    ]
    rings = []
    for rad, y_off, col in prof:
        pts = []
        cols = []
        for i in range(n_rad):
            th = (i / float(n_rad)) * 2.0 * math.pi
            # Asymmetric 3+5 harmonic wobble so craters look organic rather than square
            wobble = 1.0 + 0.14 * math.sin(3.0 * th + seed) + 0.09 * math.cos(5.0 * th - seed * 0.7)
            pts.append([cx + rad * wobble * math.cos(th), -0.40 + y_off, cz + rad * wobble * math.sin(th)])
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

    y_pool = -0.40 + 0.45 * scale
    pool_center = [cx, y_pool, cz]
    rim_pts = []
    rim_uvs = []
    for i in range(n_rad):
        th = (i / float(n_rad)) * 2.0 * math.pi
        wobble = 1.0 + 0.14 * math.sin(3.0 * th + seed) + 0.09 * math.cos(5.0 * th - seed * 0.7)
        rim_pts.append([cx + 0.68 * scale * wobble * math.cos(th), y_pool, cz + 0.68 * scale * wobble * math.sin(th)])
        rim_uvs.append((0.5 + 0.42 * math.cos(th), 0.5 + 0.42 * math.sin(th)))
    for i in range(n_rad):
        inext = (i + 1) % n_rad
        acid_pool_mb.add_flat_triangle(
            pool_center, rim_pts[inext], rim_pts[i],
            (0.5, 0.5), rim_uvs[inext], rim_uvs[i],
            (1.0, 1.0, 0.9, 1.0), (0.85, 1.0, 0.75, 1.0), (0.85, 1.0, 0.75, 1.0),
        )


def add_calcified_horn(mb, base_pos, tip_offset, base_r=0.45):
    """Adds a dark curved calcified/necrotic flesh spike (biological, NOT skeleton bone)."""
    bx, by, bz = base_pos
    tx, ty, tz = tip_offset
    n_rad = 6
    n_seg = 4
    rings = []

    for s in range(n_seg + 1):
        t = s / float(n_seg)
        cx = bx + tx * t + 0.15 * math.sin(t * math.pi)
        cy = by + ty * t
        cz = bz + tz * t
        r = base_r * ((1.0 - t) ** 0.75) + 0.04
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
    tip_pt = [bx + tx, by + ty + 0.12, bz + tz]
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


def build_props_and_bubbles():
    pillar_1 = MeshBuilder("Props_Mucosal_Pillar_01", "mat_flesh_wall", translation=(-2.5, -0.12, -2.5))
    pillar_2 = MeshBuilder("Props_Mucosal_Pillar_02", "mat_flesh_wall", translation=( 2.1, -0.12,  0.2))
    vents_mb = MeshBuilder("Props_Acid_Vents", "mat_hell_organs")
    crater_pools_mb = MeshBuilder("Props_Acid_Crater_Pools", "mat_acid")

    # 1. Combat Cover Mucosal Pillars inside Central Arena (centered pivots for pulsing)
    add_twisted_pillar(pillar_1, 0.0, 0.0, base_y=0.0, height=2.25, r_base=0.76)
    add_twisted_pillar(pillar_2, 0.0, 0.0, base_y=0.0, height=2.15, r_base=0.72)

    # 2. Gastric Ulcer Acid Vents (CARNE.jpg style volcanic flesh calderas)
    vent_locs = [
        (-1.4, 6.4, 1.05, 1.2),
        ( 4.8, 7.1, 0.95, 2.7),
        (-9.9, -1.4, 0.92, 4.1),
    ]
    for vx, vz, vsc, vsd in vent_locs:
        add_ulcer_crater(vents_mb, crater_pools_mb, vx, vz, scale=vsc, seed=vsd)

    # 3. Calcified Dark Hell-Flesh Spires along back wall corners (zero skeletons!)
    horn_specs = [
        ((-11.8, 0.2,  5.5), ( 1.4, 2.8, -0.8), 0.55),
        ((-12.4, 0.4,  4.2), ( 1.1, 2.0, -0.5), 0.42),
        (( -3.2, 0.1, 10.6), ( 0.4, 2.9, -1.3), 0.58),
        (( -2.1, 0.2, 10.9), ( 0.2, 2.1, -1.0), 0.40),
        ((  7.6, 0.2,  9.5), (-0.8, 2.7, -1.2), 0.52),
        (( 12.2, 0.1, -2.5), (-1.4, 2.4,  0.4), 0.48),
        (( 11.4, 0.1, -3.5), (-1.0, 1.7,  0.3), 0.36),
    ]
    for base, tip, br in horn_specs:
        add_calcified_horn(vents_mb, base, tip, base_r=br)

    # 4. Individual Effervescent Acid Bubbles (each has its own pivot at Y=-0.38 so it swells & pops!)
    rng = np.random.default_rng(777)
    bubble_meshes = []
    placed = 0
    attempts = 0
    while placed < 20 and attempts < 450:
        attempts += 1
        bx = float(rng.uniform(-11.8, 11.8))
        bz = float(rng.uniform(-9.5, 9.5))
        r = math.hypot(bx, bz)
        if r > outer_wall_radius(math.atan2(bz, bx)) - 1.5:
            continue
        if island_sdf(bx, bz) < 0.78:
            continue
        if sd_capsule_2d(bx, bz, -6.4, 4.3, -3.3, 1.5, 2.1) < 0:
            continue
        if sd_capsule_2d(bx, bz, 4.1, 0.6, 6.9, 1.9, 2.1) < 0:
            continue

        placed += 1
        rad = float(rng.uniform(0.24, 0.44))
        bmb = MeshBuilder(f"Acid_Bubble_{placed:02d}", "mat_acid", translation=(bx, -0.38, bz))
        add_lowpoly_dome(bmb, 0.0, 0.0, 0.0, rad, color=(0.95, 1.0, 0.85, 1.0))
        bubble_meshes.append(bmb)

    return [pillar_1, pillar_2], vents_mb, crater_pools_mb, bubble_meshes


# ---------------------------------------------------------------------------
# Main Build Entrypoint
# ---------------------------------------------------------------------------

def build_pilot_chamber(project_root):
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
    }

    floor_mb, acid_mb = build_terrain_and_acid()
    wall_back_mb, wall_front_mb, blood_drips_mb, blood_waves_mb = build_perimeter_walls()
    bridges_mb = build_bridges()
    sphincter_entry = build_sphincter_gate("Sphincter_Entry", (-9.4, 1.40, 9.3), (0.42, 0.0, -0.90), scale=1.02)
    sphincter_exit = build_sphincter_gate("Sphincter_Exit", (11.1, 1.40, 5.4), (-0.72, 0.0, -0.70), scale=0.98)
    pillar_meshes, vents_mb, crater_pools_mb, bubble_meshes = build_props_and_bubbles()

    meshes = [
        floor_mb,
        acid_mb,
        crater_pools_mb,
        bridges_mb,
        wall_back_mb,
        wall_front_mb,
        blood_drips_mb,
        blood_waves_mb,
        sphincter_entry,
        sphincter_exit,
        vents_mb,
    ] + pillar_meshes + bubble_meshes

    anchors = [
        ("Ulcer_Vent_Anchor_01", (-1.4, 0.12,  6.4)),
        ("Ulcer_Vent_Anchor_02", ( 4.8, 0.08,  7.1)),
        ("Ulcer_Vent_Anchor_03", (-9.9, 0.06, -1.4)),
        ("Blood_Drip_Anchor",    ( 0.0, 4.35,  0.0)),
    ]

    glb_path = os.path.join(project_root, "Stomach_Pilot_Chamber.glb")
    stats = export_glb(glb_path, meshes, materials_spec, root_name="Stomach_Pilot_Chamber", anchors=anchors)
    return stats


if __name__ == "__main__":
    root = r"C:\Users\user\IdeaProjects\Game final\Assets\PilotChamber"
    stats = build_pilot_chamber(root)
    print("GLB Export Stats:", stats)
