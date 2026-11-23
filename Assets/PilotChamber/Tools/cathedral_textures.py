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


def make_flesh_bricks(size=256):
    """
    1. tex_flesh_bricks.png (Tijolos de Carne e Musculo Compactado):
    Running-bond Gothic masonry courses (8 rows x 4 columns per tile) of compacted
    striated muscle blocks with 3D beveled edges, capillaries, brick-to-brick tone
    variation, and dark coagulated blood mortar joints bridged by pale sinew fibers.
    """
    yy, xx = _coords_2d(size)
    wx = fbm_periodic(size, base_period=4, octaves=3, seed=2101) - 0.5
    wy = fbm_periodic(size, base_period=4, octaves=3, seed=2102) - 0.5
    fine = fbm_periodic(size, base_period=12, octaves=3, seed=2103)
    micro = fbm_periodic(size, base_period=24, octaves=2, seed=2104)

    n_rows = 8
    n_cols = 4

    # Slight organic warp on the brick grid so blocks feel alive and compressed
    u_warp = (xx + wx * 0.018) % 1.0
    v_warp = (yy + wy * 0.014) % 1.0

    row_f = v_warp * n_rows
    row_idx = np.floor(row_f).astype(np.int32) % n_rows
    fy = row_f - np.floor(row_f)  # [0, 1) top-to-bottom within brick course

    # Running bond stagger on odd rows
    stagger = (row_idx % 2).astype(np.float32) * 0.5
    col_f = (u_warp * n_cols + stagger) % n_cols
    col_idx = np.floor(col_f).astype(np.int32) % n_cols
    fx = col_f - np.floor(col_f)  # [0, 1) left-to-right within brick

    # Deterministic per-brick variation
    rng = np.random.default_rng(2150)
    brick_tint = rng.uniform(-1.0, 1.0, size=(n_rows, n_cols)).astype(np.float32)
    brick_angle = rng.uniform(-0.35, 0.35, size=(n_rows, n_cols)).astype(np.float32)
    brick_phase = rng.uniform(0.0, 2.0 * math.pi, size=(n_rows, n_cols)).astype(np.float32)

    b_tint = brick_tint[row_idx, col_idx]
    b_ang = brick_angle[row_idx, col_idx]
    b_phs = brick_phase[row_idx, col_idx]

    # Mortar joint distance (aspect-corrected since brick width = 2x brick height)
    mortar_x = 0.045 + 0.018 * (fine - 0.5)
    mortar_y = 0.085 + 0.032 * (fine - 0.5)

    dist_left = fx / np.maximum(mortar_x, 0.01)
    dist_right = (1.0 - fx) / np.maximum(mortar_x, 0.01)
    dist_top = fy / np.maximum(mortar_y, 0.01)
    dist_bot = (1.0 - fy) / np.maximum(mortar_y, 0.01)

    edge_dist = np.minimum(np.minimum(dist_left, dist_right), np.minimum(dist_top, dist_bot))
    in_brick = np.clip((edge_dist - 0.85) / 0.55, 0.0, 1.0)
    in_mortar = 1.0 - in_brick

    # 3D bevel shading: top & left edges catch wet light, bottom & right edges fall into shadow
    bevel_top = np.exp(-((fy - 0.11) / 0.09) ** 2) * in_brick
    bevel_left = np.exp(-((fx - 0.06) / 0.05) ** 2) * in_brick
    bevel_bot = np.exp(-((fy - 0.89) / 0.10) ** 2) * in_brick
    bevel_right = np.exp(-((fx - 0.94) / 0.05) ** 2) * in_brick
    bevel_light = np.clip(bevel_top * 0.75 + bevel_left * 0.55, 0.0, 1.0)
    bevel_dark = np.clip(bevel_bot * 0.80 + bevel_right * 0.60, 0.0, 1.0)

    # Pillow bulge of each compacted muscle brick
    pillow = np.sin(np.clip(fx, 0.0, 1.0) * math.pi) * np.sin(np.clip(fy, 0.0, 1.0) * math.pi)
    pillow = (pillow ** 0.55) * in_brick

    # Internal striated muscle fibers oriented slightly differently per brick
    rot_u = (fx - 0.5) * np.cos(b_ang) - (fy - 0.5) * np.sin(b_ang)
    rot_v = (fx - 0.5) * np.sin(b_ang) + (fy - 0.5) * np.cos(b_ang)
    striations = 0.5 + 0.5 * np.sin((rot_v * 11.0 + (fine - 0.5) * 1.4) * 2.0 * math.pi + b_phs)
    cross_fascia = 0.5 + 0.5 * np.cos((rot_u * 6.0 + (micro - 0.5) * 1.2) * 2.0 * math.pi)

    # Branching capillaries and sinew threads via Voronoi edges
    _, v_edge1 = voronoi_periodic(size, num_points=36, seed=2105)
    _, v_edge2 = voronoi_periodic(size, num_points=22, seed=2106)
    capillaries = (np.clip(1.0 - (v_edge1 + wx * 0.02) * 26.0, 0.0, 1.0) ** 2.0) * in_brick
    sinew_mortar = (np.clip(1.0 - (v_edge2 + wy * 0.02) * 28.0, 0.0, 1.0) ** 2.4) * in_mortar

    # Combine brick surface height & wet specular highlights
    muscle_tex = striations * 0.58 + fine * 0.27 + micro * 0.15
    height = pillow * (0.48 + 0.52 * muscle_tex)
    wet_spec = np.clip((height - 0.68) / 0.32, 0.0, 1.0) + bevel_light * 0.55
    wet_spec = np.clip(wet_spec, 0.0, 1.0)

    # Pale fascia streaks inside muscle
    fascia_streak = np.clip((cross_fascia * striations - 0.72) / 0.28, 0.0, 1.0) * in_brick * 0.45

    # Base brick RGB (arterial crimson / maroon / purple-red variation)
    brick_r = 52.0 + height * 128.0 + b_tint * 22.0 + wet_spec * 52.0 - bevel_dark * 46.0 - capillaries * 28.0 + fascia_streak * 45.0
    brick_g = 10.0 + height * 32.0 - b_tint * 6.0 + wet_spec * 54.0 - bevel_dark * 18.0 - capillaries * 12.0 + fascia_streak * 58.0
    brick_b = 18.0 + height * 36.0 - b_tint * 14.0 + wet_spec * 48.0 - bevel_dark * 22.0 + capillaries * 12.0 + fascia_streak * 52.0

    # Mortar RGB: deep black-crimson coagulated blood with glistening wet specks & pale sinew strands
    mortar_wet = np.clip((micro - 0.76) * 4.0, 0.0, 1.0)
    mortar_r = 18.0 + fine * 28.0 + sinew_mortar * 155.0 + mortar_wet * 75.0
    mortar_g = 4.0 + fine * 6.0 + sinew_mortar * 112.0 + mortar_wet * 12.0
    mortar_b = 8.0 + fine * 10.0 + sinew_mortar * 96.0 + mortar_wet * 14.0

    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = brick_r * in_brick + mortar_r * in_mortar
    rgb[:, :, 1] = brick_g * in_brick + mortar_g * in_mortar
    rgb[:, :, 2] = brick_b * in_brick + mortar_b * in_mortar

    palette = [
        (14, 3, 6),       # abyssal coagulated mortar shadow
        (30, 6, 12),      # dark clotted mortar blood
        (52, 10, 18),     # deep necrotic maroon crevice
        (72, 18, 32),     # bruised purple-crimson muscle shadow
        (98, 16, 24),     # dark arterial brick base
        (128, 24, 32),    # compacted crimson muscle
        (158, 34, 40),    # rich visceral red muscle
        (186, 52, 54),    # illuminated muscle belly
        (212, 84, 78),    # wet specular flesh bevel highlight
        (236, 134, 122),  # glistening membrane / sinew highlight
        (164, 118, 106),  # pale fibrous tendon / fascia thread
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=22.0)


