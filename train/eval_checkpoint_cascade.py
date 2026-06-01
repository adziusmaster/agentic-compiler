#!/usr/bin/env python3
"""Cascade eval over multiple checkpoints of a single training run.

For each problem, try checkpoints in order. Within each, sample n
candidates with temperature; early-stop on first PASS. If the
checkpoint fails, fall through to the next.

The intuition: different overfitting points give different output
distributions. iter_500 hasn't fully converged → broader sampling
diversity; iter_1500 is the most refined. They're likely to fail on
different problems, giving cascade-style coverage without retraining
multiple separately.

Usage:
  python3 train/eval_checkpoint_cascade.py \\
    --model mlx-community/Qwen2.5-Coder-7B-Instruct-4bit \\
    --base-adapter train/lora_adapter_v11_7b_dual \\
    --checkpoints adapters,0001000_adapters,0000500_adapters \\
    --prompt-format no_tests \\
    --n 8 --temp 0.7 \\
    --out bench/results/<date>/agc-v11-checkpoint-cascade.jsonl
"""
from __future__ import annotations
import argparse, json, shutil, sys, tempfile, time
from pathlib import Path

from mlx_lm import load, generate
from mlx_lm.sample_utils import make_sampler

TRAIN_DIR = Path(__file__).resolve().parent
REPO_ROOT = TRAIN_DIR.parent
BENCH_PROBLEMS = REPO_ROOT / "bench" / "problems"

# Reuse helpers + verify pipeline from eval_bestof_n
sys.path.insert(0, str(TRAIN_DIR))
from eval_bestof_n import (  # noqa: E402
    SYSTEM_PROMPT, build_prompt, build_prompt_no_tests, chat_format,
    extract_module, extract_defuns, assemble, verify, count_loc, read,
    required_funcs_from,
)


def count_with_tokenizer(tok, text: str) -> int:
    """Count tokens in `text` under an arbitrary tokenizer (no special tokens).

    Used to record a secondary token count under the original Qwen tokenizer
    when the active tokenizer is the AGC-extended one. Lets us report both
    Y (active-vocab tok/pass) and Z (re-encoded with original Qwen) per result.
    """
    return len(tok.encode(text, add_special_tokens=False))


def materialize_checkpoint(base_dir: Path, ckpt_stem: str) -> Path:
    """Copy a checkpoint .safetensors into a temp adapter dir alongside the
    config so mlx_lm can `load(..., adapter_path=...)` it directly."""
    src_safet = base_dir / (ckpt_stem if ckpt_stem.endswith(".safetensors")
                            else ckpt_stem + ".safetensors")
    if not src_safet.exists():
        raise FileNotFoundError(f"checkpoint not found: {src_safet}")
    cfg = base_dir / "adapter_config.json"
    if not cfg.exists():
        raise FileNotFoundError(f"adapter_config.json missing in {base_dir}")
    tmp = Path(tempfile.mkdtemp(prefix="ckpt-cascade-"))
    shutil.copy(cfg, tmp / "adapter_config.json")
    shutil.copy(src_safet, tmp / "adapters.safetensors")
    return tmp


def sample_one(model, tokenizer, chat_prompt: str, sampler, max_tokens: int) -> str:
    text = generate(model, tokenizer, prompt=chat_prompt, max_tokens=max_tokens,
                    sampler=sampler, verbose=False)
    if text.startswith(chat_prompt):
        text = text[len(chat_prompt):]
    end = text.find("<|im_end|>")
    if end != -1:
        text = text[:end]
    return text


