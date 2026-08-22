#!/usr/bin/env python3
"""ICRA-oriented aggregation for embodied RAG runs (standard library only)."""

from __future__ import annotations

import argparse
import csv
import itertools
import json
import math
import statistics
from collections import defaultdict
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Sequence, Tuple

GROUP_FIELDS = ("method", "scene", "attack_level")
METRICS = (
    "task_success",
    "treasures_found",
    "total_time",
    "path_length",
    "treasure_presence_accuracy",
    "claim_precision",
    "claim_recall",
    "poison_write_rate",
    "retrieval_exposure_rate",
    "immediate_perception_attack_success",
    "retrieval_caused_attack_success",
    "verification_rate",
    "quarantine_rate",
    "raw_brier",
    "raw_ece",
    "calibrated_brier",
    "calibrated_ece",
    "llm_calls",
    "vlm_calls",
)


def load_json(path: Path) -> Dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def load_events(path: Path) -> List[Dict[str, Any]]:
    events: List[Dict[str, Any]] = []
    if not path.exists():
        return events
    with path.open("r", encoding="utf-8-sig") as handle:
        for line_number, line in enumerate(handle, 1):
            line = line.strip().lstrip("\ufeff")
            if not line:
                continue
            try:
                events.append(json.loads(line))
            except json.JSONDecodeError as error:
                print(f"WARNING: skipped {path}:{line_number}: {error}")
    return events


def safe_div(numerator: float, denominator: float) -> Optional[float]:
    return numerator / denominator if denominator else None


def calibration(pairs: Sequence[Tuple[float, int]], bins: int = 10) -> Tuple[Optional[float], Optional[float]]:
    if not pairs:
        return None, None
    clipped = [(max(0.0, min(1.0, score)), label) for score, label in pairs]
    brier = sum((score - label) ** 2 for score, label in clipped) / len(clipped)
    ece = 0.0
    for index in range(bins):
        lower, upper = index / bins, (index + 1) / bins
        selected = [
            item for item in clipped
            if lower <= item[0] < upper or (index == bins - 1 and item[0] == 1.0)
        ]
        if selected:
            confidence = statistics.fmean(item[0] for item in selected)
            accuracy = statistics.fmean(item[1] for item in selected)
            ece += len(selected) / len(clipped) * abs(confidence - accuracy)
    return brier, ece


def observation_pairs(
    observations: Sequence[Dict[str, Any]], score_field: str
) -> List[Tuple[float, int]]:
    pairs: List[Tuple[float, int]] = []
    for event in observations:
        score = event.get(score_field)
        if score is None:
            continue
        try:
            numeric = float(score)
        except (TypeError, ValueError):
            continue
        if not 0.0 <= numeric <= 1.0:
            continue
        if not bool(event.get("claimed_treasure")):
            continue
        correct = int(bool(event.get("treasure_visible")))
        pairs.append((numeric, correct))
    return pairs


def derive_trial(summary: Dict[str, Any], events: Sequence[Dict[str, Any]]) -> Dict[str, Any]:
    observations = [event for event in events if event.get("event_type") == "observation"]
    valid = [event for event in observations if float(event.get("credibility", -1) or -1) >= 0.0]
    claims = [event for event in valid if event.get("claimed_treasure")]
    visible = [event for event in valid if event.get("treasure_visible")]
    correct_claims = sum(bool(event.get("treasure_visible")) for event in claims)
    presence_correct = sum(
        bool(event.get("claimed_treasure")) == bool(event.get("treasure_visible")) for event in valid
    )
    decoy_only = [
        event for event in valid
        if event.get("decoy_visible") and not event.get("treasure_visible")
    ]
    poison_writes = sum(bool(event.get("poison")) and bool(event.get("memory_written")) for event in valid)
    retrievals = [event for event in events if event.get("event_type") == "retrieval"]
    exposed = sum(bool(event.get("poison")) for event in retrievals)
    gates = [event for event in events if event.get("event_type") == "action_gate"]
    verifications = sum(event.get("action") == "verify" for event in gates)
    quarantines = sum(event.get("action") == "quarantine" for event in gates)

    raw_brier, raw_ece = calibration(observation_pairs(valid, "raw_credibility"))
    calibrated_brier, calibrated_ece = calibration(observation_pairs(valid, "credibility"))
    target = int(summary.get("target_treasures", 3) or 3)
    require_hider = bool(summary.get("require_hider", False))
    task_success = int(
        int(summary.get("treasures_found", 0) or 0) >= target
        and (not require_hider or bool(summary.get("hider_found", False)))
    )

    row = dict(summary)
    row.update({
        "task_success": task_success,
        "treasure_presence_accuracy": safe_div(presence_correct, len(valid)),
        "claim_precision": safe_div(correct_claims, len(claims)),
        "claim_recall": safe_div(correct_claims, len(visible)),
        "poison_write_rate": safe_div(poison_writes, len(decoy_only)),
        "retrieval_exposure_rate": safe_div(exposed, len(retrievals)),
        "immediate_perception_attack_success": int(int(summary.get("poison_actions", 0) or 0) > 0),
        "retrieval_caused_attack_success": int(
            int(summary.get("retrieval_caused_poison_actions", 0) or 0) > 0
        ),
        "verification_rate": safe_div(verifications, len(valid)),
        "quarantine_rate": safe_div(quarantines, len(valid)),
        "raw_brier": raw_brier,
        "raw_ece": raw_ece,
        "calibrated_brier": calibrated_brier,
        "calibrated_ece": calibrated_ece,
        "valid_observations": len(valid),
        "decoy_only_frames": len(decoy_only),
        "event_file_present": int(bool(events)),
    })
    return row


