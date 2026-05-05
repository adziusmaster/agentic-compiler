#!/bin/bash
# Drive v7 evals: {iter200, final} × {n=1, n=8}.
set -e
cd /Users/andrzej.lech/Code/private/AgenticCompiler

OUT_DIR=bench/results/2026-05-05
mkdir -p "$OUT_DIR"

ADAPT_200=train/lora_adapter_v7_claude_iter200
ADAPT_FINAL=train/lora_adapter_v7_claude

run() {
  local label=$1 adapter=$2 n=$3 temp=$4 out=$5
  echo "============================================"
  echo "[$label] adapter=$adapter n=$n temp=$temp"
  echo "============================================"
  python3 train/eval_bestof_n.py --adapter "$adapter" --n "$n" --temp "$temp" --out "$out"
}

run "v7-iter200-n1"  "$ADAPT_200"   1 0.0 "$OUT_DIR/agc-v7c-iter200-n1.jsonl"
run "v7-iter200-n8"  "$ADAPT_200"   8 0.7 "$OUT_DIR/agc-v7c-iter200-n8.jsonl"
run "v7-final-n1"    "$ADAPT_FINAL" 1 0.0 "$OUT_DIR/agc-v7c-final-n1.jsonl"
run "v7-final-n8"    "$ADAPT_FINAL" 8 0.7 "$OUT_DIR/agc-v7c-final-n8.jsonl"

echo "ALL DONE"
ls -l "$OUT_DIR"/agc-v7c-*.jsonl