def make_ossified_stone(size=256):
    """
    2. tex_ossified_stone.png (Cantaria de Osso Gotico Esculpido):
    Fine gothic ashlar bone-masonry in pale ivory, weathered ash-ochre, and aged
    parchment tones (inspired by Catedral da Se stone merged with calcified bone).
    Features horizontal ashlar seams, vertical carved gothic fluting & pointed lancet
    tracery reliefs, microscopic porous marrow pits, and dark sepia/charcoal crevices.
    """
    yy, xx = _coords_2d(size)
    n_coarse = fbm_periodic(size, base_period=4, octaves=4, seed=2201)
    n_med = fbm_periodic(size, base_period=8, octaves=3, seed=2202)
    n_fine = fbm_periodic(size, base_period=16, octaves=3, seed=2203)
    wx = fbm_periodic(size, base_period=6, octaves=2, seed=2204) - 0.5

    # 4 horizontal ashlar courses, 2 wide carved bone blocks per row
    n_rows = 4
    n_cols = 2
    row_f = yy * n_rows
    row_idx = np.floor(row_f).astype(np.int32) % n_rows
    fy = row_f - np.floor(row_f)

    stagger = (row_idx % 2).astype(np.float32) * 0.5
    col_f = (xx * n_cols + stagger) % n_cols
    col_idx = np.floor(col_f).astype(np.int32) % n_cols
    fx = col_f - np.floor(col_f)

    rng = np.random.default_rng(2210)
    block_tone = rng.uniform(-0.08, 0.08, size=(n_rows, n_cols)).astype(np.float32)[row_idx, col_idx]

    # Fine ashlar mortar seams with subtle cranial-suture waviness
    suture_x = (n_fine - 0.5) * 0.012
    suture_y = (n_med - 0.5) * 0.018
    seam_y = np.minimum(fy, 1.0 - fy) + suture_y
    seam_x = np.minimum(fx, 1.0 - fx) + suture_x
    h_seam = np.clip(1.0 - seam_y / 0.038, 0.0, 1.0)
    v_seam = np.clip(1.0 - seam_x / 0.022, 0.0, 1.0)
    ashlar_seam = np.maximum(h_seam, v_seam)

    # Vertical gothic fluting & pointed lancet blind-tracery carved into each bone block
    # 4 gothic flutes per block width
    flute_u = (fx * 4.0) % 1.0
    in_panel_x = (fx > 0.07) & (fx < 0.93)
    in_panel_y = (fy > 0.14) & (fy < 0.86)

    # Pointed gothic arch top inside each flute (arch apex at flute_u = 0.5, fy = 0.18)
    dx_flute = np.abs(flute_u - 0.5) * 2.0  # 0 at center, 1 at flute rib
    arch_curve = 0.16 + 0.22 * (dx_flute ** 1.6)
    in_flute = in_panel_x & (fy > arch_curve) & (fy < 0.85) & (dx_flute < 0.76)

    # Concave 3D shading inside each gothic flute + sharp carved rib between flutes
    flute_depth = np.where(in_flute, np.sqrt(np.clip(1.0 - (dx_flute / 0.76) ** 2, 0.0, 1.0)), 0.0)
    flute_rib_edge = np.where(
        in_panel_x & (fy > arch_curve - 0.04) & (fy < 0.87),
        np.exp(-((dx_flute - 0.76) / 0.08) ** 2),
        0.0,
    )
    # Directional light from top-left across flutes
    flute_light = np.where(in_flute, (flute_u - 0.5) * 0.35, 0.0)

    # Microscopic porous bone marrow pits (trabecular pores) + fine suture cracks
    f1_pore, edge_crack = voronoi_periodic(size, num_points=52, seed=2205)
    f1_pore2, _ = voronoi_periodic(size, num_points=28, seed=2206)
    pores1 = np.clip(1.0 - (f1_pore + (n_fine - 0.5) * 0.015) / 0.028, 0.0, 1.0) ** 1.8
    pores2 = np.clip(1.0 - (f1_pore2 + (n_med - 0.5) * 0.020) / 0.036, 0.0, 1.0) ** 2.0
    marrow_pits = np.maximum(pores1 * 0.75, pores2 * 0.55) * np.clip(1.2 - n_coarse, 0.3, 1.0)

    hairline_crack = (np.clip(1.0 - (edge_crack + wx * 0.015) * 32.0, 0.0, 1.0) ** 2.2) * 0.65

    # Ashlar block bevels (top/left bright, bottom/right shadowed)
    bevel_hi = (np.exp(-((fy - 0.05) / 0.04) ** 2) * 0.6 + np.exp(-((fx - 0.03) / 0.025) ** 2) * 0.4) * (1.0 - ashlar_seam)
    bevel_lo = (np.exp(-((fy - 0.95) / 0.04) ** 2) * 0.7 + np.exp(-((fx - 0.97) / 0.025) ** 2) * 0.5) * (1.0 - ashlar_seam)

    # Combine bone-stone surface value
    base_val = (
        0.58
        + (n_coarse - 0.5) * 0.28
        + (n_med - 0.5) * 0.16
        + (n_fine - 0.5) * 0.12
        + block_tone
        + flute_rib_edge * 0.14
        + flute_light
        - flute_depth * 0.18
        + bevel_hi * 0.22
        - bevel_lo * 0.26
    )
    crevice_dark = np.clip(ashlar_seam * 0.92 + marrow_pits * 0.68 + hairline_crack * 0.58, 0.0, 1.0)
    val = np.clip(base_val * (1.0 - crevice_dark * 0.82), 0.0, 1.0)
    gleam = np.clip((val - 0.74) / 0.26, 0.0, 1.0)

    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = 28.0 + val * 188.0 + gleam * 26.0
    rgb[:, :, 1] = 22.0 + val * 174.0 + gleam * 26.0
    rgb[:, :, 2] = 18.0 + val * 144.0 + gleam * 24.0

    palette = [
        (24, 18, 16),     # deep charcoal-sepia joint & marrow cavity
        (46, 36, 30),     # dark ossified crevice
        (74, 62, 50),     # shadowed gothic flute & weathered pore
        (106, 92, 74),    # aged ash-ochre stone shadow
        (138, 122, 98),   # mid gothic bone-ashlar
        (168, 152, 124),  # weathered parchment bone
        (196, 180, 148),  # pale sculpted bone-ivory
        (220, 206, 174),  # polished cranial ivory highlight
        (240, 230, 202),  # bright gothic rib gleam
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=18.0)


