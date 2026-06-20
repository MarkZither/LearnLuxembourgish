# Plan: RunPod Fine-Tuned Model Provider & Leaderboard Journey

**Feature**: runpod-finetune
**Spec**: [docs/features/runpod-finetune/spec.md](spec.md)
**Date**: 2026-06-19
**Status**: Draft

---

## 1. Context Summary

The feature adds RunPod Serverless as a 4th LLM provider in the app, serving a custom
QLoRA-fine-tuned Gemma 3 27B model optimised for CEFR A1/A2 Luxembourgish. The architecture
impact is low: RunPod exposes an OpenAI-compatible API at `/openai/v1`, compatible with the
existing `AddOpenAIChatCompletion` call used for Groq, Mistral, and Ollama. No schema changes,
no new infrastructure dependencies, no new service registrations beyond AppHost parameters.

The feature also delivers:
- ML pipeline artefacts (`/ml/` folder) for training and deploying the fine-tuned model
- A documentation page ("Luxembourgish LLM Leaderboard Journey") in the Blazor Web app

---

## 2. Referenced ADRs

| ADR | Domain | Decision |
|-----|--------|----------|
| [ADR-0004](../../architecture/decisions/0004-llm-provider-priority-order.md) | LLM Provider Priority Order | Per-request → RunPod (config) → Groq (config) → Mistral (config) → Ollama |
| [ADR-0005](../../architecture/decisions/0005-runpod-http-timeout-strategy.md) | RunPod HTTP Timeout Strategy | Separate `HttpClient` per request, timeout from `Grammar:RunPod:TimeoutSeconds` (default 150 s) |

---

## 3. Architecture

### 3.1 Provider Integration (GrammarService)

`BuildKernel` in `GrammarService.cs` gains one new branch inserted **after** the per-request
key block and **before** the configured Groq block:

```
// 1a. Per-request RunPod key (provider=runpod)
if requestApiKey not empty AND resolvedProvider == "runpod"
    → build RunPod kernel with requestApiKey

// 1. Per-request key (other providers — unchanged)
if requestApiKey not empty AND resolvedProvider != "local"
    → Groq or Mistral per-request (unchanged)

// 2. Configured RunPod key  ← NEW
if Grammar:RunPod:ApiKey not empty AND resolvedProvider not in {"local","groq","mistral"}
    → build RunPod kernel with config key + configured timeout

// 3. Configured Groq key (unchanged)
// 4. Configured Mistral key (unchanged)
// 5. Ollama fallback (unchanged)
```

**Key construction details**:
- Model: `_configuration["Grammar:RunPod:Model"] ?? "gemma-3-27b-lb-ft"`
- Endpoint: `_configuration["Grammar:RunPod:Endpoint"]` (no default — must be set when key is set)
- Timeout: `_configuration.GetValue<int>("Grammar:RunPod:TimeoutSeconds", 150)` seconds
- Activity tag: `"runpod"`
- Log: masked key (`[REDACTED]`), model name, endpoint host only

### 3.2 Provider Integration (TranslationService)

`TranslationService` follows a different pattern (sequential `try/catch` blocks, not `BuildKernel`).
A new RunPod block is inserted after DeepL and before Mistral:

```
// DeepL (unchanged)
// RunPod (NEW) — when Translation:RunPod:ApiKey configured
// Mistral (unchanged)
// Ollama fallback (unchanged)
```

TranslationService does not currently use Semantic Kernel's `BuildKernel` pattern; the RunPod
block uses the same inline `Kernel.CreateBuilder().AddOpenAIChatCompletion(...)` approach with a
dedicated `HttpClient` as per ADR-0005.

### 3.3 AppHost Parameters

Three new parameters per service (Grammar + Translation = 6 total):

| Parameter name | Secret | Default | Environment variable injected |
|---------------|--------|---------|-------------------------------|
| `grammar-runpod-api-key` | ✅ | none | `Grammar__RunPod__ApiKey` |
| `grammar-runpod-endpoint` | ❌ | `""` | `Grammar__RunPod__Endpoint` |
| `grammar-runpod-model` | ❌ | `gemma-3-27b-lb-ft` | `Grammar__RunPod__Model` |
| `translation-runpod-api-key` | ✅ | none | `Translation__RunPod__ApiKey` |
| `translation-runpod-endpoint` | ❌ | `""` | `Translation__RunPod__Endpoint` |
| `translation-runpod-model` | ❌ | `gemma-3-27b-lb-ft` | `Translation__RunPod__Model` |

Timeout parameters (optional, non-secret):

| Parameter name | Default | Environment variable injected |
|---------------|---------|-------------------------------|
| `grammar-runpod-timeout-seconds` | `150` | `Grammar__RunPod__TimeoutSeconds` |
| `translation-runpod-timeout-seconds` | `150` | `Translation__RunPod__TimeoutSeconds` |

### 3.4 User Secrets (local development)

`docs/USER_SECRETS_SETUP.md` must be updated with the new keys. The developer sets:

