from __future__ import annotations

import json
from pathlib import Path

from PIL import Image, ImageDraw

import visual_options as vo


OPTIONS = ("C", "D", "E")
OPTION_NAMES = {
    "C": "Modern AI Engineering",
    "D": vo.OPTION_DEFINITIONS["D"]["name"],
    "E": vo.OPTION_DEFINITIONS["E"]["name"],
}
EXPECTED_DIMENSIONS = {
    "C": (1672, 941),
    "D": (3840, 2160),
    "E": (3840, 2160),
}
OUTPUT_DIR = vo.ROOT / "audit" / "comparison_CDE"
ABC_MANIFEST = vo.ROOT / "archive" / "abc_round_20260816" / "manifest_ABC.json"


def option_screen(option: str, filename: str) -> Path:
    return vo.legacy.safe_named_path(
        vo.ROOT / f"option_{option}" / "screens", filename, must_exist=True
    )


def option_master(option: str, filename: str) -> Path:
    return vo.legacy.safe_named_path(
        vo.ROOT / f"option_{option}" / "masters", filename, must_exist=True
    )


def verify_inputs() -> None:
    active = vo.load_manifest()
    errors = vo.validate_manifest(
        active, require_masters=True, require_outputs=True, require_reviews=True
    )
    if errors:
        raise ValueError("D/E final readiness failed:\n" + "\n".join(errors))

    archive = json.loads(ABC_MANIFEST.read_text(encoding="utf-8"))
    expected_archive = {
        "model": "gpt-image-2",
        "model_fallback_used": False,
        "screen_count": 24,
        "option_count": 3,
        "entry_count": 72,
        "identical_coverage": True,
        "functional_audit": "passed-for-all-entries",
    }
    for key, expected in expected_archive.items():
        if archive.get(key) != expected:
            raise ValueError(f"Archived C evidence drifted: {key}")

    expected_files = {page["filename"] for page in vo.PAGES}
    archive_by_screen = {item["screen_id"]: item for item in archive["screens"]}
    for option in OPTIONS:
        screen_dir = vo.ROOT / f"option_{option}" / "screens"
        actual_files = {path.name for path in screen_dir.glob("*.png")}
        if actual_files != expected_files:
            missing = sorted(expected_files - actual_files)
            extra = sorted(actual_files - expected_files)
            raise ValueError(f"Option {option} coverage drifted; missing={missing}, extra={extra}")
        for page in vo.PAGES:
            path = option_screen(option, page["filename"])
            with Image.open(path) as image:
                dimensions = image.size
                image.verify()
            if dimensions != EXPECTED_DIMENSIONS[option]:
                raise ValueError(
                    f"Unexpected dimensions for {option}/{page['filename']}: {dimensions}"
                )
            if option == "C":
                archived = archive_by_screen[page["screen_id"]]["options"]["C"]
                if archived["sha256"] != vo.legacy.sha256(path):
                    raise ValueError(f"Archived C hash drifted: {page['screen_id']}")


def comparison_sheet(page: dict[str, object], output: Path) -> None:
    cell_width, image_height, label_height = 900, 506, 50
    paths = [vo.legacy.root_path(str(page["current_reference"]), must_exist=True)]
    paths.extend(option_screen(option, str(page["filename"])) for option in OPTIONS)
    labels = ["CURRENT | FUNCTION AUTHORITY"]
    labels.extend(f"OPTION {option} | {OPTION_NAMES[option]}" for option in OPTIONS)

    canvas = Image.new(
        "RGB", (cell_width * len(paths), image_height + label_height), "#171c22"
    )
    draw = ImageDraw.Draw(canvas)
    font = vo.load_font(21)
    for index, (label, path) in enumerate(zip(labels, paths)):
        x = index * cell_width
        canvas.paste(
            vo.fit_image(path, (cell_width, image_height), "#222932"),
            (x, label_height),
        )
        draw.text((x + 18, 13), label, fill="#f0f3f5", font=font)
        if index:
            draw.line((x, 0, x, image_height + label_height), fill="#66717c", width=2)
    output.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(output, format="PNG", optimize=True)


