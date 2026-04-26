from __future__ import annotations

import argparse
import json
from dataclasses import dataclass, asdict
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_MANIFEST = REPO_ROOT / "quality" / "datasets" / "mvtec_ad_lite_manifest.json"
DEFAULT_OUTPUT = REPO_ROOT / "quality" / "datasets" / "mvtec_ad_lite_index.json"


@dataclass(frozen=True)
class ImageRecord:
    category: str
    split: str
    defect_type: str
    image_path: str
    mask_path: str | None
    is_anomaly: bool


def repo_relative(path: Path) -> str:
    return path.resolve().relative_to(REPO_ROOT).as_posix()


def collect_category(category: dict, require_files: bool) -> list[ImageRecord]:
    category_name = category["name"]
    root = REPO_ROOT / category["extracted_path"]
    if not root.exists():
        raise FileNotFoundError(
            f"Missing extracted category directory: {root}. "
            "Run quality/datasets/download_mvtec_ad_lite.ps1 first."
        )

    records: list[ImageRecord] = []

    train_good_dir = root / "train" / "good"
    for image_path in sorted(train_good_dir.glob("*.png")):
        records.append(
            ImageRecord(
                category=category_name,
                split="train",
                defect_type="good",
                image_path=repo_relative(image_path),
                mask_path=None,
                is_anomaly=False,
            )
        )

    test_root = root / "test"
    for defect_dir in sorted(path for path in test_root.iterdir() if path.is_dir()):
        defect_type = defect_dir.name
        is_anomaly = defect_type != "good"
        for image_path in sorted(defect_dir.glob("*.png")):
            mask_path: Path | None = None
            if is_anomaly:
                candidate = root / "ground_truth" / defect_type / f"{image_path.stem}_mask.png"
                if require_files and not candidate.exists():
                    raise FileNotFoundError(f"Missing mask for {image_path}: {candidate}")
                mask_path = candidate if candidate.exists() else None

            records.append(
                ImageRecord(
                    category=category_name,
                    split="test",
                    defect_type=defect_type,
                    image_path=repo_relative(image_path),
                    mask_path=repo_relative(mask_path) if mask_path is not None else None,
                    is_anomaly=is_anomaly,
                )
            )

    return records


def build_index(manifest_path: Path, require_files: bool) -> dict:
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    records: list[ImageRecord] = []
    for category in manifest["categories"]:
        records.extend(collect_category(category, require_files=require_files))

    by_category: dict[str, dict[str, int]] = {}
    for record in records:
        summary = by_category.setdefault(
            record.category,
            {"train_good": 0, "test_good": 0, "test_anomaly": 0, "test_masks": 0},
        )
        if record.split == "train" and not record.is_anomaly:
            summary["train_good"] += 1
        elif record.split == "test" and not record.is_anomaly:
            summary["test_good"] += 1
        elif record.split == "test" and record.is_anomaly:
            summary["test_anomaly"] += 1
            if record.mask_path:
                summary["test_masks"] += 1

    return {
        "name": manifest["name"],
        "source_dataset": manifest["source_dataset"],
        "license": manifest["license"],
        "local_root": manifest["local_root"],
        "records": [asdict(record) for record in records],
        "summary": {
            "category_count": len(by_category),
            "record_count": len(records),
            "train_count": sum(1 for record in records if record.split == "train"),
            "test_count": sum(1 for record in records if record.split == "test"),
            "test_anomaly_count": sum(1 for record in records if record.split == "test" and record.is_anomaly),
            "test_good_count": sum(1 for record in records if record.split == "test" and not record.is_anomaly),
            "by_category": by_category,
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Convert local MVTec AD Lite files into a small JSON index.")
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--no-require-files", action="store_true")
    args = parser.parse_args()

    manifest_path = args.manifest if args.manifest.is_absolute() else REPO_ROOT / args.manifest
    output_path = args.output if args.output.is_absolute() else REPO_ROOT / args.output

    index = build_index(manifest_path, require_files=not args.no_require_files)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(index, indent=2), encoding="utf-8")

    summary = index["summary"]
    print(
        "MVTec AD Lite index written: "
        f"{output_path} "
        f"(train={summary['train_count']}, test={summary['test_count']}, anomalies={summary['test_anomaly_count']})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
