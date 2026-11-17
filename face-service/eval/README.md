# Face evaluation harness

Measures how the matcher and the liveness checks behave, so any threshold change can be argued from data.
Reports go to `docs/face-evaluation/`. Nothing here changes a threshold: recommendations are written in the report
for the owner to approve.

| Script | Question it answers |
|---|---|
| `prepare_lfw.py` | Extracts a reproducible subset of Labeled Faces in the Wild from `lfw.tgz`. |
| `identity_eval.py` | FAR, FRR, genuine/impostor acceptance rate, EER and a threshold sweep; one vs several reference photos; synthetic lighting/camera degradations; per-condition breakdown for the school set. |
| `pad_eval.py` | APCER / BPCER / ACER for print, screen and replay attacks (frame level and full liveness pipeline). Reports "not measured" when no attack samples are supplied. |
| `metrics.py` | The arithmetic (tested in `tests/test_eval_metrics.py`). |

Run from `face-service/` with the service's virtualenv, for example:

```
.venv\Scripts\python.exe eval\prepare_lfw.py --archive eval\datasets\lfw_download\lfw.tgz --out eval\datasets\lfw --persons 80 --per-person 5
.venv\Scripts\python.exe eval\identity_eval.py --dataset eval\datasets\lfw --name lfw-subset --persons 80 --per-person 5 --variants
```

Datasets, downloads and the embedding cache live under `eval/datasets/` and `eval/cache/` and are git-ignored.

## School dataset (consented), for numbers that matter

The public baseline measures the model. Only the school's own cameras, lighting and families show how it behaves at the desk.

**Consent and handling**

1. Collect only from adults who agree in writing, after being told: what is captured, that it is used only to measure
   recognition accuracy, who can see it, how long it is kept, and that they can withdraw and have it deleted.
2. Do not include children. Guardians only.
3. Store the set on one access-controlled machine, never in the repository, never uploaded to a third party.
4. Use a participant code (`p001`), never a name, in folders and files. Keep the code-to-person list separately, or not at all.
5. Delete the set and the embedding cache when the evaluation is done, unless the participants agreed to a longer period.

**What to capture** (per participant, as many as practical, at least 4 images):

- Registration photo on the guardian's phone (what the app collects) and a webcam capture at the pickup desk (what verification sees).
- Different lighting at the desk: normal, dim, bright window behind, side light.
- Natural variation: glasses on/off, hair or headwear changes, a different day if possible.
- At least two cameras if more than one will be used.

**Layout**

```
eval/datasets/school/p001/phone_indoor.jpg
eval/datasets/school/p001/desk_dim.jpg
...
eval/datasets/school/meta.csv      file,camera,lighting
                                   p001/phone_indoor.jpg,phone,indoor
                                   p001/desk_dim.jpg,desk-webcam,dim
```

```
.venv\Scripts\python.exe eval\identity_eval.py --dataset eval\datasets\school --name school --persons 500 --per-person 20 --min-images 4 --meta eval\datasets\school\meta.csv --variants
```

The report then adds a table of false rejections by lighting and camera pair.

## Presentation-attack samples

Capture with the desk camera, under desk lighting, with the participant's consent:

- **print**: a printed photo of the guardian (laser and photo paper, held flat and slightly bent).
- **screen**: the guardian's photo and a short video on a phone and on a tablet, at full brightness.
- **replay**: a recorded video of the guardian performing head turns, shown on a screen.
- **live**: the same people performing the real challenge (the bona fide class).

Frame-level layout: `live/`, `print/`, `screen/` folders of images. Burst-level layout (the whole pipeline):
`bursts/<class>/<burst>/frame_000.jpg ... meta.json` with `{"steps": [...], "t_ms": [...]}`; export the frames a desk
capture sends. Then:

```
.venv\Scripts\python.exe eval\pad_eval.py --live eval\datasets\pad\live --attack print=eval\datasets\pad\print --attack screen=eval\datasets\pad\screen
.venv\Scripts\python.exe eval\pad_eval.py --bursts eval\datasets\pad\bursts
```

Public PAD corpora (CASIA-FASD, Replay-Attack, OULU-NPU) need licence agreements and are not downloaded by these scripts.

## Reading the numbers

- **FAR**: impostor pairs accepted. **FRR**: genuine pairs rejected. Genuine acceptance rate is `1 - FRR`; impostor acceptance rate equals FAR.
- **APCER**: attacks accepted as live. **BPCER**: live people rejected. **ACER** is their mean.
- Every rate carries a 95% Wilson interval. Zero errors on a small sample still has a non-zero upper bound.
- Threshold recommendations belong in the report, for approval, not in the configuration.
