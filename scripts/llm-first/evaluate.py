"""Development policy evaluation by default. No model/WhatsApp calls offline."""
import argparse
import csv
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET
import evaluation_evidence as evidence

ROOT = Path(__file__).resolve().parents[2]
DEFAULT = ROOT / "backend/NexFlow.Tests/Concierge/AgentDevelopmentConversations.json"
# Publicly exposed evaluation identity supplied by the owner. Metadata only;
# never open the public file or accept an external copy as a blind benchmark.
EXPOSED_CORPUS_DIGESTS = {"a64059637c90e802f7a4692bf3b4bed38cf9070d16bc0f0f7e8c8efea33549b5"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--offline", action="store_true", help="default; scripted interpretation, not NLU certification")
    mode.add_argument("--live", action="store_true", help="requires separate authorization, verified quota and synthetic credentials")
    parser.add_argument("--dry-run", action="store_true", help="preflight only; no dotnet, marker or HTTP")
    parser.add_argument("--corpus", type=Path, default=DEFAULT)
    parser.add_argument("--sha256", help="required for external/sealed corpus and live evaluation")
    parser.add_argument("--output", type=Path, required=True, help="new directory; never overwrite evidence")
    parser.add_argument("--case", help="live: one complete conversation, never entire corpus by default")
    parser.add_argument("--max-http", type=int, help="live: explicit shared budget, 1..12")
    parser.add_argument("--max-token-reservation", type=int, help="live: explicit shared token reservation, 2048..100000")
    parser.add_argument("--no-build", action="store_true")
    args = parser.parse_args()
    if args.live:
        # Resolve symlinks as well; reject before reading repository content.
        if args.corpus.resolve().is_relative_to(ROOT.resolve()):
            parser.error("BLOCKED: live requires a new private corpus outside the repository")
        if not args.sha256 or len(args.sha256) != 64 or any(c not in "0123456789abcdefABCDEF" for c in args.sha256):
            parser.error("A frozen external/live corpus requires --sha256 (64 hexadecimal digits)")
        if args.max_http is None or args.max_token_reservation is None:
            parser.error("BLOCKED: explicit --max-http and --max-token-reservation required")
    data = args.corpus.read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    if args.live and digest in EXPOSED_CORPUS_DIGESTS:
        parser.error("BLOCKED: exposed evaluation material cannot certify blind understanding")
    if (args.corpus.resolve() != DEFAULT.resolve() or args.live) and not args.sha256:
        parser.error("A frozen external/live corpus requires --sha256 before evaluation")
    if args.sha256 and digest.lower() != args.sha256.lower():
        parser.error("Corpus SHA256 mismatch")
    corpus = json.loads(data)
    if not 1 <= len(corpus["Conversations"]) <= 60:
        parser.error("Invalid corpus size")
    if args.output.exists(): parser.error("Output already exists; preserve evidence and use a new directory")
    env = os.environ.copy(); marker = None; attempt = None
    sha = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    revision = evidence.source_fingerprint(ROOT)
    models = {p: env.get("NEXFLOW_TEST_" + p.upper() + "_MODEL", "UNKNOWN") for p in ("Groq", "Gemini")}
    # Never inherit a previous live authorization into an offline invocation.
    if not args.live:
        env.pop("NEXFLOW_LLM_LIVE_AUTHORIZED", None)
    else:
        if not args.dry_run and (env.get("NEXFLOW_LLM_LIVE_AUTHORIZED") != "1" or env.get("NEXFLOW_LLM_QUOTAS_VERIFIED") != "1"):
            parser.error("BLOCKED: explicit live authorization and verified quotas required")
        if not args.dry_run and env.get("NEXFLOW_LLM_PRIVATE_CORPUS_APPROVED") != "1":
            parser.error("BLOCKED: independent approval of a NEW private corpus required")
        for provider in ("GROQ", "GEMINI"):
            if not args.dry_run and not all(env.get(f"NEXFLOW_TEST_{provider}_{field}") for field in ("API_KEY", "MODEL")):
                parser.error(f"BLOCKED: synthetic {provider} environment required; no settings fallback")
        if not args.case or args.case not in [c["Id"] for c in corpus["Conversations"]]:
            parser.error("--live requires --case from the sealed corpus")
        if not 1 <= args.max_http <= 12 or not 2048 <= args.max_token_reservation <= 100000:
            parser.error("Invalid bounded HTTP/token reservation")
        attempt = evidence.trial_identity(digest, args.case, sha, revision, models)
        marker = ROOT / "backend/TestResults" / ("llm-live-v2-" + attempt + ".json")
        if evidence.legacy_blocks(marker.parent, digest, args.case) or marker.exists():
            parser.error("BLOCKED: prior terminal/uncertain trial; no automatic replay")
        if not args.dry_run: marker.parent.mkdir(parents=True, exist_ok=True)
        env.update(NEXFLOW_LLM_LIVE_CASE=args.case, NEXFLOW_LLM_MAX_HTTP=str(args.max_http),
                   NEXFLOW_LLM_MAX_TOKEN_RESERVATION=str(args.max_token_reservation),
                   NEXFLOW_LLM_ATTEMPT_ID=attempt, NEXFLOW_LLM_CLAIM_FILE=str(marker.resolve()))
    if args.dry_run:
        print(json.dumps({"state": "Preflight", "mode": "live" if args.live else "offline", "corpus_sha256": digest,
            "case": args.case, "sha": sha, "revision": revision, "attempt_id": attempt, "models": models, "HTTP": 0,
            "authorization": "SEPARATELY_REQUIRED" if args.live else "NOT_APPLICABLE"}, indent=2)); return 0
    args.output.mkdir(parents=True, exist_ok=False)
    env.update(NEXFLOW_LLM_CORPUS=str(args.corpus.resolve()), NEXFLOW_LLM_CORPUS_SHA256=digest,
               NEXFLOW_LLM_EVALUATION_OUTPUT=str(args.output.resolve()))
    test = "AgentLiveEvaluationTests" if args.live else "AgentConversationEvaluationTests"
    command = ["dotnet", "test", "backend/NexFlow.Tests/NexFlow.Tests.csproj", "--no-restore", "--disable-build-servers", "--tl:off", "-m:1",
               "--filter", "FullyQualifiedName~" + test, "--logger", "trx;LogFileName=evaluation.trx",
               "--results-directory", str(args.output.resolve())]
    if args.no_build:
        command.append("--no-build")
    with (args.output / "identity.json").open("x", encoding="utf-8") as f:
        json.dump({"corpus_sha256": digest, "provenance": corpus["Provenance"], "mode": "live" if args.live else "offline",
                   "command": command, "git_sha": sha, "revision": revision, "attempt_id": attempt}, f, indent=2)
    summary = {"corpus_sha256": digest, "result": "FAIL", "real_nlu": "UNVERIFIED" if args.live else "NOT_RUN", "human_quality": "BLOCKED", "modes": {}}
    try:
        return execute(args, corpus, digest, attempt, marker, models, env, command, summary)
    except (OSError, ValueError, KeyError, TypeError, AttributeError, ET.ParseError, subprocess.TimeoutExpired) as error:
        summary["result"] = "FAIL"; summary["error"] = type(error).__name__ + ": incomplete/invalid evidence; inspect local sanitized artifacts"
        return 1
    finally:
        summary["claim_state"] = evidence.finalize_claim(marker, attempt, summary, args.output)
        (args.output / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")


def execute(args, corpus, digest, attempt, marker, models, env, command, summary):
    result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=600)
    (args.output / "console.log").write_text(result.stdout + result.stderr, encoding="utf-8")
    print(result.stdout, end="")
    expected = 1 if args.live else len(corpus["Conversations"])
    counters = evidence.read_trx(args.output / "evaluation.trx", expected)
    complete = result.returncode == 0 and counters["failed"] == 0
    summary["counts"] = counters
    if args.live:
        case = next(c for c in corpus["Conversations"] if c["Id"] == args.case)
        report = json.loads((args.output / ("live-" + args.case + ".json")).read_text(encoding="utf-8"))
        summary["live"], rows = evidence.live_report(report, case, digest, attempt, models)
        evidence.require(marker.is_file() and json.loads(marker.read_text()).get("identity") == attempt, "Missing durable HTTP claim")
        summary["result"] = "PASS" if complete and summary["live"]["observed_contract_pass"] else "FAIL"
        summary["real_nlu"] = "OBSERVED_SYNTHETIC_SAMPLE_ONLY" if summary["result"] == "PASS" else "FAILED_OR_INCOMPLETE"
        print(json.dumps(summary, indent=2))
        return 0 if summary["result"] == "PASS" else 1
    report_paths = [args.output / (c["Id"] + ".json") for c in corpus["Conversations"]]
    if not args.live and not all(p.is_file() for p in report_paths):
        complete = False
    reports = [json.loads(p.read_text(encoding="utf-8")) for p in report_paths if p.is_file()]
    for case, report in zip(corpus["Conversations"], reports):
        evidence.require(report.get("CorpusSHA256", "").lower() == digest and report.get("Mode") == "offline" and report.get("ProviderHTTP") == 0, "Offline identity mismatch")
        for mode in ("legacy", "shadow", "agent"):
            observed = [r for r in report["Observations"] if r.get("Mode") == mode]
            evidence.require(len(observed) == len(case["Turns"]) and sorted(r["Turn"] for r in observed) == list(range(len(observed)))
                and all(r["Case"] == case["Id"] and evidence.number(r.get("ElapsedMs")) for r in observed), "Offline observations incomplete")
        evidence.require(not report.get("Violations"), "Offline policy violations")
    rows = [row for report in reports for row in report["Observations"]]
    summary.update(counts=counters, result="PASS" if complete else "FAIL", real_nlu="NOT_RUN")
    for name in ("legacy", "shadow", "agent"):
        selected = [r for r in rows if r["Mode"] == name]
        if not selected:
            continue
        times = sorted(r["ElapsedMs"] for r in selected)
        summary["modes"][name] = {"turns": len(selected), "policy_matches": sum(r["PolicyMatches"] for r in selected),
            "logical_calls": sum(r["LogicalCalls"] for r in selected), "read_queries": sum(r["ReadQueries"] for r in selected),
            "commercial_effects": sum(r["CommercialEffects"] for r in selected), "repeated_response": sum(r["RepeatedResponse"] for r in selected),
            "p50_local_ms": times[math.ceil(len(times) * .5) - 1], "p95_local_ms": times[math.ceil(len(times) * .95) - 1],
            "provider_http": 0, "provider_tokens": None}
    if rows:
        with (args.output / "turns.csv").open("x", encoding="utf-8", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=list(rows[0])); writer.writeheader(); writer.writerows(rows)
    print(json.dumps(summary, indent=2))
    return 0 if result.returncode == 0 and complete else 1


if __name__ == "__main__":
    sys.exit(main())
