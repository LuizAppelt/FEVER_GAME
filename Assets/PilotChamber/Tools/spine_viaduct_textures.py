import os
import math
import numpy as np
from PIL import Image

from retro_textures import (
    periodic_value_noise,
    fbm_periodic,
    voronoi_periodic,
    apply_bayer_palette,
)


def _coords_2d(size):
    """Returns (yy, xx) in [0, 1) of shape (size, size)."""
    c = np.linspace(0.0, 1.0, size, endpoint=False, dtype=np.float32)
    return np.meshgrid(c, c, indexing="ij")


def make_vertebra_scorched_bone(size=256):
    """
    1. tex_vertebra_scorched_bone.png (256x256, seamless):
    Aged, porous, calcified vertebral bone with spongy trabecular pores, bloody
    capillary cracks, charred/scorched black soot & burned bone gradients
    (hellfire charring), and faint greenish acid-etched pitting.
    """
    yy, xx = _coords_2d(size)
    n_coarse = fbm_periodic(size, base_period=3, octaves=4, seed=3101)
    n_med = fbm_periodic(size, base_period=6, octaves=3, seed=3102)
    n_fine = fbm_periodic(size, base_period=16, octaves=3, seed=3103)
    wx = fbm_periodic(size, base_period=4, octaves=3, seed=3104) - 0.5
    wy = fbm_periodic(size, base_period=4, octaves=3, seed=3105) - 0.5

    # Longitudinal anatomical vertebral bone grain & structural ridge curvature
    u_warp = xx + wx * 0.14
    v_warp = yy + wy * 0.12
    lamellar = 0.5 + 0.5 * np.sin((u_warp * 12.0 + n_med * 0.8) * 2.0 * math.pi)
    ridge = 0.5 + 0.5 * np.cos((v_warp * 2.0 + wx * 0.30) * 2.0 * math.pi)

    # Spongy trabecular (cancellous) bone pores: dense clusters of small & medium pits
    f1_pore_xs, _ = voronoi_periodic(size, num_points=96, seed=3106)
    f1_pore_md, _ = voronoi_periodic(size, num_points=42, seed=3107)
    # Cluster spongy marrow exposure in organic eroded bone zones
    spongy_zone = np.clip((n_med - 0.30) * 2.2, 0.35, 1.0)
    pore_xs = np.clip(1.0 - (f1_pore_xs + (n_fine - 0.5) * 0.012) / 0.032, 0.0, 1.0) ** 1.4
    pore_md = np.clip(1.0 - (f1_pore_md + (n_med - 0.5) * 0.016) / 0.046, 0.0, 1.0) ** 1.6
    pores = np.maximum(pore_xs * 0.92, pore_md) * spongy_zone
    pore_rim = np.clip(1.0 - np.abs(pores - 0.32) / 0.18, 0.0, 1.0) * spongy_zone

    # Organic branching bloody capillary cracks using warped multi-scale ridge noise
    r_field1 = fbm_periodic(size, base_period=4, octaves=4, seed=3111)
    r_field2 = fbm_periodic(size, base_period=8, octaves=3, seed=3112)
    ridge_crack1 = np.clip(1.0 - np.abs(r_field1 - 0.5) * 24.0, 0.0, 1.0) ** 1.8
    ridge_crack2 = np.clip(1.0 - np.abs(r_field2 - 0.48) * 32.0, 0.0, 1.0) ** 2.2
    capillaries = np.clip(
        np.maximum(ridge_crack1, ridge_crack2 * 0.70) * np.clip((n_coarse + 0.12) * 1.15, 0.2, 1.0),
        0.0,
        1.0,
    )
    blood_core = np.clip((capillaries - 0.35) / 0.65, 0.0, 1.0)

    # Strong hellfire charring & scorched black soot gradients (high contrast!)
    char_field = fbm_periodic(size, base_period=2, octaves=4, seed=3109) * 0.65 + n_med * 0.35
    char_norm = (char_field - char_field.min()) / np.maximum(char_field.max() - char_field.min(), 1e-4)
    char_intensity = np.clip((char_norm + ridge_crack1 * 0.16 - 0.38) / 0.52, 0.0, 1.0)
    burned_amber = np.clip(1.0 - np.abs(char_intensity - 0.32) / 0.24, 0.0, 1.0)
    soot_black = np.clip((char_intensity - 0.48) / 0.44, 0.0, 1.0) ** 1.15

    # Faint greenish gastric acid-etched pitting in localized digestive patches
    acid_zone = np.clip((fbm_periodic(size, base_period=3, octaves=3, seed=3110) - 0.53) * 3.4, 0.0, 1.0)
    acid_pit_rim = np.clip(1.0 - np.abs(pores - 0.36) / 0.24, 0.0, 1.0)
    acid_speck = np.clip((n_fine - 0.48) * 2.5, 0.0, 1.0)
    acid_mask = np.clip(
        acid_zone * np.maximum(acid_pit_rim * 0.90, acid_speck * 0.55) * (1.0 - soot_black * 0.85),
        0.0,
        0.82,
    )

    # Base pale aged calcified vertebral bone value (bright bone vs deep pores)
    bone_val = np.clip(
        0.70
        + (n_coarse - 0.5) * 0.20
        + (n_fine - 0.5) * 0.14
        + (lamellar - 0.5) * 0.12
        + (ridge - 0.5) * 0.18
        + pore_rim * 0.18
        - pores * 0.72
        - capillaries * 0.32,
        0.0,
        1.0,
    )
    gleam = np.clip((bone_val - 0.74) / 0.26, 0.0, 1.0)

    # Pale ivory / weathered bone base
    r = 42.0 + bone_val * 190.0 + gleam * 22.0
    g = 36.0 + bone_val * 176.0 + gleam * 22.0
    b = 28.0 + bone_val * 144.0 + gleam * 18.0

    # Scorched toasted-amber burn transition & deep hellfire soot blackening
    r = r * (1.0 - burned_amber * 0.30) + burned_amber * 32.0
    g = g * (1.0 - burned_amber * 0.54) + burned_amber * 10.0
    b = b * (1.0 - burned_amber * 0.68)

    soot_keep = 1.0 - soot_black * 0.90
    r = r * soot_keep + soot_black * 12.0
    g = g * soot_keep + soot_black * 10.0
    b = b * soot_keep + soot_black * 12.0

    # Bloody capillary cracks (fresh crimson center + clotted maroon edge)
    blood_r = 92.0 + blood_core * 88.0
    blood_g = 10.0 + blood_core * 16.0
    blood_b = 12.0 + blood_core * 10.0
    r = r * (1.0 - capillaries * 0.86) + blood_r * (capillaries * 0.86)
    g = g * (1.0 - capillaries * 0.88) + blood_g * (capillaries * 0.88)
    b = b * (1.0 - capillaries * 0.88) + blood_b * (capillaries * 0.88)

    # Faint greenish acid-etched pitting tint
    acid_r = 74.0 + n_fine * 26.0
    acid_g = 136.0 + n_fine * 38.0
    acid_b = 44.0 + n_fine * 14.0
    r = r * (1.0 - acid_mask) + acid_r * acid_mask
    g = g * (1.0 - acid_mask) + acid_g * acid_mask
    b = b * (1.0 - acid_mask) + acid_b * acid_mask

    rgb = np.stack([r, g, b], axis=2)

    palette = [
        (14, 11, 12),     # hellfire soot black & deepest trabecular pore
        (34, 26, 24),     # charred bone shadow
        (68, 46, 34),     # scorched dark umber bone
        (106, 74, 52),    # toasted burned-bone midtone
        (142, 118, 92),   # weathered bone shadow
        (178, 158, 126),  # aged calcified bone
        (210, 194, 158),  # pale aged ivory bone
        (236, 222, 188),  # sunlit vertebral ridge highlight
        (76, 14, 16),     # clotted capillary blood
        (148, 28, 26),    # fresh arterial blood crack
        (76, 126, 46),    # faint gastric acid-etched rim
        (114, 162, 58),   # acid-pitted bile highlight
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=20.0)