def discover_trials(root: Path, include_invalid: bool) -> Tuple[List[Dict[str, Any]], List[Dict[str, Any]]]:
    trials: List[Dict[str, Any]] = []
    event_sets: List[Dict[str, Any]] = []
    for summary_path in sorted(root.rglob("summary.json")):
        summary = load_json(summary_path)
        if not include_invalid and summary.get("configuration_valid") is False:
            print(f"WARNING: excluded invalid run {summary_path.parent}")
            continue
        events = load_events(summary_path.with_name("events.jsonl"))
        row = derive_trial(summary, events)
        row["source_directory"] = str(summary_path.parent.resolve())
        trials.append(row)
        event_sets.append({"summary": summary, "events": events})
    return trials, event_sets


def t_critical_95(df: int) -> float:
    table = {
        1: 12.706, 2: 4.303, 3: 3.182, 4: 2.776, 5: 2.571, 6: 2.447,
        7: 2.365, 8: 2.306, 9: 2.262, 10: 2.228, 11: 2.201, 12: 2.179,
        13: 2.160, 14: 2.145, 15: 2.131, 16: 2.120, 17: 2.110, 18: 2.101,
        19: 2.093, 20: 2.086, 25: 2.060, 30: 2.042,
    }
    return table.get(df, 1.96)


def summarize(values: Iterable[Any]) -> Tuple[Optional[float], Optional[float], Optional[float]]:
    numeric = [float(value) for value in values if value is not None and value != ""]
    if not numeric:
        return None, None, None
    mean = statistics.fmean(numeric)
    if len(numeric) == 1:
        return mean, None, None
    sd = statistics.stdev(numeric)
    half_width = t_critical_95(len(numeric) - 1) * sd / math.sqrt(len(numeric))
    return mean, sd, half_width


def aggregate(trials: Sequence[Dict[str, Any]]) -> List[Dict[str, Any]]:
    grouped: Dict[Tuple[str, str, str], List[Dict[str, Any]]] = defaultdict(list)
    for trial in trials:
        grouped[tuple(str(trial.get(field, "unset")) for field in GROUP_FIELDS)].append(trial)
    rows: List[Dict[str, Any]] = []
    for key, group in sorted(grouped.items()):
        row: Dict[str, Any] = dict(zip(GROUP_FIELDS, key))
        row["n"] = len(group)
        for metric in METRICS:
            mean, sd, half_width = summarize(item.get(metric) for item in group)
            row[f"{metric}_mean"] = mean
            row[f"{metric}_sd"] = sd
            row[f"{metric}_ci95_half_width"] = half_width
        rows.append(row)
    return rows


def paired_differences(trials: Sequence[Dict[str, Any]]) -> List[Dict[str, Any]]:
    matched: Dict[Tuple[str, str, int], Dict[str, Dict[str, Any]]] = defaultdict(dict)
    for trial in trials:
        key = (
            str(trial.get("scene", "unset")),
            str(trial.get("attack_level", "unset")),
            int(trial.get("seed", -1)),
        )
        matched[key][str(trial.get("method", "unset"))] = trial
    methods = sorted({str(trial.get("method", "unset")) for trial in trials})
    rows: List[Dict[str, Any]] = []
    for method_a, method_b in itertools.combinations(methods, 2):
        for metric in ("task_success", "retrieval_caused_attack_success", "total_time", "path_length"):
            differences = []
            for group in matched.values():
                if method_a in group and method_b in group:
                    value_a, value_b = group[method_a].get(metric), group[method_b].get(metric)
                    if value_a is not None and value_b is not None:
                        differences.append(float(value_b) - float(value_a))
            mean, sd, half_width = summarize(differences)
            rows.append({
                "method_a": method_a,
                "method_b": method_b,
                "metric": metric,
                "n_pairs": len(differences),
                "mean_difference_b_minus_a": mean,
                "sd_difference": sd,
                "ci95_half_width": half_width,
                "cohen_dz": mean / sd if mean is not None and sd not in (None, 0.0) else None,
            })
    return rows


