"""Extract only the phase-6 allowlisted, synthetic evaluation metadata from TRX.

No dependency, credential lookup, cloud access, raw StdOut copy or customer text.
"""
import argparse
import json
import math
from pathlib import Path
import statistics
import xml.etree.ElementTree as ET


def records(path):
    root = ET.parse(path).getroot()
    for result in root.findall(".//{*}UnitTestResult"):
        for line in result.findtext(".//{*}StdOut", "").splitlines():
            if not line.startswith("{"):
                continue
            try:
                value = json.loads(line)
            except ValueError:
                continue
            if value.get("Metric") in ("CommercialCertification", "CommerceReads"):
                yield result.get("outcome"), value


def collect(results, baseline, output):
    output.mkdir(parents=True, exist_ok=True)
    allowed = {"Metric", "Case", "Workspace", "Message", "DurationMs", "Route", "SemanticCalls",
               "Understanding", "Phase", "FirestoreReads", "FirestoreCommits", "Calls", "Delivery",
               "Orders", "Reservations", "Outcome"}
    turns, cases, after = [], {}, {}
    for outcome, value in records(results):
        if value["Metric"] == "CommercialCertification":
            turns.append({k: v for k, v in value.items() if k in allowed})
            cases[value["Case"]] = outcome
        else:
            after[value["Scenario"]] = value
    before = {v["Scenario"]: v for _, v in records(baseline)} if baseline else {}
    comparisons = []
    for scenario, value in sorted(after.items()):
        old = before.get(scenario)
        def metrics(row):
            if row is None:
                return None
            return {"Products": row["Calls"].get("GetProductsAsync", 0),
                    "Services": row["Calls"].get("GetServicesAsync", 0), "SemanticCalls": row["AiCalls"],
                    "LocalMs": row["ElapsedMs"], "Calls": row["Calls"],
                    "FirestoreReads": row["FirestoreReads"], "FirestoreCommits": row["FirestoreCommits"]}
        comparisons.append({"Scenario": scenario, "Before": metrics(old), "After": metrics(value)})
    root = ET.parse(results).getroot()
    counters = root.find(".//{*}Counters")
    times = sorted(t["DurationMs"] for t in turns)
    summary = {
        "TestCounters": dict(counters.attrib) if counters is not None else {},
        "CanonicalConversations": len(cases), "CanonicalPassed": sum(v == "Passed" for v in cases.values()),
        "ResolutionPercent": 100 * sum(v == "Passed" for v in cases.values()) / len(cases) if cases else None,
        "CanonicalTurns": len(turns), "SemanticMockCalls": sum(t["SemanticCalls"] for t in turns),
        "CompositionProviderCalls": 0, "CompositionEvidence": "Asserted by every canonical conversation",
        "FirestoreRpcReadsInMemory": sum(t["FirestoreReads"] for t in turns),
        "FirestoreRpcCommitsInMemory": sum(t["FirestoreCommits"] for t in turns),
        "LocalP50Ms": statistics.median(times) if times else None,
        "LocalP95Ms": times[math.ceil(len(times) * .95) - 1] if times else None,
        "InfraLatencyResourcesBilling": "NO MEDIDO", "RealProviderTokensCost": "NO MEDIDO",
        "IndependentHumanReview": "PENDIENTE", "RepositoryComparisons": comparisons,
    }
    (output / "phase6-telemetry.ndjson").write_text("".join(json.dumps(t, ensure_ascii=False) + "\n" for t in turns), encoding="utf-8")
    (output / "phase6-metrics.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return summary


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--results", type=Path, required=True)
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    evidence = collect(args.results, args.baseline, args.output)
    print(json.dumps({k: v for k, v in evidence.items() if k != "RepositoryComparisons"}, ensure_ascii=False, indent=2))
