# Feature Specification: RunPod Fine-Tune Provider & Leaderboard Journey

**Created on**: 2026-06-19  
**Status**: Draft

## Executive Summary

- **Objective**: Add a self-hosted RunPod Serverless endpoint (serving a custom fine-tuned Gemma 3 27B model) as a first-class LLM provider in the app, and document the fine-tuning journey whose end-goal is topping the Luxembourgish LLM leaderboard at [ai-sandbox.list.lu](https://ai-sandbox.list.lu/)
- **Primary user**: App owner (developer) during training and configuration; all app users once the fine-tuned model is live
- **Value delivered**: Luxembourgish grammar and translation advice backed by a model specifically trained on CEFR A1/A2 Luxembourgish data — expected to outperform general-purpose cloud models on Lëtzebuergesch-specific tasks
- **Scope**: (1) App integration — RunPod as a 4th LLM provider in `GrammarService` and `TranslationService`; (2) ML pipeline artefacts — training script, dataset template, and serverless deployment template stored under `/ml/`; (3) Documentation page describing the leaderboard ambition and iterative fine-tuning journal
- **Primary success criterion**: The app successfully routes grammar requests to the fine-tuned model when a RunPod API key is configured, and the model's answers visibly reflect CEFR A1/A2 Luxembourgish patterns not present in baseline Gemma 3 27B

## Non-Scope

- Automated CI/CD pipeline to retrain and redeploy the model on every commit
- Multi-tenant or per-user model selection beyond the existing provider-hint mechanism
- Evaluation harness or automatic leaderboard submission — the leaderboard submission is a manual developer action
- Changes to the DeepL translation path (DeepL remains the primary translation provider)
- Fine-tuned model hosting on providers other than RunPod Serverless
- Any schema or database changes — provider selection is purely configuration-driven
- UI for end-users to select "RunPod fine-tuned model" — provider is selected server-side via configuration priority

## Assumptions

- RunPod Serverless exposes an OpenAI-compatible endpoint at `/openai/v1` (vLLM worker default); Semantic Kernel's `AddOpenAIChatCompletion` can connect to it without modification
- The API key used is the RunPod endpoint-specific key from the serverless pod settings, not the global RunPod account key
- The fine-tuned model will be registered under a custom model ID (e.g., `gemma-3-27b-lb-ft`) that is stored in configuration and not hardcoded
- `Grammar:RunPod:ApiKey` being present is the signal that RunPod is available; when absent the provider is silently skipped
- The `/ml/` folder at repo root is the agreed location for ML artefacts; it is outside the .NET solution and not built by the `dotnet` toolchain
- CEFR A1/A2 dataset is assembled by the developer incrementally as they learn Luxembourgish; the dataset template defines the schema, not the data
- The leaderboard evaluation is a periodic manual action; there is no automated scoring in CI

## User Scenarios & Tests

### User Story 1 — Use Fine-Tuned Model for Grammar Explanation (Priority: P1)

As the app owner, I have deployed my fine-tuned Gemma 3 27B model to RunPod Serverless and configured the API key in user secrets. When I request a grammar explanation, the app routes the request to RunPod and returns a Luxembourgish-specific breakdown that reflects the fine-tuned knowledge.

**Why this priority**: Core value of the feature. Everything else (ML scripts, docs) supports this outcome, but this story alone makes the fine-tuned model useful in production.

**Independent Test**: Configure `Grammar:RunPod:ApiKey` and `Grammar:RunPod:Endpoint` in user secrets, submit a grammar request via the UI, and confirm the response header/log shows `provider=runpod`.

**Acceptance Scenarios**:

1. **Given** `Grammar:RunPod:ApiKey` is set in configuration, **When** a grammar explanation is requested with no per-request provider hint, **Then** the request is routed to RunPod and the response identifies `provider=runpod` in the activity tag
2. **Given** `Grammar:RunPod:ApiKey` is set, **When** the UI sends a per-request hint of `provider=groq` with a Groq key, **Then** the per-request key takes precedence and RunPod is NOT used
3. **Given** `Grammar:RunPod:ApiKey` is set and the RunPod endpoint returns HTTP 429 or times out, **Then** the service falls back to the next configured provider (Groq or Mistral) and logs a warning
4. **Given** `Grammar:RunPod:ApiKey` is NOT set, **When** any grammar request is made, **Then** provider selection behaves exactly as before this feature was added (Groq → Mistral → Ollama)

---

### User Story 2 — ML Pipeline Artefacts Available in Repository (Priority: P2)

As a developer setting up a fine-tuning run, I can find a ready-to-use QLoRA training script, a CEFR A1/A2 dataset template, and a RunPod Serverless deployment template in the `/ml/` folder of the repository, so I can start training without rebuilding the scaffolding from scratch.

**Why this priority**: Prerequisite for having anything to deploy to RunPod; however the app integration (P1) can be tested with any OpenAI-compatible endpoint while training is in progress.

**Independent Test**: Clone the repo, navigate to `/ml/`, and confirm that `train_qlora.py`, `dataset_template.jsonl`, and `runpod_serverless_template.yaml` are present, syntactically valid, and accompanied by a `README.md` explaining each file.

**Acceptance Scenarios**:

1. **Given** the `/ml/` directory exists, **When** a developer opens `train_qlora.py`, **Then** the script contains parameterised paths for base model, dataset, output adapter, and hyperparameters; no hardcoded user paths
2. **Given** the dataset template exists, **When** a developer opens `dataset_template.jsonl`, **Then** each record has a defined schema (`instruction`, `input`, `output`, optional `cefr_level`) and the file contains at least 3 illustrative example rows
3. **Given** the deployment template exists, **When** a developer opens `runpod_serverless_template.yaml`, **Then** it specifies the Docker image (vLLM or TGI), GPU type, and environment variables for the model path and API key; it can be applied to a RunPod Serverless pod without modification beyond filling in the model path
4. **Given** a developer follows the `/ml/README.md` instructions end-to-end, **Then** they can reproduce a training run and deploy the resulting adapter to RunPod Serverless without requiring undocumented knowledge

---

### User Story 3 — Leaderboard Journey Documentation Page (Priority: P3)

As a visitor or fellow Luxembourgish learner, I can read a documentation page in the app that explains the goal of building the best Luxembourgish LLM, describes the CEFR fine-tuning approach, and tracks the iterative improvement journal — so I can understand the project's ambition and follow the progress.

**Why this priority**: Narrative and motivational value, but does not block the technical integration or training pipeline.

**Independent Test**: Navigate to the documentation page in the Blazor Web app (or a static markdown page in `docs/`), confirm all mandatory sections are present and no placeholder text remains.

**Acceptance Scenarios**:

1. **Given** the documentation page is published, **When** a visitor opens it, **Then** they can read: (a) the leaderboard ambition section with a link to `ai-sandbox.list.lu`, (b) an explanation of what CEFR A1/A2 means, (c) the fine-tuning methodology summary, and (d) a journal section with at least one entry
2. **Given** the developer completes a new training run, **When** they update the journal, **Then** the new entry is added without modifying the page structure or breaking existing entries
3. **Given** the page exists, **When** it is read by a non-technical user, **Then** no code, YAML, or raw command-line instructions appear in the body — technical details are linked to the `/ml/` folder

---

### Edge Cases

- What happens when `Grammar:RunPod:Endpoint` is set but `Grammar:RunPod:ApiKey` is empty? — RunPod is skipped; the missing key is the signal for "not configured".
- What happens when the RunPod serverless pod is cold and takes more than 30 seconds to respond? — The HTTP client timeout applies; the service logs a warning and falls back to the next provider.
- What happens when the fine-tuned model returns output in a different format than general-purpose models (e.g., missing required JSON blocks)? — The existing response parsing in `GrammarService` already handles partial/malformed LLM output; no additional handling is needed for RunPod specifically.
- What happens when both `Grammar:RunPod:ApiKey` and a per-request `apiKey` with `provider=runpod` are present? — Per-request key always wins (existing priority rule extended to RunPod).

### Failure Modes

- **RunPod pod cold start timeout**: RunPod Serverless pods may have cold start times of 30–120 seconds; the HTTP client timeout configured for the grammar service must accommodate this or the fallback chain must handle the `TaskCanceledException` gracefully.
- **Endpoint key rotation**: If the RunPod endpoint key is rotated, all in-flight requests fail; the service logs the 401 as a warning and falls back — no user-visible crash.
- **Model underloaded (OOM)**: If the GPU worker runs out of memory (e.g., long context), RunPod returns a 500; the service treats 5xx as a transient failure and falls back.
- **Dataset corruption**: A malformed JSONL record in the training dataset causes the training script to abort; the script must validate the dataset schema before starting training and report the first invalid line.

## Requirements

### Functional Requirements

**App Integration**

- **FR-001**: The system MUST support `runpod` as a named LLM provider in `GrammarService`, selectable by (a) per-request provider hint or (b) presence of `Grammar:RunPod:ApiKey` in configuration
- **FR-002**: When `Grammar:RunPod:ApiKey` is configured and no per-request key overrides it, RunPod MUST be tried before Groq (i.e., RunPod is the highest-priority configured provider)
- **FR-003**: When the RunPod endpoint fails (timeout, 4xx, 5xx), the system MUST fall back to the next available provider in the existing chain (Groq → Mistral → Ollama) without surfacing an error to the end user
- **FR-004**: The model identifier, endpoint URL, and API key for RunPod MUST each be independently configurable via `Grammar:RunPod:Model`, `Grammar:RunPod:Endpoint`, and `Grammar:RunPod:ApiKey`
- **FR-005**: The AppHost MUST inject `Grammar:RunPod:ApiKey`, `Grammar:RunPod:Endpoint`, and `Grammar:RunPod:Model` as environment variables into the API project, following the existing parameter pattern
- **FR-006**: `TranslationService` MUST support RunPod as an additional provider, tried after DeepL and before Mistral, when `Translation:RunPod:ApiKey` is configured
- **FR-007**: The activity tag `provider` MUST record `runpod` when a RunPod call is used, consistent with how `groq`, `mistral`, and `local` are tagged today
- **FR-008**: The per-request provider hint value `runpod` MUST be recognised in `BuildKernel`; when sent with an API key, it MUST route to RunPod regardless of configuration

**ML Pipeline Artefacts**

- **FR-009**: The repository MUST contain a `/ml/README.md` that describes: folder structure, prerequisites (Python version, GPU requirements), step-by-step training instructions, and deployment instructions
- **FR-010**: The repository MUST contain `/ml/train_qlora.py` — a QLoRA fine-tuning script for Gemma 3 27B that accepts command-line arguments for: base model path or HuggingFace ID, dataset path, output adapter path, number of epochs, learning rate, and LoRA rank
- **FR-011**: The repository MUST contain `/ml/dataset_template.jsonl` — a JSONL file where each record has at minimum `instruction` (string), `input` (string, may be empty), `output` (string), and an optional `cefr_level` field (`A1` or `A2`)
- **FR-012**: The repository MUST contain `/ml/deploy_runpod_serverless.yaml` — a RunPod Serverless worker configuration template that references a vLLM or TGI Docker image, specifies the GPU type, and documents required environment variables
- **FR-013**: The training script MUST validate the dataset schema before beginning training and halt with a descriptive error message on the first malformed record

**Documentation Page**

- **FR-014**: The app MUST expose a documentation page (Blazor component or static markdown) titled "Luxembourgish LLM Leaderboard Journey" containing: leaderboard goal, CEFR methodology, fine-tuning approach summary, and a dated training journal
- **FR-015**: The documentation page MUST include a direct link to the leaderboard at `https://ai-sandbox.list.lu/`
- **FR-016**: The training journal MUST be structured so that new entries can be added by editing a single file, without modifying the page layout or navigation

### Key Entities

- **RunPod Provider Configuration**: Endpoint URL, API key (secret), model identifier — all per service (Grammar, Translation)
- **CEFR Dataset Record**: `instruction` (string), `input` (string), `output` (string), `cefr_level` (enum: A1 | A2)
- **Training Journal Entry**: Date, model version, training details summary, leaderboard score (if submitted), observations

## Success Criteria

### Measurable Outcomes

- **SC-001**: When `Grammar:RunPod:ApiKey` is configured, 100% of grammar explanation requests are routed to RunPod (absent per-request overrides), verified by activity trace tags
- **SC-002**: When RunPod is unavailable, provider fallback completes within the existing HTTP timeout window and the user receives a grammar response from the next available provider — no user-visible error
- **SC-003**: The `/ml/` artefacts are sufficient for the developer to reproduce a complete QLoRA training run from scratch, measurable by successfully completing the run following only the `README.md` instructions with no external documentation
- **SC-004**: The fine-tuned model's grammar explanations contain at least one Luxembourgish-specific correction or insight not present in baseline Gemma 3 27B responses on the same input, as assessed manually on 10 test sentences
- **SC-005**: The documentation page is reachable from the app's main navigation and loads in under 2 seconds on a standard connection

## Compliance Criteria

### Compliance Cases

| ID | Scenario | Input | Expected Output |
|----|----------|-------|-----------------|
| CC-001 | RunPod used when key is configured | `Grammar:RunPod:ApiKey` set; POST `/api/grammar` with no provider hint | Response is returned; activity tag `provider=runpod`; no fallback log entry |
| CC-002 | RunPod skipped when key is absent | `Grammar:RunPod:ApiKey` not set; POST `/api/grammar` | Provider selection follows pre-existing chain (Groq → Mistral → Ollama); no RunPod-related log entries |
| CC-003 | Per-request Groq key overrides RunPod config | `Grammar:RunPod:ApiKey` set; request body includes `apiKey=<groq-key>` and `provider=groq` | Activity tag `provider=groq`; RunPod is NOT called |
| CC-004 | RunPod timeout triggers fallback | `Grammar:RunPod:ApiKey` set; RunPod endpoint unreachable | Warning logged; request completes via next provider; HTTP 200 returned to client |
| CC-005 | Dataset validation rejects malformed record | `train_qlora.py` invoked with a JSONL file containing a record missing the `output` field | Script exits with a non-zero code and prints the line number and field name of the first invalid record; training does NOT begin |
| CC-006 | Must NOT expose RunPod API key in logs | RunPod responds with any error | Log entries MUST NOT contain the literal API key value |
| CC-007 | Translation falls back to Mistral when RunPod absent | `Translation:RunPod:ApiKey` not set; `Translation:Mistral:ApiKey` set; POST `/api/translations` | Response from Mistral; activity tag `provider=mistral` |

## Invariants

- The provider fallback chain in `GrammarService` is always ordered: per-request key → RunPod (config) → Groq (config) → Mistral (config) → Ollama (local). No configuration combination should skip a higher-priority provider when its key is valid and the request is not pinned to a specific provider.
- API keys (RunPod, Groq, Mistral) are never logged, included in error responses, or stored in application state — they travel only through `IConfiguration` and are passed directly to `AddOpenAIChatCompletion`.
- The absence of `Grammar:RunPod:ApiKey` is the sole signal for "provider not configured"; an empty string MUST be treated as absent.
- The `/ml/` folder contents are developer tooling artefacts; they MUST NOT be included in any Docker image produced by the `src/` Dockerfile builds.

## Related Specs

- [Rate Limiting](../rate-limiting/spec.md) — outbound call budget applies to RunPod calls identically to Groq/Mistral calls
