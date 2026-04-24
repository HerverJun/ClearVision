from __future__ import annotations

import argparse
import json
import re
from collections import Counter
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_CARD_DIR = REPO_ROOT / "docs" / "算子资料" / "算子名片"
DEFAULT_CATALOG = DEFAULT_CARD_DIR / "CATALOG.md"
DEFAULT_BASELINE = REPO_ROOT / "quality" / "evals" / "reports" / "RegionMorphology_baseline.json"
DEFAULT_OUTPUT = REPO_ROOT / "quality" / "evals" / "reports" / "operator_quality_matrix.md"

QUALITY_RE = re.compile(r"(?P<score>\d+)\s*\((?P<level>[^)]+)\)")
DOC_LINK_RE = re.compile(r"\[([^\]]+)\]\(([^)]+)\)")
SECTION_RE = re.compile(r"^##\s+(.+?)\s*$")
CATEGORY_RE = re.compile(r"^###\s+(.+?)\s+\(\d+\)\s*$")

NON_CORE_CATEGORIES = {
    "Communication",
    "Flow Control",
    "变量",
    "流程控制",
    "通信",
    "辅助",
    "输出",
    "通用",
    "逻辑工具",
    "数据处理",
    "拆分组合",
}

HIGH_VALUE_CATEGORIES = {
    "3D",
    "AI Detection",
    "AI Inspection",
    "AI检测",
    "Detection",
    "Frequency",
    "Morphology",
    "Region",
    "Texture",
    "匹配定位",
    "定位",
    "标定",
    "检测",
    "特征提取",
    "图像处理",
    "预处理",
    "颜色处理",
}

FIRST_BATCH_OPERATORS = {
    "RegionUnion",
    "GradientShapeMatch",
    "TemplateMatching",
    "AnomalyDetection",
    "DeepLearning",
    "CaliperTool",
    "FFT1D",
}

NEXT_ACTION_OVERRIDES = {
    "AnomalyDetection": "Run MVTec AD baseline and record Image/Pixel AUROC",
    "ArcCaliper": "Add arc ROI boundary, polarity, and sub-pixel golden tests",
    "CaliperTool": "Add caliper robustness baseline and failure triage",
    "DeepLearning": "Lock YOLO output contract and expose NMS IoU tests",
    "FFT1D": "Add frequency-domain synthetic signal golden tests",
    "FrequencyFilter": "Add cutoff/phase preservation golden tests",
    "GradientShapeMatch": "Fix cache/Position contract and add multi-candidate tests",
    "InverseFFT1D": "Add inverse reconstruction and energy preservation tests",
    "TemplateMatching": "Add score contract baseline and fixed-scale failure boundary tests",
}


@dataclass(frozen=True)
class CatalogRow:
    operator: str
    operator_type: str
    display_name: str
    category: str
    qscore: int
    level: str
    version: str
    input_count: int
    output_count: int
    param_count: int
    algorithm_summary: str
    card_path: Path


@dataclass(frozen=True)
class CardFacts:
    maturity: str
    known_limitations_count: int
    card_todo_count: int
    algorithm_summary: str


@dataclass(frozen=True)
class GoldenEvidence:
    status: str
    benchmark: str
    case_count: int
    failed: int


def split_markdown_row(line: str) -> list[str]:
    return [cell.strip() for cell in line.strip().strip("|").split("|")]


def strip_markdown(value: str) -> str:
    value = value.strip()
    value = value.replace("`", "")
    return value


def clean_cell(value: str) -> str:
    return value.replace("\n", " ").replace("|", "\\|").strip()


def compact(value: str, limit: int = 90) -> str:
    value = re.sub(r"\s+", " ", strip_markdown(value)).strip()
    if len(value) <= limit:
        return value
    return value[: limit - 1].rstrip() + "..."


def parse_int(value: str) -> int:
    try:
        return int(strip_markdown(value))
    except ValueError:
        return 0


def read_text_compatible(path: Path) -> str:
    for encoding in ("utf-8-sig", "gb18030"):
        try:
            return path.read_text(encoding=encoding)
        except UnicodeDecodeError:
            continue
    return path.read_text(encoding="utf-8", errors="replace")