def make_cathedral_stained_glass(size=256):
    """
    3. tex_cathedral_stained_glass.png (Vitral Sacro-Visceral Gotico):
    Ornate dark lead/iron gothic tracery grid (pointed lancet arches, diamond quarries,
    and circular medallions) filled with glowing jewel-toned mosaic panes (deep ruby
    blood-red, toxic emerald bile-green, golden-amber pus, and royal violet-blue)
    with a glowing anatomical eye / sacred parasite motif in the center.
    """
    yy, xx = _coords_2d(size)
    # Center-relative coordinates in [-0.5, 0.5)
    cx = xx - 0.5
    cy = yy - 0.5

    fine = fbm_periodic(size, base_period=12, octaves=3, seed=2301)
    marbling = fbm_periodic(size, base_period=6, octaves=4, seed=2302)

    # 1. Outer Gothic Lancet Arch & Mullion Tracery
    # Two vertical gothic lancet bays or one grand lancet with side borders
    border_x = np.minimum(xx, 1.0 - xx)
    mullion_outer = border_x < 0.035

    # Pointed lancet arch curve near top (yy in [0.02, 0.35])
    arch_y = np.clip((0.36 - yy) / 0.32, 0.0, 1.0)
    lancet_half_w = 0.465 * np.sqrt(np.clip(1.0 - (arch_y ** 1.65), 0.0, 1.0))
    dist_lancet_arch = np.abs( np.abs(cx) - lancet_half_w )
    in_lancet_arch_line = (yy < 0.36) & (dist_lancet_arch < 0.018)
    outside_lancet_top = (yy < 0.36) & (np.abs(cx) > lancet_half_w)

    # Horizontal iron saddle bars across the window
    saddle_bars = (
        (np.abs(yy - 0.18) < 0.010)
        | (np.abs(yy - 0.50) < 0.011)
        | (np.abs(yy - 0.82) < 0.010)
        | (np.abs(yy - 0.02) < 0.016)
        | (np.abs(yy - 0.98) < 0.016)
    )

    # 2. Central Sacred-Visceral Medallion (Vesica Piscis + Quatrefoil Ring around (0.5, 0.5))
    rr = np.sqrt(cx * cx + cy * cy)
    ang = np.arctan2(cy, cx)

    # Pointed vesica piscis / aureole around center
    vesica_r = np.sqrt((cx * 1.35) ** 2 + (cy * 0.95) ** 2)
    quat_r = rr + 0.032 * np.cos(4.0 * ang)
    in_medallion = vesica_r < 0.29
    medallion_lead = (np.abs(vesica_r - 0.29) < 0.016) | (np.abs(quat_r - 0.22) < 0.013)

    # 3. Upper & Lower Small Rosettes at (0.5, 0.16) and (0.5, 0.84)
    r_top = np.sqrt(cx ** 2 + (yy - 0.17) ** 2)
    ang_top = np.arctan2(yy - 0.17, cx)
    rosette_top_lead = (np.abs(r_top - 0.11) < 0.012) | (
        (r_top < 0.11) & (np.abs(np.sin(4.0 * ang_top)) * r_top < 0.008)
    )

    r_bot = np.sqrt(cx ** 2 + (yy - 0.84) ** 2)
    ang_bot = np.arctan2(yy - 0.84, cx)
    rosette_bot_lead = (np.abs(r_bot - 0.11) < 0.012) | (
        (r_bot < 0.11) & (np.abs(np.sin(4.0 * ang_bot)) * r_bot < 0.008)
    )

    # 4. Background Diamond Quarry Lead Grid + Voronoi Mosaic Shards
    q_u = (xx * 8.0 + yy * 8.0) % 1.0
    q_v = (xx * 8.0 - yy * 8.0) % 1.0
    quarry_dist = np.minimum(np.minimum(q_u, 1.0 - q_u), np.minimum(q_v, 1.0 - q_v))
    quarry_lead = quarry_dist < 0.065

    q_id_u = np.floor(xx * 8.0 + yy * 8.0).astype(np.int32)
    q_id_v = np.floor(xx * 8.0 - yy * 8.0).astype(np.int32)
    pane_hash = (q_id_u * 7 + q_id_v * 13) % 4

    # Glow inside each diamond quarry pane (brighter at pane center, darker near lead cames)
    pane_glow = np.clip(quarry_dist / 0.38, 0.0, 1.0) ** 0.55
    glass_tex = np.clip(0.55 + 0.45 * marbling + 0.25 * (fine - 0.5), 0.2, 1.2) * (0.45 + 0.65 * pane_glow)

    # Base stained glass mosaic colors by pane_hash:
    # 0: Deep Ruby Blood-Red, 1: Toxic Emerald Bile-Green, 2: Royal Violet-Blue, 3: Golden-Amber Pus
    glass_r = np.select(
        [pane_hash == 0, pane_hash == 1, pane_hash == 2, pane_hash == 3],
        [215.0 * glass_tex, 32.0 * glass_tex, 95.0 * glass_tex, 235.0 * glass_tex],
    )
    glass_g = np.select(
        [pane_hash == 0, pane_hash == 1, pane_hash == 2, pane_hash == 3],
        [24.0 * glass_tex, 195.0 * glass_tex, 42.0 * glass_tex, 175.0 * glass_tex],
    )
    glass_b = np.select(
        [pane_hash == 0, pane_hash == 1, pane_hash == 2, pane_hash == 3],
        [38.0 * glass_tex, 72.0 * glass_tex, 215.0 * glass_tex, 36.0 * glass_tex],
    )

    # Spandrels outside upper lancet arch: deep violet & ruby gothic tracery
    spandrel_glow = np.clip(0.4 + 0.6 * marbling, 0.0, 1.0)
    glass_r = np.where(outside_lancet_top, 90.0 * spandrel_glow, glass_r)
    glass_g = np.where(outside_lancet_top, 20.0 * spandrel_glow, glass_g)
    glass_b = np.where(outside_lancet_top, 145.0 * spandrel_glow, glass_b)

    # Upper & lower small rosettes: blazing golden-amber & emerald petals
    in_small_rosette = (r_top < 0.11) | (r_bot < 0.11)
    ros_ang = np.where(r_top < 0.11, ang_top, ang_bot)
    ros_petal = (np.floor((ros_ang + math.pi) / (math.pi / 4.0)).astype(np.int32)) % 2
    glass_r = np.where(in_small_rosette, np.where(ros_petal == 0, 240.0, 38.0) * (0.7 + 0.3 * marbling), glass_r)
    glass_g = np.where(in_small_rosette, np.where(ros_petal == 0, 185.0, 210.0) * (0.7 + 0.3 * marbling), glass_g)
    glass_b = np.where(in_small_rosette, np.where(ros_petal == 0, 42.0, 68.0) * (0.7 + 0.3 * marbling), glass_b)

    # 5. Central Sacred-Visceral Eye & Parasite Tendril Motif inside Medallion
    # Outer ring of medallion (quat_r >= 0.22): radiating golden-amber & blood-ruby rays
    ray_idx = (np.floor((ang + math.pi) / (math.pi / 8.0)).astype(np.int32)) % 2
    ray_lead = ( vesica_r < 0.29 ) & ( quat_r >= 0.21 ) & ( np.abs(np.sin(8.0 * ang)) * rr < 0.008 )
    in_aureole = in_medallion & (quat_r >= 0.22)
    aur_glow = np.clip(1.0 - np.abs(quat_r - 0.25) / 0.06, 0.3, 1.0)
    glass_r = np.where(in_aureole, np.where(ray_idx == 0, 245.0, 205.0) * aur_glow, glass_r)
    glass_g = np.where(in_aureole, np.where(ray_idx == 0, 192.0, 26.0) * aur_glow, glass_g)
    glass_b = np.where(in_aureole, np.where(ray_idx == 0, 48.0, 38.0) * aur_glow, glass_b)

    # Sclera & crimson parasite veins inside quat_r < 0.22
    in_sclera = quat_r < 0.22
    vein_wave = np.abs(np.sin(ang * 6.0 + rr * 24.0 + (marbling - 0.5) * 4.0))
    blood_vein = np.clip(1.0 - vein_wave * 3.2, 0.0, 1.0) * np.clip((rr - 0.09) / 0.12, 0.0, 1.0)
    sclera_shade = np.clip(1.0 - (rr / 0.22) * 0.45 + (fine - 0.5) * 0.15, 0.0, 1.0)
    sc_r = 225.0 * sclera_shade * (1.0 - blood_vein * 0.15) + 210.0 * blood_vein
    sc_g = 205.0 * sclera_shade * (1.0 - blood_vein * 0.85) + 22.0 * blood_vein
    sc_b = 175.0 * sclera_shade * (1.0 - blood_vein * 0.85) + 32.0 * blood_vein
    glass_r = np.where(in_sclera, sc_r, glass_r)
    glass_g = np.where(in_sclera, sc_g, glass_g)
    glass_b = np.where(in_sclera, sc_b, glass_b)

    # Glowing Anatomical Iris (r < 0.115): toxic emerald outer ring + blazing amber-crimson inner ring
    in_iris = rr < 0.115
    iris_lead = np.abs(rr - 0.115) < 0.010
    t_iris = np.clip(rr / 0.115, 0.0, 1.0)
    iris_fibers = 0.5 + 0.5 * np.sin(ang * 24.0 + (fine - 0.5) * 5.0)
    ir_r = np.where(t_iris < 0.55, 245.0, 42.0 + iris_fibers * 85.0)
    ir_g = np.where(t_iris < 0.55, 165.0 + iris_fibers * 75.0, 185.0 + iris_fibers * 65.0)
    ir_b = np.where(t_iris < 0.55, 35.0, 58.0 + iris_fibers * 40.0)
    glass_r = np.where(in_iris, ir_r, glass_r)
    glass_g = np.where(in_iris, ir_g, glass_g)
    glass_b = np.where(in_iris, ir_b, glass_b)

    # Vertical cat/demon slit pupil in the center
    slit_w = 0.022 * np.clip(1.0 - (cy / 0.095) ** 2, 0.0, 1.0)
    in_pupil = (np.abs(cy) < 0.095) & (np.abs(cx) < slit_w)

    # Combine all lead cames
    is_lead = (
        mullion_outer
        | in_lancet_arch_line
        | saddle_bars
        | medallion_lead
        | rosette_top_lead
        | rosette_bot_lead
        | ray_lead
        | iris_lead
        | (quarry_lead & (~in_medallion) & (~in_small_rosette))
        | in_pupil
    )

    # Subtle metallic highlight on lead cames
    lead_val = 14.0 + fine * 22.0
    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = np.where(is_lead, lead_val, glass_r)
    rgb[:, :, 1] = np.where(is_lead, lead_val, glass_g)
    rgb[:, :, 2] = np.where(is_lead, lead_val + 4.0, glass_b)

    palette = [
        (10, 10, 14),     # pitch black iron tracery / slit pupil
        (28, 26, 34),     # dark charcoal lead came
        (52, 48, 58),     # weathered lead bevel highlight
        (96, 12, 22),     # dark ruby blood-glass shadow
        (168, 18, 30),    # rich arterial ruby stained glass
        (232, 42, 48),    # glowing crimson glass center
        (18, 92, 36),     # dark bile-emerald glass shadow
        (34, 168, 58),    # vivid emerald stained glass
        (82, 238, 96),    # glowing toxic green glass highlight
        (158, 104, 22),   # dark amber-pus glass
        (228, 174, 36),   # glowing golden-amber glass
        (252, 232, 118),  # blazing sacred gold / sclera highlight
        (44, 22, 112),    # deep nocturnal violet-blue glass
        (88, 48, 196),    # glowing royal violet-blue glass
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=18.0)


