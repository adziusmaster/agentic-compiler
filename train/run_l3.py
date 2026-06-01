#!/usr/bin/env python3
"""Train a v12_l3 adapter with the L3 variant stack:
  - Combined corpus (`agc_pairs_combined_dual.jsonl`, 5346 dual pairs)
  - LoRA rank 16 (vs the default 8)
  - Cosine LR decay 1e-4 → 1e-6 with warmup
  - 7B-4bit base, batch=4, max_seq=1024, 1 epoch

Reuses `finetune_mlx.split_dataset` for the train/valid split, then
writes an mlx_lm YAML config and runs `python3 -m mlx_lm lora -c <yaml>`.

Usage:
  python3 train/run_l3.py [--data <path>] [--adapter-path <path>]
                          [--epochs N] [--batch-size N] [--dry-run]
"""
from __future__ import annotations
import argparse
import json
import subprocess
import sys
from pathlib import Path

TRAIN_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(TRAIN_DIR))
from finetune_mlx import split_dataset, CHAT_DIR  # noqa: E402

DEFAULT_DATA = TRAIN_DIR / "dataset" / "agc_pairs_combined_dual.jsonl"
DEFAULT_ADAPTER = TRAIN_DIR / "lora_adapter_v12_l3"
DEFAULT_MODEL = "mlx-community/Qwen2.5-Coder-7B-Instruct-4bit"


def write_config(yaml_path: Path, *, model: str, data_dir: Path, adapter_path: Path,
                 batch_size: int, iters: int, max_seq: int, lora_rank: int,
                 lr_max: float, lr_min: float, warmup: int,
                 train_new_embeddings: bool = False,
                 tokenizer_path: Path | None = None) -> None:
    """Hand-rolled YAML; we only need a small fixed schema and don't want
    a PyYAML dependency in this repo."""
    # mlx-lm default LoRA targeting (no `keys` field) covers the standard
    # attention + MLP projections across `num_layers` top transformer blocks.
    # Only override `keys` when we actively want to expand the target set
    # (e.g. add `embed_tokens` for a vocab-extended base in Task 14).
    body = (
        f"model: \"{model}\"\n"
        f"train: true\n"
        f"data: \"{data_dir}\"\n"
        f"adapter_path: \"{adapter_path}\"\n"
        f"fine_tune_type: lora\n"
        f"optimizer: adamw\n"
        f"mask_prompt: true\n"
        f"num_layers: 16\n"
        f"batch_size: {batch_size}\n"
        f"iters: {iters}\n"
        f"max_seq_length: {max_seq}\n"
        f"learning_rate: {lr_max}\n"
        f"save_every: 100\n"
        f"steps_per_eval: 100\n"
        f"steps_per_report: 25\n"
        f"val_batches: 25\n"
        f"lora_parameters:\n"
        f"  rank: {lora_rank}\n"
        f"  scale: 20.0\n"
        f"  dropout: 0.0\n"
    )
    if train_new_embeddings:
        # Vocab-extended base: explicitly include embed_tokens alongside the
        # default attention/MLP targets so the new rows >= 151665 train.
        body += '  keys: ["q_proj", "v_proj", "embed_tokens"]\n'
    body += (
        f"lr_schedule:\n"
        f"  name: cosine_decay\n"
        f"  warmup: {warmup}\n"
        f"  warmup_init: 1.0e-7\n"
        f"  arguments: [{lr_max}, {iters}, {lr_min}]\n"
    )
    if tokenizer_path is not None:
        # mlx-lm does not have a documented `tokenizer_path` YAML field;
        # it loads the tokenizer colocated with `model`. We emit the path
        # as a comment for provenance and rely on Task 12 to colocate the
        # patched tokenizer with the patched base model directory.
        body += f"# tokenizer_path: \"{tokenizer_path}\"\n"
    yaml_path.write_text(body)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default=DEFAULT_MODEL)
    ap.add_argument("--data", type=Path, default=DEFAULT_DATA)
    ap.add_argument("--adapter-path", type=Path, default=DEFAULT_ADAPTER)
    ap.add_argument("--batch-size", type=int, default=4)
    ap.add_argument("--epochs", type=int, default=1)
    ap.add_argument("--max-seq", type=int, default=1024)
    ap.add_argument("--lora-rank", type=int, default=16)
    ap.add_argument("--lr-max", type=float, default=1.0e-4)
    ap.add_argument("--lr-min", type=float, default=1.0e-6)
    ap.add_argument("--warmup", type=int, default=50)
    ap.add_argument("--iters", type=int, default=None,
                    help="override total iters (else = epochs × n_train // batch_size)")
    ap.add_argument("--dry-run-iters", type=int, default=None,
                    help="if set, run only this many iters as a sanity check")
    ap.add_argument("--skip-split", action="store_true")
    ap.add_argument(
        "--train-new-embeddings",
        action="store_true",
        help="Mark embedding rows >= 151665 as trainable. "
             "Required when training on a vocab-extended base.",
    )
    ap.add_argument(
        "--tokenizer",
        type=Path,
        default=None,
        help="Optional path to an alternate tokenizer dir. "
             "Defaults to the tokenizer colocated with --model.",
    )
    args = ap.parse_args()

    if not args.data.exists():
        print(f"Dataset missing: {args.data}")
        return 1

    if not args.skip_split:
        n_train, n_valid = split_dataset(args.data)
        print(f"Split: {n_train} train, {n_valid} valid (from {args.data.name})")
    else:
        n_train = sum(1 for _ in (CHAT_DIR / "train.jsonl").open())

    iters_per_epoch = max(1, n_train // args.batch_size)
    iters = args.dry_run_iters if args.dry_run_iters else (
        args.iters if args.iters is not None else args.epochs * iters_per_epoch
    )
    print(f"  train={n_train} batch={args.batch_size} iters/epoch={iters_per_epoch} total_iters={iters}")

    args.adapter_path.mkdir(parents=True, exist_ok=True)
    yaml_path = args.adapter_path / "training_config.yaml"
    write_config(yaml_path,
                 model=args.model, data_dir=CHAT_DIR, adapter_path=args.adapter_path,
                 batch_size=args.batch_size, iters=iters, max_seq=args.max_seq,
                 lora_rank=args.lora_rank, lr_max=args.lr_max, lr_min=args.lr_min,
                 warmup=args.warmup,
                 train_new_embeddings=args.train_new_embeddings,
                 tokenizer_path=args.tokenizer)
    print(f"  config -> {yaml_path}")

    cmd = [sys.executable, "-m", "mlx_lm", "lora", "-c", str(yaml_path)]
    print("Launching:", " ".join(cmd))
    return subprocess.run(cmd).returncode


if __name__ == "__main__":
    sys.exit(main())
