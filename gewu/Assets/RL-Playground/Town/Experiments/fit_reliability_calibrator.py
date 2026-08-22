#!/usr/bin/env python3
"""Fit a held-out logit/Platt calibrator from Unity observation events.
The target is correctness among affirmative treasure claims:
`P(treasure_visible | claimed_treasure=True, raw_score)`. Calibration and test
directories must be disjoint.
`claimed_treasure == treasure_visible`. Calibration and test directories must be disjoint.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
from typing import Dict, Iterable, List, Sequence, Tuple


def clip(value: float, low: float = 1e-4, high: float = 1.0 - 1e-4) -> float:
    return max(low, min(high, value))


def sigmoid(value: float) -> float:
    if value >= 0.0:
        z = math.exp(-value)
        return 1.0 / (1.0 + z)
    z = math.exp(value)
    return z / (1.0 + z)


def logit(probability: float) -> float:
    probability = clip(probability)
    return math.log(probability / (1.0 - probability))


def iter_events(root: Path) -> Iterable[Tuple[Path, Dict[str, object]]]:
    for path in sorted(root.rglob("events.jsonl")):
        with path.open("r", encoding="utf-8-sig") as handle:
            for line_number, line in enumerate(handle, 1):
                line = line.strip().lstrip("\ufeff")
                if not line:
                    continue
                try:
                    yield path, json.loads(line)
                except json.JSONDecodeError as error:
                    raise ValueError(f"Invalid JSON at {path}:{line_number}: {error}") from error


def load_pairs(root: Path) -> Tuple[List[Tuple[float, int]], List[str]]:
    pairs: List[Tuple[float, int]] = []
    sources = set()
    for path, event in iter_events(root):
        if event.get("event_type") != "observation":
            continue
        raw = event.get("raw_credibility")
        if raw is None:
            continue
        try:
            score = float(raw)
        except (TypeError, ValueError):
            continue
        if not 0.0 <= score <= 1.0:
            continue
        if "claimed_treasure" not in event or "treasure_visible" not in event:
            continue
        claimed = bool(event.get("claimed_treasure"))
        if not claimed:
            continue
        visible = bool(event.get("treasure_visible"))
        pairs.append((score, int(visible)))
        sources.add(str(path.resolve()))
    return pairs, sorted(sources)


def fit_platt(
    pairs: Sequence[Tuple[float, int]], regularization: float = 1e-3, iterations: int = 100
) -> Tuple[float, float]:
    xs = [logit(score) for score, _ in pairs]
    ys = [label for _, label in pairs]
    scale, bias = 1.0, 0.0
    for _ in range(iterations):
        grad_scale = regularization * scale
        grad_bias = 0.0
        h_ss = regularization
        h_sb = 0.0
        h_bb = 1e-9
        for x_value, label in zip(xs, ys):
            probability = sigmoid(scale * x_value + bias)
            residual = probability - label
            weight = max(1e-9, probability * (1.0 - probability))
            grad_scale += residual * x_value
            grad_bias += residual
            h_ss += weight * x_value * x_value
            h_sb += weight * x_value
            h_bb += weight
        determinant = h_ss * h_bb - h_sb * h_sb
        if abs(determinant) < 1e-12:
            break
        delta_scale = (h_bb * grad_scale - h_sb * grad_bias) / determinant
        delta_bias = (-h_sb * grad_scale + h_ss * grad_bias) / determinant
        scale -= max(-2.0, min(2.0, delta_scale))
        bias -= max(-2.0, min(2.0, delta_bias))
        if abs(delta_scale) + abs(delta_bias) < 1e-8:
            break
    return scale, bias


def metrics(
    pairs: Sequence[Tuple[float, int]], scale: float, bias: float, bins: int = 10
) -> Dict[str, float]:
    predictions = [sigmoid(scale * logit(score) + bias) for score, _ in pairs]
    labels = [label for _, label in pairs]
    brier = sum((prediction - label) ** 2 for prediction, label in zip(predictions, labels)) / len(labels)
    nll = -sum(
        label * math.log(clip(prediction)) + (1 - label) * math.log(clip(1.0 - prediction))
        for prediction, label in zip(predictions, labels)
    ) / len(labels)
    ece = 0.0
    for index in range(bins):
        lower, upper = index / bins, (index + 1) / bins
        selected = [
            (prediction, label)
            for prediction, label in zip(predictions, labels)
            if lower <= prediction < upper or (index == bins - 1 and prediction == 1.0)
        ]
        if not selected:
            continue
        confidence = sum(item[0] for item in selected) / len(selected)
        accuracy = sum(item[1] for item in selected) / len(selected)
        ece += len(selected) / len(labels) * abs(confidence - accuracy)
    return {"brier": brier, "nll": nll, "ece_10": ece}


def main() -> int:
    parser = argparse.ArgumentParser(description="Fit held-out embodied-memory reliability calibration.")
    parser.add_argument("--input", type=Path, required=True, help="Calibration-only run directory.")
    parser.add_argument("--output", type=Path, required=True, help="JSON asset to assign in Unity.")
    parser.add_argument("--split-name", required=True, help="Frozen calibration split identifier.")
    parser.add_argument("--min-samples", type=int, default=100)
    parser.add_argument("--write-threshold", type=float, default=0.25)
    parser.add_argument("--verify-threshold", type=float, default=0.45)
    parser.add_argument("--pursue-threshold", type=float, default=0.75)
    parser.add_argument("--independent-confirmations", type=int, default=1)
    parser.add_argument("--view-distance", type=float, default=1.5)
    parser.add_argument("--view-angle", type=float, default=25.0)
    args = parser.parse_args()

    pairs, sources = load_pairs(args.input)
    positives = sum(label for _, label in pairs)
    negatives = len(pairs) - positives
    if len(pairs) < args.min_samples:
        raise SystemExit(f"Need at least {args.min_samples} calibration observations; found {len(pairs)}.")
    if positives < 10 or negatives < 10:
        raise SystemExit(f"Need at least 10 correct and 10 incorrect observations; got {positives}/{negatives}.")

    scale, bias = fit_platt(pairs)
    before = metrics(pairs, 1.0, 0.0)
    after = metrics(pairs, scale, bias)
    digest_payload = json.dumps(
        {"pairs": pairs, "split": args.split_name, "scale": scale, "bias": bias},
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    calibration_id = "platt_" + hashlib.sha256(digest_payload).hexdigest()[:12]

    result = {
        "schema_version": 1,
        "is_fitted": True,
        "calibration_id": calibration_id,
        "fitted_split": args.split_name,
        "sample_count": len(pairs),
        "correct_count": positives,
        "incorrect_count": negatives,
        "logit_scale": scale,
        "logit_bias": bias,
        "write_threshold": clip(args.write_threshold, 0.0, 1.0),
        "verify_threshold": clip(args.verify_threshold, 0.0, 1.0),
        "pursue_threshold": clip(args.pursue_threshold, 0.0, 1.0),
        "minimum_independent_confirmations": max(1, args.independent_confirmations),
        "independent_view_distance": max(0.1, args.view_distance),
        "independent_view_angle": max(1.0, min(180.0, args.view_angle)),
        "fit_metrics_before": before,
        "fit_metrics_after": after,
        "source_event_files": sources,
        "warning": "Calibration fit metrics are not test-set results.",
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {calibration_id} from n={len(pairs)} to {args.output.resolve()}")
    print(f"Brier {before['brier']:.4f} -> {after['brier']:.4f}; ECE {before['ece_10']:.4f} -> {after['ece_10']:.4f}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
