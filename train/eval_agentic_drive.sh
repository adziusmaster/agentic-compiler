#!/bin/bash
set -e
cd /Users/andrzej.lech/Code/private/AgenticCompiler

OUT_DIR=bench/results/2026-05-05
mkdir -p "$OUT_DIR"

echo "=== v6c-final + agentic (5 attempts) ==="
python3 train/eval_agentic.py \
  --adapter train/lora_adapter_v6_claude \
  --max-attempts 5 \
  --out "$OUT_DIR/agc-v6c-agentic-r5.jsonl"

echo
echo "=== v7c-final + agentic (5 attempts) ==="
python3 train/eval_agentic.py \
  --adapter train/lora_adapter_v7_claude \
  --max-attempts 5 \
  --out "$OUT_DIR/agc-v7c-agentic-r5.jsonl"

echo "ALL DONE"
