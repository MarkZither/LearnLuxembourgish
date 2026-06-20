# Tasks: RunPod Fine-Tuned Model Provider & Leaderboard Journey

**Feature**: runpod-finetune  
**Spec**: [docs/features/runpod-finetune/spec.md](spec.md)  
**Plan**: [docs/features/runpod-finetune/plan.md](plan.md)  
**ADRs**: [ADR-0004](../../architecture/decisions/0004-llm-provider-priority-order.md), [ADR-0005](../../architecture/decisions/0005-runpod-http-timeout-strategy.md)  
**Date**: 2026-06-19

---

## Phase 1 — Setup

- [ ] Add `/ml/` exclusion to `src/LearnLuxembourgish.Api/Dockerfile` and `src/LearnLuxembourgish.Web/Dockerfile` (`.dockerignore` or `COPY` scope) to prevent ML artefacts from being included in Docker images

---

## Phase 2 — Foundational

- [ ] Accept ADR-0004: update `Status` from `Proposed` to `Accepted` in `docs/architecture/decisions/0004-llm-provider-priority-order.md`
- [ ] Accept ADR-0005: update `Status` from `Proposed` to `Accepted` in `docs/architecture/decisions/0005-runpod-http-timeout-strategy.md`

---

## Phase 3 — US1: Use Fine-Tuned Model for Grammar Explanation (P1)

> Adds RunPod as the highest-priority configured LLM provider in `GrammarService` and
> `TranslationService`, with graceful fallback and configurable cold-start timeout per
> ADR-0004 (priority order) and ADR-0005 (dedicated HttpClient).

- [ ] Add per-request `runpod` provider branch and configured-key RunPod branch to `BuildKernel` in `src/LearnLuxembourgish.Api/Services/GrammarService.cs` — inserted after existing per-request block, before configured Groq block; dedicated `HttpClient` with timeout from `Grammar:RunPod:TimeoutSeconds` (default 150 s); endpoint from `Grammar:RunPod:Endpoint`; model from `Grammar:RunPod:Model` (default `gemma-3-27b-lb-ft`); activity tag `runpod`; API key masked in logs; skipped when `Grammar:RunPod:ApiKey` is absent or empty
- [ ] [P] Add `TranslateWithRunPodAsync` private helper method and RunPod try/catch block (inserted after DeepL block, before Mistral block) in `src/LearnLuxembourgish.Api/Services/TranslationService.cs` — dedicated `HttpClient` with timeout from `Translation:RunPod:TimeoutSeconds` (default 150 s); activity tag `runpod`; falls back silently on any exception; skipped when `Translation:RunPod:ApiKey` is absent or empty
- [ ] [P] Add 8 AppHost parameters (`grammar-runpod-api-key` secret, `grammar-runpod-endpoint`, `grammar-runpod-model`, `grammar-runpod-timeout-seconds`, `translation-runpod-api-key` secret, `translation-runpod-endpoint`, `translation-runpod-model`, `translation-runpod-timeout-seconds`) with corresponding `WithEnvironment` calls injecting `Grammar__RunPod__*` and `Translation__RunPod__*` env vars into the API project in `src/LearnLuxembourgish.AppHost/AppHost.cs`
- [ ] [P] Document `Grammar:RunPod` and `Translation:RunPod` configuration sections (Endpoint, Model, TimeoutSeconds with defaults; ApiKey placeholder with comment that it is set via user secrets) in `src/LearnLuxembourgish.Api/appsettings.json`
- [ ] [P] Update `docs/USER_SECRETS_SETUP.md` with `dotnet user-secrets set` commands for all 6 RunPod secrets in the AppHost section (`Parameters:grammar-runpod-api-key`, `Parameters:grammar-runpod-endpoint`, `Parameters:grammar-runpod-model`, `Parameters:translation-runpod-api-key`, `Parameters:translation-runpod-endpoint`, `Parameters:translation-runpod-model`) and the corresponding direct API project section (`Grammar:RunPod:ApiKey`, etc.)

