import os
from retro_textures import generate_all_textures
from cathedral_textures import generate_cathedral_textures
from cathedral_exterior import build_cathedral_exterior
from cathedral_interior import build_cathedral_interior
from glb_exporter import export_glb


def build_boss_cathedral_glb(tex_dir, output_glb_path):
    # Ensure both base stomach textures and cathedral textures exist
    base_paths = generate_all_textures(tex_dir)
    cath_paths = generate_cathedral_textures(tex_dir, size=256)
    all_paths = {**base_paths, **cath_paths}

    ext_meshes, ext_anchors = build_cathedral_exterior()
    int_meshes, int_anchors = build_cathedral_interior()

    meshes = ext_meshes + int_meshes
    anchors = {**ext_anchors, **int_anchors}

    materials_spec = {
        "Mat_FleshBricks": {
            "image_path": all_paths["tex_flesh_bricks.png"],
            "roughness": 0.65,
            "metallic": 0.05,
            "double_sided": True,
        },
        "Mat_OssifiedStone": {
            "image_path": all_paths["tex_ossified_stone.png"],
            "roughness": 0.72,
            "metallic": 0.05,
            "double_sided": True,
        },
        "Mat_CathedralFloor": {
            "image_path": all_paths["tex_cathedral_floor.png"],
            "roughness": 0.45,
            "metallic": 0.08,
            "double_sided": True,
        },
        "Mat_StainedGlass": {
            "image_path": all_paths["tex_cathedral_stained_glass.png"],
            "roughness": 0.20,
            "metallic": 0.0,
            "emissive_factor": [0.95, 0.85, 0.75],
            "use_emissive_texture": True,
            "double_sided": True,
        },
        "Mat_RoseWindow": {
            "image_path": all_paths["tex_cathedral_rose_window.png"],
            "roughness": 0.20,
            "metallic": 0.0,
            "emissive_factor": [1.0, 0.90, 0.80],
            "use_emissive_texture": True,
            "double_sided": True,
        },
        "Mat_CathedralDoor": {
            "image_path": all_paths["tex_cathedral_door.png"],
            "roughness": 0.68,
            "metallic": 0.15,
            "double_sided": True,
        },
        "Mat_SpireShingles": {
            "image_path": all_paths["tex_spire_shingles.png"],
            "roughness": 0.55,
            "metallic": 0.18,
            "double_sided": True,
        },
        "Mat_OrganPipes": {
            "image_path": all_paths["tex_organ_pipes.png"],
            "roughness": 0.38,
            "metallic": 0.35,
            "double_sided": True,
        },
        "Mat_HellOrgans": {
            "image_path": all_paths["tex_hell_organs.png"],
            "roughness": 0.45,
            "metallic": 0.05,
            "emissive_factor": [0.35, 0.06, 0.06],
            "use_emissive_texture": True,
            "double_sided": True,
        },
        "Mat_Cartilage": {
            "image_path": all_paths["tex_cartilage.png"],
            "roughness": 0.58,
            "metallic": 0.05,
            "double_sided": True,
        },
        "Mat_DigestedCalcified": {
            "image_path": all_paths["tex_digested_calcified.png"],
            "roughness": 0.75,
            "metallic": 0.04,
            "double_sided": True,
        },
        "Mat_GastricAcid": {
            "image_path": all_paths["tex_acid.png"],
            "roughness": 0.25,
            "metallic": 0.0,
            "emissive_factor": [0.55, 0.95, 0.22],
            "use_emissive_texture": True,
            "double_sided": True,
        },
        "Mat_BloodWaves": {
            "image_path": all_paths["tex_blood_waves.png"],
            "roughness": 0.30,
            "metallic": 0.0,
            "emissive_factor": [0.95, 0.35, 0.20],
            "use_emissive_texture": True,
            "alpha_mode": "MASK",
            "alpha_cutoff": 0.35,
            "double_sided": True,
        },
    }

    stats = export_glb(
        output_path=output_glb_path,
        meshes=meshes,
        materials_spec=materials_spec,
        root_name="Stomach_Boss_Cathedral",
        anchors=list(anchors.items()),
    )
    print(f"[generate_boss_cathedral] Exported {output_glb_path}: {stats}")
    return stats


if __name__ == "__main__":
    base_dir = r"C:\Users\user\IdeaProjects\Game final\Assets\PilotChamber"
    tex_dir = os.path.join(base_dir, "Textures")
    out_glb = os.path.join(base_dir, "Stomach_Boss_Cathedral.glb")
    build_boss_cathedral_glb(tex_dir, out_glb)