def make_spine_marrow_deck(size=256):
    """
    2. tex_spine_marrow_deck.png (256x256, seamless):
    Walkable intervertebral bone-plate / marrow causeway texture: cracked calcified
    cartilage slabs separated by glowing dark-ember / crimson-orange hellfire fissures
    and dried coagulated blood/bile grime. High surface-to-fissure contrast for crisp
    readability under a 3/4 isometric camera.
    """
    yy, xx = _coords_2d(size)
    wx = fbm_periodic(size, base_period=4, octaves=3, seed=3201) - 0.5
    wy = fbm_periodic(size, base_period=4, octaves=3, seed=3202) - 0.5
    n_coarse = fbm_periodic(size, base_period=4, octaves=4, seed=3203)
    n_fine = fbm_periodic(size, base_period=12, octaves=3, seed=3204)

    # Staggered intervertebral bone-plate slabs (4 rows x 3 cols) warped organically
    n_rows = 4
    n_cols = 3
    u_warp = (xx + wx * 0.045) % 1.0
    v_warp = (yy + wy * 0.040) % 1.0

    row_f = v_warp * n_rows
    row_idx = np.floor(row_f).astype(np.int32) % n_rows
    fy = row_f - np.floor(row_f)

    stagger = (row_idx % 2).astype(np.float32) * 0.5
    col_f = (u_warp * n_cols + stagger) % n_cols
    col_idx = np.floor(col_f).astype(np.int32) % n_cols
    fx = col_f - np.floor(col_f)

    rng = np.random.default_rng(3250)
    slab_tone = rng.uniform(-0.07, 0.07, size=(n_rows, n_cols)).astype(np.float32)[row_idx, col_idx]

    # Primary inter-slab fissures (wider along row seams like spinal disc joints)
    gap_x = 0.065 + (n_fine - 0.5) * 0.024
    gap_y = 0.095 + (n_fine - 0.5) * 0.032
    dx_edge = np.minimum(fx, 1.0 - fx) / np.maximum(gap_x, 0.02)
    dy_edge = np.minimum(fy, 1.0 - fy) / np.maximum(gap_y, 0.02)
    slab_dist = np.minimum(dx_edge, dy_edge)

    # Fissure mask: 1 inside primary glowing joint, 0 on slab plateau
    in_primary_fissure = np.clip(1.0 - (slab_dist - 0.52) / 0.55, 0.0, 1.0)
    in_slab = 1.0 - in_primary_fissure

    # Secondary organic internal fractures cracking across individual slabs
    f1_pit, _ = voronoi_periodic(size, num_points=26, seed=3205)
    frac_field = fbm_periodic(size, base_period=6, octaves=3, seed=3206)
    sec_crack = (np.clip(1.0 - np.abs(frac_field - 0.5) * 28.0, 0.0, 1.0) ** 2.0) * in_slab

    # Core glow inside deepest center of fissures + slight heat spill into secondary cracks
    ember_core = np.clip(1.0 - slab_dist / 0.64, 0.0, 1.0) ** 1.25
    ember_pulse = np.clip(0.60 + 0.40 * n_coarse, 0.30, 1.0)
    ember_glow = np.clip(
        np.maximum(ember_core * ember_pulse, sec_crack * 0.52 * n_coarse),
        0.0,
        1.0,
    )

    # 3/4 isometric directional bevel on slab edges (top/left bright, bottom/right shadowed)
    bevel_top = np.exp(-((fy - 0.14) / 0.08) ** 2) * in_slab
    bevel_left = np.exp(-((fx - 0.10) / 0.06) ** 2) * in_slab
    bevel_bot = np.exp(-((fy - 0.86) / 0.09) ** 2) * in_slab
    bevel_right = np.exp(-((fx - 0.90) / 0.06) ** 2) * in_slab
    iso_bevel = np.clip(bevel_top * 0.72 + bevel_left * 0.58, 0.0, 1.0) - np.clip(
        bevel_bot * 0.75 + bevel_right * 0.60, 0.0, 1.0
    )

    # Calcified cartilage slab texture: fibrous striations + marrow pitting
    fibers = 0.5 + 0.5 * np.sin((u_warp * 8.0 + (n_fine - 0.5) * 1.1) * 2.0 * math.pi)
    small_pits = np.clip(1.0 - (f1_pit + (n_fine - 0.5) * 0.014) / 0.024, 0.0, 1.0) ** 1.8

    slab_val = np.clip(
        0.66
        + (n_coarse - 0.5) * 0.20
        + (n_fine - 0.5) * 0.14
        + (fibers - 0.5) * 0.12
        + slab_tone
        + iso_bevel * 0.30
        - small_pits * 0.26
        - sec_crack * 0.42,
        0.0,
        1.0,
    )

    # Dried coagulated blood & bile grime hugging slab rims near fissures
    rim_zone = np.clip(1.0 - np.abs(slab_dist - 1.25) / 0.70, 0.0, 1.0) * in_slab
    blood_grime = rim_zone * np.clip((n_coarse - 0.35) * 1.9, 0.0, 1.0)
    bile_grime = rim_zone * np.clip((0.56 - n_coarse) * 2.1, 0.0, 1.0) * np.clip(n_fine * 1.4, 0.2, 1.0)

    # Slab RGB (warm readable ivory-cartilage bone floor)
    slab_r = 84.0 + slab_val * 150.0
    slab_g = 72.0 + slab_val * 134.0
    slab_b = 54.0 + slab_val * 96.0

    # Apply coagulated blood & bile staining near rims
    slab_r = slab_r * (1.0 - blood_grime * 0.55) + blood_grime * 52.0
    slab_g = slab_g * (1.0 - blood_grime * 0.78) + blood_grime * 10.0
    slab_b = slab_b * (1.0 - blood_grime * 0.75) + blood_grime * 12.0

    slab_r = slab_r * (1.0 - bile_grime * 0.45) + bile_grime * 44.0
    slab_g = slab_g * (1.0 - bile_grime * 0.28) + bile_grime * 54.0
    slab_b = slab_b * (1.0 - bile_grime * 0.60) + bile_grime * 18.0

    # Fissure RGB (charred abyssal seam -> glowing crimson-orange hellfire core)
    core_hi = np.clip((ember_glow - 0.52) / 0.48, 0.0, 1.0)
    fis_r = 22.0 + ember_glow * 188.0 + core_hi * 42.0
    fis_g = 8.0 + (ember_glow ** 1.6) * 86.0 + core_hi * 68.0
    fis_b = 8.0 + (ember_glow ** 2.4) * 24.0 + core_hi * 16.0

    # Blend secondary glowing cracks across slab surface too
    crack_blend = np.clip(in_primary_fissure + sec_crack * 0.85, 0.0, 1.0)
    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = slab_r * (1.0 - crack_blend) + fis_r * crack_blend
    rgb[:, :, 1] = slab_g * (1.0 - crack_blend) + fis_g * crack_blend
    rgb[:, :, 2] = slab_b * (1.0 - crack_blend) + fis_b * crack_blend

    palette = [
        (20, 10, 10),     # charred intervertebral fissure wall
        (78, 18, 16),     # coagulated blood & dark ember glow
        (146, 36, 20),    # crimson hellfire fissure
        (214, 78, 22),    # glowing ember orange crack
        (246, 148, 42),   # molten hellfire core
        (74, 72, 42),     # dried bile / gastric grime edge
        (104, 88, 72),    # shaded cartilage slab bevel
        (142, 124, 98),   # mid calcified marrow plate
        (178, 160, 126),  # walkable bone-cartilage surface
        (212, 194, 156),  # pale spine plate highlight
        (236, 222, 186),  # crisp 3/4 iso top-left bevel
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=18.0)