def parse_catalog(catalog_path: Path, card_dir: Path) -> list[CatalogRow]:
    rows: list[CatalogRow] = []
    current_category = ""

    for line in read_text_compatible(catalog_path).splitlines():
        category_match = CATEGORY_RE.match(line)
        if category_match:
            current_category = category_match.group(1).strip()
            continue

        if not line.startswith("| `OperatorType."):
            continue

        cells = split_markdown_row(line)
        if len(cells) < 9:
            continue

        quality_match = QUALITY_RE.search(cells[5])
        if quality_match is None:
            continue

        doc_match = DOC_LINK_RE.search(cells[8])
        if doc_match is None:
            continue

        operator_type = strip_markdown(cells[0])
        operator = operator_type.split(".")[-1]
        doc_path = doc_match.group(2).replace("./", "")

        rows.append(
            CatalogRow(
                operator=operator,
                operator_type=operator_type,
                display_name=strip_markdown(cells[1]),
                category=current_category,
                qscore=int(quality_match.group("score")),
                level=quality_match.group("level").strip(),
                version=strip_markdown(cells[6]),
                input_count=parse_int(cells[2]),
                output_count=parse_int(cells[3]),
                param_count=parse_int(cells[4]),
                algorithm_summary=compact(cells[7]),
                card_path=card_dir / doc_path,
            )
        )

    return rows


def section_lines(text: str, title_fragment: str) -> list[str]:
    lines = text.splitlines()
    section: list[str] = []
    in_section = False

    for line in lines:
        section_match = SECTION_RE.match(line)
        if section_match:
            if in_section:
                break
            in_section = title_fragment in section_match.group(1)
            continue

        if in_section:
            section.append(line)

    return section


def todo_count(text: str) -> int:
    count = 0
    previous_had_todo = False

    for raw_line in text.splitlines():
        line = raw_line.strip()
        has_todo = "TODO" in line.upper()
        if not has_todo:
            previous_had_todo = False
            continue

        if line.startswith("> English") and previous_had_todo:
            previous_had_todo = True
            continue

        count += 1
        previous_had_todo = True

    return count


def count_list_items(lines: list[str]) -> int:
    count = 0
    for line in lines:
        stripped = line.strip()
        if re.match(r"^(\d+[.)]|[-*])\s+", stripped):
            count += 1
    return count


def parse_basic_info(text: str) -> dict[str, str]:
    facts: dict[str, str] = {}
    lines = section_lines(text, "基本信息")
    for line in lines:
        if not line.strip().startswith("|"):
            continue
        cells = split_markdown_row(line)
        if len(cells) < 2:
            continue
        key = strip_markdown(cells[0])
        value = strip_markdown(cells[1])
        facts[key] = value
    return facts


def first_meaningful_line(lines: list[str]) -> str:
    for line in lines:
        stripped = line.strip().lstrip("> ").strip()
        if not stripped or stripped.startswith("English:"):
            continue
        if stripped.startswith("中文："):
            stripped = stripped.removeprefix("中文：").strip()
        return compact(stripped)
    return "-"


def parse_card(card_path: Path) -> CardFacts:
    if not card_path.exists():
        return CardFacts("-", 0, 0, "-")

    text = read_text_compatible(card_path)
    basic_info = parse_basic_info(text)
    maturity = "-"
    for key, value in basic_info.items():
        if "成熟度" in key or "Maturity" in key:
            maturity = value
            break

    known_limitations = section_lines(text, "已知限制")
    algorithm_lines = section_lines(text, "算法原理")

    return CardFacts(
        maturity=maturity,
        known_limitations_count=count_list_items(known_limitations),
        card_todo_count=todo_count(text),
        algorithm_summary=first_meaningful_line(algorithm_lines),
    )


def load_golden_evidence(baseline_path: Path) -> dict[str, GoldenEvidence]:
    if not baseline_path.exists():
        return {}

    data = json.loads(baseline_path.read_text(encoding="utf-8"))
    evidence: dict[str, GoldenEvidence] = {}

    for item in data.get("Operators", []):
        operator = str(item.get("Operator", "")).strip()
        case_count = int(item.get("CaseCount", 0) or 0)
        failed = int(item.get("Failed", 0) or 0)
        has_runtime = "RuntimeMsAvg" in item and "MemoryAllocationBytesAvg" in item

        if not operator or case_count <= 0:
            continue

        if case_count >= 20 and failed == 0:
            status = "Yes"
        else:
            status = "Partial"

        evidence[operator] = GoldenEvidence(
            status=status,
            benchmark="Yes" if has_runtime else "No",
            case_count=case_count,
            failed=failed,
        )

    return evidence


