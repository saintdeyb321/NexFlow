"""Strict evidence/attempt protocol. No providers, credentials or network calls."""
import hashlib
import json
import math
import re
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
    require(report.get("Version") == 3 and report.get("Case") == case["Id"] and report.get("Mode") == "live", "Live trace Version/identity missing")
    require(report.get("CorpusSHA256", "").lower() == digest and report.get("AttemptId") == identity, "Live source mismatch")
    rows = report.get("Observations", [])
    require(len(rows) == len(case["Turns"]) and sorted(r.get("Turn", -1) for r in rows) == list(range(len(rows))), "Incomplete observations")
    for row in rows:
        require(number(row.get("ElapsedMilliseconds")) and number(row.get("ModelCalls"), high=2), "Invalid turn metrics")
        require(isinstance(row.get("StateCorrect"), bool) and isinstance(row.get("ActCorrect"), bool), "Missing semantic/state result")
        require(row.get("CommercialEffects") == 0 and row.get("OutboundEffects") == 0, "Unauthorized effects")
        trace_report(row, case["Turns"][row["Turn"]]["Text"])
    requests = report.get("Requests", [])
    require(number(report.get("HTTPAttempts"), 1, 12) and len(requests) == report["HTTPAttempts"], "Missing HTTP evidence")
    cursor = 0
    for row in rows:
        require(type(row.get("HTTPStart")) is int and row["HTTPStart"] == cursor
                and type(row.get("HTTPCount")) is int and 0 <= row["HTTPCount"] <= 12, "Missing/misaligned per-turn HTTP evidence")
        cursor += row["HTTPCount"]
    require(cursor == len(requests), "Unassigned per-turn HTTP attempts")
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


def exact_keys(value, keys, label):
    require(isinstance(value, dict) and set(value) == set(keys.split()), "Missing/unsafe trace " + label)


def trace_report(row, input_text):
    trace = row.get("Trace")
    exact_keys(trace, "SyntheticOnly InputText InputSha256 Before After ModelDecisions ExecutedTools ResultReads FactReferences Recovered ToolCalls Failure", "envelope")
    require(trace["SyntheticOnly"] is True and isinstance(trace["InputText"], str) and len(trace["InputText"]) <= 2000
            and trace["InputSha256"] == hashlib.sha256(input_text.encode()).hexdigest(), "Invalid trace input identity")
    for state in (trace["Before"], trace["After"]):
        exact_keys(state, "Goal Step Mode VersionHash PendingAction LocationId ServiceId Date Time HasPatientName Question", "context")
        require(isinstance(state["HasPatientName"], bool), "Invalid trace name-presence flag")
        for key in ("Goal", "Step", "Mode", "VersionHash", "PendingAction", "LocationId", "ServiceId", "Date", "Time"):
            require(state[key] is None or isinstance(state[key], str), "Invalid trace context value")
        if state["Question"] is not None:
            exact_keys(state["Question"], "IdHash SourceHash Expected Text ExpiresAt", "question")
            require(all(isinstance(state["Question"][k], str) for k in state["Question"]), "Invalid trace question")
    decisions = trace["ModelDecisions"]
    require(isinstance(decisions, list) and len(decisions) == row["ModelCalls"] <= 2, "Missing trace model decisions")
    for decision in decisions:
        exact_keys(decision, "Phase Outcome Decision Tools", "model")
        require(decision["Phase"] in ("Decision", "AfterReads") and decision["Outcome"] in ("Parsed", "InvalidJson", "ProviderError"), "Invalid trace model outcome")
        require(decision["Decision"] in (None, "Information", "Clarify", "PrepareReservation", "ClarifyCancellationScope", "ProposeDiscardDraft", "HumanAssistance", "RetainDraft"), "Invalid trace decision")
        require(isinstance(decision["Tools"], list) and len(decision["Tools"]) <= 3, "Invalid trace tool plan")
        for tool in decision["Tools"]:
            exact_keys(tool, "Kind Search LocationId", "requested tool")
            require(tool["Kind"] in ("Services", "Products", "Locations", "Profile", "Hours")
                    and all(tool[k] is None or isinstance(tool[k], str) for k in ("Search", "LocationId")), "Invalid trace requested tool")
    for key in ("ExecutedTools", "ResultReads"):
        require(isinstance(trace[key], list) and len(trace[key]) <= 8, "Missing trace executed reads")
        for read in trace[key]:
            exact_keys(read, "Kind Status Error Facts SensitiveFactsOmitted" if key == "ExecutedTools" else "Index Status Source Facts SensitiveFactsOmitted", "read")
            require(read["Status"] in ("Found", "NotFound", "Unavailable", "Denied", "Invalid", "LimitReached", "Error")
                    and read["SensitiveFactsOmitted"] == 0, "Unsafe trace read result")
            require(isinstance(read["Facts"], list) and len(read["Facts"]) <= 30, "Missing trace facts")
            require(read["Status"] == "Found" or not read["Facts"], "Unverified trace facts")
            for fact in read["Facts"]:
                exact_keys(fact, "Key Value Source EntityId", "fact")
                require(isinstance(fact["Key"], str) and isinstance(fact["Value"], str)
                        and fact["Source"] in ("BusinessRepository", "TransactionEngine")
                        and (fact["EntityId"] is None or isinstance(fact["EntityId"], str)), "Invalid trace fact provenance")
    require(isinstance(trace["FactReferences"], list) and all(type(i) is int and 0 <= i < len(trace["ResultReads"])
            and trace["ResultReads"][i]["Status"] == "Found" for i in trace["FactReferences"]), "Invalid trace fact references")
    require(isinstance(trace["Recovered"], bool) and number(trace["ToolCalls"], high=8) and isinstance(trace["Failure"], str), "Invalid trace terminal metadata")
    require(not trace["Recovered"] or not decisions and not trace["ExecutedTools"], "Replay invented new trace executions")
    # Independent artifact privacy check; no conversation interpretation or phrase rules.
    for value in [trace, row.get("PublicText")]:
        strings = []
        def collect(item):
            if isinstance(item, str): strings.append(item)
            elif isinstance(item, dict):
                for key, child in item.items():
                    # Typed, repository-owned monetary integer, not free text/contact.
                    if key == "Value" and item.get("Key") == "PriceMinorUnits" and item.get("Source") == "BusinessRepository" and isinstance(child, str) and child.isascii() and child.isdigit() and 1 <= len(child) <= 18:
                        continue
                    collect(child)
            elif isinstance(item, list):
                for child in item: collect(child)
        collect(value)
        require(not any(re.search(r"\+\d[\d ()-]{5,25}\d\b|\b\d{9,15}\b|\b\d{3}[ -]\d{3}[ -]\d{3}\b|[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", s) for s in strings), "Private contact in trace")


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
