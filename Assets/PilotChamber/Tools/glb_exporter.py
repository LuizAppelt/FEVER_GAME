import json
import struct
import numpy as np


class MeshBuilder:
    """Accumulates indexed triangle geometry with positions, normals, UVs, and RGBA vertex colors."""

    def __init__(self, name, material_name, translation=None):
        self.name = name
        self.material_name = material_name
        self.translation = list(translation) if translation is not None else None
        self.positions = []
        self.normals = []
        self.uvs = []
        self.colors = []
        self.indices = []

    def add_vertex(self, pos, normal=(0.0, 1.0, 0.0), uv=(0.0, 0.0), color=(1.0, 1.0, 1.0, 1.0)):
        idx = len(self.positions)
        self.positions.append([float(pos[0]), float(pos[1]), float(pos[2])])
        self.normals.append([float(normal[0]), float(normal[1]), float(normal[2])])
        self.uvs.append([float(uv[0]), float(uv[1])])
        if len(color) == 3:
            self.colors.append([float(color[0]), float(color[1]), float(color[2]), 1.0])
        else:
            self.colors.append([float(color[0]), float(color[1]), float(color[2]), float(color[3])])
        return idx

    def add_tri_indices(self, i0, i1, i2):
        # Skip degenerate triangles
        if i0 == i1 or i1 == i2 or i0 == i2:
            return
        self.indices.extend([int(i0), int(i1), int(i2)])

    def add_flat_triangle(self, p0, p1, p2, uv0, uv1, uv2, c0, c1, c2):
        e1 = np.array(p1, dtype=np.float32) - np.array(p0, dtype=np.float32)
        e2 = np.array(p2, dtype=np.float32) - np.array(p0, dtype=np.float32)
        n = np.cross(e1, e2)
        norm = np.linalg.norm(n)
        if norm < 1e-8:
            return
        n = (n / norm).tolist()
        i0 = self.add_vertex(p0, n, uv0, c0)
        i1 = self.add_vertex(p1, n, uv1, c1)
        i2 = self.add_vertex(p2, n, uv2, c2)
        self.add_tri_indices(i0, i1, i2)

    def add_flat_quad(self, p0, p1, p2, p3, uv0, uv1, uv2, uv3, c0, c1, c2, c3):
        """CCW quad p0 -> p1 -> p2 -> p3 split into two flat triangles."""
        self.add_flat_triangle(p0, p1, p2, uv0, uv1, uv2, c0, c1, c2)
        self.add_flat_triangle(p0, p2, p3, uv0, uv2, uv3, c0, c2, c3)

    def recompute_smooth_normals(self):
        """Recomputes area-weighted smooth normals across shared vertices."""
        if not self.positions or not self.indices:
            return
        pos = np.array(self.positions, dtype=np.float32)
        tris = np.array(self.indices, dtype=np.int32).reshape(-1, 3)
        acc = np.zeros_like(pos)

        v0 = pos[tris[:, 0]]
        v1 = pos[tris[:, 1]]
        v2 = pos[tris[:, 2]]
        fn = np.cross(v1 - v0, v2 - v0)  # magnitude = 2 * triangle area

        np.add.at(acc, tris[:, 0], fn)
        np.add.at(acc, tris[:, 1], fn)
        np.add.at(acc, tris[:, 2], fn)

        lens = np.linalg.norm(acc, axis=1, keepdims=True)
        lens = np.where(lens < 1e-8, 1.0, lens)
        acc = acc / lens
        # Fallback if a vertex had zero area
        zero_mask = np.linalg.norm(acc, axis=1) < 1e-5
        acc[zero_mask] = [0.0, 1.0, 0.0]
        self.normals = acc.tolist()

    def build_arrays(self):
        pos = np.array(self.positions, dtype=np.float32)
        nrm = np.array(self.normals, dtype=np.float32)
        uv = np.array(self.uvs, dtype=np.float32)
        col = np.clip(np.array(self.colors, dtype=np.float32), 0.0, 1.0)
        if len(pos) <= 65535:
            idx = np.array(self.indices, dtype=np.uint16)
            idx_type = 5123  # UNSIGNED_SHORT
        else:
            idx = np.array(self.indices, dtype=np.uint32)
            idx_type = 5125  # UNSIGNED_INT
        return pos, nrm, uv, col, idx, idx_type


