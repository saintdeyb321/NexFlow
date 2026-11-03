"""Offline capacity model. No HTTP, credentials, SDKs or production configuration."""
import argparse
import csv
import itertools
import json
import math
from decimal import Decimal
from pathlib import Path


def validate(config):
    for key in ("messages_per_conversation", "peak_minutes", "input_tokens_p50", "input_tokens_p95", "output_tokens_p50", "output_tokens_p95"):
        value = config[key]
        if not isinstance(value, (int, float)) or not math.isfinite(value) or value <= 0:
            raise ValueError(f"Invalid {key}")
    for key in ("retry_rate", "fallback_rate", "peak_day_fraction", "safety_margin"):
        value = config[key]
        if not isinstance(value, (int, float)) or not math.isfinite(value) or not 0 <= value < 1:
            raise ValueError(f"Invalid {key}")
    if config["input_tokens_p95"] < config["input_tokens_p50"] or config["output_tokens_p95"] < config["output_tokens_p50"]:
        raise ValueError("P95 cannot be smaller than P50")
    for provider in config["providers"].values():
        for key in ("rpm", "rpd", "tpm", "tpd"):
            if provider.get(key) is not None and (not isinstance(provider[key], (int, float)) or not math.isfinite(provider[key]) or provider[key] <= 0):
                raise ValueError(f"Invalid quota {key}")


def scenarios(config):
    validate(config)
    p50 = config["input_tokens_p50"] + config["output_tokens_p50"]
    p95 = config["input_tokens_p95"] + config["output_tokens_p95"]
    for businesses, conversations, nlu, composition in itertools.product((10, 20, 30), (5, 10, 20), (0, 1, 2, 4), (0, 1)):
        daily = businesses * conversations
        for provider, primary, fallback in (("groq", nlu, composition), ("gemini", composition, nlu)):
            quota = config["providers"][provider]
            attempts = math.ceil(Decimal(daily) * (primary + fallback * Decimal(str(config["fallback_rate"]))) * (1 + Decimal(str(config["retry_rate"]))))
            rpm = math.ceil(Decimal(attempts) * Decimal(str(config["peak_day_fraction"])) / Decimal(str(config["peak_minutes"])))
            demand = {"rpm": rpm, "rpd": attempts, "tpm": rpm * p95, "tpd": attempts * p95}
            statuses = {key: "UNKNOWN" if quota.get(key) is None else "PASS" if value <= quota[key] * (1 - config["safety_margin"]) else "FAIL"
                        for key, value in demand.items()}
            status = "FAIL" if "FAIL" in statuses.values() else "UNKNOWN" if "UNKNOWN" in statuses.values() else "PASS"
            price = quota.get("paid_usd_per_million")
            cost = None
            if config.get("pricing_mode") == "paid" and price is not None:
                rates = (price["input"], price["output"])
                if any(not isinstance(x, (int, float)) or not math.isfinite(x) or x < 0 for x in rates):
                    raise ValueError("Invalid paid price")
                cost = attempts * (config["input_tokens_p95"] * rates[0] + config["output_tokens_p95"] * rates[1]) / 1_000_000
            yield dict(businesses=businesses, conversations_per_business=conversations, daily_conversations=daily,
                       daily_messages=daily * config["messages_per_conversation"], nlu_calls=nlu, composition_calls=composition,
                       provider=provider, attempts_daily=attempts, tokens_daily_p50=attempts * p50, tokens_daily_p95=attempts * p95,
                       peak_rpm=rpm, peak_tpm=rpm * p95, **{f"{key}_gate": value for key, value in statuses.items()},
                       result=status, token_source=config.get("token_source", "ASSUMPTION"), quota_source=quota.get("source", "UNKNOWN"),
                       paid_usd_daily_p95=cost, real_capacity="NOT_VERIFIED")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    config = json.loads(args.config.read_text(encoding="utf-8"))
    rows = list(scenarios(config))
    args.output.mkdir(parents=True, exist_ok=True)
    with (args.output / "capacity.csv").open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0])); writer.writeheader(); writer.writerows(rows)
    (args.output / "assumptions.json").write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8")
    lines = ["# Offline capacity scenarios", "", "All demands are simulated. Provider/account quotas remain UNKNOWN unless supplied. This is not a real load test.", "",
             "| Businesses | Conv/business/day | NLU | Composition | Provider | Attempts/day | Tokens/day P95 | RPM | TPM P95 | Result |",
             "|---:|---:|---:|---:|---|---:|---:|---:|---:|---|"]
    for r in rows:
        lines.append(f"| {r['businesses']} | {r['conversations_per_business']} | {r['nlu_calls']} | {r['composition_calls']} | {r['provider']} | {r['attempts_daily']} | {r['tokens_daily_p95']} | {r['peak_rpm']} | {r['peak_tpm']} | {r['result']} |")
    (args.output / "capacity.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(json.dumps({"scenarios": len(rows), "PASS": sum(r["result"] == "PASS" for r in rows),
                      "FAIL": sum(r["result"] == "FAIL" for r in rows), "UNKNOWN": sum(r["result"] == "UNKNOWN" for r in rows),
                      "real_provider_calls": 0}))


if __name__ == "__main__":
    main()