```json
{
  "Grammar:RunPod:ApiKey": "<endpoint-specific-key>",
  "Grammar:RunPod:Endpoint": "https://<pod-id>-8000.proxy.runpod.net/openai/v1",
  "Grammar:RunPod:Model": "gemma-3-27b-lb-ft",
  "Translation:RunPod:ApiKey": "<endpoint-specific-key>",
  "Translation:RunPod:Endpoint": "https://<pod-id>-8000.proxy.runpod.net/openai/v1",
  "Translation:RunPod:Model": "gemma-3-27b-lb-ft"
}
```

### 3.5 ML Pipeline Artefacts (`/ml/`)

New top-level folder, not part of the .NET solution or any Dockerfile. Contents:

```
/ml/
  README.md
  train_qlora.py
  dataset_template.jsonl
  deploy_runpod_serverless.yaml
  notebooks/
    colab_train_qlora.ipynb
    kaggle_train_qlora.ipynb
```

The `/ml/` folder is added to `.dockerignore` (if present) to prevent accidental inclusion in
API or Web Docker images.

#### Training platform options

Training (fine-tuning) is decoupled from deployment (inference serving). The training script
`train_qlora.py` is platform-agnostic; the notebooks provide platform-specific wrappers:

| Platform | Cost | GPU | Notes |
|----------|------|-----|-------|
| **Google Colab** (free tier) | Free | T4 (15 GB) | Suitable for small datasets and low LoRA rank; session time-limited (~12 h) |
| **Google Colab Pro** | ~$10/mo | A100 (40 GB) | Recommended for full Gemma 3 27B 4-bit; no session cut-off |
| **Kaggle** | Free | 2× T4 (2×15 GB) | 30 h/week GPU quota; output saved to Kaggle Datasets |
| **RunPod A40** | ~$0.39/h | A40 (48 GB) | On-demand; no quota limit; full VRAM for 27B |

**Inference (serving)** always runs on RunPod Serverless — the app's provider integration is
unaffected by where training happened. The output of any training run is a LoRA adapter that
is uploaded to a RunPod network volume and served via the vLLM worker.

The two Jupyter notebooks (`colab_train_qlora.ipynb`, `kaggle_train_qlora.ipynb`) are thin
wrappers that:
1. Clone the repo (or upload `train_qlora.py` and `dataset_template.jsonl`)
2. Install dependencies (`pip install transformers peft trl bitsandbytes`)
3. Call `train_qlora.py` with notebook-appropriate path defaults
4. Package and export the adapter to Google Drive (Colab) or a Kaggle Dataset (Kaggle)

### 3.6 Leaderboard Journey Documentation Page

A new Blazor component in `LearnLuxembourgish.Web` (or `LearnLuxembourgish.Shared/Components`
if shared with Mobile):

- Route: `/leaderboard-journey`
- Navigation entry added to the app's nav menu
- Content structure: static Razor page consuming a markdown-backed journal model or hardcoded
  initial structure (journal entries are added by editing the component or a companion `.md`
  file)

The journal entry model:

```csharp
record JournalEntry(DateOnly Date, string ModelVersion, string Summary, string? LeaderboardScore);
```

The page renders the static sections (goal, CEFR explanation, methodology) followed by a
reversed chronological list of journal entries. New entries are added to the list in the
component's code-behind or a companion data file — no layout changes required.

---

## 4. Decision Document

### Provider selection

- RunPod is selected when `Grammar:RunPod:ApiKey` (or `Translation:RunPod:ApiKey`) is non-empty
  and no higher-priority signal overrides it (per ADR-0004).
- The per-request hint `provider=runpod` routes to RunPod using the supplied `apiKey`, bypassing
  all config keys.
- Empty string for `ApiKey` is treated as absent (`.IsNullOrEmpty` check — consistent with Groq
  and Mistral).

### Error handling

- All RunPod failures (timeout, 4xx, 5xx, `TaskCanceledException`) are caught by the existing
  `catch (Exception ex)` block in each service method.
- The warning log message must NOT include the API key. Log only: provider name, model, endpoint
  host, HTTP status code (if available), elapsed time.
- No additional retry logic — the fallback chain is the retry mechanism.

### Security

- API keys flow exclusively through `IConfiguration` → constructor field → `AddOpenAIChatCompletion`.
  They are never logged, never included in HTTP response bodies, never stored in application state.
- AppHost parameters for secrets use `secret: true` (consistent with existing Groq/Mistral keys).
- CC-006 (key must not appear in logs) is enforced by the existing log discipline; no structured
  logging destructuring of the config object is performed.

### ML artefacts

- `/ml/` is pure developer tooling. It has no runtime dependency on the .NET solution.
- `train_qlora.py` validates dataset schema before training begins (FR-013), exiting with
  non-zero code on first invalid record.
- `deploy_runpod_serverless.yaml` uses a vLLM Docker image and documents all required env vars
  as placeholder comments.
- Training platform is developer's choice: Google Colab (free), Kaggle (free), or RunPod A40
  (paid). Platform-specific notebooks in `/ml/notebooks/` wrap `train_qlora.py`. Inference
  deployment target remains RunPod Serverless regardless of where training ran.

### Blazor documentation page

- The page is a standard Blazor component, not an external CMS. Journal entries are maintained
  by editing a single list in the component's code-behind (or a companion `.cs` data file).
