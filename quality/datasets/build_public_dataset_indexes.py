from __future__ import annotations

import argparse
import json
import struct
import zlib
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
DATA_ROOT = REPO_ROOT / "quality" / "public_datasets"
OUTPUT_ROOT = REPO_ROOT / "quality" / "datasets"
IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff"}
PLAN_SCOPE_DATASETS = (
    "hpatches",
    "coco2017",
    "kolektorsdd2",
    "mvtec_ad_lite",
    "bsds500",
    "opencv_calibration_samples",
)
SUPPORTED_DATASETS = (
    *PLAN_SCOPE_DATASETS,
    "mvtec_ad_full",
    "mvtec_loco_ad",
    "mvtec_ad2_public",
    "biped_v2",
    "uded",
)


def repo_rel(path: Path) -> str:
    return path.resolve().relative_to(REPO_ROOT).as_posix()


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def read_png_chunks(path: Path) -> tuple[tuple[int, int, int, int], bytes]:
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"Not a PNG file: {path}")

    offset = 8
    ihdr: tuple[int, int, int, int] | None = None
    idat_parts: list[bytes] = []
    while offset < len(data):
        if offset + 8 > len(data):
            raise ValueError(f"Truncated PNG chunk header: {path}")
        length = struct.unpack(">I", data[offset : offset + 4])[0]
        kind = data[offset + 4 : offset + 8]
        chunk_data = data[offset + 8 : offset + 8 + length]
        offset += 12 + length

        if kind == b"IHDR":
            if len(chunk_data) != 13:
                raise ValueError(f"Invalid PNG IHDR length: {path}")
            width, height, bit_depth, color_type = struct.unpack(">IIBB", chunk_data[:10])
            ihdr = (width, height, bit_depth, color_type)
        elif kind == b"IDAT":
            idat_parts.append(chunk_data)
        elif kind == b"IEND":
            break

    if ihdr is None:
        raise ValueError(f"PNG missing IHDR: {path}")
    return ihdr, b"".join(idat_parts)


