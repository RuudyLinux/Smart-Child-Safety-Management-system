import random
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "eval"))

import metrics as m  # noqa: E402


class Rates(unittest.TestCase):
    def test_the_accept_rule_is_greater_or_equal_like_the_service(self):
        r = m.rates_at([0.40, 0.39, 0.9], [0.40, 0.2], 0.40)
        self.assertEqual(r["genuine_rejected"], 1)   # 0.39 only
        self.assertEqual(r["impostor_accepted"], 1)  # 0.40 is accepted
        self.assertAlmostEqual(r["frr"], 1 / 3)
        self.assertAlmostEqual(r["far"], 1 / 2)
        self.assertAlmostEqual(r["gar"], 2 / 3)

    def test_perfect_separation(self):
        r = m.rates_at([0.8, 0.9, 0.7], [0.1, 0.2, 0.0], 0.4)
        self.assertEqual((r["far"], r["frr"]), (0.0, 0.0))

    def test_no_pairs_gives_nan_not_a_crash(self):
        r = m.rates_at([], [], 0.4)
        self.assertNotEqual(r["far"], r["far"])  # NaN

    def test_zero_errors_in_a_small_sample_is_not_zero_percent(self):
        lo, hi = m.wilson_interval(0, 50)
        self.assertEqual(lo, 0.0)
        self.assertGreater(hi, 0.05)

    def test_wilson_interval_shrinks_with_more_data(self):
        small = m.wilson_interval(5, 100)
        large = m.wilson_interval(500, 10000)
        self.assertLess(large[1] - large[0], small[1] - small[0])
        self.assertTrue(small[0] <= 0.05 <= small[1])

    def test_sweep_is_monotonic(self):
        rng = random.Random(1)
        g = [rng.random() * 0.5 + 0.4 for _ in range(200)]
        i = [rng.random() * 0.5 for _ in range(200)]
        rows = m.sweep(g, i, [t / 20 for t in range(21)])
        fars = [r["far"] for r in rows]
        frrs = [r["frr"] for r in rows]
        self.assertEqual(fars, sorted(fars, reverse=True))
        self.assertEqual(frrs, sorted(frrs))


class Eer(unittest.TestCase):
    def test_overlapping_distributions_have_a_nonzero_eer_between_them(self):
        rng = random.Random(3)
        g = [rng.gauss(0.6, 0.12) for _ in range(2000)]
        i = [rng.gauss(0.2, 0.12) for _ in range(2000)]
        threshold, rate = m.equal_error_rate(g, i)
        self.assertTrue(0.3 < threshold < 0.5)
        self.assertTrue(0.02 < rate < 0.08, rate)  # two unit-variance-apart Gaussians: about 4.8% in theory

    def test_perfectly_separated_scores_have_zero_eer(self):
        self.assertEqual(m.equal_error_rate([0.9, 0.8], [0.1, 0.2])[1], 0.0)


class Pairs(unittest.TestCase):
    LABELS = ["a", "a", "a", "b", "b", "c"]

    def test_genuine_pairs_stay_within_a_person(self):
        pairs = m.genuine_pairs(self.LABELS, 10, random.Random(0))
        self.assertEqual(len(pairs), 3 + 1)  # a:3 combos, b:1 combo, c:0
        self.assertTrue(all(self.LABELS[x] == self.LABELS[y] for x, y in pairs))

    def test_the_cap_per_person_is_respected(self):
        pairs = m.genuine_pairs(self.LABELS, 1, random.Random(0))
        self.assertEqual(len(pairs), 2)

    def test_impostor_pairs_cross_people_and_are_unique(self):
        pairs = m.impostor_pairs(self.LABELS, 8, random.Random(0))
        self.assertEqual(len(pairs), len(set(pairs)))
        self.assertTrue(all(self.LABELS[x] != self.LABELS[y] for x, y in pairs))

    def test_one_person_has_no_impostor_pairs(self):
        self.assertEqual(m.impostor_pairs(["a", "a", "a"], 5, random.Random(0)), [])


class Pad(unittest.TestCase):
    def test_apcer_and_bpcer(self):
        r = m.apcer_bpcer(live_scores=[0.9, 0.8, 0.3], attack_scores=[0.1, 0.7, 0.2, 0.05], threshold=0.6)
        self.assertAlmostEqual(r["bpcer"], 1 / 3)   # the 0.3 live sample is rejected
        self.assertAlmostEqual(r["apcer"], 1 / 4)   # the 0.7 attack is accepted
        self.assertAlmostEqual(r["acer"], (1 / 3 + 1 / 4) / 2)

    def test_formatting(self):
        self.assertEqual(m.fmt_pct(0.0123), "1.23%")
        self.assertEqual(m.fmt_pct(float("nan")), "n/a")


if __name__ == "__main__":
    unittest.main()
