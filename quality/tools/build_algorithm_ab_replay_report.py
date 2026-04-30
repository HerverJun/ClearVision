from __future__ import annotations

import argparse

from run_algorithm_ab_replay import generate, read_json, utc_now, validate, OUTPUT_JSON


def main() -> int:
    parser = argparse.ArgumentParser(description="Compatibility wrapper for executable algorithm A/B replay reports.")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()

    report = read_json(OUTPUT_JSON) if args.validate_only else generate(execute_matching=False)
    errors = validate(report)
    if errors:
        for error in errors:
            print(f"error: {error}")
        return 2
    print(
        "algorithm A/B replay report valid: "
        f"operators={report['summary']['operatorCount']} "
        f"replayCases={report['summary']['replayCaseCount']} "
        f"executedCandidateCases={report['summary']['executedCandidateCaseCount']} "
        f"generatedAt={utc_now()}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