def make_cathedral_rose_window(size=256):
    """
    4. tex_cathedral_rose_window.png (A Grande Rosacea - "O Olho do Juizo"):
    Radial circular mandala texture centered at (0.5, 0.5).
    - Core: blazing demonic/biological iris & slit pupil in fiery gold, crimson, and emerald.
    - Surrounded by 12 inner and 24 outer radial gothic stained-glass petals separated
      by dark stone/lead tracery rings and trefoil/quatrefoil rosette motifs.
    """
    yy, xx = _coords_2d(size)
    cx = xx - 0.5 + 0.5 / size
    cy = yy - 0.5 + 0.5 / size
    rr = np.sqrt(cx * cx + cy * cy)
    ang = np.arctan2(cy, cx)  # [-pi, pi]

    fine = fbm_periodic(size, base_period=10, octaves=3, seed=2401)
    warp = fbm_periodic(size, base_period=6, octaves=3, seed=2402) - 0.5

    rgb = np.zeros((size, size, 3), dtype=np.float32)

    # --- 1. CORE EYE ("O Olho do Juizo", rr < 0.155) ---
    # Vertical slit pupil surrounded by blazing gold collar, crimson fibers, and emerald outer limbus
    slit_half_w = 0.024 * np.clip(1.0 - (cy / 0.11) ** 2, 0.0, 1.0)
    in_pupil = (np.abs(cy) < 0.11) & (np.abs(cx) < slit_half_w)
    pupil_margin = np.clip((np.abs(cx) - slit_half_w) / 0.035, 0.0, 1.0)

    t_core = np.clip(rr / 0.155, 0.0, 1.0)
    fibers_core = 0.5 + 0.5 * np.sin(ang * 36.0 + warp * 5.0 + rr * 30.0)
    fibers_fine = 0.5 + 0.5 * np.cos(ang * 60.0 - warp * 4.0)
    fiber_mix = 0.6 * fibers_core + 0.4 * fibers_fine

    # Fiery golden-amber around slit pupil -> deep arterial crimson mid-iris -> emerald rim
    fire_collar = np.clip(1.0 - t_core * 1.45, 0.0, 1.0) + np.exp(-((pupil_margin) / 0.35) ** 2) * (np.abs(cy) < 0.12) * 0.65
    fire_collar = np.clip(fire_collar, 0.0, 1.0)
    emerald_rim = np.clip((t_core - 0.62) / 0.38, 0.0, 1.0)

    core_r = (195.0 * (1.0 - emerald_rim) + 36.0 * emerald_rim) * (0.65 + 0.45 * fiber_mix) + fire_collar * 60.0
    core_g = (28.0 * (1.0 - emerald_rim) + 195.0 * emerald_rim) * (0.65 + 0.45 * fiber_mix) + fire_collar * 185.0
    core_b = (32.0 * (1.0 - emerald_rim) + 65.0 * emerald_rim) * (0.65 + 0.45 * fiber_mix) + fire_collar * 55.0

    # --- 2. INNER RING OF 12 GOTHIC PETALS (0.165 <= rr < 0.305) ---
    n12 = 12.0
    a12 = ((ang + math.pi) / (2.0 * math.pi) * n12) % 1.0  # [0, 1) within each of 12 sectors
    idx12 = np.floor((ang + math.pi) / (2.0 * math.pi) * n12).astype(np.int32) % 12
    d_spoke12 = np.minimum(a12, 1.0 - a12) * (2.0 * math.pi / n12) * rr

    # Pointed lancet arch cap at outer end of each 12-petal
    arch12_r = 0.292 - 0.055 * (np.abs(a12 - 0.5) * 2.0) ** 1.6
    d_arch12 = np.abs(rr - arch12_r)
    in_petal12 = (rr >= 0.168) & (rr < arch12_r) & (d_spoke12 > 0.008)

    # Glow inside each 12-petal
    p12_glow = np.maximum(0.0, np.sin(np.clip(a12, 0.0, 1.0) * math.pi) * np.sin(np.clip((rr - 0.168) / 0.125, 0.0, 1.0) * math.pi))
    p12_glow = (0.45 + 0.65 * (p12_glow ** 0.55)) * (0.85 + 0.30 * fine)

    # Alternate 12-petal colors: Ruby-Crimson, Golden-Amber, Toxic-Emerald, Royal-Violet
    c12 = idx12 % 4
    p12_r = np.select([c12 == 0, c12 == 1, c12 == 2, c12 == 3], [225.0, 240.0, 38.0, 108.0]) * p12_glow
    p12_g = np.select([c12 == 0, c12 == 1, c12 == 2, c12 == 3], [28.0, 180.0, 205.0, 48.0]) * p12_glow
    p12_b = np.select([c12 == 0, c12 == 1, c12 == 2, c12 == 3], [42.0, 40.0, 74.0, 220.0]) * p12_glow

    # --- 3. OUTER RING OF 24 GOTHIC PETALS & QUATREFOIL OCULI (0.312 <= rr < 0.468) ---
    n24 = 24.0
    a24 = ((ang + math.pi) / (2.0 * math.pi) * n24) % 1.0
    idx24 = np.floor((ang + math.pi) / (2.0 * math.pi) * n24).astype(np.int32) % 24
    d_spoke24 = np.minimum(a24, 1.0 - a24) * (2.0 * math.pi / n24) * rr

    # Pointed trefoil/lancet outer tip at rr ~ 0.455
    arch24_r = 0.452 - 0.048 * (np.abs(a24 - 0.5) * 2.0) ** 1.5
    # Embedded circular rosette oculus at the tip of every even sector
    oculus_r = 0.415
    d_oculus_ang = (a12 - 0.5) * (2.0 * math.pi / n12) * oculus_r
    d_oculus = np.sqrt((rr - oculus_r) ** 2 + d_oculus_ang ** 2)
    in_oculus = d_oculus < 0.032
    oculus_lead = np.abs(d_oculus - 0.032) < 0.008

    in_petal24 = (rr >= 0.315) & (rr < arch24_r) & (d_spoke24 > 0.0065)
    p24_glow = np.maximum(0.0, np.sin(np.clip(a24, 0.0, 1.0) * math.pi) * np.sin(np.clip((rr - 0.315) / 0.135, 0.0, 1.0) * math.pi))
    p24_glow = (0.45 + 0.65 * (p24_glow ** 0.55)) * (0.85 + 0.30 * fine)

    c24 = (idx24 + 1) % 4
    p24_r = np.select([c24 == 0, c24 == 1, c24 == 2, c24 == 3], [220.0, 242.0, 42.0, 112.0]) * p24_glow
    p24_g = np.select([c24 == 0, c24 == 1, c24 == 2, c24 == 3], [26.0, 185.0, 210.0, 52.0]) * p24_glow
    p24_b = np.select([c24 == 0, c24 == 1, c24 == 2, c24 == 3], [40.0, 44.0, 78.0, 225.0]) * p24_glow

    # Bright golden/crimson eye-like centers inside the 12 outer oculi
    oc_glow = np.clip(1.0 - d_oculus / 0.032, 0.2, 1.0)
    p24_r = np.where(in_oculus, 248.0 * oc_glow, p24_r)
    p24_g = np.where(in_oculus, np.where(idx12 % 2 == 0, 200.0, 38.0) * oc_glow, p24_g)
    p24_b = np.where(in_oculus, 48.0 * oc_glow, p24_b)

    # --- 4. STONE & IRON TRACERY RINGS + CORNER SPANDRELS ---
    # Heavy stone/bone outer surround (rr >= 0.468) with corner trefoil carvings
    corner_d = np.sqrt((np.abs(cx) - 0.38) ** 2 + (np.abs(cy) - 0.38) ** 2)
    in_corner_foil = (rr > 0.475) & (corner_d < 0.075)
    foil_glow = np.clip(1.0 - corner_d / 0.075, 0.2, 1.0)

    stone_val = 36.0 + fine * 52.0 + np.sin(rr * 80.0) * 14.0

    # Assemble regions from center outward
    # Default: dark carved gothic stone/iron tracery
    rgb[:, :, 0] = stone_val + 8.0
    rgb[:, :, 1] = stone_val + 2.0
    rgb[:, :, 2] = stone_val - 2.0

    # Corner spandrel stained-glass foils
    rgb[:, :, 0] = np.where(in_corner_foil, 185.0 * foil_glow, rgb[:, :, 0])
    rgb[:, :, 1] = np.where(in_corner_foil, 24.0 * foil_glow, rgb[:, :, 1])
    rgb[:, :, 2] = np.where(in_corner_foil, 36.0 * foil_glow, rgb[:, :, 2])

    # 24 outer petals
    for ch, arr in enumerate((p24_r, p24_g, p24_b)):
        rgb[:, :, ch] = np.where(in_petal24 & (~oculus_lead), arr, rgb[:, :, ch])

    # 12 inner petals
    for ch, arr in enumerate((p12_r, p12_g, p12_b)):
        rgb[:, :, ch] = np.where(in_petal12 & (d_arch12 > 0.007), arr, rgb[:, :, ch])

    # Core eye
    in_core = rr < 0.155
    for ch, arr in enumerate((core_r, core_g, core_b)):
        rgb[:, :, ch] = np.where(in_core, arr, rgb[:, :, ch])

    # Crisp dark iron tracery rings & slit pupil
    iron_ring = (
        in_pupil
        | ((rr >= 0.153) & (rr <= 0.168))
        | ((rr >= 0.302) & (rr <= 0.316))
        | ((rr >= 0.460) & (rr <= 0.476))
    )
    rgb[:, :, 0] = np.where(iron_ring, 14.0, rgb[:, :, 0])
    rgb[:, :, 1] = np.where(iron_ring, 12.0, rgb[:, :, 1])
    rgb[:, :, 2] = np.where(iron_ring, 18.0, rgb[:, :, 2])

    palette = [
        (10, 10, 14),     # pitch black pupil & deep iron tracery
        (28, 24, 30),     # dark gothic iron/stone mullion
        (56, 48, 46),     # carved ossified stone tracery
        (92, 78, 68),     # weathered stone ring highlight
        (98, 12, 22),     # dark ruby petal shadow
        (174, 20, 32),    # rich crimson-ruby glass
        (236, 44, 52),    # blazing crimson glass/iris
        (18, 92, 36),     # dark viridian-emerald glass
        (36, 174, 62),    # glowing emerald stained glass
        (88, 242, 104),   # bright toxic emerald highlight
        (158, 104, 22),   # deep amber-gold glass
        (232, 178, 38),   # blazing golden-amber iris & petal
        (254, 236, 124),  # incandescent eye collar highlight
        (46, 24, 118),    # deep nocturnal violet petal
        (96, 52, 208),    # vivid violet-blue glass
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=18.0)


