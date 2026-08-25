from __future__ import annotations

import argparse
import os
import shutil
import sys
import tempfile
from pathlib import Path

from PIL import Image


WORKFLOW = Path(__file__).resolve().parent
ROOT = WORKFLOW.parent
sys.path.insert(0, str(WORKFLOW))

import visual_options as options  # noqa: E402


TARGET_IDS = {
    "A_06_flow_validation_error",
    "C_06_flow_validation_error",
    "A_07_flow_preview_roi",
    "C_08_run_ng_modal",
    "C_09_results_investigation",
    "B_11_station_detail",
}


def opened(path: Path, size: tuple[int, int] | None = None) -> Image.Image:
    with Image.open(path) as source:
        image = source.convert("RGB")
    if size and image.size != size:
        image = image.resize(size, Image.Resampling.LANCZOS)
    return image


def next_archive(target: Path) -> Path:
    iteration_dir = target.parent.parent / "iterations"
    iteration_dir.mkdir(parents=True, exist_ok=True)
    sequence = len(list(iteration_dir.glob(f"{target.stem}_v*.png"))) + 1
    return options.legacy.safe_named_path(iteration_dir, f"{target.stem}_v{sequence}.png")


def save_controlled(target: Path, image: Image.Image) -> Path:
    archived = next_archive(target)
    shutil.copy2(target, archived)
    with tempfile.NamedTemporaryFile(dir=target.parent, suffix=".tmp", delete=False) as handle:
        temporary = Path(handle.name)
    try:
        image.save(temporary, format="PNG", optimize=True)
        os.replace(temporary, target)
    finally:
        temporary.unlink(missing_ok=True)
    return archived


def archived_source(current_id: str) -> Path:
    manifest = options.load_manifest()
    entry = next(item for item in manifest["entries"] if item["id"] == current_id)
    return options.legacy.root_path(entry["archived_previous_output"], must_exist=True)


def composite_a06() -> tuple[Image.Image, list[str], str]:
    target = options.option_image("A", "screens", "06_flow_validation_error.png", must_exist=True)
    image = opened(target)
    current_path = options.legacy.root_path("current/r2/S04-B2.png", must_exist=True)
    current = opened(current_path, image.size)
    width, height = image.size
    workspace_y = round(height * 0.16)
    right_x = round(width * 0.766)
    image.paste(current.crop((0, 0, width, workspace_y)), (0, 0))
    image.paste(current.crop((right_x, workspace_y, width, height)), (right_x, workspace_y))
    return image, [options.legacy.rel(current_path)], "Restore CURRENT top shell and Preview/result rail"


def composite_c06() -> tuple[Image.Image, list[str], str]:
    target = options.option_image("C", "screens", "06_flow_validation_error.png", must_exist=True)
    source_path = archived_source("C_06_flow_validation_error")
    source = opened(source_path)
    master_path = options.option_image("C", "masters", "05_flow_editor.png", must_exist=True)
    image = opened(master_path, source.size)
    width, height = image.size
    source_y = round(height * 0.165)
    workspace_y = round(height * 0.132)
    right_x = round(width * 0.79)
    workspace = source.crop((0, source_y, round(width * 0.772), height)).resize(
        (right_x, height - workspace_y), Image.Resampling.LANCZOS
    )
    image.paste(workspace, (0, workspace_y))
    return image, [options.legacy.rel(master_path), options.legacy.rel(source_path)], "Place the validation workspace inside the exact same-option Master shell"


def composite_a07() -> tuple[Image.Image, list[str], str]:
    target = options.option_image("A", "screens", "07_flow_preview_roi.png", must_exist=True)
    image = opened(target)
    master_path = options.option_image("A", "masters", "05_flow_editor.png", must_exist=True)
    current_path = options.legacy.root_path("current/r2/S05-B2.png", must_exist=True)
    master = opened(master_path, image.size)
    current = opened(current_path, image.size)
    width, height = image.size
    workspace_y = round(height * 0.16)
    right_x = round(width * 0.766)
    image.paste(master.crop((0, 0, width, workspace_y)), (0, 0))
    image.paste(current.crop((right_x, workspace_y, width, height)), (right_x, workspace_y))
    return image, [options.legacy.rel(master_path), options.legacy.rel(current_path)], "Restore audited shell and CURRENT ROI Preview rail"


