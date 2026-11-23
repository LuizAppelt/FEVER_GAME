import os
import sys
import math
import numpy as np
from PIL import Image

PILOT_TOOLS_DIR = os.path.normpath(
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "PilotChamber", "Tools")
)
if PILOT_TOOLS_DIR not in sys.path:
    sys.path.insert(0, PILOT_TOOLS_DIR)

from retro_textures import (
    fbm_periodic,
    voronoi_periodic,
    apply_bayer_palette,
)


def _coords_2d(size):
    """Returns (yy, xx) in [0, 1) of shape (size, size)."""
    c = np.linspace(0.0, 1.0, size, endpoint=False, dtype=np.float32)
    return np.meshgrid(c, c, indexing="ij")


def _opaque_alpha(size):
    """Full opaque alpha mask so apply_bayer_palette outputs 32-bit RGBA PNGs."""
    return np.ones((size, size), dtype=np.float32)


def make_vein_tendon_bridge(size=128):
    """
    1. tex_vein_tendon_bridge.png (128x128 RGBA, seamless):
    Colossal suspension bridge made of woven crimson veins, arterial muscle cords,
    and glossy sinew. Strong directional diagonal/longitudinal striated muscle fibers
    and branching blood-vessel bundles on a dark fleshy/necrotic core with wet
    crimson-red highlights.
    """
    yy, xx = _coords_2d(size)
    wx = fbm_periodic(size, base_period=4, octaves=4, seed=4101) - 0.5
    wy = fbm_periodic(size, base_period=4, octaves=4, seed=4102) - 0.5
    n_med = fbm_periodic(size, base_period=6, octaves=3, seed=4103)
    n_fine = fbm_periodic(size, base_period=12, octaves=3, seed=4104)

    u = xx + wx * 0.065
    v = yy + wy * 0.055

    # 4 primary braided longitudinal arterial muscle cables separated by deep necrotic clefts
    cord_height = np.zeros((size, size), dtype=np.float32)
    cord_spec_ridge = np.zeros((size, size), dtype=np.float32)
    cord_fibers = np.zeros((size, size), dtype=np.float32)
    for k in range(4):
        cx = (k + 0.5) / 4.0 + 0.052 * np.sin((v * 2.0 + k * 0.5) * 2.0 * math.pi)
        dx = ((u - cx + 0.5) % 1.0) - 0.5
        abs_dx = np.abs(dx)
        half_w = 0.098 + (n_med - 0.5) * 0.016
        prof = np.sqrt(np.clip(1.0 - (abs_dx / half_w) ** 2, 0.0, 1.0))
        weave = 0.72 + 0.28 * np.cos((v * 2.0 + k * 0.5) * 2.0 * math.pi)
        # Directional striated muscle myofibrils twisted helically along each cord
        f_strand = 0.5 + 0.5 * np.sin((dx * 36.0 + v * 5.0 + wx * 2.2) * 2.0 * math.pi)
        # Top-left wet specular ridge along the cord curve
        ridge = np.clip(1.0 - np.abs(dx + 0.022) / 0.036, 0.0, 1.0) * prof
        h_k = prof * weave
        better = h_k > cord_height
        cord_height = np.where(better, h_k, cord_height)
        cord_fibers = np.where(better, f_strand, cord_fibers)
        cord_spec_ridge = np.maximum(cord_spec_ridge, ridge * weave)

    # Diagonal woven arterial sinew cross-cables (shallow suspension cable angle)
    d1_raw = (((u * 3.0 + v * 1.0) % 1.0) - 0.5)
    d2_raw = (((u * 3.0 - v * 1.0) % 1.0) - 0.5)
    diag_w = 0.105
    diag_prof1 = np.sqrt(np.clip(1.0 - (np.abs(d1_raw) / diag_w) ** 2, 0.0, 1.0))
    diag_prof2 = np.sqrt(np.clip(1.0 - (np.abs(d2_raw) / diag_w) ** 2, 0.0, 1.0))
    diag_fib1 = 0.5 + 0.5 * np.sin((d1_raw * 10.0 + v * 6.0 + wy * 1.5) * 2.0 * math.pi)
    diag_fib2 = 0.5 + 0.5 * np.sin((d2_raw * 10.0 - v * 6.0 + wx * 1.5) * 2.0 * math.pi)

    # True over/under weave depth between longitudinal bundles and diagonal sinew ropes
    weave_phase = 0.5 + 0.5 * np.sin((u * 4.0 + v * 2.0) * 2.0 * math.pi)
    diag_h = np.maximum(diag_prof1 * (0.65 + 0.35 * weave_phase), diag_prof2 * (0.65 + 0.35 * (1.0 - weave_phase)))
    diag_fib = np.where(diag_prof1 >= diag_prof2, diag_fib1, diag_fib2)

    use_diag = (diag_h * 0.88) > cord_height
    woven_h = np.maximum(cord_height, diag_h * 0.88)
    woven_fibers = np.where(use_diag, diag_fib, cord_fibers)

    # Branching blood-vessel bundles hugging along the muscle cables & spanning necrotic troughs
    v_field1 = fbm_periodic(size, base_period=4, octaves=4, seed=4107)
    v_field2 = fbm_periodic(size, base_period=8, octaves=3, seed=4108)
    artery_main = np.clip(1.0 - np.abs(v_field1 - 0.5 + (u - 0.5) * 0.08) * 22.0, 0.0, 1.0) ** 1.5
    artery_fork = np.clip(1.0 - np.abs(v_field2 - 0.5) * 28.0, 0.0, 1.0) ** 1.8
    _, edge_vein = voronoi_periodic(size, num_points=24, seed=4109)
    vein_web = np.clip(1.0 - (edge_vein + (n_fine - 0.5) * 0.015) * 28.0, 0.0, 1.0) ** 1.8
    veins = np.clip(np.maximum(artery_main, np.maximum(artery_fork * 0.78, vein_web * 0.62)), 0.0, 1.0)

    # Deep dark fleshy/necrotic core inside the inter-cable troughs
    necrotic_trough = np.clip(1.0 - woven_h * 1.45, 0.0, 1.0)
    striation_mod = (woven_fibers - 0.5) * (0.28 * np.clip(woven_h * 1.5, 0.15, 1.0))

    flesh_val = np.clip(
        woven_h * 0.78 + striation_mod + (n_fine - 0.5) * 0.10 - necrotic_trough * 0.42,
        0.0,
        1.0,
    )

    # Wet glossy crimson-red highlights on raised sinew ridges & taut arterial trunks
    sinew_gloss = np.clip((flesh_val - 0.58) / 0.42, 0.0, 1.0) * (0.55 + 0.45 * cord_spec_ridge)
    diag_gloss = np.where(use_diag, np.clip((diag_h - 0.68) / 0.32, 0.0, 1.0) * 0.85, 0.0)
    vein_gloss = np.clip((veins - 0.48) / 0.52, 0.0, 1.0) * (0.45 + 0.55 * woven_h)
    wet_hi = np.clip(np.maximum(np.maximum(sinew_gloss, diag_gloss), vein_gloss), 0.0, 1.0)

    r = 16.0 + flesh_val * 158.0 + veins * 56.0 + wet_hi * 48.0 - necrotic_trough * 10.0
    g = 4.0 + flesh_val * 26.0 + veins * 10.0 + wet_hi * 78.0 - necrotic_trough * 2.0
    b = 8.0 + flesh_val * 22.0 + veins * 10.0 + wet_hi * 66.0

    # Bruised dark necrotic plum shadow in deep recesses between cords (fleshy core, not void)
    nec_p = necrotic_trough * (1.0 - veins * 0.5)
    r = r * (1.0 - nec_p * 0.45) + 38.0 * (nec_p * 0.45)
    g = g * (1.0 - nec_p * 0.45) + 10.0 * (nec_p * 0.45)
    b = b * (1.0 - nec_p * 0.35) + 20.0 * (nec_p * 0.35)

    rgb = np.stack([r, g, b], axis=2)

    palette = [
        (22, 6, 12),      # deep necrotic fleshy crevice
        (40, 10, 18),     # bruised necrotic plum core
        (68, 12, 20),     # dark clotted venous trough
        (98, 15, 22),    # deep striated muscle tone
        (138, 24, 30),   # rich arterial muscle cord
        (178, 38, 40),   # bright crimson muscle / vein
        (212, 62, 56),   # wet glossy arterial highlight
        (234, 106, 90),  # glistening crimson sinew ridge
        (248, 158, 140), # sharp wet tendon specular glint
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=18.0, alpha_mask=_opaque_alpha(size))


