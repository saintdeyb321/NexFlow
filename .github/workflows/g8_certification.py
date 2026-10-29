"""G8 CI gate. Only the frozen historical battery may return test exit code 1."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
LOCAL_FILTER = "Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration"
PUBLIC = "NexFlow.Tests.Concierge.CommercialCertificationTests"
HOLDOUT = "NexFlow.Tests.Concierge.QualityHoldout61Tests"
CURRENT_FILTER = LOCAL_FILTER + f"&FullyQualifiedName!~{PUBLIC}.&FullyQualifiedName!~{HOLDOUT}."
HISTORICAL_FILTER = LOCAL_FILTER + f"&(FullyQualifiedName~{PUBLIC}.|FullyQualifiedName~{HOLDOUT}.)"
# Measured with the complete local category filter on base 664ad0e; NOT only the
# 984-case F7 directed subset. No other classes/cases may be excluded.
MIN_CURRENT = 1491
MIN_RUNTIME = 984
RUNTIME_TERMS = ("concierge", "assistant", "knowledge", "airesiliencetests", "catalogchattests",
                 "reservationsperutimetests", "zerotokenknowledgeauthorizationtests")
PINS = {
    "CommercialCertificationCorpus.json": "db52197eb3d15ba6b9415d94bdefcfa0b893d3ea69f6748bf715dec743125442",
    "QualityHoldout61.json": "541a80b3c1d00e511debd85cb7f5d881db1baa534496b48c2524fddb95d5d0a2",
    "CommercialCertificationTests.cs": "9c9b8eb89f8a5eef0de34314fd7a059e3796580e38f9523366963a9610bcce04",
    "QualityHoldout61Tests.cs": "0de0c9c605f0487e0b7e63d3852d37d33c3012a74054fd84cdd6f80031117ce4",
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def pinned_sources(root):
    folder = root / "backend/NexFlow.Tests/Concierge"
    actual = {}
    for name, digest in PINS.items():
        # Git checkout may use CRLF on Windows and LF on Ubuntu. Preserve every
        # other byte, including BOM, spaces and the final newline.
        actual[name] = hashlib.sha256((folder / name).read_bytes().replace(b"\r\n", b"\n")).hexdigest()
        require(actual[name] == digest, f"Frozen corpus/evaluator changed: {name}")
    return actual


def historical_contract(root):
    folder = root / "backend/NexFlow.Tests/Concierge"
    expected = {}
    for file, cls, method, parameter, count in (
        ("CommercialCertificationCorpus.json", PUBLIC, "Independent_commercial_conversation", "caseId", 100),
        ("QualityHoldout61.json", HOLDOUT, "Frozen_independent_conversation", "id", 30),
    ):
        cases = json.loads((folder / file).read_text(encoding="utf-8"))
        ids = [case["Id"] for case in cases]
        require(len(ids) == len(set(ids)) == count, f"Invalid frozen case inventory: {file}")
        for case_id in ids:
            name = f'{cls}.{method}({parameter}: "{case_id}")'
            reason = None
            if cls == PUBLIC:
                if case_id in {f"{sector}-{n}" for sector in ("clinic", "hotel", "restaurant", "bakery", "limited") for n in ("09", "10")}:
                    reason = f"{case_id}: VerifiedFact"
                elif case_id in {f"{sector}-{n}" for sector in ("clinic", "hotel", "restaurant", "bakery", "limited") for n in ("03", "04", "19")}:
                    # Legacy script expected an order before separate, explicit final consent.
                    reason = f"{case_id}: OrderEffects"
            elif cls == HOLDOUT:
                if case_id in ("H10", "H21"):
                    reason = f"{case_id}/3/A: VerifiedFact"
                elif case_id == "H06":
                    # Intentional security change: acknowledgement is not final booking consent.
                    reason = "H06/4/A: ReservationEffects"
                elif case_id == "H26":
                    reason = "H26/2/A: OrderEffects; H26/3/A: OrderEffects"
            expected[name] = (cls, method, reason)
    return expected


def read_evidence(path):
    root = ET.parse(path).getroot()
    summary = root.find("./{*}ResultSummary")
    require(summary is not None, "Missing TRX summary")
    counters = summary.find("./{*}Counters")
    require(counters is not None, "Missing TRX counters")
    counts = {key: int(value) for key, value in counters.attrib.items()}
    require(all(value >= 0 for value in counts.values()), "Negative TRX counter")
    for key in ("error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
                "notRunnable", "notExecuted", "disconnected", "warning", "inProgress", "pending"):
        require(counts.get(key) == 0, f"Nonzero/missing TRX counter: {key}")
    results = root.findall("./{*}Results/{*}UnitTestResult")
    definitions = {}
    for definition in root.findall("./{*}TestDefinitions/{*}UnitTest"):
        method = definition.find("./{*}TestMethod")
        require(method is not None and definition.attrib["id"] not in definitions, "Invalid/duplicate test definition")
        definitions[definition.attrib["id"]] = (method.attrib["className"], method.attrib["name"])
    require(len(results) == counts["total"] == counts["executed"], "Missing/unexecuted TRX results")
    require(len({result.attrib["executionId"] for result in results}) == len(results), "Duplicate execution")
    require(len({result.attrib["testId"] for result in results}) == len(results), "Duplicate test identity")
    require(counts["passed"] == sum(r.attrib["outcome"] == "Passed" for r in results), "Passed counter mismatch")
    require(counts["failed"] == sum(r.attrib["outcome"] == "Failed" for r in results), "Failed counter mismatch")
    require(counts["passed"] + counts["failed"] == len(results), "Skipped/unknown outcome")
    for result in results:
        require(result.attrib["testId"] in definitions, "Missing test definition")
    return counts, results, definitions, summary.attrib.get("outcome")


def validate(suite, path, test_exit_code, repo=ROOT):
    hashes = pinned_sources(repo)
    counts, results, definitions, outcome = read_evidence(path)
    failures = {}
    if suite == "current":
        require(test_exit_code == 0, f"Current dotnet test failed (exit {test_exit_code})")
        require(outcome in ("Completed", "Passed"), "Current run summary is not successful")
        require(counts["total"] >= MIN_CURRENT, f"Current coverage below {MIN_CURRENT}")
        require(counts["failed"] == 0 and counts["passed"] == counts["total"], "Current tests must all pass")
        classes = [definitions[result.attrib["testId"]][0] for result in results]
        require(not any(cls in (PUBLIC, HOLDOUT) for cls in classes), "Historical test leaked into current suite")
        runtime = sum(any(term in ".".join(definitions[result.attrib["testId"]]).lower()
                          for term in RUNTIME_TERMS) for result in results)
        require(runtime >= MIN_RUNTIME, f"Agent Runtime coverage below {MIN_RUNTIME}")
        require(classes.count("NexFlow.Tests.Concierge.RuntimeF3ContractV2Tests") >= 12, "Missing F3 v2 contracts")
    else:
        require(test_exit_code == 1, f"Historical dotnet test must return exactly 1, got {test_exit_code}")
        require(outcome == "Failed", "Historical run summary must preserve the real failure")
        require((counts["total"], counts["passed"], counts["failed"]) == (130, 101, 29),
                "Historical counts must be exactly 130/101/29")
        expected = historical_contract(repo)
        names = [result.attrib["testName"] for result in results]
        require(len(names) == len(set(names)) == 130 and set(names) == set(expected),
                "Historical case inventory changed/missing/duplicated")
        for result in results:
            name = result.attrib["testName"]
            cls, method, reason = expected[name]
            require(definitions[result.attrib["testId"]] == (cls, method), f"Wrong historical method: {name}")
            require(result.attrib["outcome"] == ("Failed" if reason else "Passed"), f"Unexpected historical outcome: {name}")
            if reason:
                message = result.findtext("./{*}Output/{*}ErrorInfo/{*}Message")
                require(message is not None and message.strip() == reason, f"Historical reason changed: {name}")
                failures[name] = reason
    return {"suite": suite, "decision": "PASS" if suite == "current" else "EXPECTED_HISTORICAL_INCOMPATIBILITIES",
            "test_exit_code": test_exit_code, "counts": counts, "frozen_lf_sha256": hashes,
            "known_failures": failures}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("run", "verify"))
    parser.add_argument("suite", choices=("current", "historical"))
    parser.add_argument("--trx", type=Path, required=True)
    parser.add_argument("--test-exit-code", type=int)
    args = parser.parse_args()
    trx = args.trx.resolve()
    require(trx.is_relative_to(ROOT), "TRX must be inside the repository")
    report = {"suite": args.suite, "decision": "NO-GO",
              "identity": {key: os.environ.get(key) for key in ("GITHUB_SHA", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "GITHUB_JOB")},
              "trx": str(trx.relative_to(ROOT))}
    try:
        pinned_sources(ROOT)
        if args.action == "run":
            require(args.test_exit_code is None, "Run cannot override the real exit code")
            trx.parent.mkdir(parents=True, exist_ok=True)
            # Never validate an old green TRX if test startup/compilation fails.
            if trx.exists():
                trx.unlink()
            command = ["dotnet", "test", "backend/NexFlow.Tests/NexFlow.Tests.csproj", "--no-build", "--no-restore",
                       "--filter", CURRENT_FILTER if args.suite == "current" else HISTORICAL_FILTER,
                       "--logger", f"trx;LogFileName={trx.name}", "--results-directory", str(trx.parent)]
            print("Executing:", subprocess.list2cmdline(command), flush=True)
            code = subprocess.run(command, cwd=ROOT, check=False).returncode
        else:
            require(args.test_exit_code is not None, "Verify requires the observed test exit code")
            code = args.test_exit_code
        report["test_exit_code"] = code
        counts, results, _, _ = read_evidence(trx)
        report["counts"] = counts
        report["observed_failures"] = {
            result.attrib["testName"]: result.findtext("./{*}Output/{*}ErrorInfo/{*}Message")
            for result in results if result.attrib["outcome"] == "Failed"}
        report.update(validate(args.suite, trx, code))
        print(json.dumps(report, indent=2))
        return 0
    except (ValueError, OSError, ET.ParseError, KeyError) as error:
        report["error"] = str(error)
        print(f"G8 NO-GO: {error}", file=sys.stderr)
        return 1
    finally:
        trx.parent.mkdir(parents=True, exist_ok=True)
        trx.with_suffix(".g8.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    sys.exit(main())