def make_infernal_iron_bone(size=256):
    """
    3. tex_infernal_iron_bone.png (256x256, seamless):
    Forged hellish black/rusted iron bands, heavy chain links, rivets, and
    scorched bone-ash patina — dark metallic charcoal, deep rust oranges,
    specular rivet highlights, crisp PS1 dither.
    """
    yy, xx = _coords_2d(size)
    n_coarse = fbm_periodic(size, base_period=4, octaves=4, seed=3301)
    n_fine = fbm_periodic(size, base_period=16, octaves=3, seed=3302)
    n_hammer = fbm_periodic(size, base_period=24, octaves=2, seed=3303)

    # Underlying hammered iron plate & vertical plate seams
    plate_seam_x = np.minimum((xx * 2.0) % 1.0, 1.0 - ((xx * 2.0) % 1.0))
    plate_seam = np.clip(1.0 - plate_seam_x / 0.025, 0.0, 1.0)

    # Heavy horizontal forged iron bands at yy ~ 0.18 and yy ~ 0.68
    dy_band1 = np.abs(((yy - 0.18 + 0.5) % 1.0) - 0.5)
    dy_band2 = np.abs(((yy - 0.68 + 0.5) % 1.0) - 0.5)
    dy_band = np.minimum(dy_band1, dy_band2)
    band_half_h = 0.105
    in_band = dy_band < band_half_h
    band_edge = np.clip(1.0 - np.abs(dy_band - band_half_h) / 0.018, 0.0, 1.0)
    # 3D beveled profile across the forged strap
    band_profile = np.where(in_band, np.clip(1.0 - (dy_band / band_half_h) ** 3, 0.0, 1.0), 0.0)
    band_top_hi = np.exp(-(((yy - (0.18 - band_half_h + 0.016)) + 0.5) % 1.0 - 0.5) ** 2 / (0.014 ** 2)) + \
                  np.exp(-(((yy - (0.68 - band_half_h + 0.016)) + 0.5) % 1.0 - 0.5) ** 2 / (0.014 ** 2))
    band_bot_sh = np.exp(-(((yy - (0.18 + band_half_h)) + 0.5) % 1.0 - 0.5) ** 2 / (0.020 ** 2)) + \
                  np.exp(-(((yy - (0.68 + band_half_h)) + 0.5) % 1.0 - 0.5) ** 2 / (0.020 ** 2))

    # Raised hemispherical iron rivets (8 per band = 16 rivets)
    riv_u = (xx * 8.0) % 1.0 - 0.5
    riv_dy1 = (((yy - 0.18 + 0.5) % 1.0) - 0.5) * 8.0
    riv_dy2 = (((yy - 0.68 + 0.5) % 1.0) - 0.5) * 8.0
    riv_dy = np.where(np.abs(riv_dy1) < np.abs(riv_dy2), riv_dy1, riv_dy2)
    riv_r = np.sqrt(riv_u * riv_u + riv_dy * riv_dy)
    riv_rad = 0.23
    in_rivet = riv_r < riv_rad
    riv_z = np.where(in_rivet, np.sqrt(np.clip(1.0 - (riv_r / riv_rad) ** 2, 0.0, 1.0)), 0.0)
    # Top-left directional lighting on rivet dome + crisp specular dot
    riv_nx = np.where(in_rivet, riv_u / riv_rad, 0.0)
    riv_ny = np.where(in_rivet, riv_dy / riv_rad, 0.0)
    riv_diff = np.clip((-0.55 * riv_nx - 0.65 * riv_ny + 0.52 * riv_z), 0.0, 1.0)
    riv_spec = np.where(in_rivet, np.clip((riv_diff - 0.68) / 0.32, 0.0, 1.0) ** 1.4, 0.0)
    riv_ao = np.exp(-((riv_r - riv_rad * 1.06) / 0.06) ** 2) * (~in_rivet)

    # Heavy interlocking horizontal chain links in the trough between bands (yy ~ 0.43 & yy ~ 0.93)
    chain_cy = np.where(np.abs(((yy - 0.43 + 0.5) % 1.0) - 0.5) < np.abs(((yy - 0.93 + 0.5) % 1.0) - 0.5), 0.43, 0.93)
    cy_local = (((yy - chain_cy + 0.5) % 1.0) - 0.5) * 4.0  # [-2, 2] scaled to 4 chain links across X

    def _capsule_link(u_shift):
        cx_local = ((xx * 4.0 + u_shift) % 1.0) - 0.5
        # Rounded stadium / oval link: half-length along X = 0.28, half-height = 0.18
        qx = np.maximum(np.abs(cx_local) - 0.16, 0.0)
        qy = np.abs(cy_local)
        d_center = np.abs(np.sqrt(qx * qx + qy * qy) - 0.20)
        tube_r = 0.072
        mask = d_center < tube_r
        z = np.where(mask, np.sqrt(np.clip(1.0 - (d_center / tube_r) ** 2, 0.0, 1.0)), 0.0)
        # Shade top-lit metallic cylinder
        shade = np.where(mask, np.clip(0.35 + z * 0.50 - cy_local * 0.55, 0.0, 1.0), 0.0)
        return mask, shade, d_center

    m_a, s_a, _ = _capsule_link(0.0)
    # Cross-links seen edge-on connecting adjacent flat links
    cx_cross = ((xx * 4.0 + 0.5) % 1.0) - 0.5
    cross_d = np.sqrt((cx_cross / 0.18) ** 2 + (cy_local / 0.075) ** 2)
    m_b = cross_d < 1.0
    s_b = np.where(m_b, np.clip(0.42 + (1.0 - cross_d) * 0.48 - cy_local * 1.4, 0.0, 1.0), 0.0)

    # Combine chain links (cross-links sit slightly over flat link ends)
    in_chain = m_a | m_b
    chain_shade = np.where(m_b, s_b, s_a)
    chain_ao = np.clip(1.0 - np.abs(cy_local) / 0.36, 0.0, 1.0) * (~in_chain) * (~in_band) * 0.45

    # Rust corrosion patches & scorched bone-ash patina settled in recesses
    rust_field = fbm_periodic(size, base_period=5, octaves=4, seed=3304)
    rust_mask = np.clip((rust_field + (n_fine - 0.5) * 0.28 - 0.46) / 0.36, 0.0, 1.0)
    # Boost rust along band edges, chain crevices, and hammered pits
    rust_mask = np.clip(rust_mask * 0.72 + band_edge * 0.35 + (1.0 - n_hammer) * 0.18, 0.0, 1.0)

    ash_field = fbm_periodic(size, base_period=6, octaves=3, seed=3305)
    # Bone-ash settles around rivet bases, band edges, and recessed iron plate
    ash_recess = np.clip(riv_ao * 0.9 + band_bot_sh * 0.5 + (1.0 - in_band.astype(np.float32)) * 0.35, 0.0, 1.0)
    bone_ash = np.clip((ash_field - 0.44) * 2.2 * ash_recess * (1.0 - in_chain.astype(np.float32) * 0.8), 0.0, 0.78)

    # Base metallic brightness
    plate_val = 0.24 + n_hammer * 0.16 + (n_fine - 0.5) * 0.12 - plate_seam * 0.18 - band_bot_sh * 0.22 - chain_ao
    band_val = 0.34 + band_profile * 0.18 + band_top_hi * 0.24 + (n_hammer - 0.5) * 0.14 - riv_ao * 0.24
    metal_val = np.where(in_band, band_val, plate_val)
    metal_val = np.where(in_chain, 0.26 + chain_shade * 0.52, metal_val)
    metal_val = np.where(in_rivet, 0.24 + riv_diff * 0.62 + riv_spec * 0.35, metal_val)
    metal_val = np.clip(metal_val, 0.0, 1.0)

    spec_hi = np.clip(
        riv_spec + np.where(in_chain, np.clip((chain_shade - 0.78) / 0.22, 0.0, 1.0) * 0.6, 0.0) + band_top_hi * 0.35,
        0.0,
        1.0,
    )

    # Dark metallic charcoal RGB
    r = 18.0 + metal_val * 110.0 + spec_hi * 68.0
    g = 18.0 + metal_val * 108.0 + spec_hi * 70.0
    b = 22.0 + metal_val * 116.0 + spec_hi * 76.0

    # Deep rust orange patina
    rust_bright = np.clip(metal_val * 1.15 + (n_fine - 0.5) * 0.25, 0.1, 1.0)
    rust_r = 72.0 + rust_bright * 116.0
    rust_g = 24.0 + rust_bright * 58.0
    rust_b = 10.0 + rust_bright * 20.0
    rust_apply = rust_mask * (1.0 - spec_hi * 0.75) * 0.72
    r = r * (1.0 - rust_apply) + rust_r * rust_apply
    g = g * (1.0 - rust_apply) + rust_g * rust_apply
    b = b * (1.0 - rust_apply) + rust_b * rust_apply

    # Scorched bone-ash dusty patina in recesses
    ash_r = 128.0 + n_fine * 44.0
    ash_g = 114.0 + n_fine * 38.0
    ash_b = 96.0 + n_fine * 30.0
    r = r * (1.0 - bone_ash) + ash_r * bone_ash
    g = g * (1.0 - bone_ash) + ash_g * bone_ash
    b = b * (1.0 - bone_ash) + ash_b * bone_ash

    rgb = np.stack([r, g, b], axis=2)

    palette = [
        (14, 13, 16),     # abyssal wrought-iron shadow
        (32, 32, 38),     # dark forged charcoal iron
        (58, 58, 66),     # mid gunmetal plate & band
        (92, 94, 104),    # brushed iron highlight
        (146, 150, 162),  # specular steel rim & rivet glint
        (68, 26, 14),     # deep dark iron oxide
        (124, 48, 18),    # crusted rust brown-orange
        (178, 78, 26),    # vibrant infernal rust orange
        (114, 102, 86),   # settled bone-ash grime
        (158, 144, 122),  # pale scorched bone-ash dust
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=18.0)


