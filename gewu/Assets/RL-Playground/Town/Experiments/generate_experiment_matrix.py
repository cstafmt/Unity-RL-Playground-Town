#!/usr/bin/env python3
"""Generate a blocked, matched-seed VRAG experiment schedule."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import random
from pathlib import Path


DEFAULT_METHODS = [
    "A_no_memory",
    "B_vanilla_rag",
    "C_heuristic_trust",
    "D_reliability_aware",
]
DEFAULT_ATTACKS = [
    "L0_clean",
    "L1_static_decoy",
    "L2_repeated_exposure",
    "L3_stale_conflict",
]


def parse_csv(value: str) -> list[str]:
    result = [item.strip() for item in value.split(",") if item.strip()]
    if not result:
        raise argparse.ArgumentTypeError("expected at least one comma-separated value")
    return result


def stable_trial_id(layout: str, attack: str, seed: int, method: str) -> str:
    source = f"{layout}|{attack}|{seed}|{method}".encode("utf-8")
    return hashlib.sha256(source).hexdigest()[:12]


def build_schedule(
    layouts: list[str], methods: list[str], attacks: list[str], seeds: list[int], order_seed: int
) -> list[dict[str, object]]:
    rng = random.Random(order_seed)
    rows: list[dict[str, object]] = []
    trial_number = 1
    for layout in layouts:
        for attack in attacks:
            for seed in seeds:
                block = [(method, stable_trial_id(layout, attack, seed, method)) for method in methods]
                rng.shuffle(block)
                block_id = f"{layout}__{attack}__seed{seed}"
                for within_block_order, (method, uid) in enumerate(block, 1):
                    rows.append(
                        {
                            "run_order": trial_number,
                            "block_id": block_id,
                            "within_block_order": within_block_order,
                            "trial_uid": uid,
                            "layout": layout,
                            "method": method,
                            "attack": attack,
                            "seed": seed,
                            "trial_id": trial_number,
                            "run_label": f"{method}__{layout}__{attack}__trial{trial_number:03d}__seed{seed}",
                            "status": "planned",
                            "exclusion_reason": "",
                        }
                    )
                    trial_number += 1
    return rows


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--layouts", type=parse_csv, required=True, help="Comma-separated frozen layout IDs")
    parser.add_argument("--methods", type=parse_csv, default=DEFAULT_METHODS)
    parser.add_argument("--attacks", type=parse_csv, default=DEFAULT_ATTACKS)
    parser.add_argument("--seeds", type=parse_csv, default=["1001", "1002", "1003"])
    parser.add_argument("--order-seed", type=int, default=20260820)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        seeds = [int(value) for value in args.seeds]
    except ValueError as error:
        raise SystemExit(f"Every seed must be an integer: {error}") from error

    rows = build_schedule(args.layouts, args.methods, args.attacks, seeds, args.order_seed)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)

    manifest = {
        "schema_version": 1,
        "layouts": args.layouts,
        "methods": args.methods,
        "attacks": args.attacks,
        "seeds": seeds,
        "order_seed": args.order_seed,
        "run_count": len(rows),
        "schedule_csv": str(args.output),
    }
    args.output.with_suffix(".manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    print(json.dumps(manifest, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
