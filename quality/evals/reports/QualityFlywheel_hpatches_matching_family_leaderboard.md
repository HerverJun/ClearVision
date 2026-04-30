# HPatches Matching Family Leaderboard

GeneratedAtUtc: `2026-04-29T15:12:56+00:00`
RankingPolicy: `viewpointPassRate desc, total passRate desc, p95PositionErrorPx asc, runtimeMs asc`

| Rank | Candidate | Viewpoint pass | Total pass | P95 error px | Mean error px | Runtime ms | Params | Report |
|---:|---|---:|---:|---:|---:|---:|---|---|
| 1 | AkazeFeatureMatch | 36/59 (0.610169) | 90/116 (0.775862) | 321.631671 | 54.341248 | 8568.21 | max=1200, ratio=0.75, ransac=5, minInlierRatio=0.25, akazeThreshold=0.001 | quality/evals/reports/AkazeFeatureMatch_hpatches_candidate_v4.json |
| 2 | OrbFeatureMatch | 35/59 (0.59322) | 90/116 (0.775862) | 267.972396 | 45.005668 | 3572.659 | max=2000, ratio=0.7, ransac=7, minInlierRatio=0.25, fast=16, edge=10 | quality/evals/reports/OrbFeatureMatch_hpatches_candidate_v4.json |
| 3 | PlanarMatching(AKAZE) | 16/59 (0.271186) | 70/116 (0.603448) | 118.723182 | 8648.600934 | 25042.919 | max=1600, ratio=0.75, ransac=5, minInlierRatio=0.2, detector=AKAZE, score=0.5 | quality/evals/reports/PlanarMatching_hpatches_akaze_baseline.json |
| 4 | PlanarMatching(ORB) | 15/59 (0.254237) | 70/116 (0.603448) | 114.786099 | 8653.275972 | 8795.378 | max=1600, ratio=0.75, ransac=5, minInlierRatio=0.2, detector=ORB, score=0.5 | quality/evals/reports/PlanarMatching_hpatches_baseline.json |

## Failure Focus

| Candidate | Top failure reasons |
|---|---|
| AkazeFeatureMatch | Projected quadrilateral is invalid. (25); At least four point correspondences are required. (1) |
| OrbFeatureMatch | Projected quadrilateral is invalid. (25); At least four point correspondences are required. (1) |
| PlanarMatching(AKAZE) | Projected quadrilateral is invalid. (24); Insufficient feature matches (1 < 6). (1); isMatch=True, score=0.741, inliers=8, totalMatches=8, error=153.71, tolerance=35 (1); isMatch=True, score=0.824, inliers=46, totalMatches=46, error=45.672, tolerance=35 (1); isMatch=True, score=0.795, inliers=258, totalMatches=259, error=97.086, tolerance=35 (1) |
| PlanarMatching(ORB) | Projected quadrilateral is invalid. (25); Insufficient feature matches (3 < 6). (1); isMatch=True, score=0.773, inliers=154, totalMatches=154, error=44.731, tolerance=35 (1); isMatch=True, score=0.748, inliers=176, totalMatches=177, error=97.186, tolerance=35 (1); isMatch=True, score=0.775, inliers=299, totalMatches=300, error=68.79, tolerance=35 (1) |
