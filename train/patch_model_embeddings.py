#!/usr/bin/env python3
"""Patch Qwen embeddings for new AGC tokens (C4).

Loads fp16 base, decomposes each new token via the old tokenizer to get
its constituent IDs, sets the new embedding row to mean(constituent rows),
re-quantizes to 4-bit, saves to ~/.cache/agc/qwen-7b-extended-v1/.

This file initially contains only the pure merge-init helper (tested
independently). The full model-loading path is appended in Task 6.

Usage (Task 6 onward):
  python3 train/patch_model_embeddings.py \
    --base-model Qwen/Qwen2.5-Coder-7B-Instruct \
    --tokenizer  train/agc_tokenizer \
    --out        ~/.cache/agc/qwen-7b-extended-v1
"""
from __future__ import annotations
from typing import Sequence
import numpy as np


def merge_init_row(
    constituent_ids: Sequence[int],
    embedding_matrix: np.ndarray,
) -> np.ndarray:
    """Return the merge-init embedding row for a new token.

    The new row is the mean of the rows in `embedding_matrix` indexed by
    `constituent_ids`. Preserves the matrix's dtype.

    Raises ValueError on empty constituent_ids.
    """
    if len(constituent_ids) == 0:
        raise ValueError("constituent_ids must not be empty")
    rows = embedding_matrix[list(constituent_ids)]
    # mean() upcasts fp16 -> fp32; cast back to preserve dtype.
    mean = rows.mean(axis=0)
    return mean.astype(embedding_matrix.dtype)


# ----- model-loading pipeline (added in Task 6) -----

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

DEFAULT_BASE = "Qwen/Qwen2.5-Coder-7B-Instruct"
DEFAULT_OUT = Path.home() / ".cache" / "agc" / "qwen-7b-extended-v1"


def _decompose_with_old_tokenizer(token: str, old_tok) -> list[int]:
    """Tokenize `token` with the old tokenizer; return constituent IDs."""
    ids = old_tok.encode(token, add_special_tokens=False)
    if not ids:
        raise ValueError(f"token {token!r} produced no constituent IDs")
    return ids


def patch_and_save(
    base_model_id: str,
    tokenizer_dir: Path,
    out_dir: Path,
) -> None:
    """Full C4 pipeline. Side effect: writes patched model + tokenizer to out_dir."""
    from transformers import AutoTokenizer, AutoModelForCausalLM
    import torch

    out_dir.mkdir(parents=True, exist_ok=True)

    # 1. Load fp16 base
    print(f"Loading {base_model_id} in fp16...")
    base = AutoModelForCausalLM.from_pretrained(
        base_model_id, torch_dtype=torch.float16
    )
    old_tok = AutoTokenizer.from_pretrained(base_model_id)

    # 2. Load extended tokenizer + new-token map
    new_tok = AutoTokenizer.from_pretrained(str(tokenizer_dir))
    id_map = json.loads((tokenizer_dir / "new_token_id_map.json").read_text())
    print(f"Extending vocab by {len(id_map)} new tokens.")

    # 3. Resize model embeddings (creates new rows, randomly init by default)
    old_size = base.get_input_embeddings().weight.shape[0]
    new_size = old_size + len(id_map)
    base.resize_token_embeddings(new_size)
    embed = base.get_input_embeddings()
    output_embed = base.get_output_embeddings()
    is_tied = base.config.tie_word_embeddings
    print(f"Resized embeddings: {old_size} -> {new_size}; "
          f"tie_word_embeddings={is_tied}")

    # 4. Merge-init each new row
    embed_np = embed.weight.detach().cpu().numpy()
    out_np = None
    if not is_tied and output_embed is not None:
        out_np = output_embed.weight.detach().cpu().numpy()

    for tok, new_id in id_map.items():
        constituent = _decompose_with_old_tokenizer(tok, old_tok)
        new_row = merge_init_row(constituent, embed_np[:old_size])
        embed_np[new_id] = new_row
        if out_np is not None:
            new_out_row = merge_init_row(constituent, out_np[:old_size])
            out_np[new_id] = new_out_row

    # Write back into the model
    embed.weight.data.copy_(torch.from_numpy(embed_np))
    if out_np is not None:
        output_embed.weight.data.copy_(torch.from_numpy(out_np))

    # 5. Save fp16 patched model + tokenizer to a staging dir
    staging = out_dir.parent / (out_dir.name + "-fp16-staging")
    staging.mkdir(parents=True, exist_ok=True)
    base.save_pretrained(staging)
    new_tok.save_pretrained(staging)
    print(f"fp16 patched model staged at {staging}")

    # 6. Re-quantize to 4-bit using mlx-lm convert
    print("Re-quantizing to 4-bit via mlx_lm.convert...")
    subprocess.run(
        [
            sys.executable, "-m", "mlx_lm", "convert",
            "--hf-path", str(staging),
            "--mlx-path", str(out_dir),
            "-q", "--q-bits", "4",
        ],
        check=True,
    )

    # 7. Copy the extended tokenizer files into out_dir (in case mlx_lm convert dropped them)
    for fname in ("tokenizer.json", "tokenizer_config.json", "special_tokens_map.json", "new_token_id_map.json"):
        src = tokenizer_dir / fname
        if src.exists():
            shutil.copy2(src, out_dir / fname)

    print(f"Done. Patched 4-bit model at {out_dir}")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--base-model", default=DEFAULT_BASE)
    ap.add_argument(
        "--tokenizer", type=Path,
        default=Path(__file__).resolve().parent / "agc_tokenizer",
    )
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    args = ap.parse_args()

    patch_and_save(args.base_model, args.tokenizer, args.out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
