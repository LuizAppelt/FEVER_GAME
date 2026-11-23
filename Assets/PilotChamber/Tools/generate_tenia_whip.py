import math
import os
import sys
import numpy as np

TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ASSETS = os.path.abspath(os.path.join(TOOLS_DIR, ".."))
TEX_DIR = os.path.join(PROJECT_ASSETS, "Textures")
PREVIEW_DIR = os.path.join(PROJECT_ASSETS, "Preview")
GLB_OUT = os.path.join(PROJECT_ASSETS, "Tenia_Whip.glb")

sys.path.insert(0, TOOLS_DIR)
from glb_exporter import MeshBuilder, export_glb
from preview_render import load_glb, render_view


def normalize(v):
    v = np.asarray(v, dtype=np.float32)
    n = np.linalg.norm(v)
    return v / n if n > 1e-8 else np.array([0.0, 1.0, 0.0], dtype=np.float32)


def catmull_rom_point(p0, p1, p2, p3, t):
    t2 = t * t
    t3 = t2 * t
    return 0.5 * (
        (2.0 * p1)
        + (-p0 + p2) * t
        + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2
        + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3
    )


def build_whip_spline(num_samples=480):
    """Builds a dramatic 3D coiled S-curve whip path (~3.2m arc length) starting at hilt exit."""
    ctrl = np.array([
        [ 0.00,  0.00,  0.05],  # phantom start for tangent
        [ 0.00,  0.04,  0.26],  # hilt exit (Seg_01 start)
        [ 0.06,  0.16,  0.58],  # rises forward out of hand
        [ 0.32,  0.20,  0.84],  # loops right-forward
        [ 0.44, -0.10,  0.74],  # drops down right side of coil
        [ 0.24, -0.34,  0.48],  # sweeps under & back (clear of hilt)
        [-0.14, -0.30,  0.46],  # crosses smoothly to left side
        [-0.32,  0.04,  0.64],  # rises on left side
        [-0.10,  0.28,  0.90],  # sweeps over top-front
        [ 0.20,  0.24,  1.10],  # extends forward toward strike
        [ 0.28,  0.34,  1.26],  # scolex neck poised to strike
        [ 0.34,  0.42,  1.38],  # phantom end for tangent
    ], dtype=np.float32)

    pts = []
    n_seg = len(ctrl) - 3
    per_seg = max(8, num_samples // n_seg)
    for i in range(n_seg):
        for j in range(per_seg):
            t = j / float(per_seg)
            pts.append(catmull_rom_point(ctrl[i], ctrl[i + 1], ctrl[i + 2], ctrl[i + 3], t))
    pts.append(ctrl[-2].copy())
    pts = np.array(pts, dtype=np.float32)

    diffs = np.linalg.norm(np.diff(pts, axis=0), axis=1)
    arc = np.concatenate([[0.0], np.cumsum(diffs)])
    return pts, arc


def sample_curve_at_s(pts, arc, s):
    s = np.clip(s, 0.0, arc[-1])
    idx = int(np.searchsorted(arc, s, side="right")) - 1
    idx = max(0, min(idx, len(pts) - 2))
    ds = arc[idx + 1] - arc[idx]
    t = (s - arc[idx]) / ds if ds > 1e-6 else 0.0
    pos = pts[idx] * (1.0 - t) + pts[idx + 1] * t
    if idx < len(pts) - 2:
        tan = normalize(pts[idx + 1] - pts[idx])
    else:
        tan = normalize(pts[-1] - pts[-2])
    return pos, tan


def compute_parallel_transport_frames(positions, tangents):
    """Computes twist-free (U, V) frames along a 3D curve."""
    n = len(positions)
    us = np.zeros((n, 3), dtype=np.float32)
    vs = np.zeros((n, 3), dtype=np.float32)

    t0 = normalize(tangents[0])
    ref = np.array([0.0, 1.0, 0.0], dtype=np.float32)
    if abs(np.dot(t0, ref)) > 0.95:
        ref = np.array([1.0, 0.0, 0.0], dtype=np.float32)
    u0 = normalize(np.cross(ref, t0))
    v0 = normalize(np.cross(t0, u0))
    us[0] = u0
    vs[0] = v0

    for i in range(1, n):
        t_prev = normalize(tangents[i - 1])
        t_curr = normalize(tangents[i])
        axis = np.cross(t_prev, t_curr)
        sin_a = np.linalg.norm(axis)
        if sin_a > 1e-6:
            axis /= sin_a
            cos_a = float(np.clip(np.dot(t_prev, t_curr), -1.0, 1.0))
            angle = math.atan2(sin_a, cos_a)
            # Rodrigues rotation of u_prev around axis
            u_prev = us[i - 1]
            u_rot = (
                u_prev * math.cos(angle)
                + np.cross(axis, u_prev) * math.sin(angle)
                + axis * np.dot(axis, u_prev) * (1.0 - math.cos(angle))
            )
            u_curr = normalize(u_rot - t_curr * np.dot(u_rot, t_curr))
        else:
            u_curr = normalize(us[i - 1] - t_curr * np.dot(us[i - 1], t_curr))
        v_curr = normalize(np.cross(t_curr, u_curr))
        us[i] = u_curr
        vs[i] = v_curr

    return us, vs


def add_lofted_tube(mb, centers, us, vs, radii_x, radii_y, sides=10,
                    u_repeat=1.0, v_coords=None, colors=None,
                    cap_start=True, cap_end=True, offset=(0.0, 0.0, 0.0),
                    squash_fn=None):
    """Lofts a closed tube with shared ring vertices and smooth normals."""
    off = np.asarray(offset, dtype=np.float32)
    n_rings = len(centers)
    if v_coords is None:
        v_coords = np.linspace(0.0, 1.0, n_rings, dtype=np.float32)
    if colors is None:
        colors = [(1.0, 1.0, 1.0, 1.0)] * n_rings

    ring_indices = []
    for r_idx in range(n_rings):
        c = centers[r_idx] - off
        u_vec = us[r_idx]
        v_vec = vs[r_idx]
        rx = radii_x[r_idx]
        ry = radii_y[r_idx]
        col = colors[r_idx]
        vc = float(v_coords[r_idx])

        ring = []
        for s_idx in range(sides + 1):
            theta = (s_idx % sides) * (2.0 * math.pi / sides)
            ct = math.cos(theta)
            st = math.sin(theta)
            if squash_fn is not None:
                rx_m, ry_m = squash_fn(r_idx, theta, rx, ry)
            else:
                rx_m, ry_m = rx, ry
            pos = c + u_vec * (ct * rx_m) + v_vec * (st * ry_m)
            uc = (s_idx / float(sides)) * u_repeat
            ring.append(mb.add_vertex(pos, (0.0, 1.0, 0.0), (uc, vc), col))
        ring_indices.append(ring)

    for r_idx in range(n_rings - 1):
        r0 = ring_indices[r_idx]
        r1 = ring_indices[r_idx + 1]
        for s_idx in range(sides):
            i00 = r0[s_idx]
            i01 = r0[s_idx + 1]
            i10 = r1[s_idx]
            i11 = r1[s_idx + 1]
            mb.add_tri_indices(i00, i01, i11)
            mb.add_tri_indices(i00, i11, i10)

    if cap_start:
        c0 = centers[0] - off
        c_idx = mb.add_vertex(c0, (0.0, 1.0, 0.0), (0.5, float(v_coords[0])), colors[0])
        r0 = ring_indices[0]
        for s_idx in range(sides):
            mb.add_tri_indices(c_idx, r0[s_idx + 1], r0[s_idx])

    if cap_end:
        c1 = centers[-1] - off
        c_idx = mb.add_vertex(c1, (0.0, 1.0, 0.0), (0.5, float(v_coords[-1])), colors[-1])
        r1 = ring_indices[-1]
        for s_idx in range(sides):
            mb.add_tri_indices(c_idx, r1[s_idx], r1[s_idx + 1])


def add_curved_claw(mb, base_pos, fwd_dir, out_dir, length=0.06, base_r=0.014,
                    bend=0.55, sides=6, color=(0.95, 0.92, 0.85, 1.0)):
    """Builds a curved chitinous spur/hook (NO bone, pure chitin)."""
    fwd = normalize(fwd_dir)
    out = normalize(out_dir - fwd * np.dot(out_dir, fwd))
    side = normalize(np.cross(fwd, out))

    steps = 5
    centers = []
    us = []
    vs = []
    rxs = []
    rys = []
    cols = []
    for i in range(steps + 1):
        t = i / float(steps)
        ang = t * bend
        tangent = normalize(out * math.cos(ang) + fwd * math.sin(ang))
        # Arc position
        arc_p = base_pos + (out * math.sin(ang) + fwd * (1.0 - math.cos(ang))) * (length / max(bend, 0.1))
        if bend < 0.05:
            arc_p = base_pos + out * (length * t)
        u_v = side
        v_v = normalize(np.cross(tangent, u_v))
        r = base_r * ((1.0 - t) ** 0.78) + 0.0012
        centers.append(arc_p)
        us.append(u_v)
        vs.append(v_v)
        rxs.append(r * 0.75)
        rys.append(r * 1.05)
        shade = 0.85 + 0.15 * t
        cols.append((color[0] * shade, color[1] * shade, color[2] * shade, 1.0))

    add_lofted_tube(
        mb, np.array(centers), np.array(us), np.array(vs),
        rxs, rys, sides=sides, u_repeat=1.0, colors=cols,
        cap_start=True, cap_end=True
    )


def build_hilt(mb_flesh, mb_cartilage):
    """Builds Whip_Hilt at origin (0,0,0) pointing +Z: twisted bloody tendons + cartilage collars & spurs."""
    # Central fleshy core from z=-0.14 to z=+0.27
    n_core = 14
    zs = np.linspace(-0.14, 0.27, n_core, dtype=np.float32)
    centers = np.column_stack([np.zeros(n_core), zs * 0.08, zs]).astype(np.float32)
    tangents = np.tile(np.array([0.0, 0.08, 1.0], dtype=np.float32), (n_core, 1))
    us, vs = compute_parallel_transport_frames(centers, tangents)

    radii = []
    cols = []
    for i, z in enumerate(zs):
        t = i / float(n_core - 1)
        # Tapered grip waist in middle, thicker at pommel and collar
        r = 0.028 + 0.012 * (1.0 - math.sin(t * math.pi)) + 0.010 * (t ** 2)
        radii.append(r)
        cols.append((0.88, 0.65, 0.65, 1.0))

    add_lofted_tube(mb_flesh, centers, us, vs, radii, radii, sides=10, u_repeat=2.0, colors=cols)

    # 5 twisted helical muscle tendon cords wrapping around the hilt
    n_strands = 5
    n_pts = 20
    for s_idx in range(n_strands):
        phase0 = s_idx * (2.0 * math.pi / n_strands)
        s_centers = []
        s_tangents = []
        s_r = []
        s_cols = []
        for i in range(n_pts):
            t = i / float(n_pts - 1)
            z = -0.13 + t * 0.39
            y_axis = z * 0.08
            twist = phase0 + t * 2.2 * math.pi
            orbit_r = 0.025 + 0.011 * (1.0 - math.sin(t * math.pi)) + 0.008 * (t ** 2)
            px = math.cos(twist) * orbit_r
            py = y_axis + math.sin(twist) * orbit_r
            s_centers.append([px, py, z])
            s_r.append(0.011 + 0.003 * math.sin(t * math.pi * 3.0 + s_idx))
            c_var = 0.80 + 0.20 * math.sin(t * math.pi)
            s_cols.append((c_var, c_var * 0.72, c_var * 0.72, 1.0))

        s_centers = np.array(s_centers, dtype=np.float32)
        for i in range(n_pts):
            if i == 0:
                s_tangents.append(normalize(s_centers[1] - s_centers[0]))
            elif i == n_pts - 1:
                s_tangents.append(normalize(s_centers[-1] - s_centers[-2]))
            else:
                s_tangents.append(normalize(s_centers[i + 1] - s_centers[i - 1]))
        su, sv = compute_parallel_transport_frames(s_centers, np.array(s_tangents, dtype=np.float32))
        add_lofted_tube(mb_flesh, s_centers, su, sv, s_r, s_r, sides=6, u_repeat=1.0, colors=s_cols)

    # Cartilage pommel collar (z = -0.145 to -0.095)
    p_zs = np.linspace(-0.15, -0.095, 5, dtype=np.float32)
    p_centers = np.column_stack([np.zeros(5), p_zs * 0.08, p_zs]).astype(np.float32)
    p_tans = np.tile(np.array([0.0, 0.08, 1.0], dtype=np.float32), (5, 1))
    pu, pv = compute_parallel_transport_frames(p_centers, p_tans)
    p_r = [0.024, 0.044, 0.046, 0.038, 0.028]
    add_lofted_tube(mb_cartilage, p_centers, pu, pv, p_r, p_r, sides=10, u_repeat=2.0)

    # Pommel curved chitin spur pointing backward-downward
    add_curved_claw(
        mb_cartilage,
        base_pos=np.array([0.0, -0.020, -0.145], dtype=np.float32),
        fwd_dir=np.array([0.0, -0.35, -1.0], dtype=np.float32),
        out_dir=np.array([0.0, -0.8, -0.6], dtype=np.float32),
        length=0.075, base_r=0.017, bend=0.75, sides=6
    )

    # Ventral row of 4 small chitinous spurs along underside (-Y) of hilt
    for k in range(4):
        zk = -0.04 + k * 0.068
        yk = zk * 0.08 - 0.028
        add_curved_claw(
            mb_cartilage,
            base_pos=np.array([0.0, yk, zk], dtype=np.float32),
            fwd_dir=np.array([0.0, 0.0, -1.0], dtype=np.float32),
            out_dir=np.array([0.0, -1.0, -0.25], dtype=np.float32),
            length=0.038, base_r=0.010, bend=0.55, sides=5
        )

    # Front cartilage guard / collar gripping Whip_Seg_01 (z = 0.21 to 0.285)
    g_zs = np.linspace(0.21, 0.285, 6, dtype=np.float32)
    g_centers = np.column_stack([np.zeros(6), g_zs * 0.08 + 0.012, g_zs]).astype(np.float32)
    g_tans = np.tile(np.array([0.0, 0.14, 1.0], dtype=np.float32), (6, 1))
    gu, gv = compute_parallel_transport_frames(g_centers, g_tans)
    g_rx = [0.036, 0.056, 0.062, 0.058, 0.050, 0.040]
    g_ry = [0.036, 0.054, 0.058, 0.054, 0.048, 0.038]
    add_lofted_tube(mb_cartilage, g_centers, gu, gv, g_rx, g_ry, sides=10, u_repeat=2.0)

    # Two lateral guard cartilage spurs
    for sx in (-1.0, 1.0):
        add_curved_claw(
            mb_cartilage,
            base_pos=np.array([sx * 0.045, 0.025, 0.245], dtype=np.float32),
            fwd_dir=np.array([sx * 0.2, 0.3, 0.9], dtype=np.float32),
            out_dir=np.array([sx * 0.95, 0.25, 0.15], dtype=np.float32),
            length=0.055, base_r=0.013, bend=0.65, sides=6
        )


def build_proglottid_segment(mb, pts, arc, s_start, s_end, seg_idx, total_segs):
    """Builds one articulated tapeworm proglottid segment with pivot at s_start."""
    pivot, _ = sample_curve_at_s(pts, arc, s_start)
    mb.translation = [float(pivot[0]), float(pivot[1]), float(pivot[2])]

    # Slight overlap at ends so Unity procedural rotation never shows gaps
    s0 = max(0.0, s_start - 0.010)
    s1 = min(arc[-1], s_end + 0.012)
    n_rings = 7
    s_vals = np.linspace(s0, s1, n_rings, dtype=np.float32)

    centers = []
    tangents = []
    for sv in s_vals:
        p, t = sample_curve_at_s(pts, arc, sv)
        centers.append(p)
        tangents.append(t)
    centers = np.array(centers, dtype=np.float32)
    tangents = np.array(tangents, dtype=np.float32)
    us, vs = compute_parallel_transport_frames(centers, tangents)

    # Taper smoothly from thick near hilt (0.046m) to slender neck (0.020m)
    t_glob0 = seg_idx / float(total_segs)
    t_glob1 = (seg_idx + 1) / float(total_segs)
    r_base0 = 0.046 * (1.0 - 0.56 * t_glob0)
    r_base1 = 0.046 * (1.0 - 0.56 * t_glob1)

    # Proglottid organic profile: pinched at joints, swollen in middle, flared rim at distal end
    profile = [0.76, 0.96, 1.08, 1.12, 1.08, 1.16, 0.82]
    rx_list = []
    ry_list = []
    cols = []
    v_coords = np.linspace(0.05, 0.95, n_rings, dtype=np.float32)
    for i in range(n_rings):
        u_loc = i / float(n_rings - 1)
        r_b = r_base0 * (1.0 - u_loc) + r_base1 * u_loc
        r = r_b * profile[i]
        # Slightly flattened dorsoventrally like a real tapeworm proglottid
        rx_list.append(r * 1.12)
        ry_list.append(r * 0.86)
        # Subtle darkening at mucosal joint creases
        joint_shade = 0.78 if (i == 0 or i == n_rings - 1) else 1.0
        cols.append((joint_shade, joint_shade * 0.96, joint_shade * 0.94, 1.0))

    def proglottid_groove(r_idx, theta, rx, ry):
        # Subtle squared-pillow tapeworm cross-section + central longitudinal groove
        groove = 1.0 - 0.08 * (math.cos(2.0 * theta) ** 2)
        return rx * groove, ry * groove

    add_lofted_tube(
        mb, centers, us, vs, rx_list, ry_list, sides=10,
        u_repeat=2.0, v_coords=v_coords, colors=cols,
        cap_start=True, cap_end=True, offset=pivot,
        squash_fn=proglottid_groove
    )


def build_scolex(mb_head, mb_suckers, mb_hooks, mb_acid, pts, arc):
    """Builds the bulbous hooked scolex at the tip of Whip_Seg_12 with 4 suckers, 6 hooks, and acid drips."""
    pivot, fwd = sample_curve_at_s(pts, arc, arc[-1])
    fwd = normalize(fwd)
    ref = np.array([0.0, 1.0, 0.0], dtype=np.float32)
    if abs(np.dot(fwd, ref)) > 0.95:
        ref = np.array([1.0, 0.0, 0.0], dtype=np.float32)
    u_ax = normalize(np.cross(ref, fwd))
    v_ax = normalize(np.cross(fwd, u_ax))

    for mb in (mb_head, mb_suckers, mb_hooks, mb_acid):
        mb.translation = [float(pivot[0]), float(pivot[1]), float(pivot[2])]

    # 1. Bulbous pear-shaped scolex head (length ~0.16m)
    head_len = 0.165
    n_rings = 9
    ts = np.linspace(-0.015, 1.0, n_rings, dtype=np.float32)
    # Radius swells from neck (0.020) to bulbous head (0.068) then rounds to rostellum dome
    profile = [0.020, 0.030, 0.052, 0.066, 0.068, 0.060, 0.045, 0.026, 0.008]
    centers = np.array([fwd * (t * head_len) for t in ts], dtype=np.float32)
    us = np.tile(u_ax, (n_rings, 1))
    vs = np.tile(v_ax, (n_rings, 1))
    cols = [(0.96, 0.90, 0.88, 1.0)] * n_rings
    cols[-1] = (0.78, 0.95, 0.65, 1.0)  # bile/acid stained tip

    add_lofted_tube(
        mb_head, centers, us, vs, profile, profile, sides=12,
        u_repeat=2.0, colors=cols, cap_start=True, cap_end=True
    )

    # 2. 4 dark recessed sucker-mouths around the sides of the bulbous scolex
    sucker_z = 0.078
    sucker_r_pos = 0.058
    for k in range(4):
        ang = k * (math.pi * 0.5) + math.pi * 0.25
        out_dir = normalize(u_ax * math.cos(ang) + v_ax * math.sin(ang))
        s_center = fwd * sucker_z + out_dir * sucker_r_pos
        s_u = normalize(np.cross(fwd, out_dir))
        s_v = normalize(np.cross(out_dir, s_u))

        # Sucker rim + deep dark recessed cup
        s_centers = np.array([
            s_center - out_dir * 0.008,
            s_center + out_dir * 0.006,
            s_center + out_dir * 0.015,
            s_center + out_dir * 0.012,
            s_center - out_dir * 0.012,
        ], dtype=np.float32)
        s_us = np.tile(s_u, (5, 1))
        s_vs = np.tile(s_v, (5, 1))
        s_radii = [0.022, 0.025, 0.021, 0.013, 0.007]
        s_cols = [
            (0.85, 0.45, 0.45, 1.0),
            (0.95, 0.55, 0.55, 1.0),
            (0.65, 0.22, 0.24, 1.0),
            (0.22, 0.06, 0.08, 1.0),
            (0.08, 0.02, 0.03, 1.0),
        ]
        add_lofted_tube(
            mb_suckers, s_centers, s_us, s_vs, s_radii, s_radii, sides=8,
            u_repeat=1.0, colors=s_cols, cap_start=True, cap_end=True
        )

    # 3. Crown of 6 curved chitinous hooks at the rostellum apex
    hook_z = 0.138
    hook_ring_r = 0.029
    for k in range(6):
        ang = k * (2.0 * math.pi / 6.0)
        radial = normalize(u_ax * math.cos(ang) + v_ax * math.sin(ang))
        h_base = fwd * hook_z + radial * hook_ring_r
        # Hooks erupt forward and curve outward/backward like wicked talons
        h_out = normalize(fwd * 0.88 + radial * 0.48)
        h_bend_dir = normalize(-fwd * 0.35 + radial * 0.94)
        add_curved_claw(
            mb_hooks,
            base_pos=h_base,
            fwd_dir=h_bend_dir,
            out_dir=h_out,
            length=0.070, base_r=0.013, bend=1.20, sides=6,
            color=(0.95, 0.90, 0.82, 1.0)
        )

    # 4. Glowing toxic-green gastric acid pool on rostellum + dripping strands
    acid_pool_centers = np.array([
        fwd * 0.128,
        fwd * 0.146,
        fwd * 0.162,
        fwd * 0.170,
    ], dtype=np.float32)
    add_lofted_tube(
        mb_acid, acid_pool_centers, np.tile(u_ax, (4, 1)), np.tile(v_ax, (4, 1)),
        [0.038, 0.034, 0.022, 0.008], [0.038, 0.034, 0.022, 0.008], sides=8, u_repeat=1.0
    )

    # Dripping acid strands hanging straight down (-Y in world space) from scolex underside
    down = np.array([0.0, -1.0, 0.0], dtype=np.float32)
    du = np.array([1.0, 0.0, 0.0], dtype=np.float32)
    dv = np.array([0.0, 0.0, 1.0], dtype=np.float32)
    drip_specs = [
        (fwd * 0.125 + down * 0.028, 0.11, 0.009),
        (fwd * 0.090 + down * 0.048 + u_ax * 0.020, 0.14, 0.010),
        (fwd * 0.075 + down * 0.050 - u_ax * 0.022, 0.09, 0.008),
        (fwd * 0.145 + down * 0.015 + u_ax * 0.014, 0.07, 0.007),
    ]
    for d_start, d_len, d_rad in drip_specs:
        d_c = np.array([
            d_start,
            d_start + down * (d_len * 0.35),
            d_start + down * (d_len * 0.72),
            d_start + down * (d_len * 0.88),
            d_start + down * d_len,
        ], dtype=np.float32)
        d_r = [d_rad, d_rad * 0.55, d_rad * 0.45, d_rad * 1.15, d_rad * 0.30]
        add_lofted_tube(
            mb_acid, d_c, np.tile(du, (5, 1)), np.tile(dv, (5, 1)),
            d_r, d_r, sides=6, u_repeat=1.0
        )


def main():
    pts, arc = build_whip_spline()
    total_len = float(arc[-1])
    print(f"Whip spline arc length: {total_len:.3f}m")

    mb_hilt = MeshBuilder("Whip_Hilt", "mat_flesh_wall", translation=(0.0, 0.0, 0.0))
    mb_hilt_cart = MeshBuilder("Whip_Hilt_Cartilage", "mat_cartilage", translation=(0.0, 0.0, 0.0))
    build_hilt(mb_hilt, mb_hilt_cart)

    num_segs = 12
    seg_builders = []
    for i in range(num_segs):
        s0 = (i / float(num_segs)) * total_len
        s1 = ((i + 1) / float(num_segs)) * total_len
        name = f"Whip_Seg_{i + 1:02d}"
        mb_seg = MeshBuilder(name, "mat_tenia_proglottid")
        build_proglottid_segment(mb_seg, pts, arc, s0, s1, i, num_segs)
        seg_builders.append(mb_seg)

    mb_scolex = MeshBuilder("Whip_Scolex", "mat_tenia_proglottid")
    mb_suckers = MeshBuilder("Whip_Scolex_Suckers", "mat_hell_organs")
    mb_hooks = MeshBuilder("Whip_Scolex_Hooks", "mat_cartilage")
    mb_acid = MeshBuilder("Whip_Scolex_Acid", "mat_acid")
    build_scolex(mb_scolex, mb_suckers, mb_hooks, mb_acid, pts, arc)

    all_meshes = [mb_hilt, mb_hilt_cart] + seg_builders + [mb_scolex, mb_suckers, mb_hooks, mb_acid]
    for mb in all_meshes:
        mb.recompute_smooth_normals()

    materials_spec = {
        "mat_flesh_wall": {
            "image_path": os.path.join(TEX_DIR, "tex_flesh_wall.png"),
            "roughness": 0.72,
            "metallic": 0.0,
            "double_sided": False,
        },
        "mat_cartilage": {
            "image_path": os.path.join(TEX_DIR, "tex_cartilage.png"),
            "roughness": 0.62,
            "metallic": 0.05,
            "double_sided": False,
        },
        "mat_tenia_proglottid": {
            "image_path": os.path.join(TEX_DIR, "tex_tenia_proglottid.png"),
            "roughness": 0.58,
            "metallic": 0.0,
            "double_sided": False,
        },
        "mat_hell_organs": {
            "image_path": os.path.join(TEX_DIR, "tex_hell_organs.png"),
            "roughness": 0.55,
            "metallic": 0.0,
            "double_sided": False,
        },
        "mat_acid": {
            "image_path": os.path.join(TEX_DIR, "tex_acid.png"),
            "roughness": 0.25,
            "metallic": 0.0,
            "emissive_factor": [0.45, 0.95, 0.22],
            "use_emissive_texture": True,
            "double_sided": False,
        },
    }

    info = export_glb(GLB_OUT, all_meshes, materials_spec, root_name="Tenia_Whip")
    print("Exported Tenia_Whip.glb:", info)

    # Render side & isometric previews using preview_render.py
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    gltf, loaded_meshes = load_glb(GLB_OUT)
    node_names = [m["name"] for m in loaded_meshes]
    print(f"Loaded GLB nodes ({len(node_names)}):", node_names)

    views = [
        ("preview_tenia_whip_side.png", (-2.15, 0.15, 0.62), (0.05, 0.05, 0.62), 42.0),
        ("preview_tenia_whip_iso.png", (-1.55, 1.15, -1.15), (0.08, 0.05, 0.60), 44.0),
    ]
    for fname, cam, tgt, fov in views:
        im = render_view(loaded_meshes, cam, tgt, width=720, height=540, fov_deg=fov)
        out_p = os.path.join(PREVIEW_DIR, fname)
        im.save(out_p)
        print("Saved preview:", out_p)


if __name__ == "__main__":
    main()
