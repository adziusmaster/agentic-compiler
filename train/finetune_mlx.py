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
DATASET_PATH = TRAIN_DIR / "dataset" / "agc_pairs.jsonl"
CHAT_DIR = TRAIN_DIR / "mlx_chat"
ADAPTER_DIR = TRAIN_DIR / "lora_adapter"

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


def split_dataset(seed: int = 42, valid_frac: float = 0.1) -> tuple[int, int]:
    pairs = [json.loads(l) for l in DATASET_PATH.read_text().splitlines() if l.strip()]
    random.Random(seed).shuffle(pairs)
    n_valid = max(10, int(len(pairs) * valid_frac))
    valid, train = pairs[:n_valid], pairs[n_valid:]
    CHAT_DIR.mkdir(parents=True, exist_ok=True)
    (CHAT_DIR / "train.jsonl").write_text(
        "\n".join(json.dumps(to_chat(r)) for r in train) + "\n")
    (CHAT_DIR / "valid.jsonl").write_text(
        "\n".join(json.dumps(to_chat(r)) for r in valid) + "\n")
    return len(train), len(valid)


def run_lora(model: str, epochs: int, batch_size: int, lora_layers: int) -> int:
    # Compute iterations ≈ epochs * (train_size / batch_size)
    # Good practice: let the CLI derive iters from --iters or just pass a number.
    # Iters: with 450 train / batch 4 = ~112 iters/epoch. 3 epochs = ~336 iters.
    iters = epochs * 112
    cmd = [
        sys.executable, "-m", "mlx_lm", "lora",
        "--model", model,
        "--train",
        "--data", str(CHAT_DIR),
        "--adapter-path", str(ADAPTER_DIR),
        "--num-layers", str(lora_layers),
        "--batch-size", str(batch_size),
        "--iters", str(iters),
        "--save-every", "100",
        "--steps-per-eval", "50",
        "--steps-per-report", "25",
        "--learning-rate", "1e-4",
        "--mask-prompt",       # only compute loss on assistant output
        "--max-seq-length", "2048",
    ]
    print("Launching:", " ".join(cmd))
    return subprocess.run(cmd).returncode


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model",
                    default="mlx-community/Qwen2.5-Coder-1.5B-Instruct-4bit")
    ap.add_argument("--epochs", type=int, default=3)
    ap.add_argument("--batch-size", type=int, default=4)
    ap.add_argument("--lora-layers", type=int, default=16,
                    help="number of transformer layers to apply LoRA to")
    ap.add_argument("--skip-split", action="store_true")
    args = ap.parse_args()

    if not DATASET_PATH.exists():
        print(f"Dataset missing: {DATASET_PATH}. Run generate_dataset.py first.")
        return 1

    if not args.skip_split:
        n_train, n_valid = split_dataset()
        print(f"Split: {n_train} train, {n_valid} valid")

    ADAPTER_DIR.mkdir(parents=True, exist_ok=True)
    return run_lora(args.model, args.epochs, args.batch_size, args.lora_layers)


if __name__ == "__main__":
    sys.exit(main())
