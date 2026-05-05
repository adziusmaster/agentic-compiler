#!/bin/bash
# Drive 4 evals: {iter200, final} × {n=1 single-shot, n=8 best-of-N}.
# Pass rate, wall time, and tokens/problem feed the token-efficiency thesis.
set -e

cd /Users/andrzej.lech/Code/private/AgenticCompiler

OUT_DIR=bench/results/2026-05-05
mkdir -p "$OUT_DIR"

ADAPT_200=train/lora_adapter_v6_claude_iter200
ADAPT_FINAL=train/lora_adapter_v6_claude

run() {
  local label=$1 adapter=$2 n=$3 temp=$4 out=$5
  echo "============================================"
  echo "[$label] adapter=$adapter n=$n temp=$temp"
  echo "out=$out"
  echo "============================================"
  python3 train/eval_bestof_n.py \
    --adapter "$adapter" \
    --n "$n" --temp "$temp" \
    --out "$out"
}

run "iter200-n1"  "$ADAPT_200"   1 0.0 "$OUT_DIR/agc-v6c-iter200-n1.jsonl"
run "iter200-n8"  "$ADAPT_200"   8 0.7 "$OUT_DIR/agc-v6c-iter200-n8.jsonl"
run "final-n1"    "$ADAPT_FINAL" 1 0.0 "$OUT_DIR/agc-v6c-final-n1.jsonl"
run "final-n8"    "$ADAPT_FINAL" 8 0.7 "$OUT_DIR/agc-v6c-final-n8.jsonl"

echo "============================================"
echo "ALL DONE"
ls -l "$OUT_DIR"/agc-v6c-*.jsonl
