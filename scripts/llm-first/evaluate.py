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

ROOT = Path(__file__).resolve().parents[2]
DEFAULT = ROOT / "backend/NexFlow.Tests/Concierge/AgentDevelopmentConversations.json"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--offline", action="store_true", help="default; scripted interpretation, not NLU certification")
    mode.add_argument("--live", action="store_true", help="requires separate authorization, verified quota and synthetic credentials")
    parser.add_argument("--corpus", type=Path, default=DEFAULT)
    parser.add_argument("--sha256", help="required for external/sealed corpus and live evaluation")
    parser.add_argument("--output", type=Path, required=True, help="new directory; never overwrite evidence")
    parser.add_argument("--case", help="live: one complete conversation, never entire corpus by default")
    parser.add_argument("--max-http", type=int, default=12)
    parser.add_argument("--max-token-reservation", type=int, default=30000)
    parser.add_argument("--no-build", action="store_true")
    args = parser.parse_args()
    data = args.corpus.read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    if (args.corpus.resolve() != DEFAULT.resolve() or args.live) and not args.sha256:
        parser.error("A frozen external/live corpus requires --sha256 before evaluation")
    if args.sha256 and digest.lower() != args.sha256.lower():
        parser.error("Corpus SHA256 mismatch")
    corpus = json.loads(data)
    if not 1 <= len(corpus["Conversations"]) <= 60:
        parser.error("Invalid corpus size")
    env = os.environ.copy()
    # Never inherit a previous live authorization into an offline invocation.
    if not args.live:
        env.pop("NEXFLOW_LLM_LIVE_AUTHORIZED", None)
    else:
        if env.get("NEXFLOW_LLM_LIVE_AUTHORIZED") != "1" or env.get("NEXFLOW_LLM_QUOTAS_VERIFIED") != "1":
            parser.error("BLOCKED: explicit live authorization and verified quotas required")
        for provider in ("GROQ", "GEMINI"):
            if not all(env.get(f"NEXFLOW_TEST_{provider}_{field}") for field in ("API_KEY", "MODEL")):
                parser.error(f"BLOCKED: synthetic {provider} environment required; no settings fallback")
        if not args.case or args.case not in [c["Id"] for c in corpus["Conversations"]]:
            parser.error("--live requires --case from the sealed corpus")
        if not 1 <= args.max_http <= 12 or not 2048 <= args.max_token_reservation <= 100000:
            parser.error("Invalid bounded HTTP/token reservation")
        # One permanent marker per corpus: uncertain live attempts are not silently rerun.
        marker = ROOT / "backend/TestResults" / ("llm-live-" + digest + ".started")
        marker.parent.mkdir(parents=True, exist_ok=True)
        with marker.open("x", encoding="utf-8") as f:
            json.dump({"corpus_sha256": digest, "case": args.case, "max_http": args.max_http,
                       "max_token_reservation": args.max_token_reservation}, f)
        env.update(NEXFLOW_LLM_LIVE_CASE=args.case, NEXFLOW_LLM_MAX_HTTP=str(args.max_http),
                   NEXFLOW_LLM_MAX_TOKEN_RESERVATION=str(args.max_token_reservation))
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
                   "command": command, "git_sha": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()}, f, indent=2)
    result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=600)
    (args.output / "console.log").write_text(result.stdout + result.stderr, encoding="utf-8")
    print(result.stdout, end="")
    trx = ET.parse(args.output / "evaluation.trx").getroot()
    counters = trx.find("./{*}ResultSummary/{*}Counters").attrib
    expected = 1 if args.live else len(corpus["Conversations"])
    complete = (int(counters["executed"]) == expected == int(counters["passed"]) and int(counters["failed"]) == 0
                and int(counters["notExecuted"]) == 0)
    report_paths = [args.output / (c["Id"] + ".json") for c in corpus["Conversations"]]
    if not args.live and not all(p.is_file() for p in report_paths):
        complete = False
    reports = [json.loads(p.read_text(encoding="utf-8")) for p in report_paths if p.is_file()]
    rows = [row for report in reports for row in report["Observations"]]
    summary = {"corpus_sha256": digest, "counts": counters, "result": "PASS" if result.returncode == 0 and complete else "FAIL",
               "real_nlu": "NOT_RUN" if not args.live else "INSPECT_LIVE_REPORT", "human_quality": "BLOCKED", "modes": {}}
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
    with (args.output / "summary.json").open("x", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)
    if rows:
        with (args.output / "turns.csv").open("x", encoding="utf-8", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=list(rows[0])); writer.writeheader(); writer.writerows(rows)
    print(json.dumps(summary, indent=2))
    return 0 if result.returncode == 0 and complete else 1


if __name__ == "__main__":
    sys.exit(main())