def composite_c08() -> tuple[Image.Image, list[str], str]:
    target = options.option_image("C", "screens", "08_run_ng_modal.png", must_exist=True)
    candidate = opened(target)
    current_path = options.legacy.root_path("current/r2/S06-B0.png", must_exist=True)
    current = opened(current_path, candidate.size)
    width, height = candidate.size
    modal_box = (
        round(width * 0.263),
        round(height * 0.17),
        round(width * 0.758),
        round(height * 0.872),
    )
    current.paste(candidate.crop(modal_box), modal_box[:2])
    return current, [options.legacy.rel(current_path)], "Use CURRENT dimmed workspace and retain the image2 modal layer"


def composite_c09() -> tuple[Image.Image, list[str], str]:
    target = options.option_image("C", "screens", "09_results_investigation.png", must_exist=True)
    source_path = options.option_image("C", "iterations", "09_results_investigation_v2.png", must_exist=True)
    candidate = opened(source_path)
    current_path = options.legacy.root_path("current/r2/S07-B0.png", must_exist=True)
    current = opened(current_path, candidate.size)
    width, height = candidate.size
    rail_width = round(width * 0.042)
    image = candidate.crop((rail_width, 0, width, height)).resize(
        (width, height), Image.Resampling.LANCZOS
    )
    header_y = round(height * 0.06)
    image.paste(current.crop((0, 0, width, header_y)), (0, 0))
    return image, [options.legacy.rel(current_path), options.legacy.rel(source_path)], "Restore CURRENT header and remove the invented icon rail"


def composite_b11() -> tuple[Image.Image, list[str], str]:
    target = options.option_image("B", "screens", "11_station_detail.png", must_exist=True)
    candidate = opened(target)
    width, height = candidate.size
    header_y = round(height * 0.068)
    rail_width = round(width * 0.144)
    body = candidate.crop((rail_width, header_y, width, height)).resize(
        (width, height - header_y), Image.Resampling.LANCZOS
    )
    candidate.paste(body, (0, header_y))
    return candidate, [], "Remove duplicated Station side navigation while retaining the audited top shell"


COMPOSITES = {
    "A_06_flow_validation_error": composite_a06,
    "C_06_flow_validation_error": composite_c06,
    "A_07_flow_preview_roi": composite_a07,
    "C_08_run_ng_modal": composite_c08,
    "C_09_results_investigation": composite_c09,
    "B_11_station_detail": composite_b11,
}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--ids", help="Comma-separated subset of controlled composites")
    args = parser.parse_args()
    selected_ids = TARGET_IDS
    if args.ids:
        selected_ids = {value.strip() for value in args.ids.split(",") if value.strip()}
        unsupported = selected_ids - TARGET_IDS
        if unsupported:
            raise ValueError(f"Unsupported ids: {', '.join(sorted(unsupported))}")
    manifest = options.load_manifest()
    entries = {entry["id"]: entry for entry in manifest["entries"]}
    missing = selected_ids - entries.keys()
    if missing:
        raise ValueError(f"Missing entries: {', '.join(sorted(missing))}")

    for current_id in sorted(selected_ids):
        entry = entries[current_id]
        target = options.option_image(entry["option"], "screens", entry["filename"], must_exist=True)
        image, sources, note = COMPOSITES[current_id]()
        archived = save_controlled(target, image)
        entry["status"] = "Generated"
        entry["generated_at"] = options.utc_now()
        entry["sha256"] = options.legacy.sha256(target)
        entry["actual_dimensions"] = {"width": image.width, "height": image.height}
        entry["archived_previous_output"] = options.legacy.rel(archived)
        generation = entry.setdefault("generation", {})
        generation["reference_policy"] = "controlled-functional-layer-composite"
        generation["postprocess"] = {
            "method": "deterministic-picture-layer-composite",
            "note": note,
            "sources": sources,
            "applied_at": options.utc_now(),
        }
        print(f"[{current_id}] composited -> {options.legacy.rel(target)}")

    options.legacy.atomic_write_json(options.MANIFEST_PATH, manifest)


if __name__ == "__main__":
    main()
