#!/usr/bin/env python3
"""LoRA fine-tune a small code model on the verified AGC dataset using MLX.

Format: MLX-LM expects completions jsonl with a `text` field, or chat jsonl
with `messages`. We emit chat format, system+user+assistant roles.

Usage:
  python3 finetune_mlx.py \\
    --model mlx-community/Qwen2.5-Coder-1.5B-Instruct-4bit \\
    --epochs 3

Output:
  train/mlx_chat/train.jsonl, valid.jsonl (90/10 split)
  train/lora_adapter/       (LoRA weights ~30 MB)
"""
from __future__ import annotations

import argparse
import json
import random
import subprocess
import sys
from pathlib import Path

TRAIN_DIR = Path(__file__).resolve().parent
DEFAULT_DATASET = TRAIN_DIR / "dataset" / "agc_pairs.jsonl"
CHAT_DIR = TRAIN_DIR / "mlx_chat"
DEFAULT_ADAPTER_DIR = TRAIN_DIR / "lora_adapter"

# Minimal system prompt the fine-tuned model should respond to at inference.
# Purpose: the model learns to generate AGC given ONLY this short prefix.
INFERENCE_SYSTEM = (
    "Output only AGC (Agentic Compiler) S-expression source code, no prose."
)


def to_chat(rec: dict) -> dict:
    user = (
        f"Write an AGC module that satisfies this objective:\n\n{rec['objective']}\n\n"
        "Include self-verifying (test ...) blocks. Output ONLY the module source."
    )
    return {
        "messages": [
            {"role": "system", "content": INFERENCE_SYSTEM},
            {"role": "user", "content": user},
            {"role": "assistant", "content": rec["solution"]},
        ]
    }


def split_dataset(dataset_path: Path, seed: int = 42, valid_frac: float = 0.1) -> tuple[int, int]:
    pairs = [json.loads(l) for l in dataset_path.read_text().splitlines() if l.strip()]
    random.Random(seed).shuffle(pairs)
    n_valid = max(10, int(len(pairs) * valid_frac))
    valid, train = pairs[:n_valid], pairs[n_valid:]
    CHAT_DIR.mkdir(parents=True, exist_ok=True)
    (CHAT_DIR / "train.jsonl").write_text(
        "\n".join(json.dumps(to_chat(r)) for r in train) + "\n")
    (CHAT_DIR / "valid.jsonl").write_text(
        "\n".join(json.dumps(to_chat(r)) for r in valid) + "\n")
    return len(train), len(valid)


def run_lora(model: str, epochs: int, batch_size: int, lora_layers: int,
             adapter_dir: Path, max_seq_length: int) -> int:
    train_path = CHAT_DIR / "train.jsonl"
    n_train = sum(1 for _ in train_path.open()) if train_path.exists() else 450
    iters_per_epoch = max(1, n_train // batch_size)
    iters = epochs * iters_per_epoch
    print(f"  train_examples={n_train} batch={batch_size} iters/epoch={iters_per_epoch} total_iters={iters}")
    cmd = [
        sys.executable, "-m", "mlx_lm", "lora",
        "--model", model,
        "--train",
        "--data", str(CHAT_DIR),
        "--adapter-path", str(adapter_dir),
        "--num-layers", str(lora_layers),
        "--batch-size", str(batch_size),
        "--iters", str(iters),
        "--save-every", "100",
        "--steps-per-eval", "50",
        "--steps-per-report", "25",
        "--learning-rate", "1e-4",
        "--mask-prompt",       # only compute loss on assistant output
        "--max-seq-length", str(max_seq_length),
    ]
    print("Launching:", " ".join(cmd))
    return subprocess.run(cmd).returncode


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model",
                    default="mlx-community/Qwen2.5-Coder-3B-Instruct-4bit")
    ap.add_argument("--epochs", type=int, default=3)
    ap.add_argument("--batch-size", type=int, default=4)
    ap.add_argument("--lora-layers", type=int, default=16,
                    help="number of transformer layers to apply LoRA to")
    ap.add_argument("--data", type=Path, default=DEFAULT_DATASET,
                    help="path to a JSONL dataset (objective+solution rows)")
    ap.add_argument("--adapter-path", type=Path, default=DEFAULT_ADAPTER_DIR,
                    help="where to write the LoRA adapter")
    ap.add_argument("--max-seq-length", type=int, default=2048)
    ap.add_argument("--skip-split", action="store_true")
    args = ap.parse_args()

    if not args.data.exists():
        print(f"Dataset missing: {args.data}. Run generate_dataset.py first.")
        return 1

    if not args.skip_split:
        n_train, n_valid = split_dataset(args.data)
        print(f"Split: {n_train} train, {n_valid} valid (from {args.data.name})")

    args.adapter_path.mkdir(parents=True, exist_ok=True)
    return run_lora(args.model, args.epochs, args.batch_size, args.lora_layers,
                    args.adapter_path, args.max_seq_length)


if __name__ == "__main__":
    sys.exit(main())