def make_cathedral_floor(size=256):
    """
    5. tex_cathedral_floor.png (Piso Cerimonial da Nave Gotica):
    Ornate diagonal diamond / checkerboard gothic pavement (4x4 diagonal diamond tiles).
    Alternating tiles of:
    (a) Polished dark obsidian-crimson coagulated bloodstone with subtle veins, and
    (b) Pale carved ivory-bone slabs inlaid with geometric gothic quatrefoil / cross-rune
        reliefs and recessed crimson blood-grooves along the tile borders.
    """
    yy, xx = _coords_2d(size)
    n_coarse = fbm_periodic(size, base_period=6, octaves=4, seed=2501)
    n_fine = fbm_periodic(size, base_period=16, octaves=3, seed=2502)
    _, v_edge = voronoi_periodic(size, num_points=32, seed=2503)

    # Diagonal diamond grid: 4x4 diagonal periods across tile
    n_diag = 4.0
    du = (xx + yy) * n_diag
    dv = (xx - yy + 1.0) * n_diag

    iu = np.floor(du).astype(np.int32)
    iv = np.floor(dv).astype(np.int32)
    fu = du - np.floor(du)  # [0, 1)
    fv = dv - np.floor(dv)  # [0, 1)

    # Centered tile coordinates in [-0.5, 0.5)
    tu = fu - 0.5
    tv = fv - 0.5
    dist_edge = 0.5 - np.maximum(np.abs(tu), np.abs(tv))  # 0 at seam, 0.5 at tile center

    # Alternating checkerboard parity
    is_bone_tile = ((iu + iv) % 2) == 0

    # Deep blood-filled mortar channel between diamond tiles
    in_mortar = dist_edge < 0.036
    # 3D bevel along tile perimeter
    bevel_light = np.exp(-((dist_edge - 0.055) / 0.025) ** 2) * ((tu < 0) | (tv < 0)).astype(np.float32)
    bevel_dark = np.exp(-((dist_edge - 0.055) / 0.025) ** 2) * ((tu >= 0) & (tv >= 0)).astype(np.float32)

    # --- (A) Polished Dark Obsidian-Crimson Bloodstone Tile ---
    vein = np.clip(1.0 - (v_edge + (n_fine - 0.5) * 0.02) * 28.0, 0.0, 1.0) ** 2.0
    spec_sweep = np.exp(-(((tu + tv) - 0.12 + (n_coarse - 0.5) * 0.25) / 0.28) ** 2) * 0.35
    obs_val = np.clip(0.25 + n_coarse * 0.45 + n_fine * 0.30 + bevel_light * 0.35 - bevel_dark * 0.25, 0.0, 1.0)

    obs_r = 22.0 + obs_val * 58.0 + vein * 135.0 + spec_sweep * 65.0
    obs_g = 6.0 + obs_val * 14.0 + vein * 16.0 + spec_sweep * 32.0
    obs_b = 12.0 + obs_val * 24.0 + vein * 22.0 + spec_sweep * 38.0

    # --- (B) Pale Carved Ivory-Bone Slab with Gothic Quatrefoil & Blood-Groove Inlay ---
    # Recessed crimson blood-groove border around the bone slab (dist_edge in [0.09, 0.13])
    blood_groove_border = np.exp(-((dist_edge - 0.11) / 0.018) ** 2)

    # Central Gothic Quatrefoil + Cross-Rune Relief inside dist_edge > 0.16
    r_tile = np.sqrt(tu * tu + tv * tv)
    ang_tile = np.arctan2(tv, tu)
    quat_curve = 0.21 + 0.055 * np.cos(4.0 * ang_tile)
    quat_ring = np.exp(-((r_tile - quat_curve) / 0.020) ** 2) * (dist_edge > 0.14)

    # Carved cross-rune arms inside the quatrefoil
    cross_arm = (
        ((np.abs(tu) < 0.038) & (np.abs(tv) < 0.22))
        | ((np.abs(tv) < 0.038) & (np.abs(tu) < 0.22))
    )
    cross_edge = (
        (np.abs(np.abs(tu) - 0.038) < 0.014) & (np.abs(tv) < 0.22)
        | (np.abs(np.abs(tv) - 0.038) < 0.014) & (np.abs(tu) < 0.22)
    )
    center_rosette = r_tile < 0.065

    bone_base = (
        0.62
        + (n_coarse - 0.5) * 0.24
        + (n_fine - 0.5) * 0.16
        + quat_ring * 0.18
        + cross_edge.astype(np.float32) * 0.15
        + bevel_light * 0.22
        - bevel_dark * 0.25
        - (cross_arm & (~center_rosette)).astype(np.float32) * 0.18
    )
    bone_base = np.clip(bone_base, 0.0, 1.0)

    # Blood inlay inside the perimeter blood-groove and central cross-rune
    blood_inlay = np.clip(
        blood_groove_border * 0.92 + (cross_arm & (~center_rosette)).astype(np.float32) * 0.78,
        0.0,
        1.0,
    )

    bone_r = (58.0 + bone_base * 172.0) * (1.0 - blood_inlay) + 148.0 * blood_inlay
    bone_g = (48.0 + bone_base * 156.0) * (1.0 - blood_inlay) + 18.0 * blood_inlay
    bone_b = (38.0 + bone_base * 128.0) * (1.0 - blood_inlay) + 24.0 * blood_inlay

    # Combine alternating tiles + coagulated blood mortar joints
    mortar_r = 28.0 + n_fine * 36.0
    mortar_g = 5.0 + n_fine * 8.0
    mortar_b = 8.0 + n_fine * 10.0

    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = np.where(in_mortar, mortar_r, np.where(is_bone_tile, bone_r, obs_r))
    rgb[:, :, 1] = np.where(in_mortar, mortar_g, np.where(is_bone_tile, bone_g, obs_g))
    rgb[:, :, 2] = np.where(in_mortar, mortar_b, np.where(is_bone_tile, bone_b, obs_b))

    palette = [
        (16, 4, 8),       # deep obsidian-bloodstone & mortar shadow
        (34, 8, 16),      # polished dark obsidian-crimson slab
        (58, 16, 26),     # mid bloodstone reflection
        (96, 14, 22),     # recessed crimson blood-groove inlay
        (148, 22, 30),    # glowing crimson bloodstone vein
        (192, 46, 52),    # wet specular bloodstone glint
        (78, 66, 54),     # shadowed bone slab bevel
        (118, 102, 82),   # weathered ash-bone relief shadow
        (158, 142, 114),  # mid carved bone-ivory slab
        (194, 178, 146),  # pale gothic bone tile
        (224, 210, 178),  # polished ivory quatrefoil highlight
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=18.0)