def png_has_nonzero_pixels(path: Path) -> bool:
    (width, height, bit_depth, color_type), compressed = read_png_chunks(path)
    if bit_depth != 8:
        raise ValueError(f"Unsupported PNG bit depth {bit_depth}: {path}")

    channels_by_color = {
        0: 1,  # grayscale
        2: 3,  # RGB
        3: 1,  # indexed palette
        4: 2,  # grayscale+alpha
        6: 4,  # RGBA
    }
    if color_type not in channels_by_color:
        raise ValueError(f"Unsupported PNG color type {color_type}: {path}")

    channels = channels_by_color[color_type]
    row_bytes = width * channels
    raw = zlib.decompress(compressed)
    expected = (row_bytes + 1) * height
    if len(raw) < expected:
        raise ValueError(f"PNG data shorter than expected: {path}")

    prev = bytearray(row_bytes)
    pos = 0
    for _ in range(height):
        filter_type = raw[pos]
        pos += 1
        row = bytearray(raw[pos : pos + row_bytes])
        pos += row_bytes

        for i in range(row_bytes):
            left = row[i - channels] if i >= channels else 0
            up = prev[i]
            up_left = prev[i - channels] if i >= channels else 0
            if filter_type == 1:
                row[i] = (row[i] + left) & 0xFF
            elif filter_type == 2:
                row[i] = (row[i] + up) & 0xFF
            elif filter_type == 3:
                row[i] = (row[i] + ((left + up) // 2)) & 0xFF
            elif filter_type == 4:
                p = left + up - up_left
                pa = abs(p - left)
                pb = abs(p - up)
                pc = abs(p - up_left)
                predictor = left if pa <= pb and pa <= pc else up if pb <= pc else up_left
                row[i] = (row[i] + predictor) & 0xFF
            elif filter_type != 0:
                raise ValueError(f"Unsupported PNG filter {filter_type}: {path}")

        if any(row):
            return True
        prev = row

    return False


def build_bsds500_index() -> None:
    root = DATA_ROOT / "bsds500"
    data_root = root / "extracted" / "BSR" / "BSDS500" / "data"
    archive = root / "_downloads" / "BSR_bsds500.tgz"
    if not data_root.exists():
        raise FileNotFoundError(f"BSDS500 data root not found: {data_root}")

    records: list[dict[str, Any]] = []
    split_counts: dict[str, int] = {}
    for split in ("train", "val", "test"):
        image_dir = data_root / "images" / split
        gt_dir = data_root / "groundTruth" / split
        images = sorted(image_dir.glob("*.jpg"))
        split_counts[split] = len(images)
        for image in images:
            stem = image.stem
            gt = gt_dir / f"{stem}.mat"
            records.append(
                {
                    "id": stem,
                    "split": split,
                    "image_path": repo_rel(image),
                    "ground_truth_path": repo_rel(gt),
                    "has_ground_truth": gt.exists(),
                }
            )

    write_json(
        OUTPUT_ROOT / "bsds500_index.json",
        {
            "name": "BSDS500",
            "source_dataset": "Berkeley Segmentation Dataset and Benchmark 500",
            "created_at": "2026-04-29",
            "local_root": repo_rel(root),
            "archive": {
                "path": repo_rel(archive),
                "size_bytes": archive.stat().st_size,
            },
            "split_counts": split_counts,
            "record_count": len(records),
            "records": records,
        },
    )


def build_opencv_calibration_index() -> None:
    root = DATA_ROOT / "opencv_calibration_samples"
    if not root.exists():
        raise FileNotFoundError(f"OpenCV calibration sample root not found: {root}")

    pairs: list[dict[str, Any]] = []
    for left in sorted(root.glob("left[0-9][0-9].jpg")):
        index = left.stem[-2:]
        right = root / f"right{index}.jpg"
        if right.exists():
            pairs.append(
                {
                    "index": index,
                    "left_image_path": repo_rel(left),
                    "right_image_path": repo_rel(right),
                }
            )

    single_camera_images = [
        repo_rel(path)
        for path in sorted(root.glob("left[0-9][0-9].jpg"))
    ]

    write_json(
        OUTPUT_ROOT / "opencv_calibration_samples_index.json",
        {
            "name": "OpenCV calibration samples",
            "source_dataset": "opencv/opencv samples/data",
            "created_at": "2026-04-29",
            "local_root": repo_rel(root),
            "single_camera_images": single_camera_images,
            "stereo_pairs": pairs,
            "calibration_files": {
                "intrinsics": repo_rel(root / "intrinsics.yml"),
                "left_intrinsics": repo_rel(root / "left_intrinsics.yml"),
                "stereo_calib": repo_rel(root / "stereo_calib.xml"),
            },
            "record_count": len(pairs),
        },
    )


def build_kolektorsdd2_index() -> None:
    root = DATA_ROOT / "kolektorsdd2"
    extracted = root / "extracted"
    archive = root / "_downloads" / "KolektorSDD2.zip"
    if not extracted.exists():
        raise FileNotFoundError(f"KolektorSDD2 extracted root not found: {extracted}")

    records: list[dict[str, Any]] = []
    split_counts: dict[str, dict[str, int]] = {}
    for split in ("train", "test"):
        split_dir = extracted / split
        images = sorted(
            path
            for path in split_dir.glob("*.png")
            if "_GT" not in path.stem and "(copy)" not in path.stem
        )
        positive = 0
        negative = 0
        missing_masks = 0
        for image in images:
            mask = image.with_name(f"{image.stem}_GT.png")
            has_mask = mask.exists()
            is_defect = png_has_nonzero_pixels(mask) if has_mask else False
            if is_defect:
                positive += 1
            else:
                negative += 1
            if not has_mask:
                missing_masks += 1

            records.append(
                {
                    "id": image.stem,
                    "split": split,
                    "image_path": repo_rel(image),
                    "mask_path": repo_rel(mask) if has_mask else "",
                    "has_mask": has_mask,
                    "is_defect": is_defect,
                }
            )

        split_counts[split] = {
            "images": len(images),
            "positive": positive,
            "negative": negative,
            "missing_masks": missing_masks,
        }

    write_json(
        OUTPUT_ROOT / "kolektorsdd2_index.json",
        {
            "name": "KolektorSDD2",
            "source_dataset": "Kolektor Surface-Defect Dataset 2",
            "created_at": "2026-04-29",
            "local_root": repo_rel(root),
            "archive": {
                "path": repo_rel(archive),
                "size_bytes": archive.stat().st_size,
            },
            "split_counts": split_counts,
            "record_count": len(records),
            "records": records,
        },
    )


def build_coco2017_index() -> None:
    root = DATA_ROOT / "coco2017"
    extracted = root / "extracted"
    image_dir = extracted / "val2017"
    annotations = extracted / "annotations" / "instances_val2017.json"
    if not image_dir.exists() or not annotations.exists():
        raise FileNotFoundError(f"COCO 2017 val images/annotations not found under: {extracted}")

    with annotations.open("r", encoding="utf-8") as handle:
        data = json.load(handle)

    annotations_by_image: dict[int, list[dict[str, Any]]] = {}
    for annotation in data.get("annotations", []):
        annotations_by_image.setdefault(int(annotation["image_id"]), []).append(annotation)

    categories = {int(item["id"]): item["name"] for item in data.get("categories", [])}
    records: list[dict[str, Any]] = []
    for image in sorted(data.get("images", []), key=lambda item: item["file_name"]):
        image_id = int(image["id"])
        file_name = image["file_name"]
        labels = annotations_by_image.get(image_id, [])
        records.append(
            {
                "id": str(image_id),
                "split": "val2017",
                "image_path": repo_rel(image_dir / file_name),
                "width": image.get("width"),
                "height": image.get("height"),
                "annotation_count": len(labels),
                "category_ids": sorted({int(label["category_id"]) for label in labels}),
            }
        )

    write_json(
        OUTPUT_ROOT / "coco2017_index.json",
        {
            "name": "COCO 2017 validation",
            "source_dataset": "COCO 2017",
            "created_at": "2026-04-29",
            "local_root": repo_rel(root),
            "image_split": repo_rel(image_dir),
            "annotation_file": repo_rel(annotations),
            "category_count": len(categories),
            "annotation_count": len(data.get("annotations", [])),
            "record_count": len(records),
            "records": records,
        },
    )


def build_hpatches_index() -> None:
    root = DATA_ROOT / "hpatches"
    extracted = root / "extracted"
    sequence_root = extracted / "hpatches-sequences-release"
    if not sequence_root.exists():
        sequence_root = extracted
    if not sequence_root.exists():
        raise FileNotFoundError(f"HPatches extracted root not found: {extracted}")

    records: list[dict[str, Any]] = []
    for sequence in sorted(path for path in sequence_root.iterdir() if path.is_dir()):
        image_paths = sorted(sequence.glob("*.ppm"))
        homographies = sorted(sequence.glob("H_1_*"))
        if not image_paths:
            continue
        records.append(
            {
                "id": sequence.name,
                "sequence_path": repo_rel(sequence),
                "image_count": len(image_paths),
                "images": [repo_rel(path) for path in image_paths],
                "homography_count": len(homographies),
                "homographies": [repo_rel(path) for path in homographies],
                "sequence_type": "illumination" if sequence.name.startswith("i_") else "viewpoint"
                if sequence.name.startswith("v_")
                else "unknown",
            }
        )

    write_json(
        OUTPUT_ROOT / "hpatches_index.json",
        {
            "name": "HPatches",
            "source_dataset": "HPatches image matching benchmark",
            "created_at": "2026-04-29",
            "local_root": repo_rel(root),
            "sequence_root": repo_rel(sequence_root),
            "record_count": len(records),
            "records": records,
        },
    )


def find_mvtec_payload_root(root: Path) -> Path:
    candidates = [root / "extracted", root]
    for candidate in candidates:
        if not candidate.exists():
            continue

        category_dirs = [
            path
            for path in candidate.iterdir()
            if path.is_dir() and (path / "train").is_dir() and (path / "test").is_dir()
        ]
        if category_dirs:
            return candidate

        for child in sorted(path for path in candidate.iterdir() if path.is_dir()):
            category_dirs = [
                path
                for path in child.iterdir()
                if path.is_dir() and (path / "train").is_dir() and (path / "test").is_dir()
            ]
            if category_dirs:
                return child

    raise FileNotFoundError(f"MVTec-style extracted root not found under: {root}")


def find_mvtec_mask(category_dir: Path, defect_type: str, image_stem: str) -> Path | None:
    mask_dir = category_dir / "ground_truth" / defect_type
    if not mask_dir.exists():
        return None

    candidates = [
        mask_dir / f"{image_stem}_mask.png",
        mask_dir / f"{image_stem}.png",
        mask_dir / f"{image_stem}_mask.bmp",
        mask_dir / f"{image_stem}.bmp",
    ]
    return next((path for path in candidates if path.exists()), None)


def build_mvtec_style_index(
    dataset_id: str,
    display_name: str,
    source_dataset: str,
    output_name: str,
    license_id: str,
) -> None:
    root = DATA_ROOT / dataset_id
    payload_root = find_mvtec_payload_root(root)
    records: list[dict[str, Any]] = []
    split_counts: dict[str, dict[str, int]] = {}
    category_counts: dict[str, dict[str, int]] = {}

    for category_dir in sorted(path for path in payload_root.iterdir() if path.is_dir()):
        if not (category_dir / "train").is_dir() or not (category_dir / "test").is_dir():
            continue

        category = category_dir.name
        category_counts[category] = {"train": 0, "test": 0, "anomaly": 0, "normal": 0}
        for split_dir in sorted(path for path in category_dir.iterdir() if path.is_dir() and path.name in {"train", "test", "val"}):
            split = split_dir.name
            for defect_dir in sorted(path for path in split_dir.iterdir() if path.is_dir()):
                defect_type = defect_dir.name
                for image in sorted(path for path in defect_dir.iterdir() if path.suffix.lower() in IMAGE_EXTENSIONS):
                    is_anomaly = split != "train" and defect_type.lower() not in {"good", "normal", "ok"}
                    mask = find_mvtec_mask(category_dir, defect_type, image.stem) if is_anomaly else None
                    records.append(
                        {
                            "id": f"{category}/{split}/{defect_type}/{image.stem}",
                            "category": category,
                            "split": split,
                            "defect_type": defect_type,
                            "image_path": repo_rel(image),
                            "mask_path": repo_rel(mask) if mask is not None else "",
                            "is_anomaly": is_anomaly,
                            "has_mask": mask is not None,
                        }
                    )

                    split_counts.setdefault(split, {"images": 0, "anomaly": 0, "normal": 0})
                    split_counts[split]["images"] += 1
                    split_counts[split]["anomaly" if is_anomaly else "normal"] += 1
                    category_counts[category][split] = category_counts[category].get(split, 0) + 1
                    category_counts[category]["anomaly" if is_anomaly else "normal"] += 1

    if not records:
        raise FileNotFoundError(f"No MVTec-style image records found under: {payload_root}")

    write_json(
        OUTPUT_ROOT / output_name,
        {
            "name": display_name,
            "source_dataset": source_dataset,
            "created_at": "2026-04-30",
            "local_root": repo_rel(root),
            "payload_root": repo_rel(payload_root),
            "license": license_id,
            "split_counts": split_counts,
            "category_counts": category_counts,
            "record_count": len(records),
            "records": records,
            "claim_boundary": "Public benchmark research evidence only; not real production-site validation or sign-off.",
        },
    )


def infer_split(path: Path) -> str:
    parts = {part.lower() for part in path.parts}
    if "train" in parts or "training" in parts:
        return "train"
    if "val" in parts or "validation" in parts:
        return "val"
    if "test" in parts or "testing" in parts:
        return "test"
    return "unknown"


def is_edge_label_path(path: Path) -> bool:
    parts = {part.lower() for part in path.parts}
    return any(
        token in part
        for part in parts
        for token in ("edge", "edges", "label", "labels", "gt", "mask", "groundtruth", "ground_truth", "annotation")
    )


def candidate_edge_label_paths(image: Path, dataset_root: Path) -> list[Path]:
    relative = image.relative_to(dataset_root)
    parts = list(relative.parts)
    replacements = {
        "imgs": "edge_maps",
        "images": "edges",
        "image": "edge",
        "rgb": "edge",
        "rgbr": "edge",
        "data": "labels",
    }

    candidates: list[Path] = []
    for index, part in enumerate(parts[:-1]):
        lower = part.lower()
        if lower in replacements:
            replaced = parts.copy()
            replaced[index] = replacements[lower]
            for suffix in (".png", ".jpg", ".jpeg", ".bmp"):
                replaced[-1] = f"{image.stem}{suffix}"
                candidates.append(dataset_root / Path(*replaced))

    for name in (
        f"{image.stem}.png",
        f"{image.stem}_edge.png",
        f"{image.stem}_edges.png",
        f"{image.stem}_gt.png",
        f"{image.stem}_label.png",
    ):
        candidates.append(image.with_name(name))

    return candidates


def build_generic_edge_index(dataset_id: str, display_name: str, source_dataset: str, output_name: str, license_id: str) -> None:
    root = DATA_ROOT / dataset_id
    dataset_root = root / "extracted" if (root / "extracted").exists() else root
    if not dataset_root.exists():
        raise FileNotFoundError(f"{display_name} root not found: {dataset_root}")

    images = [
        path
        for path in sorted(dataset_root.rglob("*"))
        if path.is_file()
        and path.suffix.lower() in IMAGE_EXTENSIONS
        and "_downloads" not in path.parts
        and not is_edge_label_path(path)
    ]
    records: list[dict[str, Any]] = []
    split_counts: dict[str, dict[str, int]] = {}
    for image in images:
        label = next((path for path in candidate_edge_label_paths(image, dataset_root) if path.exists()), None)
        split = infer_split(image.relative_to(dataset_root))
        records.append(
            {
                "id": image.relative_to(dataset_root).with_suffix("").as_posix(),
                "split": split,
                "image_path": repo_rel(image),
                "edge_path": repo_rel(label) if label is not None else "",
                "has_ground_truth": label is not None,
            }
        )
        split_counts.setdefault(split, {"images": 0, "with_ground_truth": 0})
        split_counts[split]["images"] += 1
        if label is not None:
            split_counts[split]["with_ground_truth"] += 1

    if not records:
        raise FileNotFoundError(f"No edge image records found under: {dataset_root}")

    write_json(
        OUTPUT_ROOT / output_name,
        {
            "name": display_name,
            "source_dataset": source_dataset,
            "created_at": "2026-04-30",
            "local_root": repo_rel(root),
            "payload_root": repo_rel(dataset_root),
            "license": license_id,
            "split_counts": split_counts,
            "record_count": len(records),
            "records": records,
            "claim_boundary": "Public edge benchmark research evidence only; not real production-site validation or sign-off.",
        },
    )


def main() -> int:
    global DATA_ROOT

    parser = argparse.ArgumentParser(description="Build manifest-only indexes for downloaded public quality datasets.")
    parser.add_argument(
        "--root",
        default=str(DATA_ROOT.relative_to(REPO_ROOT)),
        help="Public dataset root. Defaults to quality/public_datasets.",
    )
    parser.add_argument(
        "--dataset",
        action="append",
        choices=SUPPORTED_DATASETS,
        help="Dataset to index. Repeatable. Defaults to the six-dataset 2026-05-01 plan scope.",
    )
    args = parser.parse_args()

    root = Path(args.root)
    DATA_ROOT = root if root.is_absolute() else (REPO_ROOT / root)

    datasets = args.dataset or list(PLAN_SCOPE_DATASETS)
    for dataset in datasets:
        if dataset == "bsds500":
            build_bsds500_index()
        elif dataset == "opencv_calibration_samples":
            build_opencv_calibration_index()
        elif dataset == "kolektorsdd2":
            build_kolektorsdd2_index()
        elif dataset == "coco2017":
            build_coco2017_index()
        elif dataset == "hpatches":
            build_hpatches_index()
        elif dataset == "mvtec_ad_lite":
            build_mvtec_style_index("mvtec_ad_lite", "MVTec AD Lite", "MVTec AD Lite subset", "mvtec_ad_lite_index.json", "CC-BY-NC-SA-4.0")
        elif dataset == "mvtec_ad_full":
            build_mvtec_style_index("mvtec_ad_full", "MVTec AD full", "MVTec AD", "mvtec_ad_full_index.json", "CC-BY-NC-SA-4.0")
        elif dataset == "mvtec_loco_ad":
            build_mvtec_style_index("mvtec_loco_ad", "MVTec LOCO AD", "MVTec LOCO AD", "mvtec_loco_ad_index.json", "CC-BY-NC-SA-4.0")
        elif dataset == "mvtec_ad2_public":
            build_mvtec_style_index("mvtec_ad2_public", "MVTec AD 2 public part", "MVTec AD 2", "mvtec_ad2_public_index.json", "MVTec-AD2-dataset-terms")
        elif dataset == "biped_v2":
            build_generic_edge_index("biped_v2", "BIPED v2", "BIPED v2", "biped_v2_index.json", "non-commercial-research")
        elif dataset == "uded":
            build_generic_edge_index("uded", "UDED", "UDED", "uded_index.json", "upstream-research-terms")
        print(f"indexed {dataset}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
