#!/usr/bin/env python3
"""Build an auditable fixed replay corpus and decision-level mask cases.

This script does not call an LLM. It verifies stored frame hashes, links each
decision to the most recent retrieval, and creates factual/masked planner inputs.
Run the produced cases through the exact same frozen planner separately; only an
action change supports a direct decision-level causal claim.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable


@dataclass(frozen=True)
class InvalidMemory:
    memory_id: str
    description: str
    reason: str
    local_memory_id: str = ""


def read_jsonl(path: Path) -> list[dict[str, Any]]:
    records: list[dict[str, Any]] = []
    if not path.exists():
        return records
    for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip():
            continue
        try:
            value = json.loads(line)
        except json.JSONDecodeError as error:
            raise ValueError(f"{path}:{line_number}: {error}") from error
        if isinstance(value, dict):
            records.append(value)
    return records


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def canonical_ids(raw: str) -> list[str]:
    return sorted({item.strip() for item in (raw or "").split(",") if item.strip()})


def invalid_memories(events: Iterable[dict[str, Any]]) -> dict[str, InvalidMemory]:
    result: dict[str, InvalidMemory] = {}
    for event in events:
        if event.get("event_type") not in {"object_claim", "observation"}:
            continue
        memory_id = str(event.get("memory_id") or "").strip()
        if not memory_id:
            continue
        reason = ""
        if event.get("poison"):
            reason = "decoy_poison"
        elif event.get("hallucination"):
            reason = "unmatched_hallucination"
        elif event.get("alignment_status") == "ambiguous_overlap":
            reason = "ambiguous_overlap"
        if reason:
            result[memory_id] = InvalidMemory(
                memory_id=memory_id,
                description=str(event.get("description") or ""),
                reason=reason,
            )
    return result


def resolve_invalid_memory(
    evidence_id: str, invalid: dict[str, InvalidMemory]
) -> InvalidMemory | None:
    direct = invalid.get(evidence_id)
    if direct is not None:
        return InvalidMemory(evidence_id, direct.description, direct.reason, evidence_id)
    matches = [
        value for local_id, value in invalid.items()
        if evidence_id.endswith("__" + local_id)
    ]
    if len(matches) != 1:
        return None
    value = matches[0]
    return InvalidMemory(evidence_id, value.description, value.reason, value.memory_id)

def mask_context(text: str, memories: Iterable[InvalidMemory]) -> tuple[str, list[str]]:
    kept: list[str] = []
    removed: set[str] = set()
    values = list(memories)
    for line in (text or "").splitlines():
        lowered = line.lower()
        matched = False
        for memory in values:
            aliases = {memory.memory_id.lower()}
            if memory.local_memory_id:
                aliases.add(memory.local_memory_id.lower())
            probe = memory.description[:80].strip().lower()
            if any(alias in lowered for alias in aliases) or (probe and probe in lowered):
                removed.add(memory.memory_id)
                matched = True
                break
        if not matched:
            kept.append(line)
    return "\n".join(kept), sorted(removed)


def verify_frames(run_dir: Path) -> list[dict[str, Any]]:
    manifest_path = run_dir / "replay" / "frames.jsonl"
    frames = read_jsonl(manifest_path)
    verified: list[dict[str, Any]] = []
    for record in frames:
        relative = str(record.get("rgb_relative_path") or "")
        frame_path = run_dir / "replay" / relative
        expected = str(record.get("rgb_sha256") or "")
        status = "missing"
        actual = ""
        if frame_path.exists():
            actual = sha256_file(frame_path)
            status = "ok" if actual == expected else "hash_mismatch"
        verified.append(
            {
                "run_id": record.get("run_id"),
                "frame_id": record.get("frame_id"),
                "image_path": str(frame_path.resolve()),
                "expected_sha256": expected,
                "actual_sha256": actual,
                "verification": status,
                "method": record.get("method"),
                "scene": record.get("scene"),
                "attack_level": record.get("attack_level"),
                "seed": record.get("seed"),
            }
        )
    return verified


def build_cases(run_dir: Path, events: list[dict[str, Any]]) -> list[dict[str, Any]]:
    invalid = invalid_memories(events)
    cases: list[dict[str, Any]] = []
    last_retrieval: dict[str, Any] | None = None
    stale_ids: set[str] = set()

    for event in events:
        event_type = event.get("event_type")
        if event_type == "stale_transition":
            detail = str(event.get("detail") or "")
            object_id = detail.split(":", 1)[0].strip()
            if object_id:
                stale_ids.add(object_id)
        elif event_type == "retrieval":
            last_retrieval = event
        elif event_type == "decision" and last_retrieval is not None:
            evidence_ids = canonical_ids(str(event.get("evidence_ids") or ""))
            selected = [
                resolved for mid in evidence_ids
                if (resolved := resolve_invalid_memory(mid, invalid)) is not None
            ]
            # Stale IDs are recorded for audit; mapping object->memory is resolved in
            # the richer object_claim log during the next protocol revision.
            local_masked, local_removed = mask_context(
                str(last_retrieval.get("local_context") or ""), selected
            )
            database_masked, database_removed = mask_context(
                str(last_retrieval.get("database_context") or ""), selected
            )
            removed = sorted(set(local_removed + database_removed))
            selected_by_id = {item.memory_id: item for item in selected}
            if not removed:
                continue
            cases.append(
                {
                    "schema_version": 1,
                    "run_dir": str(run_dir.resolve()),
                    "run_id": event.get("run_id"),
                    "decision_id": event.get("decision_id") or event.get("event_id"),
                    "method": event.get("method"),
                    "factual_destination": event.get("destination"),
                    "factual_evidence_ids": evidence_ids,
                    "mask_type": "all_known_invalid",
                    "removed_memory_ids": removed,
                    "invalid_reasons": {
                        mid: selected_by_id[mid].reason for mid in removed
                        if mid in selected_by_id
                    },
                    "query": last_retrieval.get("query"),
                    "factual_local_context": last_retrieval.get("local_context", ""),
                    "factual_database_context": last_retrieval.get("database_context", ""),
                    "masked_local_context": local_masked,
                    "masked_database_context": database_masked,
                    "known_stale_object_ids_at_decision": sorted(stale_ids),
                    "interpretation_limit": (
                        "A masked-vs-factual action change is direct evidence only for "
                        "this planner decision, not the full episode."
                    ),
                }
            )
    return cases


def write_jsonl(path: Path, values: Iterable[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as stream:
        for value in values:
            stream.write(json.dumps(value, ensure_ascii=False, sort_keys=True) + "\n")


def discover_runs(inputs: list[Path]) -> list[Path]:
    runs: set[Path] = set()
    for value in inputs:
        if (value / "events.jsonl").exists():
            runs.add(value)
        elif value.exists():
            for event_path in value.rglob("events.jsonl"):
                runs.add(event_path.parent)
    return sorted(runs)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("inputs", nargs="+", type=Path, help="Run directories or a root containing runs")
    parser.add_argument("--output", type=Path, required=True, help="Output corpus directory")
    args = parser.parse_args()

    runs = discover_runs(args.inputs)
    if not runs:
        raise SystemExit("No run directory containing events.jsonl was found.")

    frame_index: list[dict[str, Any]] = []
    cases: list[dict[str, Any]] = []
    for run_dir in runs:
        events = read_jsonl(run_dir / "events.jsonl")
        frame_index.extend(verify_frames(run_dir))
        cases.extend(build_cases(run_dir, events))

    write_jsonl(args.output / "frame_index.jsonl", frame_index)
    write_jsonl(args.output / "counterfactual_cases.jsonl", cases)
    summary = {
        "schema_version": 1,
        "run_count": len(runs),
        "frame_count": len(frame_index),
        "verified_frame_count": sum(item["verification"] == "ok" for item in frame_index),
        "counterfactual_case_count": len(cases),
    }
    (args.output / "summary.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(summary, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