def make_cathedral_door(size=256):
    """
    6. tex_cathedral_door.png (Portao Monumental de Queratina e Madeira Sangrenta):
    Heavy vertical gothic timber/keratin planks in dark charred mahogany & necrotic
    brown-red, reinforced with horizontal wrought-iron / calcified-bone strap hinges,
    gothic pointed-arch recessed panels, and raised metallic/bone studs.
    """
    yy, xx = _coords_2d(size)
    n_coarse = fbm_periodic(size, base_period=4, octaves=4, seed=2601)
    n_fine = fbm_periodic(size, base_period=16, octaves=3, seed=2602)

    # 8 heavy vertical keratin/timber planks across the door
    n_planks = 8.0
    pf = xx * n_planks
    p_idx = np.floor(pf).astype(np.int32) % 8
    px = pf - np.floor(pf)  # [0, 1) within plank

    rng = np.random.default_rng(2610)
    plank_tone = rng.uniform(-0.10, 0.10, size=8).astype(np.float32)[p_idx]
    plank_phase = rng.uniform(0.0, 6.28, size=8).astype(np.float32)[p_idx]

    # Fibrous vertical wood/keratin grain
    grain_wave = np.sin((px * 6.0 + yy * 1.5 + (n_coarse - 0.5) * 2.2) * 2.0 * math.pi + plank_phase)
    fine_grain = np.sin((xx * 64.0 + (n_fine - 0.5) * 3.0) * 2.0 * math.pi)
    grain = 0.5 + 0.32 * grain_wave + 0.18 * fine_grain

    # Deep vertical plank seams & beveled plank edges
    plank_seam = np.minimum(px, 1.0 - px) < 0.045
    plank_bevel_hi = np.exp(-((px - 0.08) / 0.05) ** 2)
    plank_bevel_lo = np.exp(-((px - 0.92) / 0.05) ** 2)

    # Gothic Pointed-Arch Recessed Panels (2 columns of lancet panels, upper & lower tiers)
    bay_x = (xx * 2.0) % 1.0
    dx_bay = np.abs(bay_x - 0.5) * 2.0  # 0 at bay center, 1 at bay edge

    # Upper gothic pointed-arch panel (yy in [0.16, 0.44]) & lower panel (yy in [0.58, 0.86])
    arch_top_u = 0.16 + 0.11 * (dx_bay ** 1.6)
    in_upper_panel = (dx_bay < 0.72) & (yy > arch_top_u) & (yy < 0.44)
    panel_rim_u = (dx_bay < 0.78) & (yy > arch_top_u - 0.025) & (yy < 0.46) & (~in_upper_panel)

    arch_top_l = 0.58 + 0.11 * (dx_bay ** 1.6)
    in_lower_panel = (dx_bay < 0.72) & (yy > arch_top_l) & (yy < 0.86)
    panel_rim_l = (dx_bay < 0.78) & (yy > arch_top_l - 0.025) & (yy < 0.88) & (~in_lower_panel)

    in_recess = in_upper_panel | in_lower_panel
    in_molding = panel_rim_u | panel_rim_l

    # Carved gothic tracery rib inside each recessed panel
    tracery_rib = in_recess & (
        (np.abs(dx_bay - 0.36) < 0.045) | (np.abs(dx_bay) < 0.045)
    )

    # Base timber/keratin shading (dark charred mahogany + necrotic dried-blood crimson)
    wood_val = (
        0.38
        + grain * 0.38
        + (n_coarse - 0.5) * 0.22
        + plank_tone
        + plank_bevel_hi * 0.18
        - plank_bevel_lo * 0.20
        - in_recess.astype(np.float32) * 0.16
        + tracery_rib.astype(np.float32) * 0.20
        + in_molding.astype(np.float32) * 0.14 * np.where(dx_bay < 0.5, 1.0, -1.0)
    )
    wood_val = np.where(plank_seam, 0.05, np.clip(wood_val, 0.05, 1.0))

    wood_r = 24.0 + wood_val * 124.0
    wood_g = 8.0 + wood_val * 44.0
    wood_b = 10.0 + wood_val * 38.0

    # Horizontal Wrought-Iron & Calcified-Bone Strap Hinges (3 bands: yy=0.09, 0.50, 0.91)
    strap_y_centers = [0.09, 0.50, 0.91]
    in_strap = np.zeros((size, size), dtype=bool)
    strap_bevel = np.zeros((size, size), dtype=np.float32)
    in_stud = np.zeros((size, size), dtype=bool)
    stud_shade = np.zeros((size, size), dtype=np.float32)
    in_bone_inlay = np.zeros((size, size), dtype=bool)

    for sy in strap_y_centers:
        dy = yy - sy
        # Ornate gothic strap with scalloped / fleur-de-lis wave along its edge
        scallop = 0.008 * np.cos(xx * 16.0 * math.pi)
        half_h = 0.038 + scallop
        band = np.abs(dy) <= half_h
        in_strap |= band
        strap_bevel = np.where(
            band,
            np.exp(-((dy + half_h * 0.7) / 0.012) ** 2) * 0.65
            - np.exp(-((dy - half_h * 0.7) / 0.012) ** 2) * 0.55,
            strap_bevel,
        )

        # Carved calcified-bone medial strip inside the iron strap
        in_bone_inlay |= (np.abs(dy) < 0.011) & (np.cos(xx * 16.0 * math.pi) > -0.6)

        # 8 raised pyramidal/round bone-iron studs along each strap
        stud_cx = (np.floor(xx * 8.0) + 0.5) / 8.0
        sdx = (xx - stud_cx) * 1.0
        sdy = dy
        sr = np.sqrt(sdx * sdx + sdy * sdy)
        this_stud = sr < 0.020
        in_stud |= this_stud
        # 3D highlight toward top-left of stud
        s_hi = np.clip(1.0 - np.sqrt((sdx + 0.006) ** 2 + (sdy + 0.006) ** 2) / 0.022, 0.0, 1.0)
        stud_shade = np.where(this_stud, s_hi, stud_shade)

    # Vertical center lock-plate / spine bar near xx=0.5 with 3D bevel + Gothic knocker ring
    center_bar = np.abs(xx - 0.5) < 0.026
    in_strap |= center_bar
    center_bevel = np.where(
        center_bar,
        np.exp(-((xx - 0.480) / 0.008) ** 2) * 0.65 - np.exp(-((xx - 0.520) / 0.008) ** 2) * 0.55,
        0.0,
    )
    strap_bevel = np.where(center_bar & (np.abs(strap_bevel) < 0.1), center_bevel, strap_bevel)

    # Ornate Gothic ring knocker / lock escutcheon at (0.5, 0.58)
    kr = np.sqrt((xx - 0.5) ** 2 + (yy - 0.62) ** 2)
    knocker_ring = (kr > 0.042) & (kr < 0.062)
    in_strap |= knocker_ring
    in_bone_inlay |= (kr > 0.048) & (kr < 0.056)

    # Shade iron straps, calcified bone inlays, and raised studs
    iron_base = 34.0 + n_fine * 38.0 + strap_bevel * 42.0
    iron_r = np.clip(iron_base + 6.0, 12.0, 140.0)
    iron_g = np.clip(iron_base + 4.0, 12.0, 135.0)
    iron_b = np.clip(iron_base + 10.0, 16.0, 148.0)

    # Calcified bone inlay & bone-tipped studs
    bone_val = 145.0 + n_fine * 55.0
    strap_r = np.where(in_bone_inlay, bone_val, iron_r)
    strap_g = np.where(in_bone_inlay, bone_val - 14.0, iron_g)
    strap_b = np.where(in_bone_inlay, bone_val - 34.0, iron_b)

    # Studs override with bright 3D specular bone/brass dome
    stud_r = 65.0 + stud_shade * 175.0
    stud_g = 55.0 + stud_shade * 158.0
    stud_b = 45.0 + stud_shade * 132.0

    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = np.where(in_stud, stud_r, np.where(in_strap, strap_r, wood_r))
    rgb[:, :, 1] = np.where(in_stud, stud_g, np.where(in_strap, strap_g, wood_g))
    rgb[:, :, 2] = np.where(in_stud, stud_b, np.where(in_strap, strap_b, wood_b))

    palette = [
        (14, 5, 7),       # deep plank seam & necrotic shadow
        (32, 10, 12),     # charred mahogany recess
        (56, 16, 18),     # dark blood-stained timber
        (86, 26, 24),     # rich mahogany-keratin plank
        (118, 38, 32),    # warm blood-wood grain highlight
        (152, 58, 44),    # sunlit keratin plank bevel
        (26, 26, 32),     # dark wrought-iron hinge shadow
        (52, 52, 62),     # forged iron strap base
        (88, 88, 100),    # metallic iron bevel highlight
        (148, 134, 108),  # calcified bone strap inlay
        (214, 198, 164),  # raised bone/metallic stud highlight
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=18.0)


