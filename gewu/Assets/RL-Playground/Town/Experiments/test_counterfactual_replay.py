#!/usr/bin/env python3
from pathlib import Path
import unittest

from build_counterfactual_replay import build_cases


class CounterfactualReplayTests(unittest.TestCase):
    def test_backend_mid_resolves_local_object_claim_id(self) -> None:
        events = [
            {
                "event_type": "object_claim",
                "memory_id": "mem_0",
                "description": "yellow cube decoy at spawn",
                "poison": True,
            },
            {
                "event_type": "retrieval",
                "query": "treasure",
                "local_context": "[MID:mem_0] yellow cube decoy at spawn",
                "database_context": "[MID:run_abc__mem_0] yellow cube decoy at spawn",
            },
            {
                "event_type": "decision",
                "run_id": "run_abc",
                "decision_id": "decision_0",
                "destination": "Spawn",
                "evidence_ids": "run_abc__mem_0",
            },
        ]

        cases = build_cases(Path("."), events)
        self.assertEqual(len(cases), 1)
        self.assertEqual(cases[0]["removed_memory_ids"], ["run_abc__mem_0"])
        self.assertEqual(
            cases[0]["invalid_reasons"], {"run_abc__mem_0": "decoy_poison"}
        )


if __name__ == "__main__":
    unittest.main()