def priority_for(row: CatalogRow, card: CardFacts) -> str:
    if row.level == "C":
        return "P0"
    if card.card_todo_count > 0 and row.category in HIGH_VALUE_CATEGORIES:
        return "P0"
    if row.category in NON_CORE_CATEGORIES:
        return "P3"
    if row.level == "B" and (row.category in HIGH_VALUE_CATEGORIES or row.operator in FIRST_BATCH_OPERATORS):
        return "P1"
    if row.operator in FIRST_BATCH_OPERATORS:
        return "P2"
    if row.level == "A" and row.category in {"AI Detection", "AI Inspection", "AI检测", "匹配定位", "标定"}:
        return "P2"
    return "P3"


def next_action_for(row: CatalogRow, card: CardFacts, golden: GoldenEvidence) -> str:
    if row.operator in NEXT_ACTION_OVERRIDES and golden.status != "Yes":
        return NEXT_ACTION_OVERRIDES[row.operator]
    if golden.status == "Yes" and card.card_todo_count > 0:
        return "Backfill card/source TODO, then review QScore/Level"
    if golden.status == "Yes":
        return "Review QScore/Level from golden evidence"
    if row.level == "C":
        return "Add golden tests and failure triage"
    if card.card_todo_count > 0:
        return "Clear card TODO and known limitations"
    if row.operator in NEXT_ACTION_OVERRIDES:
        return NEXT_ACTION_OVERRIDES[row.operator]
    if row.category in NON_CORE_CATEGORIES:
        return "Add parameter and error-contract tests"
    return "Add baseline evidence if operator stays in quality scope"


def owner_for(row: CatalogRow, card: CardFacts, golden: GoldenEvidence, priority: str) -> str:
    if row.level == "C" and golden.status != "Yes":
        return "Golden Dataset Agent"
    if card.card_todo_count > 0:
        return "Card Auditor Agent"
    if golden.status != "Yes" and priority in {"P0", "P1", "P2"}:
        return "Golden Dataset Agent"
    if row.category in NON_CORE_CATEGORIES:
        return "Contract Test Agent"
    return "Quality Flywheel Agent"


def evidence_or_default(evidence: dict[str, GoldenEvidence], operator: str) -> GoldenEvidence:
    return evidence.get(operator, GoldenEvidence(status="No", benchmark="No", case_count=0, failed=0))


