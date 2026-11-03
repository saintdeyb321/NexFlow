"""Strict evidence/attempt protocol. No providers, credentials or network calls."""
import hashlib
import json
import math
import xml.etree.ElementTree as ET


def require(condition, message):
    if not condition:
        raise ValueError(message)


def number(value, low=0, high=1e12):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value) and low <= value <= high


def read_trx(path, expected):
    tree = ET.parse(path).getroot()
    counts = {k: int(v) for k, v in tree.find("./{*}ResultSummary/{*}Counters").attrib.items()}
    for k in ("error", "timeout", "aborted", "inconclusive", "passedButRunAborted", "notRunnable", "notExecuted", "disconnected", "warning", "inProgress", "pending"):
        require(counts.get(k) == 0, "TRX incomplete: " + k)
    rows = tree.findall("./{*}Results/{*}UnitTestResult")
    require(len(rows) == counts.get("total") == counts.get("executed") == expected, "Missing/extra tests")
    require(len({r.get("testId") for r in rows}) == expected and len({r.get("executionId") for r in rows}) == expected, "Duplicate tests")
    require(counts.get("passed") == sum(r.get("outcome") == "Passed" for r in rows)
            and counts.get("failed") == sum(r.get("outcome") == "Failed" for r in rows)
            and counts["passed"] + counts["failed"] == expected, "TRX outcomes mismatch")
    return counts


def live_report(report, case, digest, identity, models):
    require(report.get("Version") == 2 and report.get("Case") == case["Id"] and report.get("Mode") == "live", "Live identity missing")
    require(report.get("CorpusSHA256", "").lower() == digest and report.get("AttemptId") == identity, "Live source mismatch")
    rows = report.get("Observations", [])
    require(len(rows) == len(case["Turns"]) and sorted(r.get("Turn", -1) for r in rows) == list(range(len(rows))), "Incomplete observations")
    for row in rows:
        require(number(row.get("ElapsedMilliseconds")) and number(row.get("ModelCalls"), high=2), "Invalid turn metrics")
        require(isinstance(row.get("StateCorrect"), bool) and isinstance(row.get("ActCorrect"), bool), "Missing semantic/state result")
        require(row.get("CommercialEffects") == 0 and row.get("OutboundEffects") == 0, "Unauthorized effects")
    requests = report.get("Requests", [])
    require(number(report.get("HTTPAttempts"), 1, 12) and len(requests) == report["HTTPAttempts"], "Missing HTTP evidence")
    for r in requests:
        require(r.get("Transport") == "ProviderHttp" and r.get("Provider") in models and r.get("Model") == models[r["Provider"]], "Unobserved provider/model or intercepted HTTP")
        require(number(r.get("ElapsedMs")) and number(r.get("ReservedTokens"), 1)
                and (r.get("Status") is None or number(r["Status"], 100, 599)), "Invalid HTTP metrics")
        require(r.get("ReportedTokens") is None or number(r["ReportedTokens"]), "Invalid token metrics")
    usage = [r["ReportedTokens"] for r in requests if r.get("ReportedTokens") is not None]
    require(report.get("UsageResponses") == len(usage) and report.get("ReportedTokens") == (sum(usage) if usage else None), "Usage mismatch")
    require(report.get("TokenReservation") == sum(r["ReservedTokens"] for r in requests), "Reservation mismatch")
    quality = not report.get("Failures") and all(r["StateCorrect"] and r["ActCorrect"]
        and (r.get("ResponseValidation") == "ServerRealizedVerifiedSegments" or r.get("ResponseValidation") == "HumanSuppressed"
            and r.get("Outcome") == "NoExperimentalResponse" and r["ModelCalls"] == 0 and not r.get("PublicText")) for r in rows)
    times = sorted(r["ElapsedMs"] for r in requests)
    return {"http_attempts": len(requests), "providers_observed": sorted({r["Provider"] for r in requests}),
        "models_observed": sorted({r["Model"] for r in requests}), "reported_tokens": sum(usage) if usage else None,
        "usage_coverage": f"{len(usage)}/{len(requests)}", "total_consumption": "UNKNOWN", "reserved_tokens": report["TokenReservation"],
        "http_p50_ms": times[math.ceil(len(times) * .5)-1], "http_p95_ms": times[math.ceil(len(times) * .95)-1],
        "observed_contract_pass": quality, "uncertain_http": any(r.get("Status") is None for r in requests)}, rows


def source_fingerprint(root):
    paths = list((root / "backend/NexFlow.Application/Features/Automation/Agent").glob("*.cs"))
    paths += list((root / "backend/NexFlow.Tests/Concierge").glob("Agent*.cs"))
    paths += list((root / "scripts/llm-first").glob("*.py"))
    paths += [root / "backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreAgentTurnJournal.cs"]
    hashes = {str(p.relative_to(root)): hashlib.sha256(p.read_bytes().replace(b"\r\n", b"\n")).hexdigest() for p in sorted(paths) if p.is_file()}
    return hashlib.sha256(json.dumps(hashes, sort_keys=True).encode()).hexdigest()


def trial_identity(digest, case, sha, revision, models):
    return hashlib.sha256(json.dumps({"version": 2, "corpus": digest, "case": case, "sha": sha, "revision": revision,
        "models": models, "route": "AiRouter:Groq/Gemini:JSON:8s"}, sort_keys=True).encode()).hexdigest()


def legacy_blocks(folder, digest, case):
    old = folder / ("llm-live-" + digest + ".started")
    if not old.exists(): return False
    try: return json.loads(old.read_text(encoding="utf-8")).get("case") in (None, case)
    except (ValueError, OSError): return True  # Unknown historical attempt remains uncertain.


def finalize_claim(claim, identity, summary, output):
    if not claim or not claim.exists(): return "NotStarted"
    try:
        old = json.loads(claim.read_text(encoding="utf-8")); require(old.get("identity") == identity, "Claim identity changed")
        old.update(state="Terminal" if summary["result"] == "PASS" and not summary.get("live", {}).get("uncertain_http", True) else "Uncertain",
            result=summary["result"], evidence=str(output.resolve()))
        # Keep the original identity/claim on a crash; never delete or reopen it.
        claim.write_text(json.dumps(old, indent=2), encoding="utf-8")
        return old["state"]
    except (ValueError, OSError): return "Uncertain_UnreadableClaimPreserved"
