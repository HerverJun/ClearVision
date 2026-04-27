# Quality Evaluation Suites

These manifests split the Quality Flywheel runners into predictable lanes:

- `quick_contract_suite.json`: local/CI-friendly contract evidence, intended to stay under the quick gate budget.
- `golden_core50_suite.json`: synthetic or protocol-oracle baselines for the frozen Core 50.
- `dataset_heavy_suite.json`: dataset, public-proxy, and heavy benchmark evidence. This lane is manual or scheduled.

Inspect or run suites with:

```powershell
python quality/tools/run_quality_suite.py --suite quick_contract_suite --list
python quality/tools/run_quality_suite.py --suite quick_contract_suite --validate-only
python quality/tools/run_quality_suite.py --suite quick_contract_suite --dry-run
python quality/tools/run_quality_suite.py --suite quick_contract_suite --run
```

The runner executes entries serially. Planned G3 entries may appear in the dataset suite so the roadmap is visible without accidentally running unfinished dataset work.