def export_glb(output_path, meshes, materials_spec, root_name="Stomach_Pilot_Chamber", anchors=None):
    """
    Exports a list of MeshBuilder objects and materials_spec into a self-contained glTF 2.0 .glb.

    materials_spec: dict mapping material_name -> {
        "image_path": str,
        "roughness": float,
        "metallic": float,
        "emissive_factor": [r, g, b] (optional),
        "use_emissive_texture": bool (optional),
        "double_sided": bool (optional),
    }
    """
    bin_chunks = bytearray()
    buffer_views = []
    accessors = []
    images = []
    textures = []
    materials = []
    gltf_meshes = []
    nodes = []

    def append_bin(raw_bytes, target=None):
        # Pad current buffer to 4-byte boundary before appending
        pad = (-len(bin_chunks)) % 4
        if pad:
            bin_chunks.extend(b"\x00" * pad)
        offset = len(bin_chunks)
        bin_chunks.extend(raw_bytes)
        bv = {
            "buffer": 0,
            "byteOffset": offset,
            "byteLength": len(raw_bytes),
        }
        if target is not None:
            bv["target"] = target
        bv_idx = len(buffer_views)
        buffer_views.append(bv)
        return bv_idx

    # 1. Single nearest-neighbor sampler for authentic PS1 point filtering
    samplers = [{
        "magFilter": 9728,  # NEAREST
        "minFilter": 9728,  # NEAREST
        "wrapS": 10497,     # REPEAT
        "wrapT": 10497,     # REPEAT
    }]

    # 2. Embed PNG images and create materials
    mat_name_to_idx = {}
    img_path_to_tex_idx = {}

    for mat_name, spec in materials_spec.items():
        img_path = spec["image_path"]
        if img_path not in img_path_to_tex_idx:
            with open(img_path, "rb") as f:
                png_bytes = f.read()
            bv_idx = append_bin(png_bytes)
            img_idx = len(images)
            images.append({
                "bufferView": bv_idx,
                "mimeType": "image/png",
                "name": mat_name + "_img",
            })
            tex_idx = len(textures)
            textures.append({
                "sampler": 0,
                "source": img_idx,
                "name": mat_name + "_tex",
            })
            img_path_to_tex_idx[img_path] = tex_idx
        else:
            tex_idx = img_path_to_tex_idx[img_path]

        mat_def = {
            "name": mat_name,
            "pbrMetallicRoughness": {
                "baseColorTexture": {"index": tex_idx},
                "metallicFactor": float(spec.get("metallic", 0.0)),
                "roughnessFactor": float(spec.get("roughness", 0.85)),
            },
            "doubleSided": bool(spec.get("double_sided", False)),
        }
        if spec.get("emissive_factor"):
            mat_def["emissiveFactor"] = [float(x) for x in spec["emissive_factor"]]
            if spec.get("use_emissive_texture", True):
                mat_def["emissiveTexture"] = {"index": tex_idx}
        if spec.get("alpha_mode"):
            mat_def["alphaMode"] = spec["alpha_mode"]
            if spec["alpha_mode"] == "MASK":
                mat_def["alphaCutoff"] = float(spec.get("alpha_cutoff", 0.45))

        mat_name_to_idx[mat_name] = len(materials)
        materials.append(mat_def)

    # 3. Build mesh accessors and nodes
    child_node_indices = []
    total_verts = 0
    total_tris = 0

    for mb in meshes:
        if not mb.positions or not mb.indices:
            continue
        pos, nrm, uv, col, idx, idx_type = mb.build_arrays()
        total_verts += len(pos)
        total_tris += len(idx) // 3

        bv_pos = append_bin(pos.tobytes(), target=34962)  # ARRAY_BUFFER
        acc_pos = len(accessors)
        accessors.append({
            "bufferView": bv_pos,
            "componentType": 5126,  # FLOAT
            "count": int(len(pos)),
            "type": "VEC3",
            "min": [float(x) for x in pos.min(axis=0)],
            "max": [float(x) for x in pos.max(axis=0)],
        })

        bv_nrm = append_bin(nrm.tobytes(), target=34962)
        acc_nrm = len(accessors)
        accessors.append({
            "bufferView": bv_nrm,
            "componentType": 5126,
            "count": int(len(nrm)),
            "type": "VEC3",
        })

        bv_uv = append_bin(uv.tobytes(), target=34962)
        acc_uv = len(accessors)
        accessors.append({
            "bufferView": bv_uv,
            "componentType": 5126,
            "count": int(len(uv)),
            "type": "VEC2",
        })

        bv_col = append_bin(col.tobytes(), target=34962)
        acc_col = len(accessors)
        accessors.append({
            "bufferView": bv_col,
            "componentType": 5126,
            "count": int(len(col)),
            "type": "VEC4",
        })

        bv_idx = append_bin(idx.tobytes(), target=34963)  # ELEMENT_ARRAY_BUFFER
        acc_idx = len(accessors)
        accessors.append({
            "bufferView": bv_idx,
            "componentType": idx_type,
            "count": int(len(idx)),
            "type": "SCALAR",
            "min": [int(idx.min())],
            "max": [int(idx.max())],
        })

        mesh_idx = len(gltf_meshes)
        gltf_meshes.append({
            "name": mb.name,
            "primitives": [{
                "attributes": {
                    "POSITION": acc_pos,
                    "NORMAL": acc_nrm,
                    "TEXCOORD_0": acc_uv,
                    "COLOR_0": acc_col,
                },
                "indices": acc_idx,
                "material": mat_name_to_idx[mb.material_name],
                "mode": 4,  # TRIANGLES
            }]
        })

        node_idx = len(nodes)
        node_def = {
            "name": mb.name,
            "mesh": mesh_idx,
        }
        if mb.translation is not None:
            node_def["translation"] = [float(v) for v in mb.translation]
        nodes.append(node_def)
        child_node_indices.append(node_idx)

    if anchors:
        for anchor_name, pos in anchors:
            node_idx = len(nodes)
            nodes.append({
                "name": anchor_name,
                "translation": [float(v) for v in pos],
            })
            child_node_indices.append(node_idx)

    # Pad final BIN chunk to 4-byte boundary
    bin_pad = (-len(bin_chunks)) % 4
    if bin_pad:
        bin_chunks.extend(b"\x00" * bin_pad)

    root_node_idx = len(nodes)
    nodes.append({
        "name": root_name,
        "children": child_node_indices,
    })

    gltf = {
        "asset": {
            "version": "2.0",
            "generator": "MAPA Stomach Pilot Chamber Generator (PS1 Retro Low-Poly)",
        },
        "scene": 0,
        "scenes": [{
            "name": root_name + "_Scene",
            "nodes": [root_node_idx],
        }],
        "nodes": nodes,
        "meshes": gltf_meshes,
        "materials": materials,
        "textures": textures,
        "images": images,
        "samplers": samplers,
        "accessors": accessors,
        "bufferViews": buffer_views,
        "buffers": [{"byteLength": len(bin_chunks)}],
    }

    json_bytes = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    json_pad = (-len(json_bytes)) % 4
    if json_pad:
        json_bytes += b" " * json_pad

    total_length = 12 + 8 + len(json_bytes) + 8 + len(bin_chunks)

    with open(output_path, "wb") as f:
        # GLB Header: magic, version=2, total_length
        f.write(struct.pack("<III", 0x46546C67, 2, total_length))
        # Chunk 0: JSON
        f.write(struct.pack("<II", len(json_bytes), 0x4E4F534A))
        f.write(json_bytes)
        # Chunk 1: BIN
        f.write(struct.pack("<II", len(bin_chunks), 0x004E4942))
        f.write(bin_chunks)

    return {
        "path": output_path,
        "bytes": total_length,
        "vertices": total_verts,
        "triangles": total_tris,
        "meshes": len(gltf_meshes),
    }