---

## Phase 4 — US2: ML Pipeline Artefacts Available in Repository (P2)

> Creates the `/ml/` folder at repo root with training script, dataset template,
> deployment template, platform notebooks, and README. Training platform is the developer's
> choice: Google Colab (free), Kaggle (free, 30 h/week), or RunPod A40 (paid). Inference
> serving always targets RunPod Serverless. These files are outside the .NET solution and
> are not built by the `dotnet` toolchain.

- [ ] Create `/ml/train_qlora.py` — platform-agnostic QLoRA fine-tuning script for Gemma 3 27B accepting CLI arguments (`--base-model`, `--dataset`, `--output`, `--epochs`, `--lr`, `--lora-rank`) with no hardcoded user paths; validates dataset schema (required fields: `instruction`, `input`, `output`; optional: `cefr_level`) before training begins and halts with a descriptive error message identifying the first malformed record and line number; runnable on Colab, Kaggle, and RunPod without modification
- [ ] [P] Create `/ml/notebooks/colab_train_qlora.ipynb` — Google Colab-ready Jupyter notebook that installs dependencies (`transformers`, `peft`, `trl`, `bitsandbytes`), mounts Google Drive, calls `train_qlora.py` with Drive-relative paths, and saves the LoRA adapter back to Drive; includes a note that free-tier T4 (15 GB) is too small for full Gemma 3 27B — recommends Colab Pro (A100) or reducing LoRA rank
- [ ] [P] Create `/ml/notebooks/kaggle_train_qlora.ipynb` — Kaggle-ready Jupyter notebook that installs dependencies, calls `train_qlora.py`, and writes the adapter to `/kaggle/working/` so it is saved as a Kaggle Dataset output; GPU accelerator set to 2×T4
- [ ] [P] Create `/ml/dataset_template.jsonl` — JSONL template where each record has `instruction` (string), `input` (string, may be empty), `output` (string), and optional `cefr_level` (`A1` or `A2`); file contains at least 3 illustrative CEFR A1/A2 Luxembourgish grammar/translation examples
- [ ] [P] Create `/ml/deploy_runpod_serverless.yaml` — RunPod Serverless worker configuration template specifying vLLM Docker image reference, GPU type (A40 or configurable equivalent), and documented required environment variables (`MODEL_PATH`, `SERVED_MODEL_NAME`, and API key); usable without modification beyond filling in the model path placeholder
- [ ] [P] Create `/ml/README.md` — covers folder structure and file purposes; training platform comparison table (Colab free/pro, Kaggle, RunPod A40 with cost and VRAM notes); step-by-step instructions for each platform (Colab, Kaggle, RunPod); dataset preparation guide; deployment instructions (upload adapter, configure `deploy_runpod_serverless.yaml`, start serverless pod, verify OpenAI-compatible endpoint)

---

## Phase 5 — US3: Leaderboard Journey Documentation Page (P3)

> Adds a Blazor page at `/leaderboard-journey` that documents the LLM leaderboard
> ambition, CEFR fine-tuning methodology, and a dated training journal. New journal
> entries are added by editing a single list in the component without touching layout.

- [ ] Create `src/LearnLuxembourgish.Web/Components/Pages/LeaderboardJourney.razor` at route `/leaderboard-journey` — define `JournalEntry` record (`DateOnly Date`, `string ModelVersion`, `string Summary`, `string? LeaderboardScore`); static sections: (1) leaderboard goal with direct link to `https://ai-sandbox.list.lu/`, (2) CEFR A1/A2 explanation, (3) fine-tuning approach summary, (4) reverse-chronological journal list with at least one initial entry; no raw command-line instructions in body text; non-technical audience language throughout
- [ ] [P] Add "Leaderboard Journey" nav link entry (route `/leaderboard-journey`, appropriate Bootstrap Icon) to `src/LearnLuxembourgish.Web/Components/Layout/NavMenu.razor`
