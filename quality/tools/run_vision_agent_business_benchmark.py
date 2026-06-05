#!/usr/bin/env python3
"""Run the executable offline Vision Agent business benchmark.

The benchmark cases and metrics live in the C# runner so they can execute the
registered Vision Agent tool chain. This Python entrypoint remains for the
quality-suite command contract only.
"""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
RUNNER_PROJECT = (
    REPO_ROOT
    / "quality"
    / "tools"
    / "VisionAgentBusinessBenchmarkRunner"
    / "VisionAgentBusinessBenchmarkRunner.csproj"
)
DEFAULT_OUTPUT = (
    REPO_ROOT
    / "quality"
    / "evals"
    / "reports"
    / "VisionAgent_business_benchmark_baseline.json"
)
DEFAULT_REPORT = (
    REPO_ROOT
    / "quality"
    / "evals"
    / "reports"
    / "VisionAgent_business_benchmark_baseline.md"
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", default=str(DEFAULT_OUTPUT))
    parser.add_argument("--report", default=str(DEFAULT_REPORT))
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    command = [
        "dotnet",
        "run",
        "--project",
        str(RUNNER_PROJECT),
        "--",
        "--output",
        str(Path(args.output)),
        "--report",
        str(Path(args.report)),
    ]
    completed = subprocess.run(command, cwd=REPO_ROOT, check=False)
    return completed.returncode


if __name__ == "__main__":
    sys.exit(main())
