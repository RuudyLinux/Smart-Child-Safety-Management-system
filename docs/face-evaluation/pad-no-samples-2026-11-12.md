# Presentation-attack evaluation: no-samples (2026-11-12)

**Not measured.** No live captures and attack samples were supplied, so no APCER, BPCER or ACER can be reported. The checks that exist are covered by unit tests with synthetic inputs (challenge logic, fail-closed paths), which say nothing about how well real print, screen or replay attacks are caught.

Supply samples as described in `eval/README.md` and run this script again.

## Limits

- Results hold for the cameras, attack artefacts and people in the supplied samples only.
- A determined attacker (a live video of the guardian that can be steered through the requested head turns, a 3D mask) is outside what this measures.
- Passive scores are per frame; the production decision combines five frames with a head-turn challenge and a continuity check.
