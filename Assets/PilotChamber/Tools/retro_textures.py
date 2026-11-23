import os
import math
import numpy as np
from PIL import Image

# 4x4 Bayer ordered dithering matrix normalized to [-0.5, 0.5]
BAYER_4X4 = (np.array([
    [ 0,  8,  2, 10],
    [12,  4, 14,  6],
    [ 3, 11,  1,  9],
    [15,  7, 13,  5]
], dtype=np.float32) / 16.0) - 0.5


def periodic_value_noise(size, period, seed=0):
    """Generates seamlessly tileable 2D periodic value noise on a size x size grid."""
    rng = np.random.default_rng(seed)
    grid = rng.random((period, period), dtype=np.float32)

    coords = np.linspace(0, period, size, endpoint=False, dtype=np.float32)
    x0 = np.floor(coords).astype(np.int32) % period
    x1 = (x0 + 1) % period
    fx = coords - np.floor(coords)
    ux = fx * fx * (3.0 - 2.0 * fx)

    Y0, X0 = np.meshgrid(x0, x0, indexing='ij')
    Y1, X1 = np.meshgrid(x1, x1, indexing='ij')
    UY, UX = np.meshgrid(ux, ux, indexing='ij')

    v00 = grid[Y0, X0]
    v10 = grid[Y0, X1]
    v01 = grid[Y1, X0]
    v11 = grid[Y1, X1]

    nx0 = v00 * (1.0 - UX) + v10 * UX
    nx1 = v01 * (1.0 - UX) + v11 * UX
    return nx0 * (1.0 - UY) + nx1 * UY


def fbm_periodic(size, base_period=4, octaves=4, seed=42):
    """Tileable Fractal Brownian Motion noise in [0, 1]."""
    total = np.zeros((size, size), dtype=np.float32)
    amp = 1.0
    norm = 0.0
    period = base_period
    for i in range(octaves):
        total += amp * periodic_value_noise(size, period, seed=seed + i * 31)
        norm += amp
        amp *= 0.5
        period *= 2
    return total / norm


def voronoi_periodic(size, num_points=18, seed=10):
    """Tileable 2D Voronoi distance field (F1 and F2-F1) for cells/bubbles/veins."""
    rng = np.random.default_rng(seed)
    pts = rng.random((num_points, 2), dtype=np.float32)
    yy, xx = np.meshgrid(
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        indexing='ij'
    )
    f1 = np.full((size, size), 10.0, dtype=np.float32)
    f2 = np.full((size, size), 10.0, dtype=np.float32)

    for px, py in pts:
        dx = np.abs(xx - px)
        dx = np.minimum(dx, 1.0 - dx)
        dy = np.abs(yy - py)
        dy = np.minimum(dy, 1.0 - dy)
        d = np.sqrt(dx * dx + dy * dy)
        mask1 = d < f1
        f2 = np.where(mask1, f1, np.minimum(f2, d))
        f1 = np.where(mask1, d, f1)

    return f1, f2 - f1


