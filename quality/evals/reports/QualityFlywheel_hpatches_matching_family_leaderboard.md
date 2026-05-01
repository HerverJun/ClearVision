# HPatches Matching Family Leaderboard

GeneratedAtUtc: `2026-04-30T11:24:57+00:00`
RankingPolicy: `viewpointPassRate desc, total passRate desc, p95PositionErrorPx asc, runtimeMs asc`

| Rank | Candidate | Viewpoint pass | Total pass | P95 position px | P95 corner px | Mean error px | Runtime ms | Params | Report |
|---:|---|---:|---:|---:|---:|---:|---:|---|---|
| 1 | AkazeFeatureMatch | 58/59 (0.983051) | 114/116 (0.982759) | 2.014785 | 9.246627 | 4.176081 | 6786.379 | max=1200, ratio=0.75, ransac=5, minInlierRatio=0.25, akazeThreshold=0.001, centerOnly=True | quality/evals/reports/AkazeFeatureMatch_hpatches_candidate_center_only_v1.json |
| 2 | OrbFeatureMatch | 56/59 (0.949153) | 112/116 (0.965517) | 2.561756 | 8.454432 | 7.270331 | 2954.6 | max=2000, ratio=0.7, ransac=7, minInlierRatio=0.25, fast=16, edge=10, centerOnly=True | quality/evals/reports/OrbFeatureMatch_hpatches_candidate_center_only_v1.json |
| 3 | PlanarMatching(AKAZE) | 16/59 (0.271186) | 70/116 (0.603448) | 118.723182 | 8.253566 | 8648.600934 | 20655.251 | max=1600, ratio=0.75, ransac=5, minInlierRatio=0.2, detector=AKAZE, score=0.5 | quality/evals/reports/PlanarMatching_hpatches_akaze_baseline.json |
| 4 | PlanarMatching(ORB) | 15/59 (0.254237) | 70/116 (0.603448) | 114.786099 | 10.309544 | 8653.275972 | 7330.209 | max=1600, ratio=0.75, ransac=5, minInlierRatio=0.2, detector=ORB, score=0.5 | quality/evals/reports/PlanarMatching_hpatches_baseline.json |

## Failure Focus

| Candidate | Top failure reasons |
|---|---|
| AkazeFeatureMatch | At least four point correspondences are required. (1); Projected quadrilateral is invalid. (1) |
| OrbFeatureMatch | Projected quadrilateral is invalid. (3); At least four point correspondences are required. (1) |
| PlanarMatching(AKAZE) | Projected quadrilateral is invalid. (24); Insufficient feature matches (1 < 6). (1); isMatch=True, score=0.741, inliers=8, totalMatches=8, error=153.71, tolerance=35 (1); isMatch=True, score=0.824, inliers=46, totalMatches=46, error=45.672, tolerance=35 (1); isMatch=True, score=0.795, inliers=258, totalMatches=259, error=97.086, tolerance=35 (1) |
| PlanarMatching(ORB) | Projected quadrilateral is invalid. (25); Insufficient feature matches (3 < 6). (1); isMatch=True, score=0.773, inliers=154, totalMatches=154, error=44.731, tolerance=35 (1); isMatch=True, score=0.748, inliers=176, totalMatches=177, error=97.186, tolerance=35 (1); isMatch=True, score=0.775, inliers=299, totalMatches=300, error=68.79, tolerance=35 (1) |