def render_matrix(rows: list[CatalogRow], card_dir: Path, catalog_path: Path, baseline_path: Path, evidence: dict[str, GoldenEvidence]) -> str:
    facts_by_operator = {row.operator: parse_card(row.card_path) for row in rows}

    enriched = []
    for row in rows:
        card = facts_by_operator[row.operator]
        golden = evidence_or_default(evidence, row.operator)
        priority = priority_for(row, card)
        enriched.append((priority, row.qscore, row.operator, row, card, golden))

    enriched.sort(key=lambda item: (item[0], item[1], item[2]))

    level_counts = Counter(row.level for _, _, _, row, _, _ in enriched)
    priority_counts = Counter(priority for priority, *_ in enriched)
    golden_counts = Counter(golden.status for *_, golden in enriched)
    todo_rows = [item for item in enriched if item[4].card_todo_count > 0]
    p0_rows = [item for item in enriched if item[0] == "P0"]
    p0_without_golden = [item for item in p0_rows if item[5].status != "Yes"]
    c_without_golden = [item for item in enriched if item[3].level == "C" and item[5].status != "Yes"]

    lines: list[str] = [
        "# Operator Quality Matrix",
        "",
        f"GeneratedAtUtc: `{datetime.now(timezone.utc).isoformat(timespec='seconds')}`",
        f"SourceCatalog: `{catalog_path.relative_to(REPO_ROOT).as_posix()}`",
        f"CardDirectory: `{card_dir.relative_to(REPO_ROOT).as_posix()}`",
        f"GoldenEvidence: `{baseline_path.relative_to(REPO_ROOT).as_posix()}`",
        "",
        "## Summary",
        "",
        f"- Total operators: {len(enriched)}",
        f"- Level counts: {format_counts(level_counts, ['A', 'B', 'C'])}",
        f"- Priority counts: {format_counts(priority_counts, ['P0', 'P1', 'P2', 'P3'])}",
        f"- Golden test status: {format_counts(golden_counts, ['Yes', 'Partial', 'No'])}",
        f"- Cards with TODO: {len(todo_rows)}",
        f"- P0 without golden evidence: {len(p0_without_golden)}",
        f"- C-level without golden evidence: {len(c_without_golden)}",
        "",
        "## Focus Rows",
        "",
        "| Operator | Q | Level | Card TODO | Known Limitations | Golden Test | Cases | Benchmark | Priority | Next Action |",
        "|---|---:|---|---:|---:|---|---:|---|---|---|",
    ]

    focus_operators = {item[3].operator for item in p0_rows}
    focus_operators.update(FIRST_BATCH_OPERATORS)
    for priority, _, _, row, card, golden in enriched:
        if row.operator not in focus_operators:
            continue
        lines.append(
            "| "
            + " | ".join(
                [
                    clean_cell(row.operator),
                    str(row.qscore),
                    clean_cell(row.level),
                    str(card.card_todo_count),
                    str(card.known_limitations_count),
                    golden.status,
                    str(golden.case_count),
                    golden.benchmark,
                    priority,
                    clean_cell(next_action_for(row, card, golden)),
                ]
            )
            + " |"
        )

    lines.extend(
        [
            "",
            "## Full Matrix",
            "",
            "| OperatorType | DisplayName | Category | QScore | Level | Version | Maturity | Inputs | Outputs | Params | AlgorithmSummary | KnownLimitationsCount | CardTodoCount | HasGoldenTest | GoldenCases | HasPublicDataset | HasFieldDataset | HasBenchmark | Priority | OwnerAgent | NextAction |",
            "|---|---|---|---:|---|---|---|---:|---:|---:|---|---:|---:|---|---:|---|---|---|---|---|---|",
        ]
    )

    for priority, _, _, row, card, golden in enriched:
        algorithm_summary = row.algorithm_summary if row.algorithm_summary != "-" else card.algorithm_summary
        owner = owner_for(row, card, golden, priority)
        lines.append(
            "| "
            + " | ".join(
                [
                    clean_cell(row.operator_type),
                    clean_cell(row.display_name),
                    clean_cell(row.category),
                    str(row.qscore),
                    clean_cell(row.level),
                    clean_cell(row.version),
                    clean_cell(card.maturity),
                    str(row.input_count),
                    str(row.output_count),
                    str(row.param_count),
                    clean_cell(algorithm_summary),
                    str(card.known_limitations_count),
                    str(card.card_todo_count),
                    golden.status,
                    str(golden.case_count),
                    "No",
                    "No",
                    golden.benchmark,
                    priority,
                    clean_cell(owner),
                    clean_cell(next_action_for(row, card, golden)),
                ]
            )
            + " |"
        )

    lines.append("")
    return "\n".join(lines)


def format_counts(counter: Counter[str], order: list[str]) -> str:
    ordered = [f"{key}={counter.get(key, 0)}" for key in order if counter.get(key, 0) > 0]
    extras = [f"{key}={value}" for key, value in sorted(counter.items()) if key not in order]
    return ", ".join(ordered + extras) if ordered or extras else "-"


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate operator_quality_matrix.md from operator cards and quality evidence.")
    parser.add_argument("--catalog", type=Path, default=DEFAULT_CATALOG)
    parser.add_argument("--card-dir", type=Path, default=DEFAULT_CARD_DIR)
    parser.add_argument("--baseline", type=Path, default=DEFAULT_BASELINE)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()

    rows = parse_catalog(args.catalog, args.card_dir)
    evidence = load_golden_evidence(args.baseline)
    output = render_matrix(rows, args.card_dir, args.catalog, args.baseline, evidence)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(output, encoding="utf-8", newline="\n")

    print(f"Generated {args.output} from {len(rows)} operators")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
