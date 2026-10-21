"""Summarize Phase 6.1 TRX metadata only; never export test text or raw StdOut.

Use one final TRX (not overlapping reruns). SDK in-memory metrics are not Emulator,
provider, WhatsApp, worker/DeadLetter, billing or production measurements.
"""
import argparse
from collections import Counter
import json
import math
from pathlib import Path
import re
import statistics
import xml.etree.ElementTree as ET


def collect(source, output):
    root = ET.parse(source).getroot()
    rows, cases = [], {}
    numeric = ("DurationMs", "Processed", "NoResponse", "SemanticCalls", "CompositionCalls",
               "FirestoreReads", "FirestoreCommits", "Orders", "Reservations", "Handoffs")
    for result in root.findall(".//{*}UnitTestResult"):
        for line in result.findtext(".//{*}StdOut", "").splitlines():
            try:
                value = json.loads(line)
            except ValueError:
                continue
            if not isinstance(value, dict) or value.get("Metric") != "QualityHoldout61":
                continue
            if not re.fullmatch(r"H\d{2}", str(value.get("Case", ""))):
                continue
            row = {"Case": value["Case"], "Scope": "B" if value.get("Scope") == "B" else "A"}
            for key in numeric:
                number = value.get(key)
                if isinstance(number, (int, float)) and not isinstance(number, bool) and math.isfinite(number):
                    row[key] = number
            for key in ("IntentionalSuppression", "SemanticStub", "ProviderFailureStub"):
                row[key] = value.get(key) is True
            row["Outcome"] = "Passed" if value.get("Outcome") == "Passed" else "Failed"
            row["HandlerError"] = value.get("Error") is not None
            row["Delivery"] = value.get("Delivery") if value.get("Delivery") in ("Sent", "Failed", "UnknownDelivery", "NoNewDelivery") else "UNCLASSIFIED"
            calls = value.get("Calls", {})
            row["Calls"] = {k: n for k, n in calls.items() if re.fullmatch(r"[A-Za-z]+Async", k) and isinstance(n, int) and n >= 0}
            rows.append(row)
            cases[row["Case"]] = result.get("outcome")
    durations = sorted(r["DurationMs"] for r in rows)
    repositories = Counter()
    for row in rows:
        repositories.update(row["Calls"])
    summary = {
        "Scope": "SYNTHETIC_HANDLER_AND_IN_MEMORY_FIRESTORE_SDK",
        "TestCounters": dict(root.find(".//{*}Counters").attrib),
        "SkippedResults": sum(r.get("outcome") == "NotExecuted" for r in root.findall(".//{*}UnitTestResult")),
        "Conversations": len(cases), "ConversationsPassed": sum(v == "Passed" for v in cases.values()),
        "TurnInvocationsIncludingReplayAndSecondWorkspace": len(rows),
        "TurnCriteriaPassed": sum(r["Outcome"] == "Passed" for r in rows),
        "Processed": sum(r["Processed"] for r in rows),
        "UnexpectedNoResponse": sum(r["NoResponse"] for r in rows),
        "IntentionalHumanSuppression": sum(r["IntentionalSuppression"] for r in rows),
        "HandlerErrors": sum(r["HandlerError"] for r in rows),
        "LocalTurns": sum(r["SemanticCalls"] == 0 for r in rows),
        "SemanticMockCalls": sum(r["SemanticCalls"] for r in rows),
        "ExplicitSemanticStubTurns": sum(r["SemanticStub"] for r in rows),
        "CompositionMockCalls": sum(r["CompositionCalls"] for r in rows),
        "P50LocalMs": statistics.median(durations) if durations else None,
        "P95LocalMs": durations[math.ceil(len(durations) * .95) - 1] if durations else None,
        "RepositoryCalls": dict(repositories),
        "FirestoreSdkReadsInMemory": sum(r["FirestoreReads"] for r in rows),
        "FirestoreSdkCommitsInMemory": sum(r["FirestoreCommits"] for r in rows),
        "Handoffs": sum(r["Handoffs"] for r in rows),
        "Orders": sum(r["Orders"] for r in rows),
        "Reservations": sum(r["Reservations"] for r in rows),
        "SyntheticDeliveries": dict(Counter(r["Delivery"] for r in rows)),
        "DeadLetters": "NO_MEDIDO_Handler_fixture_has_no_worker",
        "RealProviderCallsTokensCost": "NO_MEDIDO_No_authorized_live_run",
        "EmulatorRedisPostgres": "PENDIENTE_Separate_CI_TRX_required",
        "RealWhatsAppDeliveryAndInfraLatency": "NO_MEDIDO",
        "IndependentHumanScores": "PENDIENTE",
    }
    output.mkdir(parents=True, exist_ok=True)
    (output / "quality61-telemetry.ndjson").write_text("".join(json.dumps(r) + "\n" for r in rows), encoding="utf-8")
    (output / "quality61-metrics.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    return summary


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--results", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(collect(args.results, args.output), indent=2))
