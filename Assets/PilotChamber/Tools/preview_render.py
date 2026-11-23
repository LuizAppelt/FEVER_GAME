import io
import json
import os
import struct
import numpy as np
from PIL import Image


def load_glb(glb_path):
    """Loads and validates a .glb file, returning meshes (with node translation) and decoded RGBA textures."""
    with open(glb_path, "rb") as f:
        data = f.read()

    magic, version, total_len = struct.unpack("<III", data[:12])
    assert magic == 0x46546C67, f"Bad GLB magic: {hex(magic)}"
    assert version == 2, f"Unsupported glTF version: {version}"
    assert total_len == len(data), f"Length mismatch: {total_len} != {len(data)}"

    pos = 12
    json_bytes = None
    bin_bytes = None
    while pos < len(data):
        clen, ctype = struct.unpack("<II", data[pos:pos + 8])
        pos += 8
        chunk = data[pos:pos + clen]
        pos += clen
        if ctype == 0x4E4F534A:
            json_bytes = chunk
        elif ctype == 0x004E4942:
            bin_bytes = chunk

    gltf = json.loads(json_bytes.decode("utf-8"))

    def read_accessor(acc_idx):
        acc = gltf["accessors"][acc_idx]
        bv = gltf["bufferViews"][acc["bufferView"]]
        offset = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
        count = acc["count"]
        comp_map = {5121: np.uint8, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
        type_dims = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}
        dtype = comp_map[acc["componentType"]]
        dims = type_dims[acc["type"]]
        arr = np.frombuffer(bin_bytes, dtype=dtype, count=count * dims, offset=offset)
        return arr.reshape((count, dims)) if dims > 1 else arr

    decoded_images = []
    for img in gltf.get("images", []):
        bv = gltf["bufferViews"][img["bufferView"]]
        raw = bin_bytes[bv["byteOffset"]:bv["byteOffset"] + bv["byteLength"]]
        im = Image.open(io.BytesIO(raw)).convert("RGBA")
        decoded_images.append(np.array(im, dtype=np.float32) / 255.0)

    loaded_meshes = []
    for node in gltf["nodes"]:
        if "mesh" not in node:
            continue
        mesh = gltf["meshes"][node["mesh"]]
        trans = np.array(node.get("translation", [0.0, 0.0, 0.0]), dtype=np.float32)
        prim = mesh["primitives"][0]
        attrs = prim["attributes"]
        positions = read_accessor(attrs["POSITION"]) + trans[None, :]
        normals = read_accessor(attrs["NORMAL"])
        uvs = read_accessor(attrs["TEXCOORD_0"])
        colors = read_accessor(attrs["COLOR_0"])
        indices = read_accessor(prim["indices"]).reshape(-1, 3)

        mat = gltf["materials"][prim["material"]]
        tex_idx = mat["pbrMetallicRoughness"]["baseColorTexture"]["index"]
        img_idx = gltf["textures"][tex_idx]["source"]
        emissive = np.array(mat.get("emissiveFactor", [0.0, 0.0, 0.0]), dtype=np.float32)
        alpha_cutoff = mat.get("alphaCutoff", 0.0) if mat.get("alphaMode") == "MASK" else 0.0
        double_sided = bool(mat.get("doubleSided", False))

        loaded_meshes.append({
            "name": node["name"],
            "positions": positions,
            "normals": normals,
            "uvs": uvs,
            "colors": colors[:, :3],
            "indices": indices,
            "texture": decoded_images[img_idx],
            "emissive": emissive,
            "alpha_cutoff": alpha_cutoff,
            "double_sided": double_sided,
        })

    return gltf, loaded_meshes