def apply_bayer_palette(rgb_float, palette_rgb, dither_strength=28.0, alpha_mask=None):
    """
    Applies PS1-style 4x4 Bayer ordered dithering and snaps to an explicit retro palette.
    If alpha_mask is provided (H x W float in [0,1]), outputs RGBA with hard retro cutout.
    """
    h, w, _ = rgb_float.shape
    bayer = np.tile(BAYER_4X4, (h // 4 + 1, w // 4 + 1))[:h, :w]
    dithered = rgb_float + bayer[:, :, None] * dither_strength
    dithered = np.clip(dithered, 0.0, 255.0)

    pal = np.array(palette_rgb, dtype=np.float32)
    diff = dithered[:, :, None, :] - pal[None, None, :, :]
    dist2 = np.sum(diff * diff, axis=3)
    idx = np.argmin(dist2, axis=2)
    rgb_u8 = pal[idx].astype(np.uint8)

    if alpha_mask is None:
        return Image.fromarray(rgb_u8, mode='RGB')

    alpha_u8 = np.where(alpha_mask >= 0.5, 255, 0).astype(np.uint8)
    rgba = np.dstack([rgb_u8, alpha_u8])
    return Image.fromarray(rgba, mode='RGBA')


def make_flesh_floor(size=128):
    """Gastric mucosa (rugae folds + blood capillaries) in deep crimson PS1 palette."""
    wx = fbm_periodic(size, base_period=4, octaves=3, seed=101) - 0.5
    wy = fbm_periodic(size, base_period=4, octaves=3, seed=202) - 0.5

    yy, xx = np.meshgrid(
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        indexing='ij'
    )
    u = xx + wx * 0.35
    v = yy + wy * 0.35
    folds = 0.5 + 0.5 * np.sin((u * 3.0 + v * 2.0) * 2.0 * math.pi)
    folds2 = 0.5 + 0.5 * np.cos((u * 2.0 - v * 4.0) * 2.0 * math.pi + wx * 4.0)
    micro = fbm_periodic(size, base_period=8, octaves=3, seed=303)

    _, edge = voronoi_periodic(size, num_points=22, seed=404)
    veins = np.clip(1.0 - edge * 24.0, 0.0, 1.0) ** 2.2

    height = folds * 0.45 + folds2 * 0.30 + micro * 0.25
    height = np.clip(height - veins * 0.35, 0.0, 1.0)
    highlight = np.clip((height - 0.72) / 0.28, 0.0, 1.0)

    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = 38 + height * 165 + highlight * 45 - veins * 20
    rgb[:, :, 1] = 7 + height * 48 + highlight * 55 - veins * 10
    rgb[:, :, 2] = 12 + height * 45 + highlight * 50

    palette = [
        (22, 4, 8),
        (44, 7, 13),
        (74, 12, 20),
        (108, 18, 28),
        (142, 28, 36),
        (172, 46, 52),
        (198, 76, 76),
        (222, 118, 112),
        (56, 22, 34),
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=24.0)


def make_flesh_wall(size=128):
    """Muscular peristaltic ribbed stomach walls + necrotic purple crevices."""
    wx = fbm_periodic(size, base_period=4, octaves=3, seed=511) - 0.5
    wy = fbm_periodic(size, base_period=4, octaves=3, seed=612) - 0.5

    yy, xx = np.meshgrid(
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        indexing='ij'
    )
    rib = 0.5 + 0.5 * np.sin((yy * 5.0 + wy * 0.45 + np.sin(xx * 2 * math.pi) * 0.08) * 2.0 * math.pi)
    fiber = 0.5 + 0.5 * np.sin((xx * 10.0 + wx * 0.6) * 2.0 * math.pi)
    _, edge = voronoi_periodic(size, num_points=28, seed=713)
    deep_cracks = np.clip(1.0 - edge * 18.0, 0.0, 1.0) ** 1.8

    bump = rib * 0.60 + fiber * 0.18 + fbm_periodic(size, base_period=8, octaves=3, seed=814) * 0.22
    bump = np.clip(bump - deep_cracks * 0.42, 0.0, 1.0)
    wet = np.clip((bump - 0.78) / 0.22, 0.0, 1.0)

    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = 28 + bump * 170 + wet * 40
    rgb[:, :, 1] = 10 + bump * 38 + wet * 48
    rgb[:, :, 2] = 18 + bump * 34 + wet * 40

    palette = [
        (18, 6, 12),
        (36, 14, 22),
        (58, 12, 18),
        (92, 16, 24),
        (132, 24, 30),
        (168, 38, 42),
        (196, 68, 62),
        (220, 108, 96),
        (68, 36, 48),
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=26.0)


def make_gastric_acid(size=128):
    """Toxic fluorescent green swirling & bubbling gastric acid."""
    wx = fbm_periodic(size, base_period=4, octaves=3, seed=905) - 0.5
    wy = fbm_periodic(size, base_period=4, octaves=3, seed=906) - 0.5
    swirl = fbm_periodic(size, base_period=3, octaves=4, seed=901)
    swirl2 = fbm_periodic(size, base_period=6, octaves=3, seed=902)

    _, edge_a = voronoi_periodic(size, num_points=24, seed=903)
    f1_b, _ = voronoi_periodic(size, num_points=46, seed=904)

    foam_web = np.clip(1.0 - edge_a * 16.0, 0.0, 1.0) ** 1.6
    bubbles = np.clip(1.0 - (f1_b + np.abs(wx) * 0.04) * 18.0, 0.0, 1.0) ** 2.2
    bright_blobs = np.clip((swirl2 - 0.55) * 2.5, 0.0, 1.0)

    flow = np.clip(swirl * 0.55 + swirl2 * 0.45 + wx * 0.25 + wy * 0.25, 0.0, 1.0)
    intensity = np.clip(flow * 0.72 + foam_web * 0.40 + bubbles * 0.45 + bright_blobs * 0.25, 0.0, 1.0)

    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = 6 + intensity * 140 + bubbles * 50
    rgb[:, :, 1] = 44 + intensity * 211
    rgb[:, :, 2] = 6 + intensity * 40 + bubbles * 35

    palette = [
        (10, 44, 10),
        (14, 78, 14),
        (20, 124, 16),
        (36, 178, 20),
        (64, 226, 28),
        (112, 250, 52),
        (168, 255, 98),
        (216, 255, 168),
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=22.0)


def make_cartilage_bridge(size=128):
    """Fibrous stomach cartilage & tendon bundles (anatomical, NO wood/skeletons)."""
    yy, xx = np.meshgrid(
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        indexing='ij'
    )
    noise = fbm_periodic(size, base_period=6, octaves=3, seed=1101)
    strands = 0.5 + 0.5 * np.sin((xx * 12.0 + (noise - 0.5) * 1.4) * 2.0 * math.pi)
    cross_bands = 0.5 + 0.5 * np.sin((yy * 4.0 + noise * 0.5) * 2.0 * math.pi)

    val = np.clip(strands * 0.55 + noise * 0.30 + cross_bands * 0.15, 0.0, 1.0)
    _, edge = voronoi_periodic(size, num_points=14, seed=1102)
    blood_stain = np.clip(1.0 - edge * 22.0, 0.0, 1.0) * 0.45

    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = 105 + val * 125 - blood_stain * 25
    rgb[:, :, 1] = 62 + val * 140 - blood_stain * 55
    rgb[:, :, 2] = 58 + val * 120 - blood_stain * 50

    palette = [
        (78, 22, 26),
        (116, 48, 46),
        (146, 86, 76),
        (176, 126, 108),
        (202, 166, 142),
        (224, 198, 170),
        (238, 222, 198),
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=20.0)


def make_hell_organs(size=128):
    """Dark calcified/scarred stomach tissue with inflamed bloody venous fissures (no eyes)."""
    n1 = fbm_periodic(size, base_period=4, octaves=4, seed=1201)
    n2 = fbm_periodic(size, base_period=8, octaves=3, seed=1205)
    _, edge = voronoi_periodic(size, num_points=22, seed=1202)
    _, edge2 = voronoi_periodic(size, num_points=11, seed=1203)

    fissures = np.clip(1.0 - edge * 20.0, 0.0, 1.0) ** 1.8
    deep_veins = np.clip(1.0 - edge2 * 26.0, 0.0, 1.0) ** 2.0
    plate = np.clip(n1 * 0.65 + n2 * 0.35 - fissures * 0.4, 0.0, 1.0)

    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = 24 + plate * 68 + fissures * 115 + deep_veins * 70
    rgb[:, :, 1] = 14 + plate * 38 + fissures * 18 + deep_veins * 15
    rgb[:, :, 2] = 22 + plate * 52 + fissures * 16

    palette = [
        (16, 10, 16),
        (34, 20, 30),
        (54, 34, 44),
        (78, 48, 56),
        (106, 26, 28),
        (148, 34, 32),
        (192, 54, 40),
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=22.0)


def _shade_blood_streams(density, highlight_boost=False):
    """Shared PS1 Bayer shading + wet crimson highlight pass for wall blood textures."""
    alpha_mask = (density > 0.34).astype(np.float32)
    thickness = np.clip((density - 0.34) / 0.66, 0.0, 1.0)

    if highlight_boost:
        spec = np.clip((thickness - 0.62) / 0.38, 0.0, 1.0)
        rgb = np.zeros((*density.shape, 3), dtype=np.float32)
        rgb[:, :, 0] = 65 + thickness * 155 + spec * 35
        rgb[:, :, 1] = 4 + thickness * 24 + spec * 90
        rgb[:, :, 2] = 8 + thickness * 22 + spec * 85
        palette = [
            (48, 3, 7),
            (98, 6, 12),
            (148, 12, 18),
            (198, 22, 26),
            (235, 58, 54),
            (255, 130, 122),
        ]
        dither = 16.0
    else:
        rolled_hi = np.roll(thickness, shift=(1, 1), axis=(0, 1))
        ridge = np.clip((thickness - rolled_hi * 0.45) * 1.4, 0.0, 1.0)
        spec = np.clip((thickness - 0.62) / 0.38, 0.0, 1.0) * (0.55 + 0.45 * ridge)
        rgb = np.zeros((*density.shape, 3), dtype=np.float32)
        rgb[:, :, 0] = 52 + thickness * 145 + spec * 36
        rgb[:, :, 1] = 3 + thickness * 15 + spec * 44
        rgb[:, :, 2] = 6 + thickness * 13 + spec * 38
        palette = [
            (36, 2, 5),
            (78, 5, 9),
            (126, 9, 14),
            (174, 16, 20),
            (214, 34, 32),
            (238, 72, 66),
        ]
        dither = 14.0

    return apply_bayer_palette(rgb, palette, dither_strength=dither, alpha_mask=alpha_mask)


def make_blood_drips(size=128):
    """
    Classic PS1 beaded blood rivulets and falling droplets arranged cleanly inside
    4 vertical lanes (with transparent lane borders) so each lane can scroll downward
    at independent alternating speeds without tearing.
    """
    yy, xx = np.meshgrid(
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        indexing='ij'
    )
    rng = np.random.default_rng(1337)
    noise_x = fbm_periodic(size, base_period=8, octaves=3, seed=1301) - 0.5
    noise_y = fbm_periodic(size, base_period=4, octaves=3, seed=1302)

    n_lanes = 4
    lane_w = 1.0 / float(n_lanes)
    density = np.zeros((size, size), dtype=np.float32)

    for lane in range(n_lanes):
        u_min = lane * lane_w + 0.032
        u_max = (lane + 1) * lane_w - 0.032
        in_lane = (xx >= lane * lane_w + 0.015) & (xx <= (lane + 1) * lane_w - 0.015)

        # 2 slender beaded rivulets inside each lane
        for sub in range(2):
            cx = u_min + (sub + 0.35 + rng.uniform(-0.15, 0.15)) * (u_max - u_min) * 0.55
            w = rng.uniform(0.014, 0.026)
            freq_y = int(rng.integers(1, 4))
            phase_y = rng.uniform(0.0, 2.0 * math.pi)
            dx = np.abs(xx - cx + noise_x * 0.016)
            bead = 0.5 + 0.5 * np.sin(yy * freq_y * 2.0 * math.pi + phase_y)
            stream_w = w * (0.45 + 0.85 * (bead ** 1.6))
            gap = 0.55 + 0.45 * np.sin(yy * 2.0 * math.pi + (lane * 2 + sub) * 1.7 + noise_y * 2.0)
            contrib = np.where(in_lane, np.clip(1.0 - (dx / stream_w) ** 2, 0.0, 1.0) * gap, 0.0)
            density = np.maximum(density, contrib)

        # 2-3 detached falling drops strictly within the lane's safe inner margin
        n_drops = 3 if lane % 2 == 0 else 2
        for _ in range(n_drops):
            cx = rng.uniform(u_min + 0.015, u_max - 0.015)
            cy = rng.uniform(0.0, 1.0)
            rx = rng.uniform(0.013, 0.020)
            ry = rng.uniform(0.032, 0.056)
            dx = np.abs(xx - cx) / rx
            dy = np.abs(yy - cy)
            dy = np.minimum(dy, 1.0 - dy) / ry
            drop = np.where(in_lane, np.clip(1.0 - (dx * dx + dy * dy), 0.0, 1.0), 0.0)
            density = np.maximum(density, drop)

    return _shade_blood_streams(density, highlight_boost=True)


def make_blood_waves(size=128):
    """
    Sparse, sinuous wavy blood streams arranged cleanly inside 3 vertical lanes
    (3 streams per tile with wide spacing to avoid over-covering the stomach walls).
    yy=0 holds upper trails and yy=1 holds bottom rounded drip bulbs.
    """
    yy, xx = np.meshgrid(
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        indexing='ij'
    )
    rng = np.random.default_rng(2027)
    noise_x = fbm_periodic(size, base_period=6, octaves=3, seed=1301) - 0.5
    noise_w = fbm_periodic(size, base_period=4, octaves=2, seed=1302) - 0.5

    n_lanes = 3
    lane_w = 1.0 / float(n_lanes)
    density = np.zeros((size, size), dtype=np.float32)

    for k in range(n_lanes):
        cx = (k + 0.5) * lane_w + rng.uniform(-0.025, 0.025)
        in_lane = (xx >= k * lane_w + 0.025) & (xx <= (k + 1) * lane_w - 0.025)

        wave_freq = int(rng.choice([1, 2]))
        wave_amp = rng.uniform(0.026, 0.044)
        wave_phase = rng.uniform(0.0, 2.0 * math.pi)
        base_w = rng.uniform(0.022, 0.034)

        center_x = cx + wave_amp * np.sin(yy * wave_freq * 2.0 * math.pi + wave_phase) + noise_x * 0.016
        dx = np.abs(xx - center_x)

        surge_freq = int(rng.choice([1, 2]))
        surge_phase = rng.uniform(0.0, 2.0 * math.pi)
        surge = 0.5 + 0.5 * np.sin(yy * surge_freq * 2.0 * math.pi + surge_phase)

        if k in (0, 2):
            w = base_w * (0.48 + 0.72 * (surge ** 1.35) + noise_w * 0.25)
            contrib = np.clip(1.0 - (dx / np.maximum(w, 0.008)) ** 2, 0.0, 1.0)
        else:
            y_head = rng.uniform(0.55, 0.90)
            dy_up = np.mod(y_head - yy, 1.0)
            tail_len = rng.uniform(0.58, 0.80)
            in_run = (dy_up <= tail_len)
            s = np.clip(dy_up / tail_len, 0.0, 1.0)
            bulb = np.sqrt(np.clip(1.0 - ((np.clip(0.11 - dy_up, 0.0, 0.11)) / 0.11) ** 2, 0.0, 1.0))
            tip_round = np.where(dy_up < 0.07, np.sqrt(np.clip(dy_up / 0.07, 0.0, 1.0)), 1.0)
            taper = (1.30 * np.exp(-s * 1.9) + 0.28 * (1.0 - s)) * tip_round * bulb
            fade_tail = np.clip((1.0 - s) / 0.18, 0.0, 1.0)
            w = np.maximum(base_w * taper, 0.004)
            contrib = np.where(in_run, np.clip(1.0 - (dx / w) ** 2, 0.0, 1.0) * fade_tail, 0.0)

        density = np.maximum(density, np.where(in_lane, contrib, 0.0))

    return _shade_blood_streams(density, highlight_boost=False)


def make_teeth(size=128):
    """
    Weathered, cracked ivory-yellow horror teeth with bloody root stains (yy=0 root, yy=1 biting edge).
    PS1 Bayer-dithered RGB texture matching the fleshy doorway tooth style.
    """
    yy, xx = np.meshgrid(
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        indexing='ij'
    )
    fbm_low = fbm_periodic(size, base_period=4, octaves=4, seed=1501)
    fbm_fine = fbm_periodic(size, base_period=8, octaves=3, seed=1502)

    # Vertical enamel streaks
    enamel_rib = 0.5 + 0.5 * np.sin((xx * 18.0 + (fbm_low - 0.5) * 2.2) * 2.0 * math.pi)
    base_ivory = np.clip(0.55 + 0.30 * fbm_low + 0.15 * enamel_rib - 0.18 * (1.0 - yy), 0.0, 1.0)

    # Deep vertical fractures near the bottom biting tip
    crack_mask = np.zeros((size, size), dtype=np.float32)
    rng = np.random.default_rng(1510)
    for _ in range(7):
        cx = rng.uniform(0.08, 0.92)
        y_start = rng.uniform(0.42, 0.72)
        in_y = np.clip((yy - y_start) / (1.0 - y_start), 0.0, 1.0)
        wiggle = (fbm_fine - 0.5) * 0.035 + 0.012 * np.sin(yy * 14.0)
        dx = np.abs(xx - (cx + wiggle))
        dx = np.minimum(dx, 1.0 - dx)
        c_w = 0.008 + 0.014 * (in_y ** 1.4)
        crack = np.where(yy >= y_start, np.clip(1.0 - dx / c_w, 0.0, 1.0) * (in_y ** 0.4), 0.0)
        crack_mask = np.maximum(crack_mask, crack)

    # Bloody gum seepage near top root (yy near 0) and mottled decay patches
    gum_blood = np.clip((0.30 - yy + (fbm_low - 0.5) * 0.16) / 0.25, 0.0, 1.0) ** 1.25
    rot_spot = np.clip((0.68 - fbm_low) * 2.0, 0.0, 1.0) * 0.30

    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = 140 + base_ivory * 94 - crack_mask * 115 - rot_spot * 45
    rgb[:, :, 1] = 86 + base_ivory * 116 - crack_mask * 95 - rot_spot * 55
    rgb[:, :, 2] = 46 + base_ivory * 94 - crack_mask * 65 - rot_spot * 35

    # Blend deep crimson toward tooth root
    rgb[:, :, 0] = rgb[:, :, 0] * (1.0 - gum_blood * 0.55) + 132 * (gum_blood * 0.55)
    rgb[:, :, 1] = rgb[:, :, 1] * (1.0 - gum_blood * 0.85) + 22 * (gum_blood * 0.85)
    rgb[:, :, 2] = rgb[:, :, 2] * (1.0 - gum_blood * 0.82) + 20 * (gum_blood * 0.82)

    palette = [
        (28, 12, 12),    # deep tooth crack / cavity
        (68, 22, 20),    # clotted root stain
        (114, 34, 26),   # inflamed gum smear
        (118, 82, 48),   # stained yellow-brown plaque
        (158, 120, 74),  # weathered ochre enamel
        (194, 158, 106), # dirty bone yellow
        (222, 196, 146), # worn ivory tooth highlight
        (240, 224, 186), # pale crown gleam
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=18.0)


def make_eyeball(size=128):
    """
    Bloodshot sclera with crimson veins, striated purple iris, black round pupil and crisp wet gleam
    matching the eye reference image in PS1 Bayer-dithered aesthetic.
    """
    yy, xx = np.meshgrid(
        np.linspace(-0.5, 0.5, size, endpoint=False, dtype=np.float32) + 0.5 / size,
        np.linspace(-0.5, 0.5, size, endpoint=False, dtype=np.float32) + 0.5 / size,
        indexing='ij'
    )
    rr = np.sqrt(xx * xx + yy * yy)
    ang = np.arctan2(yy, xx)
    noise_a = fbm_periodic(size, base_period=4, octaves=4, seed=1601) - 0.5
    noise_b = fbm_periodic(size, base_period=8, octaves=3, seed=1602) - 0.5

    r_pupil = 0.115
    r_iris = 0.265

    # Sclera base shading (bright near iris, subtly shadowed toward outer equator)
    sclera_shade = np.clip(1.0 - np.clip((rr - 0.24) / 0.26, 0.0, 1.0) * 0.34 + noise_a * 0.08, 0.0, 1.0)
    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = 178 + sclera_shade * 64
    rgb[:, :, 1] = 162 + sclera_shade * 70
    rgb[:, :, 2] = 166 + sclera_shade * 68

    # Branching bloodshot crimson veins radiating inward from outer sclera
    veins = np.zeros((size, size), dtype=np.float32)
    rng = np.random.default_rng(1620)
    for k in range(16):
        base_a = (k / 16.0) * 2.0 * math.pi + rng.uniform(-0.15, 0.15)
        r_start = rng.uniform(0.245, 0.31)
        wobble = noise_a * 0.35 + noise_b * 0.20 + 0.10 * np.sin(rr * 28.0 + k)
        d_ang = np.abs(np.arctan2(np.sin(ang - (base_a + wobble)), np.cos(ang - (base_a + wobble))))
        arc_dist = rr * d_ang
        in_vein = (rr >= r_start)
        main_w = 0.0065 + 0.004 * np.clip((rr - r_start) / 0.22, 0.0, 1.0)
        v1 = np.where(in_vein, np.clip(1.0 - arc_dist / main_w, 0.0, 1.0), 0.0)
        veins = np.maximum(veins, v1)

        # Secondary capillary fork around mid-sclera
        fork_a = base_a + rng.choice([-1.0, 1.0]) * rng.uniform(0.18, 0.34)
        fork_start = rng.uniform(0.32, 0.39)
        d_ang2 = np.abs(np.arctan2(np.sin(ang - (fork_a + wobble * 1.15)), np.cos(ang - (fork_a + wobble * 1.15))))
        v2 = np.where(rr >= fork_start, np.clip(1.0 - (rr * d_ang2) / 0.005, 0.0, 1.0), 0.0)
        veins = np.maximum(veins, v2 * 0.85)

    # Apply veins onto sclera
    vein_red = np.clip(veins * 0.95, 0.0, 1.0)
    rgb[:, :, 0] = rgb[:, :, 0] * (1.0 - vein_red * 0.22) + 195 * (vein_red * 0.22)
    rgb[:, :, 1] = rgb[:, :, 1] * (1.0 - vein_red * 0.88)
    rgb[:, :, 2] = rgb[:, :, 2] * (1.0 - vein_red * 0.86)

    # Vivid purple-magenta striated iris
    in_iris = (rr <= r_iris) & (rr >= r_pupil)
    t_iris = np.clip((rr - r_pupil) / (r_iris - r_pupil), 0.0, 1.0)
    fibers = 0.5 + 0.5 * np.sin(ang * 34.0 + noise_a * 7.0 + rr * 18.0)
    fibers2 = 0.5 + 0.5 * np.cos(ang * 56.0 - noise_b * 8.0)
    iris_val = np.clip(0.35 + 0.45 * fibers + 0.20 * fibers2, 0.0, 1.0)
    # Dark outer limbal ring + subtle bright collar around pupil
    limbus = np.clip((t_iris - 0.72) / 0.28, 0.0, 1.0) ** 1.6
    inner_glow = np.clip(1.0 - t_iris / 0.35, 0.0, 1.0) * 0.25

    iris_r = 92 + iris_val * 112 + inner_glow * 40 - limbus * 68
    iris_g = 24 + iris_val * 68 + inner_glow * 30 - limbus * 18
    iris_b = 118 + iris_val * 118 + inner_glow * 45 - limbus * 80

    # Faint crimson capillaries creeping slightly into outer purple iris
    iris_blood = veins * np.clip((t_iris - 0.55) / 0.45, 0.0, 1.0)
    iris_r = iris_r * (1.0 - iris_blood * 0.4) + 185 * (iris_blood * 0.4)
    iris_g = iris_g * (1.0 - iris_blood * 0.8)
    iris_b = iris_b * (1.0 - iris_blood * 0.8)

    for ch, val in enumerate((iris_r, iris_g, iris_b)):
        rgb[:, :, ch] = np.where(in_iris, val, rgb[:, :, ch])

    # Deep black round pupil
    in_pupil = rr < r_pupil
    rgb[:, :, 0] = np.where(in_pupil, 10.0, rgb[:, :, 0])
    rgb[:, :, 1] = np.where(in_pupil, 6.0, rgb[:, :, 1])
    rgb[:, :, 2] = np.where(in_pupil, 14.0, rgb[:, :, 2])

    # Crisp wet corneal specular glint (top-right offset like Image 2)
    gx, gy = xx - 0.085, yy - (-0.085)
    glint = ((gx * gx + gy * gy) < (0.045 * 0.045)) | (((xx - 0.13) ** 2 + (yy + 0.035) ** 2) < (0.020 ** 2))
    rgb[:, :, 0] = np.where(glint, 250.0, rgb[:, :, 0])
    rgb[:, :, 1] = np.where(glint, 240.0, rgb[:, :, 1])
    rgb[:, :, 2] = np.where(glint, 252.0, rgb[:, :, 2])

    palette = [
        (8, 6, 12),      # pitch pupil black
        (40, 12, 54),    # deep violet limbus
        (84, 24, 114),   # dark purple iris
        (134, 48, 168),  # vivid violet iris
        (184, 92, 212),  # bright lavender fiber highlight
        (110, 12, 16),   # clotted sclera vein
        (192, 26, 30),   # bright crimson capillary
        (158, 138, 142), # shaded outer sclera
        (206, 192, 194), # mid sclera
        (244, 236, 238), # crisp sclera / eye gleam
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=14.0)


def make_pixel_particle(size=16):
    """Crisp 16x16 white pixel-art droplet sprite for Unity Particle Systems (tinted per emitter)."""
    yy, xx = np.meshgrid(
        np.linspace(-1, 1, size, dtype=np.float32),
        np.linspace(-1, 1, size, dtype=np.float32),
        indexing='ij'
    )
    r = np.sqrt(xx * xx + yy * yy)
    alpha = np.where(r <= 0.88, 255, 0).astype(np.uint8)
    bright = np.where(r <= 0.48, 255, 205).astype(np.uint8)
    rgba = np.stack([bright, bright, bright, alpha], axis=-1)
    return Image.fromarray(rgba, mode='RGBA')


def make_pustule(size=128):
    """
    Swollen, inflamed ulcerated stomach pustules / toxic blisters clustered on dark crimson flesh.
    Centers of blisters glow with sickly amber/toxic yellow-green pus surrounded by crimson-violet rims.
    """
    warp = fbm_periodic(size, base_period=4, octaves=4, seed=1701)
    fine = fbm_periodic(size, base_period=8, octaves=3, seed=1702)
    f1_a, edge_a = voronoi_periodic(size, num_points=16, seed=1703)
    f1_b, edge_b = voronoi_periodic(size, num_points=30, seed=1704)

    # Organic blister domes (primary swollen pustules + smaller secondary blisters)
    d_a = np.maximum(0.0, f1_a + (warp - 0.5) * 0.032)
    d_b = np.maximum(0.0, f1_b + (fine - 0.5) * 0.022)
    dome_a = np.clip(1.0 - d_a / 0.145, 0.0, 1.0) ** 1.25
    dome_b = (np.clip(1.0 - d_b / 0.095, 0.0, 1.0) ** 1.35) * 0.82
    blister = np.maximum(dome_a, dome_b)

    # Deep ulcerated creases between blisters
    crevice = np.clip(1.0 - np.minimum(edge_a * 18.0, edge_b * 22.0), 0.0, 1.0) ** 1.7
    rim_ring = np.clip(1.0 - np.abs(blister - 0.36) / 0.22, 0.0, 1.0)
    pus_core = np.clip((blister - 0.42) / 0.58, 0.0, 1.0)
    pus_peak = np.clip((blister - 0.74) / 0.26, 0.0, 1.0)
    toxic_shift = np.clip((warp - 0.38) * 1.9, 0.0, 1.0) * (1.0 - pus_peak * 0.65)

    # Base inflamed crimson-violet mucosa
    flesh_val = np.clip(0.25 + warp * 0.45 + fine * 0.30 - crevice * 0.45, 0.0, 1.0)
    r = 38.0 + flesh_val * 68.0 + rim_ring * 52.0
    g = 9.0 + flesh_val * 16.0 + rim_ring * 12.0
    b = 16.0 + flesh_val * 20.0 + rim_ring * 14.0

    # Blend in swollen toxic yellow-green / sickly amber pus centers
    pus_r = 198.0 - toxic_shift * 32.0 + pus_peak * 47.0
    pus_g = 154.0 + toxic_shift * 44.0 + pus_peak * 66.0
    pus_b = 42.0 + toxic_shift * 8.0 + pus_peak * 40.0

    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = r * (1.0 - pus_core) + pus_r * pus_core
    rgb[:, :, 1] = g * (1.0 - pus_core) + pus_g * pus_core
    rgb[:, :, 2] = b * (1.0 - pus_core) + pus_b * pus_core

    palette = [
        (24, 6, 12),     # deep necrotic fissure
        (42, 10, 18),    # dark ulcerated crease
        (88, 16, 26),    # inflamed crimson-violet flesh
        (142, 28, 36),   # swollen crimson blister rim
        (176, 56, 40),   # irritated orange-red collar
        (134, 146, 36),  # dark olive-pus margin
        (168, 198, 48),  # toxic yellow-green pus
        (212, 162, 42),  # sickly amber pus
        (245, 220, 82),  # glowing pustule apex
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=20.0)


def make_digested_calcified(size=128):
    """
    Dense calcified stomach nodules / chyme-corroded fibrous cartilage mass partially etched
    by gastric acid (strictly NO skeletons or bones). Used for submerged stepping stones & islands.
    """
    n_coarse = fbm_periodic(size, base_period=4, octaves=4, seed=1801)
    n_fine = fbm_periodic(size, base_period=8, octaves=3, seed=1802)
    wx = fbm_periodic(size, base_period=4, octaves=3, seed=1803) - 0.5
    wy = fbm_periodic(size, base_period=4, octaves=3, seed=1806) - 0.5

    yy, xx = np.meshgrid(
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        indexing='ij'
    )
    # Calcified nodular plates + chyme-corroded fibrous cartilage grain
    f1_nod, edge_nod = voronoi_periodic(size, num_points=14, seed=1804)
    f1_pit, _ = voronoi_periodic(size, num_points=42, seed=1805)
    f1_pit2, edge_acid = voronoi_periodic(size, num_points=19, seed=1807)

    u = xx + wx * 0.32
    v = yy + wy * 0.32
    fibers = 0.5 + 0.5 * np.sin((u * 6.0 + v * 4.0) * 2.0 * math.pi)
    fibers2 = 0.5 + 0.5 * np.cos((u * 3.0 - v * 7.0) * 2.0 * math.pi + n_coarse * 3.5)
    nodule = np.clip(1.0 - (f1_nod + wx * 0.04) * 3.8, 0.0, 1.0)
    tissue = np.clip(n_coarse * 0.42 + n_fine * 0.22 + fibers * 0.20 + fibers2 * 0.16 + nodule * 0.18 - 0.08, 0.0, 1.0)

    # Eroded Dissolution Pits (irregular porous cavities chewed by stomach enzymes)
    pit1 = np.clip(1.0 - (f1_pit + (n_fine - 0.5) * 0.028) / (0.042 + 0.025 * (1.0 - n_coarse)), 0.0, 1.0) ** 1.6
    pit2 = np.clip(1.0 - (f1_pit2 + (n_coarse - 0.5) * 0.035) / 0.052, 0.0, 1.0) ** 1.8
    pits = np.clip(np.maximum(pit1, pit2 * 0.85), 0.0, 1.0)

    # Organic toxic green gastric acid veins and etched crevice stains (broken up by noise)
    vein_warp = edge_acid + (n_fine - 0.5) * 0.026 + (wx * 0.018)
    acid_vein = (np.clip(1.0 - vein_warp * 24.0, 0.0, 1.0) ** 1.6) * np.clip((n_coarse + 0.15) * 1.2, 0.0, 1.0)
    acid_crevice = (np.clip(1.0 - (edge_nod + wy * 0.022) * 18.0, 0.0, 1.0) ** 1.5) * np.clip((0.68 - n_coarse) * 2.0, 0.0, 1.0)
    pit_rim_acid = np.clip(1.0 - np.abs(pits - 0.32) / 0.20, 0.0, 1.0) * np.clip((n_fine - 0.42) * 2.2, 0.0, 1.0)
    acid_mask = np.clip(np.maximum(np.maximum(acid_vein, acid_crevice), pit_rim_acid * 0.75) * (1.0 - np.clip((pits - 0.55) * 2.2, 0.0, 1.0)), 0.0, 1.0)
    acid_bright = np.clip((acid_mask - 0.45) / 0.55, 0.0, 1.0)

    # Pale ochre / ash-grey calcified mass base
    height = np.clip(tissue * (1.0 - pits * 0.82), 0.0, 1.0)
    highlight = np.clip((height - 0.68) / 0.32, 0.0, 1.0)
    r = 72.0 + height * 118.0 + highlight * 22.0 - pits * 48.0
    g = 62.0 + height * 112.0 + highlight * 22.0 - pits * 42.0
    b = 50.0 + height * 96.0 + highlight * 20.0 - pits * 32.0

    # Etch toxic green gastric acid into crevices
    acid_r = 58.0 + acid_bright * 38.0
    acid_g = 134.0 + acid_bright * 54.0
    acid_b = 32.0 + acid_bright * 10.0

    rgb = np.zeros((size, size, 3), dtype=np.float32)
    rgb[:, :, 0] = r * (1.0 - acid_mask) + acid_r * acid_mask
    rgb[:, :, 1] = g * (1.0 - acid_mask) + acid_g * acid_mask
    rgb[:, :, 2] = b * (1.0 - acid_mask) + acid_b * acid_mask

    palette = [
        (28, 22, 20),     # deep enzyme-dissolved pore
        (46, 38, 34),     # dark eroded pits
        (76, 68, 56),     # shadowed chyme-corroded cartilage
        (108, 96, 78),    # weathered ash-ochre calcified tissue
        (148, 136, 112),  # mid porous calcified nodule
        (186, 176, 148),  # pale ochre/ash-grey calcified surface
        (210, 200, 172),  # mineral-encrusted cartilage highlight
        (58, 134, 32),    # dark toxic green acid-etched stain
        (96, 188, 42),    # bright gastric acid vein
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=20.0)


def make_tenia_proglottid(size=128):
    """Pale fleshy-ochre tapeworm proglottid skin with crimson veins, mucosal creases, and bile borders."""
    wx = fbm_periodic(size, base_period=4, octaves=3, seed=9101) - 0.5
    wy = fbm_periodic(size, base_period=4, octaves=3, seed=9102) - 0.5
    n_coarse = fbm_periodic(size, base_period=4, octaves=3, seed=9103)
    n_fine = fbm_periodic(size, base_period=12, octaves=3, seed=9104)

    yy, xx = np.meshgrid(
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        np.linspace(0, 1, size, endpoint=False, dtype=np.float32),
        indexing='ij'
    )
    u = xx + wx * 0.14
    v = yy + wy * 0.10

    # Transverse mucosal creases and subtle longitudinal ridges
    crease_band = np.clip(1.0 - np.minimum(yy, 1.0 - yy) * 5.0, 0.0, 1.0)
    micro_creases = 0.5 + 0.5 * np.sin((v * 5.0 + wx * 0.8) * 2.0 * math.pi)
    mucosal_fold = np.clip(crease_band * 0.72 + (1.0 - micro_creases) * 0.25 * (0.4 + 0.6 * n_coarse), 0.0, 1.0)

    # Branching crimson veins via warped Voronoi edges
    _, edge1 = voronoi_periodic(size, num_points=14, seed=9105)
    _, edge2 = voronoi_periodic(size, num_points=28, seed=9106)
    v_warp1 = np.clip(edge1 + (n_fine - 0.5) * 0.032 + wx * 0.020, 0.0, 1.0)
    v_warp2 = np.clip(edge2 + (n_coarse - 0.5) * 0.025 + wy * 0.018, 0.0, 1.0)
    veins = np.clip(
        np.maximum(
            (np.clip(1.0 - v_warp1 * 26.0, 0.0, 1.0) ** 1.7),
            (np.clip(1.0 - v_warp2 * 32.0, 0.0, 1.0) ** 2.0) * 0.65
        ) * np.clip(0.55 + n_coarse * 0.7, 0.0, 1.0),
        0.0, 1.0
    )

    # Subtle greenish-bile tint near segment borders
    bile_mask = np.clip(crease_band * (0.45 + 0.75 * n_coarse) * (1.0 - veins * 0.6), 0.0, 0.85)

    height = np.clip(0.45 + 0.35 * n_coarse + 0.20 * n_fine - mucosal_fold * 0.55, 0.0, 1.0)
    highlight = np.clip((height - 0.68) / 0.32, 0.0, 1.0)

    r = 128.0 + height * 92.0 + highlight * 22.0 - mucosal_fold * 52.0
    g = 86.0 + height * 88.0 + highlight * 24.0 - mucosal_fold * 46.0
    b = 72.0 + height * 72.0 + highlight * 22.0 - mucosal_fold * 38.0

    # Dark crimson capillary veins
    r = r * (1.0 - veins * 0.55) + 96.0 * (veins * 0.55)
    g = g * (1.0 - veins * 0.82) + 20.0 * (veins * 0.82)
    b = b * (1.0 - veins * 0.78) + 26.0 * (veins * 0.78)

    # Subtle greenish-bile border tint
    r = r * (1.0 - bile_mask * 0.45) + 104.0 * (bile_mask * 0.45)
    g = g * (1.0 - bile_mask * 0.25) + 138.0 * (bile_mask * 0.25)
    b = b * (1.0 - bile_mask * 0.55) + 54.0 * (bile_mask * 0.55)

    rgb = np.stack([r, g, b], axis=-1)
    palette = [
        (56, 20, 24),     # deep mucosal crease
        (92, 22, 30),     # dark crimson vein
        (128, 40, 44),    # mid blood capillary
        (146, 98, 86),    # shadowed fleshy-ochre
        (178, 134, 114),  # mid tapeworm flesh
        (206, 168, 138),  # pale fleshy-ochre
        (230, 198, 168),  # slimy specular highlight
        (92, 116, 48),    # dark bile-stained border
        (124, 148, 66),   # greenish-bile tint
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=20.0)


def generate_all_textures(out_dir):
    os.makedirs(out_dir, exist_ok=True)
    specs = {
        "tex_flesh_floor.png": make_flesh_floor(128),
        "tex_flesh_wall.png": make_flesh_wall(128),
        "tex_acid.png": make_gastric_acid(128),
        "tex_cartilage.png": make_cartilage_bridge(128),
        "tex_hell_organs.png": make_hell_organs(128),
        "tex_blood_drips.png": make_blood_drips(128),
        "tex_blood_waves.png": make_blood_waves(128),
        "tex_teeth.png": make_teeth(128),
        "tex_eyeball.png": make_eyeball(128),
        "tex_pixel_particle.png": make_pixel_particle(16),
        "tex_pustule.png": make_pustule(128),
        "tex_digested_calcified.png": make_digested_calcified(128),
        "tex_tenia_proglottid.png": make_tenia_proglottid(128),
    }
    paths = {}
    for name, img in specs.items():
        p = os.path.join(out_dir, name)
        img.save(p, format="PNG")
        paths[name] = p
    return paths


if __name__ == "__main__":
    out = r"C:\Users\user\IdeaProjects\Game final\Assets\PilotChamber\Textures"
    paths = generate_all_textures(out)
    print("Generated textures:", list(paths.keys()))
