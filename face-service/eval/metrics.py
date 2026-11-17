"""Verification metrics shared by the evaluation scripts. Pure Python, no TensorFlow."""
from __future__ import annotations

import itertools
import math
import random
from typing import Dict, Iterable, List, Optional, Sequence, Tuple


def wilson_interval(successes: int, total: int, z: float = 1.96) -> Tuple[float, float]:
    """95% Wilson score interval for a proportion. Honest about small samples (0 of 5000 is not 0%)."""
    if total == 0:
        return 0.0, 1.0
    p = successes / total
    denom = 1 + z * z / total
    centre = (p + z * z / (2 * total)) / denom
    half = z * math.sqrt(p * (1 - p) / total + z * z / (4 * total * total)) / denom
    return max(0.0, centre - half), min(1.0, centre + half)


def rates_at(genuine: Sequence[float], impostor: Sequence[float], threshold: float) -> Dict[str, float]:
    """
    Accept when score >= threshold (the same rule the service uses).
      FRR = genuine pairs rejected / genuine pairs        GAR = 1 - FRR (genuine acceptance rate)
      FAR = impostor pairs accepted / impostor pairs      the same number is the impostor acceptance rate
    """
    g_reject = sum(s < threshold for s in genuine)
    i_accept = sum(s >= threshold for s in impostor)
    frr = g_reject / len(genuine) if genuine else float("nan")
    far = i_accept / len(impostor) if impostor else float("nan")
    frr_lo, frr_hi = wilson_interval(g_reject, len(genuine))
    far_lo, far_hi = wilson_interval(i_accept, len(impostor))
    return {
        "threshold": threshold, "far": far, "frr": frr, "gar": 1 - frr if genuine else float("nan"),
        "far_ci": (far_lo, far_hi), "frr_ci": (frr_lo, frr_hi),
        "genuine_pairs": len(genuine), "impostor_pairs": len(impostor),
        "genuine_rejected": g_reject, "impostor_accepted": i_accept,
    }


def sweep(genuine: Sequence[float], impostor: Sequence[float], thresholds: Iterable[float]) -> List[Dict[str, float]]:
    return [rates_at(genuine, impostor, t) for t in thresholds]


def equal_error_rate(genuine: Sequence[float], impostor: Sequence[float]) -> Tuple[float, float]:
    """(threshold, rate) where FAR and FRR are closest. A summary of separation, not a recommended operating point."""
    if not genuine or not impostor:
        return float("nan"), float("nan")
    best = None
    for t in sorted(set(genuine) | set(impostor)):
        r = rates_at(genuine, impostor, t)
        gap = abs(r["far"] - r["frr"])
        if best is None or gap < best[0]:
            best = (gap, t, (r["far"] + r["frr"]) / 2)
    return best[1], best[2]


def genuine_pairs(labels: Sequence[str], cap_per_person: int, rng: random.Random) -> List[Tuple[int, int]]:
    by_person: Dict[str, List[int]] = {}
    for i, label in enumerate(labels):
        by_person.setdefault(label, []).append(i)
    pairs: List[Tuple[int, int]] = []
    for indices in by_person.values():
        combos = list(itertools.combinations(indices, 2))
        rng.shuffle(combos)
        pairs.extend(combos[:cap_per_person])
    return pairs


def impostor_pairs(labels: Sequence[str], count: int, rng: random.Random) -> List[Tuple[int, int]]:
    n = len(labels)
    if len({*labels}) < 2:
        return []
    pairs = set()
    attempts = 0
    while len(pairs) < count and attempts < count * 20:
        a, b = rng.randrange(n), rng.randrange(n)
        attempts += 1
        if a != b and labels[a] != labels[b]:
            pairs.add((min(a, b), max(a, b)))
    return sorted(pairs)


def apcer_bpcer(live_scores: Sequence[float], attack_scores: Sequence[float], threshold: float) -> Dict[str, float]:
    """
    Presentation-attack detection at a "real" score threshold (higher = more real; accept as live at >= threshold).
      APCER: attacks accepted as live / attacks    BPCER: live samples rejected / live samples
      ACER = (APCER + BPCER) / 2
    """
    apcer = sum(s >= threshold for s in attack_scores) / len(attack_scores) if attack_scores else float("nan")
    bpcer = sum(s < threshold for s in live_scores) / len(live_scores) if live_scores else float("nan")
    return {
        "threshold": threshold, "apcer": apcer, "bpcer": bpcer, "acer": (apcer + bpcer) / 2,
        "attack_samples": len(attack_scores), "live_samples": len(live_scores),
        "apcer_ci": wilson_interval(sum(s >= threshold for s in attack_scores), len(attack_scores)),
        "bpcer_ci": wilson_interval(sum(s < threshold for s in live_scores), len(live_scores)),
    }


def fmt_pct(x: Optional[float], digits: int = 2) -> str:
    return "n/a" if x is None or (isinstance(x, float) and math.isnan(x)) else f"{100 * x:.{digits}f}%"


def fmt_ci(ci: Tuple[float, float]) -> str:
    return f"[{fmt_pct(ci[0])}, {fmt_pct(ci[1])}]"