def make_membrane_backlit(size=128):
    """
    2. tex_membrane_backlit.png (128x128 RGBA, seamless):
    Giant translucent backlit stomach cavern alveoli ceiling. Voronoi cellular
    honeycomb/webbing where dark crimson-maroon fleshy struts surround glowing
    amber-red-orange translucent membranous windows (warm bright core inside cells,
    purely living glowing internal stomach membrane — NO green clichés).
    """
    n_coarse = fbm_periodic(size, base_period=4, octaves=4, seed=4201)
    n_med = fbm_periodic(size, base_period=8, octaves=3, seed=4202)
    n_fine = fbm_periodic(size, base_period=16, octaves=2, seed=4203)

    # Balanced 16-cell Voronoi alveolar network (seed 4209 gives optimal spacing)
    f1_cell, edge_cell = voronoi_periodic(size, num_points=16, seed=4209)
    edge_warped = np.maximum(0.0, edge_cell + (n_coarse - 0.5) * 0.022 + (n_med - 0.5) * 0.012)

    # Organic webbed fleshy trabeculae struts (~25-30% coverage) + rounded alveolar junction fillets
    junction_fillet = np.clip((f1_cell - 0.145) / 0.085, 0.0, 1.0) * 0.022
    strut_w = 0.028 + (n_coarse - 0.5) * 0.008 + junction_fillet
    strut_mask = np.clip(1.0 - (edge_warped - strut_w * 0.50) / 0.030, 0.0, 1.0)
    cell_interior = 1.0 - strut_mask

    # Sculpted fleshy relief on dark crimson-maroon trabeculae struts
    strut_center = np.clip(1.0 - edge_warped / (strut_w * 1.25), 0.0, 1.0)
    strut_fiber = 0.5 + 0.5 * np.sin((n_med * 3.5 + n_fine * 2.0) * 2.0 * math.pi)
    strut_val = np.clip(0.28 + (1.0 - strut_center) * 0.42 + strut_fiber * 0.20 + (n_fine - 0.5) * 0.14, 0.0, 1.0)

    # Translucent backlit glow inside each large membranous alveolar window
    dist_in = np.clip((edge_warped - strut_w * 0.45) / 0.115, 0.0, 1.0)
    core_focus = np.clip(1.0 - f1_cell / 0.22, 0.0, 1.0)
    cell_var = np.clip(0.84 + (n_coarse - 0.5) * 0.34, 0.60, 1.12)
    backlit = np.clip((dist_in * 0.62 + core_focus * 0.38) * cell_var, 0.0, 1.0)

    # Delicate silhouetted internal capillary blood vessels inside backlit membranes
    cap_field1 = fbm_periodic(size, base_period=5, octaves=3, seed=4212)
    cap_field2 = fbm_periodic(size, base_period=10, octaves=2, seed=4213)
    cap1 = np.clip(1.0 - np.abs(cap_field1 - 0.5) * 28.0, 0.0, 1.0) ** 1.7
    cap2 = np.clip(1.0 - np.abs(cap_field2 - 0.49) * 34.0, 0.0, 1.0) ** 1.9
    inner_veins = np.clip(np.maximum(cap1, cap2 * 0.7) * cell_interior * 0.62, 0.0, 0.62)

    # Dark crimson-maroon fleshy struts
    strut_r = 22.0 + strut_val * 66.0
    strut_g = 4.0 + strut_val * 12.0
    strut_b = 9.0 + strut_val * 15.0

    # Glowing amber-red-orange translucent membranous windows (warm bright core inside cells)
    core_hi = np.clip((backlit - 0.62) / 0.38, 0.0, 1.0)
    mid_orange = np.clip((backlit - 0.20) / 0.55, 0.0, 1.0)
    mem_r = 132.0 + backlit * 102.0 + mid_orange * 24.0
    mem_g = 24.0 + (backlit ** 1.32) * 126.0 + core_hi * 66.0
    mem_b = 18.0 + (backlit ** 2.15) * 52.0 + core_hi * 44.0

    # Silhouette internal capillaries against backlit tissue
    mem_r = mem_r * (1.0 - inner_veins * 0.48) + 92.0 * (inner_veins * 0.48)
    mem_g = mem_g * (1.0 - inner_veins * 0.76) + 14.0 * (inner_veins * 0.76)
    mem_b = mem_b * (1.0 - inner_veins * 0.72) + 14.0 * (inner_veins * 0.72)

    r = strut_r * strut_mask + mem_r * cell_interior
    g = strut_g * strut_mask + mem_g * cell_interior
    b = strut_b * strut_mask + mem_b * cell_interior
    rgb = np.stack([r, g, b], axis=2)

    palette = [
        (20, 4, 9),       # deep crimson-maroon strut core
        (46, 9, 16),      # dark crimson-maroon fleshy strut
        (82, 15, 22),     # raised fleshy trabecula rim
        (126, 24, 21),    # blood-red translucent membrane edge
        (168, 44, 20),    # deep backlit red-orange tissue
        (208, 74, 23),    # glowing fiery amber-red membrane
        (236, 118, 34),   # bright translucent amber-orange window
        (250, 166, 60),   # warm golden-amber translucent glow
        (255, 210, 114),  # luminous warm backlit cell core
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=18.0, alpha_mask=_opaque_alpha(size))