def render_view(meshes, cam_pos, target, width=720, height=540, fov_deg=44.0, light_dir=None):
    cam_pos = np.array(cam_pos, dtype=np.float32)
    target = np.array(target, dtype=np.float32)
    fwd = target - cam_pos
    fwd /= np.linalg.norm(fwd)
    up_ref = np.array([0.0, 1.0, 0.0], dtype=np.float32)
    if abs(np.dot(fwd, up_ref)) > 0.99:
        up_ref = np.array([0.0, 0.0, 1.0], dtype=np.float32)
    right = np.cross(fwd, up_ref)
    right /= np.linalg.norm(right)
    up = np.cross(right, fwd)

    f = 1.0 / np.tan(np.deg2rad(fov_deg) * 0.5)
    aspect = width / float(height)

    color_buf = np.full((height, width, 3), [14.0/255.0, 4.0/255.0, 7.0/255.0], dtype=np.float32)
    z_buf = np.full((height, width), 1e9, dtype=np.float32)

    if light_dir is None:
        light_dir = np.array([0.35, 0.85, -0.35], dtype=np.float32)
    else:
        light_dir = np.array(light_dir, dtype=np.float32)
    light_dir /= np.linalg.norm(light_dir)

    for m in meshes:
        pos = m["positions"]
        nrm = m["normals"]
        uv = m["uvs"]
        col = m["colors"]
        tris = m["indices"]
        tex = m["texture"]
        th, tw, _ = tex.shape
        emissive = m["emissive"]
        cutoff = m["alpha_cutoff"]
        double_sided = m["double_sided"]

        rel = pos - cam_pos
        cx = rel @ right
        cy = rel @ up
        cz = rel @ fwd

        valid_z = cz > 0.5
        inv_z = np.where(valid_z, 1.0 / np.maximum(cz, 0.5), 0.0)
        sx = (cx * f / aspect) * inv_z
        sy = (cy * f) * inv_z
        px = (sx * 0.5 + 0.5) * width
        py = (-sy * 0.5 + 0.5) * height

        ndotl = np.clip(nrm @ light_dir, 0.0, 1.0)
        lit = col * (0.42 + 0.68 * ndotl[:, None]) + emissive[None, :] * 0.48
        lit = np.clip(lit, 0.0, 1.35)

        p0 = tris[:, 0]
        p1 = tris[:, 1]
        p2 = tris[:, 2]

        keep = valid_z[p0] & valid_z[p1] & valid_z[p2]
        x0, y0 = px[p0], py[p0]
        x1, y1 = px[p1], py[p1]
        x2, y2 = px[p2], py[p2]
        area2 = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0)

        v0_3 = pos[p0]
        fn = np.cross(pos[p1] - v0_3, pos[p2] - v0_3)
        cam_ray = cam_pos[None, :] - v0_3
        front = np.ones(len(tris), dtype=bool) if double_sided else (np.sum(fn * cam_ray, axis=1) > 0.0)

        keep = keep & front & (np.abs(area2) > 0.1)
        idx_keep = np.where(keep)[0]

        for ti in idx_keep:
            i0, i1, i2 = tris[ti]
            ax, ay, az = px[i0], py[i0], cz[i0]
            bx, by, bz = px[i1], py[i1], cz[i1]
            cx_, cy_, cz_ = px[i2], py[i2], cz[i2]

            minx = max(0, int(np.floor(min(ax, bx, cx_))))
            maxx = min(width - 1, int(np.ceil(max(ax, bx, cx_))))
            miny = max(0, int(np.floor(min(ay, by, cy_))))
            maxy = min(height - 1, int(np.ceil(max(ay, by, cy_))))
            if minx > maxx or miny > maxy:
                continue

            denom = (by - cy_) * (ax - cx_) + (cx_ - bx) * (ay - cy_)
            if abs(denom) < 1e-6:
                continue

            xs_box = np.arange(minx, maxx + 1, dtype=np.float32) + 0.5
            ys_box = np.arange(miny, maxy + 1, dtype=np.float32) + 0.5
            gx, gy = np.meshgrid(xs_box, ys_box)

            w0 = ((by - cy_) * (gx - cx_) + (cx_ - bx) * (gy - cy_)) / denom
            w1 = ((cy_ - ay) * (gx - cx_) + (ax - cx_) * (gy - cy_)) / denom
            w2 = 1.0 - w0 - w1

            inside = (w0 >= -1e-4) & (w1 >= -1e-4) & (w2 >= -1e-4)
            if not np.any(inside):
                continue

            iz0, iz1, iz2 = 1.0 / az, 1.0 / bz, 1.0 / cz_
            w0p = w0 * iz0
            w1p = w1 * iz1
            w2p = w2 * iz2
            wp_sum = w0p + w1p + w2p
            z_interp = 1.0 / np.maximum(wp_sum, 1e-6)

            sub_z = z_buf[miny:maxy + 1, minx:maxx + 1]
            vis = inside & (z_interp < sub_z)
            if not np.any(vis):
                continue

            vy, vx = np.where(vis)
            bw0 = w0p[vy, vx] * z_interp[vy, vx]
            bw1 = w1p[vy, vx] * z_interp[vy, vx]
            bw2 = w2p[vy, vx] * z_interp[vy, vx]

            u_pix = bw0 * uv[i0, 0] + bw1 * uv[i1, 0] + bw2 * uv[i2, 0]
            v_pix = bw0 * uv[i0, 1] + bw1 * uv[i1, 1] + bw2 * uv[i2, 1]

            tx = np.floor(np.mod(u_pix, 1.0) * tw).astype(np.int32) % tw
            ty = np.floor(np.mod(v_pix, 1.0) * th).astype(np.int32) % th
            texel = tex[ty, tx]

            if cutoff > 0.0:
                opaque = texel[:, 3] >= cutoff
                if not np.any(opaque):
                    continue
                vy = vy[opaque]
                vx = vx[opaque]
                bw0 = bw0[opaque]
                bw1 = bw1[opaque]
                bw2 = bw2[opaque]
                texel = texel[opaque]

            sub_z[vy, vx] = z_interp[vy, vx]
            c_pix = bw0[:, None] * lit[i0] + bw1[:, None] * lit[i1] + bw2[:, None] * lit[i2]
            final_col = np.clip(texel[:, :3] * c_pix, 0.0, 1.0)
            color_buf[miny + vy, minx + vx] = final_col

    out_u8 = np.clip(color_buf * 255.0, 0, 255).astype(np.uint8)
    return Image.fromarray(out_u8, mode="RGB")


if __name__ == "__main__":
    project_root = r"C:\Users\user\IdeaProjects\Game final\Assets\PilotChamber"
    glb_path = os.path.join(project_root, "Stomach_Pilot_Chamber.glb")
    out_dir = os.path.join(project_root, "Preview")
    os.makedirs(out_dir, exist_ok=True)

    gltf, meshes = load_glb(glb_path)
    print(f"Validated GLB! NodesWithMesh={len(meshes)}, Materials={len(gltf['materials'])}, Textures={len(gltf['textures'])}")

    views = [
        ("preview_iso_sw.png", (-17.5, 20.5, -18.5), (0.0, 0.5, 1.5), 46.0),
        ("preview_iso_se.png", ( 17.5, 20.5, -18.5), (0.0, 0.5, 1.5), 46.0),
        ("preview_topdown.png", (0.0, 32.0, -0.5), (0.0, 0.0, 0.5), 48.0),
    ]
    for fname, cam, tgt, fov in views:
        im = render_view(meshes, cam, tgt, width=720, height=540, fov_deg=fov)
        out_p = os.path.join(out_dir, fname)
        im.save(out_p)
        print("Saved preview:", out_p)