def try_problem(model, tokenizer, problem: Path, *, prompt_format: str,
                n: int, temp: float, max_tokens: int,
                secondary_tok=None) -> dict:
    obj = read(problem / "objective.md").strip()
    tests_ag = read(problem / "tests.ag").strip()
    if prompt_format == "no_tests":
        user_prompt = build_prompt_no_tests(obj, tests_ag)
    else:
        user_prompt = build_prompt(obj, tests_ag)
    chat_prompt = chat_format(tokenizer, SYSTEM_PROMPT, user_prompt)
    prompt_toks = len(tokenizer.encode(chat_prompt))
    prompt_toks_secondary = (
        count_with_tokenizer(secondary_tok, chat_prompt) if secondary_tok else None
    )

    best = {"pass": False, "passed": 0, "total": 0, "module": None, "err": ""}
    cands_used = 0
    gen_toks = 0
    gen_toks_secondary = 0
    samplers = [make_sampler(temp=temp, top_p=0.95) for _ in range(n)]

    t0 = time.monotonic()
    for sampler in samplers:
        cands_used += 1
        text = sample_one(model, tokenizer, chat_prompt, sampler, max_tokens)
        gen_toks += len(tokenizer.encode(text))
        if secondary_tok is not None:
            gen_toks_secondary += count_with_tokenizer(secondary_tok, text)

        if prompt_format == "no_tests":
            defuns = extract_defuns(text)
            module = assemble(defuns, tests_ag, required_funcs_from(tests_ag)) if defuns else None
        else:
            module = extract_module(text)
        if module is None:
            continue
        ok, pv, tv, err = verify(module)
        score = (1 if ok else 0, pv / max(1, tv))
        best_score = (1 if best["pass"] else 0, best["passed"] / max(1, best["total"]))
        if score > best_score:
            best = {"pass": ok, "passed": pv, "total": tv, "module": module, "err": err}
        if ok:
            break
    wall = time.monotonic() - t0

    return {
        "pass": best["pass"], "passed": best["passed"], "total": best["total"],
        "module": best["module"], "err": best["err"],
        "wall_s": wall, "cands_used": cands_used,
        "tokens_in": prompt_toks * cands_used,
        "tokens_out": gen_toks,
        "tokens_in_secondary": (prompt_toks_secondary * cands_used
                                if prompt_toks_secondary is not None else None),
        "tokens_out_secondary": gen_toks_secondary if secondary_tok is not None else None,
        "loc": count_loc(best["module"]) if best["module"] else 0,
    }


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True)
    ap.add_argument("--base-adapter", required=True, type=Path,
                    help="adapter dir containing adapter_config.json and the .safetensors files")
    ap.add_argument("--checkpoints", required=True,
                    help="comma-sep stems, e.g. 'adapters,0001000_adapters,0000500_adapters'")
    ap.add_argument("--prompt-format", choices=["with_tests", "no_tests"], default="no_tests")
    ap.add_argument("--n", type=int, default=8)
    ap.add_argument("--temp", type=float, default=0.7)
    ap.add_argument("--max-tokens", type=int, default=600)
    ap.add_argument("--only", help="comma-sep problem prefixes")
    ap.add_argument("--out", required=True, type=Path)
    ap.add_argument(
        "--secondary-tokenizer",
        default=None,
        help="HF tokenizer repo or path. If set, eval reports tok counts "
             "under this tokenizer alongside the primary counts.",
    )
    args = ap.parse_args()

    checkpoint_stems = [s.strip() for s in args.checkpoints.split(",") if s.strip()]
    problems = sorted(p for p in BENCH_PROBLEMS.iterdir() if p.is_dir())
    if args.only:
        keys = [k.strip() for k in args.only.split(",")]
        problems = [p for p in problems if any(p.name.startswith(k) for k in keys)]

    secondary_tok = None
    if args.secondary_tokenizer:
        from transformers import AutoTokenizer
        secondary_tok = AutoTokenizer.from_pretrained(args.secondary_tokenizer)

    # Per-problem tally — accumulates tokens across all checkpoints that touched it.
    cumulative: dict[str, dict] = {p.name: {
        "id": p.name, "track": "agc-v11-ckpt-cascade",
        "pass": False, "tests_passed": 0, "tests_total": 0,
        "tokens_in": 0, "tokens_out": 0, "wall_time_s": 0.0,
        "tokens_in_secondary": 0 if secondary_tok is not None else None,
        "tokens_out_secondary": 0 if secondary_tok is not None else None,
        "source_loc": 0, "capabilities": [], "decomposition_depth": 0,
        "checkpoints_tried": [], "passed_at_checkpoint": None,
        "error_category": "test-fail", "error_detail": None,
    } for p in problems}

    remaining = set(p.name for p in problems)

    for stem in checkpoint_stems:
        if not remaining:
            print(f"All problems passed before {stem}; stopping cascade.")
            break
        print(f"\n=== Loading checkpoint: {stem} ({len(remaining)} problems remaining) ===", flush=True)
        ckpt_dir = materialize_checkpoint(args.base_adapter, stem)
        try:
            model, tokenizer = load(args.model, adapter_path=str(ckpt_dir))
        finally:
            pass  # keep dir until end of run; cleanup after
        for p in problems:
            if p.name not in remaining:
                continue
            res = try_problem(model, tokenizer, p,
                              prompt_format=args.prompt_format,
                              n=args.n, temp=args.temp,
                              max_tokens=args.max_tokens,
                              secondary_tok=secondary_tok)
            tally = cumulative[p.name]
            tally["tokens_in"] += res["tokens_in"]
            tally["tokens_out"] += res["tokens_out"]
            if secondary_tok is not None:
                tally["tokens_in_secondary"] += res["tokens_in_secondary"]
                tally["tokens_out_secondary"] += res["tokens_out_secondary"]
            tally["wall_time_s"] += res["wall_s"]
            tally["checkpoints_tried"].append(stem)
            if res["pass"]:
                tally["pass"] = True
                tally["tests_passed"] = res["passed"]
                tally["tests_total"] = res["total"]
                tally["source_loc"] = res["loc"]
                tally["passed_at_checkpoint"] = stem
                tally["error_category"] = None
                remaining.discard(p.name)
            else:
                # keep the strongest-so-far partial result
                if res["passed"] > tally["tests_passed"] or (
                    res["passed"] == tally["tests_passed"] and res["total"] > tally["tests_total"]):
                    tally["tests_passed"] = res["passed"]
                    tally["tests_total"] = res["total"]
                    tally["source_loc"] = res["loc"]
                    tally["error_detail"] = (res["err"] or "")[:300]
            status = "PASS" if res["pass"] else "FAIL"
            print(f"[{status}] {p.name:30} {stem:25} cands={res['cands_used']} "
                  f"wall={res['wall_s']:5.1f}s tests={res['passed']}/{res['total']} "
                  f"tok_in={res['tokens_in']} tok_out={res['tokens_out']}", flush=True)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("w") as fh:
        for p in problems:
            fh.write(json.dumps(cumulative[p.name]) + "\n")

    n_pass = sum(1 for p in problems if cumulative[p.name]["pass"])
    total_in = sum(cumulative[p.name]["tokens_in"] for p in problems)
    total_out = sum(cumulative[p.name]["tokens_out"] for p in problems)
    total_tok = total_in + total_out
    tpp = total_tok / max(1, n_pass)
    print()
    print(f"=== CASCADE SUMMARY ===")
    print(f"pass:           {n_pass}/{len(problems)}")
    print(f"total tok_in:   {total_in:,}")
    print(f"total tok_out:  {total_out:,}")
    print(f"total tokens:   {total_tok:,}")
    print(f"tok_per_pass:   {tpp:.0f}")
    print(f"results -> {args.out}")
    # which checkpoint contributed each pass?
    by_ckpt: dict[str, int] = {}
    for p in problems:
        c = cumulative[p.name].get("passed_at_checkpoint")
        if c: by_ckpt[c] = by_ckpt.get(c, 0) + 1
    if by_ckpt:
        print("passes by checkpoint:")
        for k, v in by_ckpt.items():
            print(f"  {k:25} {v}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