def make_horned_bone_bound(size=128):
    """
    3. tex_horned_bone_bound.png (128x128 RGBA, seamless):
    Ancient cracked ivory horns lashed with dark dried bloodied tendons/leather cords.
    Weathered off-white/ochre calcified ivory horn striations, deep bone fractures,
    and periodic dark sinew/ligament wrapping bands.
    """
    yy, xx = _coords_2d(size)
    n_coarse = fbm_periodic(size, base_period=4, octaves=4, seed=4301)
    n_med = fbm_periodic(size, base_period=8, octaves=3, seed=4302)
    n_fine = fbm_periodic(size, base_period=16, octaves=2, seed=4303)

    # Strong calcified ivory horn longitudinal striations & weathered growth rings
    horn_flutes = 0.5 + 0.5 * np.sin((xx * 16.0 + (n_coarse - 0.5) * 1.2) * 2.0 * math.pi)
    fine_grain = 0.5 + 0.5 * np.sin((xx * 34.0 + (n_med - 0.5) * 1.6) * 2.0 * math.pi)
    growth_rings = 0.5 + 0.5 * np.sin((yy * 6.0 + (n_coarse - 0.5) * 0.4) * 2.0 * math.pi)

    # Deep bone fractures (longitudinal splits + organic branching fissures)
    crack_mask = np.zeros((size, size), dtype=np.float32)
    rng = np.random.default_rng(4310)
    for k in range(7):
        cx = rng.uniform(0.05, 0.95)
        y_center = rng.uniform(0.0, 1.0)
        dy_wrap = np.abs(((yy - y_center + 0.5) % 1.0) - 0.5)
        half_len = rng.uniform(0.20, 0.36)
        in_len = np.clip(1.0 - dy_wrap / half_len, 0.0, 1.0)
        wiggle = (n_med - 0.5) * 0.030 + 0.009 * np.sin(yy * 4.0 * 2.0 * math.pi + k)
        dx = np.abs(((xx - (cx + wiggle) + 0.5) % 1.0) - 0.5)
        c_w = 0.007 + 0.013 * (in_len ** 0.75)
        crack = np.clip(1.0 - dx / c_w, 0.0, 1.0) * (in_len ** 0.45)
        crack_mask = np.maximum(crack_mask, crack)

    r_frac = fbm_periodic(size, base_period=5, octaves=4, seed=4315)
    hairline = np.clip(1.0 - np.abs(r_frac - 0.5) * 28.0, 0.0, 1.0) ** 2.0
    fractures = np.clip(np.maximum(crack_mask, hairline * 0.75), 0.0, 1.0)
    fracture_stain = np.clip(np.maximum(crack_mask * 0.70, hairline * 0.35), 0.0, 1.0)

    # Weathered off-white / ochre calcified ivory bone value
    bone_val = np.clip(
        0.65
        + (n_coarse - 0.5) * 0.24
        + (n_fine - 0.5) * 0.10
        + (horn_flutes - 0.5) * 0.22
        + (fine_grain - 0.5) * 0.10
        + (growth_rings - 0.5) * 0.10
        - fractures * 0.88,
        0.0,
        1.0,
    )
    bone_gleam = np.clip((bone_val - 0.70) / 0.30, 0.0, 1.0)

    bone_r = 96.0 + bone_val * 128.0 + bone_gleam * 20.0 - fracture_stain * 58.0
    bone_g = 74.0 + bone_val * 126.0 + bone_gleam * 24.0 - fracture_stain * 64.0
    bone_b = 48.0 + bone_val * 114.0 + bone_gleam * 28.0 - fracture_stain * 48.0

    # Periodic dark dried bloodied tendons / leather wrapping bands (2 bundled multi-cord straps)
    band_centers = (0.24, 0.74)
    band_half_h = 0.105

    def _build_lashing_band(cy, twist_dir, x_knot):
        # Gentle organic cinch undulation around the horn circumference
        dy = ((yy - cy + 0.014 * np.sin((xx * 2.0 + cy) * 2.0 * math.pi) + (n_med - 0.5) * 0.014 + 0.5) % 1.0) - 0.5
        in_b = np.abs(dy) < band_half_h
        # 4 tightly wrapped cylindrical tendon/leather cords bundled across the band
        sub = (dy + band_half_h) / (2.0 * band_half_h) * 4.0 + twist_dir * 0.22 * np.sin(xx * 2.0 * 2.0 * math.pi)
        sub_local = np.abs((sub % 1.0) - 0.5) * 2.0
        cord_z = np.where(in_b, np.sqrt(np.clip(1.0 - sub_local ** 2, 0.0, 1.0)), 0.0)
        twist = 0.5 + 0.5 * np.sin((xx * 10.0 + dy * 28.0 * twist_dir + n_fine * 1.2) * 2.0 * math.pi)
        cord_sh = np.where(in_b, np.clip(0.22 + cord_z * 0.58 + (twist - 0.5) * 0.22 - dy * 1.1, 0.0, 1.0), 0.0)

        # Localized crisscross cinch strap tucked cleanly inside the band's wrapped edges
        kx = ((xx - x_knot + 0.5) % 1.0) - 0.5
        in_knot_bounds = (np.abs(dy) <= band_half_h * 0.94) & (np.abs(kx) < 0.21)
        d_cross_a = np.abs(dy - kx * 0.55)
        d_cross_b = np.abs(dy + kx * 0.55)
        strap_w = 0.032
        in_ca = in_knot_bounds & (d_cross_a < strap_w)
        in_cb = in_knot_bounds & (d_cross_b < strap_w)
        sh_ca = np.where(in_ca, 0.32 + np.sqrt(np.clip(1.0 - (d_cross_a / strap_w) ** 2, 0.0, 1.0)) * 0.58, 0.0)
        sh_cb = np.where(in_cb, 0.32 + np.sqrt(np.clip(1.0 - (d_cross_b / strap_w) ** 2, 0.0, 1.0)) * 0.58, 0.0)

        mask = in_b | in_ca | in_cb
        sh = cord_sh
        sh = np.where(in_ca, np.maximum(sh * 0.45, sh_ca), sh)
        sh = np.where(in_cb, np.maximum(sh * 0.45, sh_cb), sh)

        # Dried blood seepage / ambient occlusion hugging the taut ligament edges
        ao = np.exp(-((np.abs(dy) - band_half_h * 1.03) / 0.032) ** 2) * (~mask)
        return mask, sh, ao

    m1, sh1, ao1 = _build_lashing_band(band_centers[0], 1.0, 0.32)
    m2, sh2, ao2 = _build_lashing_band(band_centers[1], -1.0, 0.78)

    in_band = m1 | m2
    cord_shade = np.where(m1, sh1, np.where(m2, sh2, 0.0))
    stain_ao = np.clip(np.maximum(ao1, ao2), 0.0, 1.0)

    bone_r = bone_r * (1.0 - stain_ao * 0.34) + 84.0 * (stain_ao * 0.34)
    bone_g = bone_g * (1.0 - stain_ao * 0.68) + 24.0 * (stain_ao * 0.68)
    bone_b = bone_b * (1.0 - stain_ao * 0.64) + 22.0 * (stain_ao * 0.64)

    cord_hi = np.clip((cord_shade - 0.64) / 0.36, 0.0, 1.0)
    cord_r = 22.0 + cord_shade * 104.0 + cord_hi * 30.0
    cord_g = 9.0 + cord_shade * 30.0 + cord_hi * 24.0
    cord_b = 10.0 + cord_shade * 26.0 + cord_hi * 18.0

    r = np.where(in_band, cord_r, bone_r)
    g = np.where(in_band, cord_g, bone_g)
    b = np.where(in_band, cord_b, bone_b)
    rgb = np.stack([r, g, b], axis=2)

    palette = [
        (22, 10, 10),     # deep bone fracture & cord crevice shadow
        (48, 15, 16),     # dark dried bloodied tendon base
        (86, 24, 22),     # clotted oxblood leather cord
        (124, 38, 30),    # dried bloodied sinew highlight
        (154, 62, 46),    # worn taut ligament rim
        (112, 86, 56),    # aged dark ochre horn shadow
        (152, 122, 82),   # weathered ochre ivory
        (192, 164, 118),  # calcified bone midtone
        (222, 200, 156),  # off-white ancient ivory
        (242, 230, 196),  # weathered ivory striation gleam
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=18.0, alpha_mask=_opaque_alpha(size))


