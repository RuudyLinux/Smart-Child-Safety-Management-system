# Identity evaluation: lfw-subset (2026-11-12)

Threshold under test: **0.4** cosine similarity (unchanged; `FACE_MATCH_THRESHOLD`). Accept when score >= threshold.

## Data

- 400 images of 80 people (of 80 people with >= 5 images), seed 7.
- 0 images had no usable face and were excluded (they would be rejections in production).
- 480 genuine pairs (same person, at most 6 per person) and 5000 impostor pairs (different people).

## At the production threshold

| threshold | genuine pairs | FRR (false rejection) [95% CI] | impostor pairs | FAR (false acceptance) [95% CI] |
|---|---|---|---|---|
| 0.4 | 480 | 19.38% [16.09%, 23.15%] | 5000 | 0.06% [0.02%, 0.18%] |

Genuine acceptance rate: 80.62%. Impostor acceptance rate: 0.06%. Equal error rate: 4.58% at threshold 0.23.

## Threshold sweep

| threshold | FRR | FAR |
|---|---|---|
| 0.00 | 1.04% | 75.14% |
| 0.05 | 1.67% | 54.44% |
| 0.10 | 2.92% | 32.88% |
| 0.15 | 3.33% | 16.34% |
| 0.20 | 4.58% | 7.38% |
| 0.25 | 5.21% | 3.00% |
| 0.30 | 8.12% | 1.24% |
| 0.32 | 9.58% | 0.80% |
| 0.34 | 11.04% | 0.42% |
| 0.35 | 12.71% | 0.36% |
| 0.36 | 14.17% | 0.28% |
| 0.38 | 16.88% | 0.20% |
| 0.40 | 19.38% | 0.06% |
| 0.42 | 24.79% | 0.02% |
| 0.44 | 28.12% | 0.02% |
| 0.45 | 30.63% | 0.02% |
| 0.46 | 32.92% | 0.02% |
| 0.48 | 36.67% | 0.00% |
| 0.50 | 39.79% | 0.00% |
| 0.55 | 54.79% | 0.00% |
| 0.60 | 67.29% | 0.00% |
| 0.65 | 79.38% | 0.00% |
| 0.70 | 89.38% | 0.00% |
| 0.75 | 95.62% | 0.00% |
| 0.80 | 98.54% | 0.00% |
| 0.85 | 99.79% | 0.00% |
| 0.90 | 100.00% | 0.00% |
| 0.95 | 100.00% | 0.00% |
| 1.00 | 100.00% | 0.00% |

## One versus 3 reference photos per person

Enrol the first 3 photos of each person with at least 4; probe with the rest; with several references the best score counts.

| setup | genuine probes | FRR [95% CI] | impostor probes | FAR [95% CI] |
|---|---|---|---|---|
| 1 reference photo | 160 | 20.00% [14.54%, 26.87%] | 800 | 0.12% [0.02%, 0.70%] |
| 3 reference photos (best score) | 160 | 7.50% [4.34%, 12.65%] | 800 | 0.25% [0.07%, 0.91%] |

## Synthetic lighting / camera degradations

The probe photo is degraded after the fact (the reference stays original). **Synthetic: this is not a measurement of real cameras or real lighting.** An unusable degraded photo counts as a rejection.

| probe photo | genuine probes | FRR [95% CI] | impostor probes | FAR [95% CI] |
|---|---|---|---|---|
| none (original photo) | 120 | 20.83% [14.53%, 28.95%] | 360 | 0.00% [0.00%, 1.06%] |
| dark (gamma 2.2, x0.7) | 120 | 28.33% [21.04%, 36.97%] | 360 | 0.00% [0.00%, 1.06%] |
| bright (+70) | 120 | 20.83% [14.53%, 28.95%] | 357 | 0.28% [0.05%, 1.57%] |
| low contrast (x0.35) | 120 | 22.50% [15.95%, 30.76%] | 360 | 0.00% [0.00%, 1.06%] |
| blur (sigma 2.5) | 120 | 60.83% [51.89%, 69.10%] | 360 | 0.00% [0.00%, 1.06%] |
| sensor noise (sigma 18) | 120 | 30.00% [22.53%, 38.72%] | 360 | 0.00% [0.00%, 1.06%] |
| JPEG quality 25 | 120 | 24.17% [17.39%, 32.55%] | 360 | 0.00% [0.00%, 1.06%] |
| low resolution (x0.35) | 120 | 30.83% [23.27%, 39.58%] | 360 | 0.28% [0.05%, 1.56%] |
| side shadow | 120 | 17.50% [11.74%, 25.28%] | 360 | 0.28% [0.05%, 1.56%] |

## Reading this

- Intervals are 95% Wilson intervals: a rate of 0 on a small sample still has a non-zero upper bound.
- Public photographs of public figures taken with many cameras are not guardians at a school desk; treat this as a measure of the model, then repeat on the school set (`eval/README.md`).
- The pair counts are limited by the subset size; larger subsets narrow the intervals.
- Run took 3652 s. Embedding failures: 1.

## Recommendations (for approval; nothing applied)

1. **Keep 0.40 for now.** On these 400 web photos 0.40 gives FAR 0.06% and FRR 19.4%. Lowering to 0.35 would give FRR 12.7% but FAR 0.36%
   (6x more false acceptances). LFW photos are unconstrained (pose, age, camera), so desk FRR may well be lower, but that is unmeasured.
   Decide the threshold only after running the school set (`face-service/eval/README.md`).
2. **Register 3 or more reference photos per guardian.** FRR fell from 20.0% to 7.5% with 3 references (FAR 0.12% to 0.25%, intervals overlap).
   This is the main accuracy lever and is now supported.
3. **Blur is the worst condition** (FRR 60.8% at sigma 2.5), which supports blocking blurry captures at the quality layer.
   `FACE_MIN_SHARPNESS` (45) is uncalibrated: calibrate it on real webcam captures.
4. Dark, noisy and low-resolution probes cost 8 to 10 points of FRR; bright, low-contrast, JPEG and side shadow cost little.
   These are synthetic degradations, not real cameras.