def write_audit_index() -> None:
    lines = [
        "# ClearVision C/D/E Visual Audit Index",
        "",
        (
            "All generated images are visual references. Current ClearVision screenshots, "
            "current code, and current contracts remain authoritative for copy, controls, "
            "routes, workflow names, state, and business data."
        ),
        "",
        "## Delivery Status",
        "",
        "- Frozen real screens/states: `24`.",
        "- Option coverage: `C=24`, `D=24`, `E=24` with identical filenames.",
        "- Model: exact `gpt-image-2`; fallback: `false`.",
        "- C evidence: archived functional gate `24/24` plus targeted re-audit; the archived schema predates the five-part Reject Gate.",
        "- D/E evidence: active functional gate `48/48` and five-part Reject Gate `48/48`.",
        "- Product-owner status: awaiting selection; no visual option is approved yet.",
        "",
        "## Design Directions",
        "",
        "- C - Modern AI Engineering: cool layered work zones with a slightly more breathable AI-assisted engineering character.",
        "- D - Roboflow Workflow Engineering: light canvas-first topology, contextual tools, and low permanent chrome translated to verified ClearVision functions only.",
        "- E - Apple-inspired Premium Engineering: refined achromatic materials, exact typography, disciplined spacing, and quiet high-density engineering finish.",
        "",
        "## Master Chains",
        "",
        "- `C_FLOW_MASTER -> C_AI_MASTER -> C_SETTINGS_MASTER`",
        "- `D_FLOW_MASTER -> D_AI_MASTER -> D_SETTINGS_MASTER -> D_FULL_SET`",
        "- `E_FLOW_MASTER -> E_AI_MASTER -> E_SETTINGS_MASTER -> E_FULL_SET`",
        "",
        "C, D, and E each have three image Master Screens. `D_FULL_SET` and `E_FULL_SET` name the 24-screen suite stage, not a fourth Master image. Each option references only its own Masters. CURRENT remains the functional authority for every local screen.",
        "",
        "## Fast Review",
        "",
        "- `cde_comparison_index.png`: overview of all 24 `CURRENT | C | D | E` page sheets.",
        "- `option_C_contact_sheet.png`, `option_D_contact_sheet.png`, `option_E_contact_sheet.png`: whole-option scans.",
        "- `cde_master_contact_sheet.png`: the nine option-specific Master Screens.",
        "- `functional_truth_audit.md`: rejected hallucinations, accepted corrections, and evidence boundaries.",
        "- Individual page sheets in this directory preserve the same `CURRENT | C | D | E` order.",
        "",
        "## Page Mapping",
        "",
        "| Screen | Page | Current | C | D | E |",
        "| --- | --- | --- | --- | --- | --- |",
    ]
    for page in vo.PAGES:
        filename = str(page["filename"])
        lines.append(
            "| `{screen}` | {page_name} | `{current}` | `option_C/screens/{filename}` | "
            "`option_D/screens/{filename}` | `option_E/screens/{filename}` |".format(
                screen=page["screen_id"],
                page_name=page["page_name"],
                current=page["current_reference"],
                filename=filename,
            )
        )
    lines.extend(
        [
            "",
            "## Evidence Boundary",
            "",
            "- Generated text, numbers, labels, and data are never product facts.",
            "- C is retained from the audited archived A/B/C round; it has no five-part Reject Gate fields.",
            "- D/E are the active v3 manifest set and passed the current five-part Reject Gate.",
            "- Static Chromium evidence does not prove real WebView2 behavior, Windows 125% DPI, authenticated live endpoints, physical Camera/PLC/Station operation, release publish, or full CI.",
            "- These files do not authorize frontend implementation; product-owner selection is still required.",
            "",
        ]
    )
    (OUTPUT_DIR / "audit_index.md").write_text("\n".join(lines), encoding="utf-8")


def build() -> None:
    verify_inputs()
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    comparison_items: list[tuple[str, Path]] = []
    option_items: dict[str, list[tuple[str, Path]]] = {option: [] for option in OPTIONS}
    for page in vo.PAGES:
        output = vo.legacy.safe_named_path(OUTPUT_DIR, str(page["filename"]))
        comparison_sheet(page, output)
        comparison_items.append((str(page["screen_id"]), output))
        for option in OPTIONS:
            option_items[option].append(
                (str(page["screen_id"]), option_screen(option, str(page["filename"])))
            )

    for option in OPTIONS:
        vo.contact_sheet(
            option_items[option],
            OUTPUT_DIR / f"option_{option}_contact_sheet.png",
            f"ClearVision Option {option} | {OPTION_NAMES[option]}",
        )

    vo.contact_sheet(
        comparison_items,
        OUTPUT_DIR / "cde_comparison_index.png",
        "ClearVision CURRENT | C | D | E - Comparison Index",
        columns=2,
        card_width=1080,
        preview_height=160,
    )

    master_items: list[tuple[str, Path]] = []
    master_files = (
        ("FLOW", "05_flow_editor.png"),
        ("AI", "13_ai_workspace.png"),
        ("SETTINGS", "16_system_settings.png"),
    )
    for option in OPTIONS:
        for role, filename in master_files:
            master_items.append(
                (f"{option}_{role} | {OPTION_NAMES[option]}", option_master(option, filename))
            )
    vo.contact_sheet(
        master_items,
        OUTPUT_DIR / "cde_master_contact_sheet.png",
        "ClearVision C/D/E Master Screens",
        columns=3,
    )
    write_audit_index()
    print(f"Built {len(comparison_items)} CURRENT/C/D/E page sheets in {vo.legacy.rel(OUTPUT_DIR)}")


if __name__ == "__main__":
    build()
