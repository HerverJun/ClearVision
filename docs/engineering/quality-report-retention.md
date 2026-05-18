# Quality Report Retention

ClearVision keeps quality evidence reviewable by separating durable evidence summaries from bulky runner payloads.

## Keep In Git

- Operator quality matrices, release gate summaries, manifests, and human-readable `.md` reports.
- Small baseline JSON files that are needed by documentation, CI gates, or downstream tooling.
- `*.summary.json` files for large reports. A summary must include source metadata, key metrics, representative failures, source SHA-256, and the retention decision.

## Keep Outside Git

- Sweep detail dumps, per-image/component arrays, repeated candidate replay payloads, and other raw runner output that is useful for investigation but not for normal review.
- Raw component telemetry CSV files. Keep a compact summary in git with profile-level distributions, representative components, source report links, source SHA-256, and the retention decision.
- Raw payloads should be published as CI artifacts, release assets, or regenerated locally from the runner command and dataset manifest.

## Size Guard

`scripts/check-quality-report-size.ps1` fails when `quality/evals/reports/*.json` or `quality/evals/reports/*.csv` exceeds the default 1 MB limit unless the path is explicitly listed in `quality/evals/reports/quality-report-size-allowlist.txt`.

The allowlist is a temporary grandfathering mechanism. New large reports should normally be summarized instead of added to the allowlist.