def make_spire_shingles(size=256):
    """
    7. tex_spire_shingles.png (Telhas Escamosas de Queratina e Cobre Oxidado):
    Overlapping pointed gothic fish-scale / diamond roof shingles.
    Color palette combines the oxidized verdigris emerald-teal of the Catedral da Se
    spires and eerie moonlight cyan-green, with longitudinal keratin ridges and dark
    mossy/necrotic shadows between overlapping scales.
    """
    yy, xx = _coords_2d(size)
    n_coarse = fbm_periodic(size, base_period=6, octaves=4, seed=2701)
    n_fine = fbm_periodic(size, base_period=16, octaves=3, seed=2702)

    n_rows = 8
    n_cols = 6
    rng = np.random.default_rng(2710)
    tile_patina = rng.uniform(-0.14, 0.14, size=(n_rows, n_cols)).astype(np.float32)

    # Evaluate overlapping pointed gothic scales from bottom row to top row
    # In UV space, yy=0 is top, yy=1 is bottom; row r overlaps row r+1 below it.
    ry = yy * n_rows
    base_row = np.floor(ry).astype(np.int32) % n_rows
    fy = ry - np.floor(ry)  # [0, 1) within row band

    # Check if the pointed tip from row (base_row - 1) above extends down into this pixel
    prev_row = (base_row - 1) % n_rows
    stagger_cur = (base_row % 2).astype(np.float32) * 0.5
    stagger_prev = (prev_row % 2).astype(np.float32) * 0.5

    cx_cur = (xx * n_cols + stagger_cur) % 1.0 - 0.5
    col_cur = np.floor((xx * n_cols + stagger_cur) % n_cols).astype(np.int32)

    cx_prev = (xx * n_cols + stagger_prev) % 1.0 - 0.5
    col_prev = np.floor((xx * n_cols + stagger_prev) % n_cols).astype(np.int32)

    # Pointed gothic ogive tip profile of the upper overlapping shingle extending into fy in [0, 0.42]
    tip_reach = 0.38 * (1.0 - (np.abs(cx_prev) * 2.0) ** 1.35)
    under_upper_tip = fy < tip_reach

    active_row = np.where(under_upper_tip, prev_row, base_row)
    active_col = np.where(under_upper_tip, col_prev, col_cur)
    local_x = np.where(under_upper_tip, cx_prev, cx_cur)  # [-0.5, 0.5)
    local_y = np.where(under_upper_tip, fy + 1.0, fy)      # [0, 1.38] along scale length

    # Distance to the lower pointed edge of the active scale
    active_tip_y = 1.0 + 0.38 * (1.0 - (np.abs(local_x) * 2.0) ** 1.35)
    dist_lower_rim = active_tip_y - local_y
    dist_side_seam = 0.5 - np.abs(local_x)
    edge_dist = np.minimum(dist_lower_rim * 0.85, dist_side_seam * 1.4)

    # Distance below the overlapping upper scale's pointed tip (for cast drop shadow)
    shadow_dist = np.where(under_upper_tip, active_tip_y - local_y, fy - np.maximum(tip_reach, 0.0))
    cast_shadow = np.where(~under_upper_tip, np.exp(-(np.maximum(fy - tip_reach, 0.0) / 0.16) ** 2), 0.0)

    # Raised 3D beveled rim along the pointed scale edge + central longitudinal keratin ridge
    scale_rim = np.exp(-((edge_dist - 0.065) / 0.032) ** 2) * (edge_dist >= 0.025)
    scale_seam = edge_dist < 0.028
    center_ridge = np.exp(-((local_x) / 0.11) ** 2) * (1.0 - np.clip(dist_lower_rim / 1.2, 0.0, 1.0) * 0.4)

    # Fine longitudinal keratin/copper striations on each shingle
    striation = 0.5 + 0.5 * np.sin((local_x * 18.0 + (n_fine - 0.5) * 1.2) * 2.0 * math.pi)
    patina = tile_patina[active_row, active_col]

    # Combine height & moonlight verdigris shading
    val = (
        0.46
        + (n_coarse - 0.5) * 0.26
        + (n_fine - 0.5) * 0.16
        + striation * 0.12
        + patina
        + scale_rim * 0.26
        + center_ridge * 0.18
        - cast_shadow * 0.38
    )
    val = np.where(scale_seam, 0.04, np.clip(val, 0.04, 1.0))
    gleam = np.clip((val - 0.72) / 0.28, 0.0, 1.0)

    # Oxidized copper-verdigris (emerald-teal) + eerie moonlight cyan-green highlights
    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = 10.0 + val * 84.0 + gleam * 52.0
    rgb[:, :, 1] = 22.0 + val * 158.0 + gleam * 58.0
    rgb[:, :, 2] = 20.0 + val * 136.0 + gleam * 56.0

    palette = [
        (8, 16, 16),      # deep necrotic moss crevice between scales
        (16, 34, 32),     # dark overlap drop shadow
        (24, 58, 52),     # shadowed dark teal-verdigris shingle
        (34, 86, 76),     # deep oxidized copper-green
        (46, 118, 102),   # mid Catedral da Se spire verdigris
        (64, 152, 128),   # vibrant oxidized emerald-teal scale
        (88, 184, 156),   # moonlight cyan-green keratin ridge
        (124, 214, 186),  # bright verdigris bevel highlight
        (172, 238, 216),  # eerie pale moonlight patina gleam
        (62, 48, 40),     # tarnished dark bronze/keratin undertone
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=18.0)