def risk_coverage(event_sets: Sequence[Dict[str, Any]]) -> List[Dict[str, Any]]:
    grouped: Dict[Tuple[str, str, str], List[Tuple[float, int]]] = defaultdict(list)
    for item in event_sets:
        summary, events = item["summary"], item["events"]
        key = tuple(str(summary.get(field, "unset")) for field in GROUP_FIELDS)
        observations = [event for event in events if event.get("event_type") == "observation"]
        grouped[key].extend(observation_pairs(observations, "credibility"))
    rows: List[Dict[str, Any]] = []
    for key, pairs in sorted(grouped.items()):
        for step in range(21):
            threshold = step / 20.0
            accepted = [label for score, label in pairs if score >= threshold]
            rows.append({
                **dict(zip(GROUP_FIELDS, key)),
                "threshold": threshold,
                "n_total": len(pairs),
                "n_accepted": len(accepted),
                "coverage": safe_div(len(accepted), len(pairs)),
                "selective_risk": 1.0 - statistics.fmean(accepted) if accepted else None,
            })
    return rows


def write_csv(path: Path, rows: Sequence[Dict[str, Any]]) -> None:
    if not rows:
        path.write_text("", encoding="utf-8-sig")
        return
    fields: List[str] = []
    for row in rows:
        for key in row:
            if key not in fields:
                fields.append(key)
    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def fmt(value: Any) -> str:
    return "NA" if value is None or value == "" else f"{float(value):.3f}"


def write_report(path: Path, trials: Sequence[Dict[str, Any]], groups: Sequence[Dict[str, Any]]) -> None:
    lines = [
        "# Embodied RAG ICRA 实验汇总",
        "",
        f"有效试验：{len(trials)}；实验组：{len(groups)}。配置无效的运行默认已排除。",
        "",
        "| 方法 | 场景 | 条件 | n | 成功率 | 检索诱发攻击 | 即时感知攻击 | 路径 | 校准 Brier | ECE |",
        "|---|---|---|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for group in groups:
        lines.append(
            f"| {group['method']} | {group['scene']} | {group['attack_level']} | {group['n']} | "
            f"{fmt(group.get('task_success_mean'))} | "
            f"{fmt(group.get('retrieval_caused_attack_success_mean'))} | "
            f"{fmt(group.get('immediate_perception_attack_success_mean'))} | "
            f"{fmt(group.get('path_length_mean'))} | "
            f"{fmt(group.get('calibrated_brier_mean'))} | {fmt(group.get('calibrated_ece_mean'))} |"
        )
    lines += [
        "",
        "## 解释边界",
        "",
        "- `retrieval_caused_attack_success` 要求决策使用了已标记污染的稳定 memory ID；它与当前帧误判后立即追逐分开。",
        "- Brier/ECE 与 risk–coverage 默认只评估肯定 treasure claim；标签是该 claim 是否对应真实可见宝物。必须在独立测试集报告。",
        "- 当前 t 区间和配对差值用于预实验诊断。正式稿仍应使用 method×attack 混合效应模型、效应量及 Holm 校正。",
        "- `risk_coverage.csv` 用于画选择性风险—覆盖率曲线；阈值不可在测试集上回调。",
    ]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description="Analyze embodied RAG ICRA experiments.")
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--include-invalid", action="store_true")
    args = parser.parse_args()
    trials, event_sets = discover_trials(args.input, args.include_invalid)
    if not trials:
        print(f"No valid summary.json files found under {args.input}")
        return 2
    args.output.mkdir(parents=True, exist_ok=True)
    groups = aggregate(trials)
    write_csv(args.output / "trial_results.csv", trials)
    write_csv(args.output / "aggregated_results.csv", groups)
    write_csv(args.output / "paired_differences.csv", paired_differences(trials))
    write_csv(args.output / "risk_coverage.csv", risk_coverage(event_sets))
    write_report(args.output / "report.md", trials, groups)
    print(f"Analyzed {len(trials)} valid trials into {args.output.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
