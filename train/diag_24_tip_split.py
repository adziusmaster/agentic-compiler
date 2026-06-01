#!/usr/bin/env python3
"""Phase-1 diagnostic for the v14 24-tip-split regression.

Loads v14 (patched base + extended tokenizer) AND v13 baseline (original
base, same training data) and samples N candidates each for the
24-tip-split problem at the cascade evaluator's temp=0.7 / top_p=0.95.
Dumps every raw generation, the assembled module, and the verifier
verdict to bench/results/<date>/{v13,v14}-<problem>-cands.jsonl.

The point: v14 fails all 24 cascade candidates with `round_up` returning
43.33 instead of 43.34. Need to see whether the model literally emits
`math.round`/`math.floor` instead of `math.ceil`, or whether the
implementation diverges some other way.

Usage:
  python3 train/diag_24_tip_split.py --variant both --n 4
"""
from __future__ import annotations
import argparse
import json
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
TRAIN_DIR = REPO / "train"
sys.path.insert(0, str(TRAIN_DIR))

from mlx_lm import load, generate                              # noqa: E402
from mlx_lm.sample_utils import make_sampler                   # noqa: E402

from eval_bestof_n import (                                    # noqa: E402
    SYSTEM_PROMPT, build_prompt_no_tests, chat_format,
    extract_defuns, assemble, verify, read, required_funcs_from,
)
from eval_checkpoint_cascade import materialize_checkpoint     # noqa: E402


def sample(model, tokenizer, chat_prompt: str, sampler, max_tokens: int = 600) -> str:
    text = generate(model, tokenizer, prompt=chat_prompt, max_tokens=max_tokens,
                    sampler=sampler, verbose=False)
    if text.startswith(chat_prompt):
        text = text[len(chat_prompt):]
    end = text.find("<|im_end|>")
    if end != -1:
        text = text[:end]
    return text


def run(label: str, model_path: str, base_adapter: Path, stem: str,
        problem: Path, n: int, temp: float, out_dir: Path) -> dict:
    print(f"\n=== {label}: model={model_path}", flush=True)
    print(f"    adapter={base_adapter}  stem={stem}", flush=True)
    ckpt_dir = materialize_checkpoint(base_adapter, stem)
    t0 = time.monotonic()
    model, tok = load(model_path, adapter_path=str(ckpt_dir))
    print(f"    model loaded in {time.monotonic() - t0:.1f}s", flush=True)

    obj = read(problem / "objective.md").strip()
    tests_ag = read(problem / "tests.ag").strip()
    user_prompt = build_prompt_no_tests(obj, tests_ag)
    chat_prompt = chat_format(tok, SYSTEM_PROMPT, user_prompt)
    required = required_funcs_from(tests_ag)
    samplers = [make_sampler(temp=temp, top_p=0.95) for _ in range(n)]

    results = []
    n_pass = 0
    for i, s in enumerate(samplers):
        ti = time.monotonic()
        text = sample(model, tok, chat_prompt, s)
        defuns = extract_defuns(text)
        module = assemble(defuns, tests_ag, required) if defuns else None
        if module is None:
            ok, pv, tv, err = False, 0, 0, "no defuns extracted"
        else:
            ok, pv, tv, err = verify(module)
        wall = time.monotonic() - ti
        if ok:
            n_pass += 1
        print(f"  cand {i}: pass={ok} {pv}/{tv} wall={wall:.1f}s err={(err or '')[:120]}",
              flush=True)
        results.append({
            "label": label, "cand": i, "pass": ok, "passed": pv, "total": tv,
            "err": err, "raw": text, "module": module, "wall_s": wall,
        })

    out_path = out_dir / f"{label}-{problem.name}-cands.jsonl"
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with out_path.open("w") as fh:
        for r in results:
            fh.write(json.dumps(r) + "\n")
    print(f"  -> {out_path}   ({n_pass}/{n} pass)", flush=True)
    return {"label": label, "n_pass": n_pass, "n": n, "out": str(out_path)}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--problem", default=str(REPO / "bench/problems/24-tip-split"))
    ap.add_argument("--n", type=int, default=4)
    ap.add_argument("--temp", type=float, default=0.7)
    ap.add_argument("--out", default=str(REPO / "bench/results/2026-05-29"))
    ap.add_argument("--variant", choices=["v12", "v13", "v14", "both", "all"], default="both")
    args = ap.parse_args()

    problem = Path(args.problem)
    out_dir = Path(args.out)

    summary = []
    if args.variant in ("v14", "both", "all"):
        summary.append(run(
            "v14",
            "/Users/andrzej.lech/.cache/agc/qwen-7b-extended-v1",
            REPO / "train/lora_adapter_v14_tokenizer",
            "adapters",
            problem, args.n, args.temp, out_dir,
        ))
    if args.variant in ("v13", "both", "all"):
        summary.append(run(
            "v13",
            "mlx-community/Qwen2.5-Coder-7B-Instruct-4bit",
            REPO / "train/lora_adapter_v13_baseline",
            "0000500_adapters",  # the checkpoint where v13 originally passed 24-tip-split
            problem, args.n, args.temp, out_dir,
        ))
    if args.variant in ("v12", "all"):
        summary.append(run(
            "v12",
            "mlx-community/Qwen2.5-Coder-7B-Instruct-4bit",
            REPO / "train/lora_adapter_v12_l3",
            "adapters",
            problem, args.n, args.temp, out_dir,
        ))

    print("\n=== SUMMARY ===")
    for s in summary:
        print(f"  {s['label']}: {s['n_pass']}/{s['n']} -> {s['out']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
