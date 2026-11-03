import copy
import json
from pathlib import Path
import unittest
from capacity_simulator import scenarios


class CapacityTests(unittest.TestCase):
    def config(self):
        return json.loads(Path(__file__).with_name("capacity_assumptions.example.json").read_text(encoding="utf-8"))

    def test_grid_covers_all_business_loads_and_unknown_is_not_unlimited(self):
        rows = list(scenarios(self.config()))
        self.assertEqual(144, len(rows))
        self.assertEqual({10, 20, 30}, {r["businesses"] for r in rows})
        self.assertEqual({5, 10, 20}, {r["conversations_per_business"] for r in rows})
        self.assertEqual({"UNKNOWN"}, {r["result"] for r in rows})
        self.assertTrue(all(r["paid_usd_daily_p95"] is None for r in rows))

    def test_zero_calls_never_invents_tokens_or_retries(self):
        rows = [r for r in scenarios(self.config()) if r["nlu_calls"] == r["composition_calls"] == 0]
        self.assertTrue(all(r["attempts_daily"] == r["tokens_daily_p95"] == r["peak_rpm"] == 0 for r in rows))

    def test_each_limit_can_fail_independently_with_synthetic_quotas(self):
        for key in ("rpm", "rpd", "tpm", "tpd"):
            config = self.config()
            config["providers"]["groq"].update(dict.fromkeys(("rpm", "rpd", "tpm", "tpd"), 10**10))
            config["providers"]["groq"][key] = 1
            row = next(r for r in scenarios(config) if r["provider"] == "groq" and r["nlu_calls"] == 1)
            self.assertEqual("FAIL", row[key + "_gate"])
            self.assertEqual("FAIL", row["result"])

    def test_retry_fallback_and_composition_are_charged_to_the_correct_provider(self):
        config = self.config(); config["retry_rate"] = .1; config["fallback_rate"] = .2
        rows = list(scenarios(config))
        row = next(r for r in rows if r["businesses"] == 20 and r["conversations_per_business"] == 20
                   and r["nlu_calls"] == 2 and r["composition_calls"] == 1 and r["provider"] == "groq")
        self.assertEqual(968, row["attempts_daily"])
        self.assertEqual(row["attempts_daily"] * 3000, row["tokens_daily_p95"])

    def test_cost_requires_paid_mode_and_explicit_prices(self):
        config = self.config(); config["pricing_mode"] = "paid"
        config["providers"]["groq"]["paid_usd_per_million"] = {"input": 1, "output": 2}
        row = next(r for r in scenarios(config) if r["nlu_calls"] == 1 and r["provider"] == "groq")
        self.assertAlmostEqual(row["attempts_daily"] * 3300 / 1_000_000, row["paid_usd_daily_p95"])

    def test_invalid_assumptions_are_rejected(self):
        for key, value in (("fallback_rate", 2), ("safety_margin", -1), ("input_tokens_p95", 1), ("peak_minutes", 0)):
            config = copy.deepcopy(self.config()); config[key] = value
            with self.assertRaises(ValueError): list(scenarios(config))

    def test_synthetic_known_limits_can_pass_without_claiming_real_capacity(self):
        config = self.config()
        for provider in config["providers"].values():
            provider.update(dict.fromkeys(("rpm", "rpd", "tpm", "tpd"), 10**12))
            provider["source"] = "SYNTHETIC_UNIT_TEST"
        rows = list(scenarios(config))
        self.assertEqual({"PASS"}, {r["result"] for r in rows})
        self.assertEqual({"NOT_VERIFIED"}, {r["real_capacity"] for r in rows})


if __name__ == "__main__":
    unittest.main()