- No database writes, no admin UI — the developer adds entries at training milestones.

### Alternatives discarded

- **Registering RunPod via `IHttpClientFactory` named client**: Adds constructor complexity across
  two services for a single additional provider. Inline `HttpClient` construction is consistent
  with the existing approach (ADR-0005).
- **Feature flag / UI toggle for provider**: Out of scope per spec; provider selection is
  server-side configuration only.
- **Shared `BuildKernel` helper extracted to a service**: Worthwhile if a 5th provider is added;
  premature at 4 providers given the low complexity of the current method.

---

## 5. Testing Strategy

Tests live in `tests/LearnLuxembourgish.Tests/`.

### Unit tests (GrammarService)

| Test | Scenario |
|------|----------|
| `BuildKernel_RunPodConfigured_ReturnsRunPodKernel` | `Grammar:RunPod:ApiKey` set, no per-request hint → `resolvedProvider == "runpod"` |
| `BuildKernel_RunPodAbsent_SkipsRunPod` | `Grammar:RunPod:ApiKey` absent → falls through to Groq branch |
| `BuildKernel_PerRequestRunPod_UsesRequestKey` | `provider=runpod` + `apiKey` supplied → uses request key, not config |
| `BuildKernel_PerRequestGroq_OverridesRunPodConfig` | `Grammar:RunPod:ApiKey` set + `provider=groq` request hint → uses Groq |
| `ExplainGrammarAsync_RunPodTimeout_FallsBackAndLogsWarning` | RunPod throws `TaskCanceledException` → warning logged, returns `null` (triggers fallback in controller) |

### Unit tests (TranslationService)

| Test | Scenario |
|------|----------|
| `TranslateAsync_RunPodConfigured_UsedAfterDeepL` | DeepL absent, RunPod key present → RunPod attempted |
| `TranslateAsync_RunPodAbsent_UsesMistral` | RunPod key absent → Mistral used (unchanged behaviour) |

### ML artefacts

| Test | Method |
|------|--------|
| Dataset validation rejects missing `output` field | Run `python ml/train_qlora.py --dataset <malformed.jsonl> --dry-run`; assert exit code != 0 and stderr contains line number |
| Dataset validation accepts valid template | Run with `dataset_template.jsonl`; assert validation passes |
| Colab notebook installs deps and invokes train_qlora.py | Run notebook top-to-bottom on Colab free tier with dataset_template.jsonl; assert no unhandled exceptions |
| Kaggle notebook exports adapter to Kaggle Dataset | Run notebook on Kaggle with dataset_template.jsonl; confirm output directory contains adapter_config.json |

*Note*: Python tests are developer-executed, not part of the `dotnet test` suite. They are
documented in `/ml/README.md`.

---

## 6. Out of Scope

- Automated CI/CD retraining pipeline
- Multi-tenant provider selection
- UI for end-user model selection
- Evaluation harness or automated leaderboard submission
- DeepL path changes
- Database schema changes

---

## 7. Engineering Practices

No new practices required. The feature follows established conventions:

| Practice | Convention | Reference |
|----------|-----------|-----------|
| Secrets management | User secrets (dev) / AppHost parameters (prod) | Existing Groq/Mistral pattern |
| Provider integration | `AddOpenAIChatCompletion` + inline `Kernel.CreateBuilder` | Existing pattern in `GrammarService.BuildKernel` |
| Activity tracing | `ActivitySource.StartActivity` + `provider` tag | Existing tracing in Grammar/Translation services |
| Error handling | Catch-all → log warning → return `null` → controller fallback | Existing pattern |

---

## 8. Commands

### Build

```
dotnet build LearnLuxembourgish.slnx
```

### Tests

```
dotnet test tests/LearnLuxembourgish.Tests --verbosity normal
```

### Local Run (Aspire AppHost)

```
dotnet run --project src/LearnLuxembourgish.AppHost
```

### ML training (Python, on RunPod A40)

```
python ml/train_qlora.py \
  --base-model google/gemma-3-27b \
  --dataset ml/dataset_template.jsonl \
  --output-adapter ./adapters/gemma-3-27b-lb-ft-v1 \
  --epochs 3 \
  --lr 2e-4 \
  --lora-rank 16
```

---

## 9. Implementation Order

The tasks are sequenced so each step leaves the test suite green:

1. **ADR review** — Review ADR-0004 and ADR-0005 with the team before implementation begins.
2. **AppHost parameters** — Add 6 config parameters + 2 timeout parameters to `AppHost.cs` and update `USER_SECRETS_SETUP.md`.
3. **GrammarService** — Insert RunPod branch in `BuildKernel`; write unit tests.
4. **TranslationService** — Insert RunPod block; write unit tests.
5. **ML artefacts** — Create `/ml/` folder with `train_qlora.py`, `dataset_template.jsonl`, `deploy_runpod_serverless.yaml`, `README.md`.
6. **Leaderboard Journey page** — Add Blazor component, route, and nav entry.
7. **Documentation** — Update `USER_SECRETS_SETUP.md`; add initial journal entry to the page.
