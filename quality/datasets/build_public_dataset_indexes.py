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


def main() -> int:
    parser = argparse.ArgumentParser(description="Build manifest-only indexes for downloaded public quality datasets.")
    parser.add_argument(
        "--dataset",
        action="append",
        choices=("bsds500", "opencv_calibration_samples", "kolektorsdd2"),
        help="Dataset to index. Repeatable. Defaults to all supported datasets.",
    )
    args = parser.parse_args()

    datasets = args.dataset or ["bsds500", "opencv_calibration_samples", "kolektorsdd2"]
    for dataset in datasets:
        if dataset == "bsds500":
            build_bsds500_index()
        elif dataset == "opencv_calibration_samples":
            build_opencv_calibration_index()
        elif dataset == "kolektorsdd2":
            build_kolektorsdd2_index()
        print(f"indexed {dataset}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