def make_hell_ember_lantern(size=128):
    """
    4. tex_hell_ember_lantern.png (128x128, seamless):
    Glowing infernal ember & brimstone brazier texture: bright molten orange/gold
    core cracking through blackened crust with hints of sulfurous greenish-yellow
    edge flame.
    """
    n_coarse = fbm_periodic(size, base_period=4, octaves=4, seed=3403)
    n_fine = fbm_periodic(size, base_period=8, octaves=3, seed=3404)

    # Organic cracked slag crust using multi-scale ridge noise + subtle chunk cells
    r1 = fbm_periodic(size, base_period=3, octaves=4, seed=3405)
    r2 = fbm_periodic(size, base_period=6, octaves=3, seed=3406)
    fissure_main = np.clip(1.0 - np.abs(r1 - 0.5) * 13.0, 0.0, 1.0) ** 1.3
    fissure_sub = np.clip(1.0 - np.abs(r2 - 0.5) * 18.0, 0.0, 1.0) ** 1.7

    # Combine primary molten rivers, branching ember veins, and glowing coal pockets
    hot_pocket = np.clip((n_coarse - 0.58) * 2.4, 0.0, 0.75)
    heat = np.clip(
        np.maximum(fissure_main * (0.82 + 0.28 * n_coarse), fissure_sub * 0.76) + hot_pocket,
        0.0,
        1.0,
    )

    # Blackened slag crust surface relief on cool islands
    crust_mask = np.clip(1.0 - heat * 1.35, 0.0, 1.0)
    crust_bump = np.clip(0.30 + n_fine * 0.55, 0.0, 1.0) * crust_mask

    # Incandescent core & glowing edge falloff
    molten_core = np.clip((heat - 0.62) / 0.38, 0.0, 1.0)
    bright_orange = np.clip((heat - 0.30) / 0.45, 0.0, 1.0)

    # Sulfurous greenish-yellow edge flame licking along select crack transitions
    sulfur_noise = fbm_periodic(size, base_period=5, octaves=3, seed=3407)
    edge_band = np.clip(1.0 - np.abs(heat - 0.34) / 0.17, 0.0, 1.0)
    sulfur_flame = np.clip(edge_band * np.clip((sulfur_noise - 0.44) * 2.8, 0.0, 1.0), 0.0, 1.0)

    # Base crust + glowing ember RGB
    r = 14.0 + crust_bump * 34.0 + heat * 195.0 + bright_orange * 52.0 + molten_core * 25.0
    g = 10.0 + crust_bump * 24.0 + (heat ** 1.5) * 98.0 + bright_orange * 68.0 + molten_core * 84.0
    b = 12.0 + crust_bump * 22.0 + (molten_core ** 1.6) * 110.0

    # Blend sulfurous chartreuse/greenish-yellow brimstone flame edges
    sul_r = 166.0 + n_fine * 48.0
    sul_g = 212.0 + n_fine * 34.0
    sul_b = 46.0 + n_fine * 24.0
    r = r * (1.0 - sulfur_flame * 0.78) + sul_r * (sulfur_flame * 0.78)
    g = g * (1.0 - sulfur_flame * 0.85) + sul_g * (sulfur_flame * 0.85)
    b = b * (1.0 - sulfur_flame * 0.75) + sul_b * (sulfur_flame * 0.75)

    rgb = np.stack([r, g, b], axis=2)

    palette = [
        (14, 10, 12),     # blackened volcanic crust
        (36, 22, 22),     # dark ash slag
        (98, 22, 14),     # deep ember crimson edge
        (178, 48, 16),    # glowing magma red-orange
        (232, 98, 20),    # blazing hellfire orange
        (252, 168, 38),   # molten amber-gold
        (255, 228, 124),  # incandescent white-gold core
        (144, 184, 42),   # sulfurous brimstone green-yellow edge
        (206, 232, 72),   # bright sulfuric flame lick
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=18.0)


def generate_spine_viaduct_textures(out_dir):
    """Generates all 4 Spine Viaduct (Stomach + Hell) PS1 textures."""
    os.makedirs(out_dir, exist_ok=True)
    specs = {
        "tex_vertebra_scorched_bone.png": make_vertebra_scorched_bone(256),
        "tex_spine_marrow_deck.png": make_spine_marrow_deck(256),
        "tex_infernal_iron_bone.png": make_infernal_iron_bone(256),
        "tex_hell_ember_lantern.png": make_hell_ember_lantern(128),
    }
    paths = {}
    for name, img in specs.items():
        p = os.path.join(out_dir, name)
        img.save(p, format="PNG")
        paths[name] = p
    return paths


if __name__ == "__main__":
    out = r"C:\Users\user\IdeaProjects\Game final\Assets\PilotChamber\Textures"
    generated = generate_spine_viaduct_textures(out)
    for k, v in generated.items():
        with Image.open(v) as im:
            print(f"Generated {k}: {v} ({im.size[0]}x{im.size[1]} {im.mode}, {os.path.getsize(v)} bytes)")