def make_portal_vortex(size=128):
    """
    4. tex_portal_vortex.png (128x128 RGBA, seamless):
    Glowing portal core inside a stomach horror world. Concentric radial vortex
    swirls in bioluminescent cyan-teal and toxic gastric emerald-green transitioning
    from deep abyssal teal outer rim to a white-cyan glowing center, crisp PS1 4x4
    Bayer ordered dithering.
    """
    yy, xx = _coords_2d(size)
    dx = xx - 0.5 + 0.5 / size
    dy = yy - 0.5 + 0.5 / size
    rr = np.sqrt(dx * dx + dy * dy)
    ang = np.arctan2(dy, dx)

    n_a = fbm_periodic(size, base_period=4, octaves=4, seed=4401) - 0.5
    n_b = fbm_periodic(size, base_period=8, octaves=3, seed=4402) - 0.5

    # Smooth outer envelope tapering to 0 at rr >= 0.495 so tile boundaries wrap seamlessly
    outer_env = np.clip((0.495 - rr) / 0.14, 0.0, 1.0) ** 1.05
    ang_fade = np.clip((rr - 0.035) / 0.065, 0.0, 1.0)

    # Tight multi-turn spiral vortex across the disc (smoothly spinning into the core)
    swirl = 42.0 * (rr ** 0.55) - 6.0 * rr
    arm_main = 0.5 + 0.5 * np.sin(5.0 * ang + swirl + n_a * 1.2 * outer_env)
    arm_fine = 0.5 + 0.5 * np.cos(8.0 * ang + swirl * 1.28 - n_b * 1.4 * outer_env)
    conc_rings = 0.5 + 0.5 * np.sin(rr * 66.0 + n_a * 1.4 - np.sin(2.0 * ang) * 0.30 * ang_fade)

    swirl_tex = arm_main * 0.42 + arm_fine * 0.24 + conc_rings * 0.34

    # Radial glow curve: deep abyssal teal outer rim -> swirling vortex -> white-cyan core
    radial_falloff = np.clip(1.0 - rr / 0.495, 0.0, 1.0) ** 0.72
    core_glow = np.exp(-((rr / 0.105) ** 2.2))

    # Suppress angular pinching inside central eye so the glowing center stays round and luminous
    swirl_modulated = (0.24 + 0.76 * swirl_tex) * ang_fade + (1.0 - ang_fade) * 0.96
    energy = np.clip(
        radial_falloff * swirl_modulated * outer_env + core_glow * 0.96,
        0.0,
        1.0,
    )

    # Interweave toxic gastric emerald-green spirals alongside bioluminescent cyan-teal swirls
    emerald_wave = 0.5 + 0.5 * np.sin(4.0 * ang + swirl * 1.04 + n_b * 1.5)
    emerald_mix = (emerald_wave ** 1.25) * outer_env * (1.0 - core_glow * 0.96) * np.clip(energy * 1.6, 0.0, 1.0)

    # Cyan-teal -> blazing white-cyan core ramp
    inner_bloom = np.clip((energy - 0.64) / 0.36, 0.0, 1.0)
    cyan_r = 6.0 + (energy ** 1.65) * 92.0 + inner_bloom * 142.0
    cyan_g = 18.0 + (energy ** 0.95) * 194.0 + inner_bloom * 48.0
    cyan_b = 24.0 + (energy ** 0.95) * 190.0 + inner_bloom * 44.0

    # Toxic gastric emerald-green spiral tendrils modulated by vortex energy
    em_r = 8.0 + (energy ** 1.4) * 76.0
    em_g = 26.0 + (energy ** 0.90) * 204.0
    em_b = 18.0 + (energy ** 1.1) * 98.0

    r = cyan_r * (1.0 - emerald_mix * 0.78) + em_r * (emerald_mix * 0.78)
    g = cyan_g * (1.0 - emerald_mix * 0.72) + em_g * (emerald_mix * 0.72)
    b = cyan_b * (1.0 - emerald_mix * 0.84) + em_b * (emerald_mix * 0.84)
    rgb = np.stack([r, g, b], axis=2)

    palette = [
        (6, 18, 24),      # deep abyssal teal outer rim
        (10, 42, 50),     # abyssal teal shadow
        (16, 82, 94),     # deep bioluminescent teal
        (22, 134, 148),   # mid cyan-teal vortex swirl
        (46, 194, 202),   # vivid glowing cyan-teal
        (14, 96, 54),     # dark toxic gastric emerald
        (32, 162, 82),    # toxic gastric emerald-green swirl
        (78, 224, 120),   # bright bioluminescent gastric emerald
        (114, 240, 226),  # glowing aqua-cyan inner ring
        (186, 254, 246),  # blazing pale cyan halo
        (238, 255, 254),  # white-cyan portal core
    ]
    return apply_bayer_palette(rgb, palette, dither_strength=16.0, alpha_mask=_opaque_alpha(size))


def generate_abyss_textures(out_dir):
    """Generates all 4 tileable 128x128 RGBA PS1 textures for StomachAbyss."""
    os.makedirs(out_dir, exist_ok=True)
    specs = {
        "tex_vein_tendon_bridge.png": make_vein_tendon_bridge(128),
        "tex_membrane_backlit.png": make_membrane_backlit(128),
        "tex_horned_bone_bound.png": make_horned_bone_bound(128),
        "tex_portal_vortex.png": make_portal_vortex(128),
    }
    paths = {}
    for name, img in specs.items():
        p = os.path.join(out_dir, name)
        img.save(p, format="PNG")
        paths[name] = p
    return paths


if __name__ == "__main__":
    out = os.path.normpath(
        os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Textures")
    )
    generated = generate_abyss_textures(out)
    for name, path in generated.items():
        with Image.open(path) as im:
            print(
                f"{name}: path={path} size={im.size[0]}x{im.size[1]} "
                f"mode={im.mode} bytes={os.path.getsize(path)}"
            )
