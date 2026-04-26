# MVTec AD Lite Manifest

Collected: 2026-04-25

This local subset is intended for the first AnomalyDetection public-data baseline. It uses one object category and one texture category from the official MVTec AD dataset.

## Source

- Dataset: MVTec AD
- Official overview: https://www.mvtec.com/company/research/datasets/mvtec-ad
- Official downloads: https://www.mvtec.com/company/research/datasets/mvtec-ad/downloads
- License: Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International
- License URL: https://creativecommons.org/licenses/by-nc-sa/4.0/

## Local Layout

The extracted data is local-only and ignored by git:

```text
quality/public_datasets/mvtec_ad_lite/
  _downloads/
    toothbrush.tar.xz
    grid.tar.xz
  toothbrush/
  grid/
```

Programmatic metadata is also stored in:

```text
quality/datasets/mvtec_ad_lite_manifest.json
```

To recreate the local dataset on another machine, run:

```powershell
./quality/datasets/download_mvtec_ad_lite.ps1
```

The script downloads archives listed in the JSON manifest, verifies SHA256 hashes, and extracts them under the git-ignored local data root.

## Archives

| Category | Type | Archive | Size bytes | SHA256 |
| --- | --- | --- | ---: | --- |
| toothbrush | object | `quality/public_datasets/mvtec_ad_lite/_downloads/toothbrush.tar.xz` | 109357096 | `FE72A34B4B0D074F84E844BE7EF8C4F64F7F02F20E25F19C9E3B4A231C8BEA77` |
| grid | texture | `quality/public_datasets/mvtec_ad_lite/_downloads/grid.tar.xz` | 160763852 | `D94A9A651CBFAEE22C94D06CF8FA8204A590C872362DE0489EB34F1645328DE8` |

## Split Counts

| Category | Train good | Test good | Test anomaly | Ground-truth masks |
| --- | ---: | ---: | ---: | ---: |
| toothbrush | 60 | 12 | 30 | 30 |
| grid | 264 | 21 | 57 | 57 |

### Toothbrush Defects

| Defect type | Test images | Masks |
| --- | ---: | ---: |
| defective | 30 | 30 |

### Grid Defects

| Defect type | Test images | Masks |
| --- | ---: | ---: |
| bent | 12 | 12 |
| broken | 12 | 12 |
| glue | 11 | 11 |
| metal_contamination | 11 | 11 |
| thread | 11 | 11 |

## Notes

- The subset covers both object and texture anomaly classes while keeping the first public-data baseline small.
- Use this only for non-commercial/research-compatible evaluation unless project licensing is reviewed.
- Converter: `quality/datasets/converters/convert_mvtec_ad.py`
- Baseline report: `quality/evals/reports/AnomalyDetection_mvtec_baseline.md`
