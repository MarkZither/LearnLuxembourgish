# LLM Provider Priority Order

**Status**: Proposed
**Date**: 2026-06-19

## Context

The app uses a fallback chain of LLM providers in `GrammarService` and `TranslationService`.
Adding RunPod as a 4th provider (serving a custom fine-tuned Gemma 3 27B model) requires a
decision on where RunPod sits in the resolution order.

The current chain for Grammar is: per-request key → Groq (config) → Mistral (config) → Ollama (local).

RunPod is a self-hosted, fine-tuned model that is expected to outperform general-purpose cloud
providers on Lëtzebuergesch-specific tasks. However, RunPod Serverless is pay-per-use and may
have cold-start latency; it is not always configured.

The priority order must be consistent across `GrammarService` and `TranslationService` to avoid
surprising divergence in which model is used for each operation.

## Priorities and Requirements (ordered)

1. **Fine-tuned quality first** — When the developer has deployed and configured a fine-tuned
   model, it must be preferred over general-purpose cloud models. The fine-tuned model is the
   primary motivation for this feature.
2. **Zero configuration regression** — When RunPod is not configured (`Grammar:RunPod:ApiKey`
   absent or empty), the behaviour must be identical to the pre-feature chain. No existing
   deployment should change behaviour.
3. **Per-request override always wins** — A caller supplying an explicit `apiKey` + `provider`
   hint must bypass the configuration chain regardless of which providers are configured.
4. **Deterministic ordering** — The order must be expressed unambiguously in code; no
   nondeterministic or environment-dependent selection.

## Options Considered

### Option 1: RunPod after per-request, before Groq

Chain: per-request key → RunPod (config) → Groq (config) → Mistral (config) → Ollama (local)

RunPod is tried immediately after per-request overrides, making it the highest-priority
server-configured provider when present.

**Evaluation against priorities**:
- **Fine-tuned quality first**: ✅ RunPod (fine-tuned) is preferred over general-purpose Groq
  and Mistral whenever configured.
- **Zero configuration regression**: ✅ When `Grammar:RunPod:ApiKey` is absent, the branch is
  skipped and the chain is unchanged.
- **Per-request override always wins**: ✅ Per-request branch is evaluated before the RunPod
  config branch.
- **Deterministic ordering**: ✅ Linear fallback; position is explicit in source code.

### Option 2: RunPod after Groq, before Mistral

Chain: per-request key → Groq (config) → RunPod (config) → Mistral (config) → Ollama (local)

RunPod would only be used when Groq is not configured, treating Groq as the primary cloud provider.

**Evaluation against priorities**:
- **Fine-tuned quality first**: ❌ Groq (general-purpose cloud model) takes priority over the
  fine-tuned model. This contradicts the stated purpose of the feature.
- **Zero configuration regression**: ✅ No change when RunPod absent.
- **Per-request override always wins**: ✅
- **Deterministic ordering**: ✅

### Option 3: RunPod as last resort before Ollama

Chain: per-request key → Groq (config) → Mistral (config) → RunPod (config) → Ollama (local)

**Evaluation against priorities**:
- **Fine-tuned quality first**: ❌ Fine-tuned model is used only when both Groq and Mistral
  are unavailable. This negates the value of fine-tuning.
- **Zero configuration regression**: ✅
- **Per-request override always wins**: ✅
- **Deterministic ordering**: ✅

## Decision

Option 1: RunPod after per-request, before Groq.

**Chain (Grammar and Translation)**:
`per-request key → RunPod (config) → Groq (config) → Mistral (config) → Ollama (local)`

For `TranslationService`, DeepL precedes all LLM providers and is unaffected:
`DeepL → RunPod (config) → Mistral (config) → Ollama (local)`

This order satisfies the primary requirement that the fine-tuned model is preferred when
configured, while preserving the existing behaviour when it is not.

## Implementation Notes

- `Grammar:RunPod:ApiKey` being non-empty is the sole signal for "RunPod configured".
  An empty string is treated as absent.
- The per-request hint `provider=runpod` with a supplied `apiKey` must be recognised in
  `BuildKernel` as a first-class provider value, routing to the RunPod endpoint.
- The `provider` activity tag value is `runpod` (lowercase), consistent with `groq`, `mistral`,
  and `local`.
