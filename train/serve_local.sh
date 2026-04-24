#!/usr/bin/env bash
# Serve the LoRA-fine-tuned model locally via MLX-LM's OpenAI-compatible API.
#
# The bench harness can then hit http://localhost:8080 using its OpenAI client
# path by setting OPENAI_BASE_URL.
#
# Usage:
#   ./train/serve_local.sh
#   # in another shell:
#   OPENAI_BASE_URL=http://localhost:8080/v1 OPENAI_API_KEY=dummy \
#     AGENTIC_PROVIDER=openai OPENAI_MODEL=qwen-agc \
#     python3 bench/run.py --track python-oneshot --only 01

set -euo pipefail

MODEL="${MODEL:-mlx-community/Qwen2.5-Coder-1.5B-Instruct-4bit}"
ADAPTER="${ADAPTER:-$(cd "$(dirname "$0")" && pwd)/lora_adapter}"
PORT="${PORT:-8080}"

echo "Serving $MODEL with adapter $ADAPTER on port $PORT"
exec python3 -m mlx_lm.server \
  --model "$MODEL" \
  --adapter-path "$ADAPTER" \
  --port "$PORT" \
  --host 127.0.0.1
