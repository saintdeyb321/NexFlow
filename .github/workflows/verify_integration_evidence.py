"""Require real-store journal evidence in addition to the existing full matrix."""
import json
import sys
from g8_certification import read_evidence, require

JOURNAL = ("NexFlow.Tests.Integration.AgentJournalEmulatorTests",
           "Two_emulator_clients_enforce_one_owner_atomic_context_and_physical_tombstone")


def validate(path):
    counts, results, definitions, outcome = read_evidence(path)
    require(outcome in ("Completed", "Passed"), "Integration summary is not successful")
    require(counts["total"] >= 37, "RC3 and commercial integrations were not discovered")
    require(counts["total"] == counts["passed"] and counts["failed"] == 0, "All integrations must pass; no omissions")
    journal = [r for r in results if definitions[r.attrib["testId"]] == JOURNAL]
    require(len(journal) == 1 and journal[0].attrib["outcome"] == "Passed", "Exactly one real Firestore journal PASS is required")
    return counts


if __name__ == "__main__":
    try:
        print(json.dumps(validate(sys.argv[1]), sort_keys=True))
    except (ValueError, OSError) as error:
        sys.exit("NO-GO: " + str(error))