def make_organ_pipes(size=256):
    """
    8. tex_organ_pipes.png (Tubos do Orgao de Traqueias e Ossos):
    Parallel vertical cylindrical pipes shaded with strong 3D cylindrical highlights
    (pale bone-ivory and tarnished antique brass/gold), horizontal cartilaginous
    tracheal rings, and dark acoustic mouth slots across the lower band.
    """
    yy, xx = _coords_2d(size)
    n_coarse = fbm_periodic(size, base_period=6, octaves=3, seed=2801)
    n_fine = fbm_periodic(size, base_period=16, octaves=3, seed=2802)

    # 8 vertical organ pipes across the width
    n_pipes = 8
    pf = xx * n_pipes
    p_idx = np.floor(pf).astype(np.int32) % n_pipes
    px = pf - np.floor(pf)  # [0, 1) across each pipe

    # Normal X across cylinder in [-1, 1]
    nx = (px - 0.5) * 2.0
    in_pipe = np.abs(nx) < 0.88
    cyl_z = np.sqrt(np.clip(1.0 - (nx / 0.88) ** 2, 0.0, 1.0))

    # 3D Cylindrical diffuse + off-center specular highlight (light from front-left)
    light_dir_x = -0.28
    cyl_diffuse = np.clip(cyl_z * 0.88 - nx * light_dir_x * 0.35, 0.0, 1.0)
    cyl_spec = np.exp(-((nx - (-0.24)) / 0.18) ** 2) * in_pipe

    # Stepped gothic mitre heights for the acoustic mouth slots across the 8 pipes
    # Symmetric V/W cathedral pipe-organ mouth line around yy ~ 0.72..0.84
    mouth_centers = np.array([0.72, 0.76, 0.80, 0.84, 0.84, 0.80, 0.76, 0.72], dtype=np.float32)
    m_y = mouth_centers[p_idx]

    # Upper decorative gothic crown/collar near yy = 0.08..0.18 (stepped inversely)
    crown_centers = np.array([0.16, 0.13, 0.10, 0.07, 0.07, 0.10, 0.13, 0.16], dtype=np.float32)
    c_y = crown_centers[p_idx]

    # Acoustic mouth slot (dark rectangular/arched windway opening + beveled upper lip / labium)
    dy_mouth = yy - m_y
    in_mouth_slot = (np.abs(nx) < 0.68) & (dy_mouth > -0.028) & (dy_mouth < 0.022)
    # Upper triangular/beveled gothic labium flat above the mouth slot
    in_upper_lip = (np.abs(nx) < 0.66) & (dy_mouth <= -0.028) & (dy_mouth > -0.105)
    # Lower windway collar ring right below the mouth slot
    in_lower_collar = (np.abs(nx) < 0.86) & (dy_mouth >= 0.022) & (dy_mouth < 0.052)

    # Horizontal cartilaginous tracheal rings along the pipe body (above the labium & below the collar)
    ring_wave = np.sin((yy * 28.0 + (n_coarse - 0.5) * 0.35) * 2.0 * math.pi)
    tracheal_ring = np.clip((ring_wave - 0.35) / 0.65, 0.0, 1.0) * (
        (~in_mouth_slot) & (~in_upper_lip)
    )
    tracheal_groove = np.clip((-ring_wave - 0.45) / 0.55, 0.0, 1.0) * (
        (~in_mouth_slot) & (~in_upper_lip)
    )

    # Conical pipe foot tapering slightly below the mouth collar (yy > m_y + 0.052)
    in_foot = yy > (m_y + 0.052)

    # Alternate subtle material richness: Bone-Ivory trachea vs Tarnished Brass-Gold ossified pipe
    is_brass_pipe = (p_idx % 2) == 1
    collar_band = in_lower_collar | (np.abs(yy - c_y) < 0.022)

    # Combine 3D pipe shading
    surf_val = (
        cyl_diffuse * 0.68
        + cyl_spec * 0.28
        + tracheal_ring * 0.16 * cyl_z
        - tracheal_groove * 0.18
        + (n_fine - 0.5) * 0.10
        + collar_band.astype(np.float32) * 0.18
        - in_upper_lip.astype(np.float32) * 0.12
        - in_foot.astype(np.float32) * 0.08
    )
    surf_val = np.clip(surf_val, 0.0, 1.0)

    # Bone-ivory vs tarnished antique brass/gold tinting
    use_brass = is_brass_pipe | collar_band | in_upper_lip
    pipe_r = np.where(use_brass, 48.0 + surf_val * 186.0 + cyl_spec * 22.0, 52.0 + surf_val * 178.0 + cyl_spec * 18.0)
    pipe_g = np.where(use_brass, 36.0 + surf_val * 152.0 + cyl_spec * 20.0, 44.0 + surf_val * 164.0 + cyl_spec * 18.0)
    pipe_b = np.where(use_brass, 18.0 + surf_val * 88.0 + cyl_spec * 12.0, 34.0 + surf_val * 134.0 + cyl_spec * 16.0)

    # Dark acoustic mouth opening & deep inter-pipe shadow grooves (pipes run full height 0..1)
    dark_cavity = (~in_pipe) | in_mouth_slot
    cavity_r = 16.0 + n_fine * 18.0
    cavity_g = 10.0 + n_fine * 12.0
    cavity_b = 12.0 + n_fine * 14.0

    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = np.where(dark_cavity, cavity_r, pipe_r)
    rgb[:, :, 1] = np.where(dark_cavity, cavity_g, pipe_g)
    rgb[:, :, 2] = np.where(dark_cavity, cavity_b, pipe_b)

    palette = [
        (12, 8, 10),      # pitch black acoustic mouth slot & inter-pipe void
        (28, 20, 20),     # deep cylinder edge shadow
        (54, 40, 32),     # dark tarnished bronze/bone shadow
        (86, 66, 46),     # shaded antique brass & tracheal groove
        (124, 96, 58),    # mid tarnished brass-gold pipe
        (164, 130, 74),   # warm antique gold pipe body
        (204, 168, 96),   # bright brass-gold collar & ring
        (112, 98, 80),    # shaded cartilaginous trachea bone
        (156, 140, 114),  # mid ossified tracheal pipe
        (196, 180, 148),  # pale bone-ivory tracheal ring
        (236, 222, 186),  # gleaming cylindrical specular highlight
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=18.0)


def generate_cathedral_textures(out_dir, size=256):
    """Generates all 8 Gothic Body-Horror Cathedral PS1 textures (256x256 PNGs)."""
    os.makedirs(out_dir, exist_ok=True)
    specs = {
        "tex_flesh_bricks.png": make_flesh_bricks(size),
        "tex_ossified_stone.png": make_ossified_stone(size),
        "tex_cathedral_stained_glass.png": make_cathedral_stained_glass(size),
        "tex_cathedral_rose_window.png": make_cathedral_rose_window(size),
        "tex_cathedral_floor.png": make_cathedral_floor(size),
        "tex_cathedral_door.png": make_cathedral_door(size),
        "tex_spire_shingles.png": make_spire_shingles(size),
        "tex_organ_pipes.png": make_organ_pipes(size),
    }
    paths = {}
    for name, img in specs.items():
        p = os.path.join(out_dir, name)
        img.save(p, format="PNG")
        paths[name] = p
    return paths


if __name__ == "__main__":
    out = r"C:\Users\user\IdeaProjects\Game final\Assets\PilotChamber\Textures"
    generated = generate_cathedral_textures(out, size=256)
    for k, v in generated.items():
        print(f"Generated {k}: {v} ({os.path.getsize(v)} bytes)")
