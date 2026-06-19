# RunPod HTTP Timeout Strategy

**Status**: Proposed
**Date**: 2026-06-19

## Context

RunPod Serverless workers are cold-started on demand. When a worker has been idle, a request
will trigger container startup before the model begins loading. Cold-start times for a Gemma 3
27B model on an A40 GPU typically range from 30 to 120 seconds.

The existing HTTP clients for Groq and Mistral are constructed inline via
`Kernel.CreateBuilder().AddOpenAIChatCompletion(...)` without an explicit `HttpClient`. The
default `HttpClient` timeout in .NET is 100 seconds, which may be insufficient for a cold RunPod
pod (worst-case ~120 s) and is disproportionately long for Groq/Mistral (typically 5–15 s).

If the timeout expires before RunPod responds, a `TaskCanceledException` or
`OperationCanceledException` is thrown. The existing `catch (Exception ex)` in `ExplainGrammarAsync`
already handles this and falls back to the next provider, logging a warning. The question is
whether the timeout should be configured separately for RunPod to avoid making Groq/Mistral
requests wait 100 s before falling back.

## Priorities and Requirements (ordered)

1. **Cold-start survivability** — A cold RunPod pod must not cause the grammar request to fail
   from the user's perspective. The fallback chain must be triggered, not a hard error.
2. **Fallback responsiveness** — If RunPod times out, the next provider in the chain (Groq or
   Mistral) should start within a time budget acceptable to the user. Waiting the full default
   100 s before falling back is unacceptable.
3. **No new abstractions for a single provider** — The codebase builds Semantic Kernel instances
   inline per-request. Introducing a named `IHttpClientFactory` registration adds indirection
   without clear lifecycle benefit at current scale.
4. **Configurable without redeployment** — The timeout value should be available in
   configuration so the developer can adjust it without a code change.

## Options Considered

### Option 1: Separate `HttpClient` with configured timeout passed to `AddOpenAIChatCompletion`

`AddOpenAIChatCompletion` accepts an optional `HttpClient` parameter. A dedicated `HttpClient`
with a configurable timeout (default 150 s) is constructed when building the RunPod kernel, read
from `Grammar:RunPod:TimeoutSeconds`.

Groq and Mistral continue to use the default `HttpClient` (100 s), which is acceptable for
cloud providers.

**Evaluation against priorities**:
- **Cold-start survivability**: ✅ 150 s covers worst-case cold starts; value is configurable.
- **Fallback responsiveness**: ⚠️ If RunPod is cold and timeout is 150 s, fallback to Groq
  starts after 150 s. This is the unavoidable cost of waiting for a cold pod. A shorter timeout
  (e.g., 60 s) can be set if the developer prefers fast fallback over cold-start tolerance.
- **No new abstractions**: ✅ `new HttpClient { Timeout = ... }` is a one-liner at the
  construction site. No factory or named client required.
- **Configurable without redeployment**: ✅ Read from `IConfiguration`.

### Option 2: Shared `IHttpClientFactory` with named client

Register a named `HttpClient` (`"runpod"`) in `Program.cs` with the configured timeout, and
inject `IHttpClientFactory` into `GrammarService` and `TranslationService`.

**Evaluation against priorities**:
- **Cold-start survivability**: ✅ Same as Option 1.
- **Fallback responsiveness**: ✅ Same as Option 1.
- **No new abstractions**: ❌ Requires changing the constructor signature of both services,
  adding a registration in `Program.cs`, and threading the factory through `BuildKernel`.
  The complexity is unjustified for a single provider.
- **Configurable without redeployment**: ✅

### Option 3: No change — rely on default 100 s timeout

Accept the default 100 s `HttpClient` timeout for RunPod. Cold starts over 100 s will trigger
the existing fallback path (which already catches `TaskCanceledException`). Warm pods respond
in 2–10 s.

**Evaluation against priorities**:
- **Cold-start survivability**: ⚠️ 100 s covers most cold starts but not worst-case (up to
  120 s). A request arriving during an extreme cold start will fall back; this is tolerable.
- **Fallback responsiveness**: ⚠️ When RunPod is cold, the user waits up to 100 s before
  receiving a response from Groq/Mistral. This is a poor experience.
- **No new abstractions**: ✅ Zero code change.
- **Configurable without redeployment**: ❌ Not configurable without code change.

## Decision

Option 1: Separate `HttpClient` with configured timeout.

When building the RunPod Semantic Kernel instance, pass an `HttpClient` whose timeout is read
from `Grammar:RunPod:TimeoutSeconds` (and `Translation:RunPod:TimeoutSeconds`), defaulting to
`150` seconds. This covers worst-case cold starts while remaining configurable.

The existing `catch (Exception ex)` fallback in `ExplainGrammarAsync` and equivalent methods
already handles `TaskCanceledException`; no additional error handling is required.

## Implementation Notes

```csharp
var timeoutSeconds = _configuration.GetValue<int>("Grammar:RunPod:TimeoutSeconds", 150);
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
return (
    Kernel.CreateBuilder()
        .AddOpenAIChatCompletion(model, new Uri(endpoint), apiKey, httpClient: httpClient)
        .Build(),
    "runpod"
);
```

The `HttpClient` instance is created per-request (consistent with how Groq/Mistral kernels are
built today). Disposal is handled by the `Kernel` when it goes out of scope.

AppHost must inject `Grammar__RunPod__TimeoutSeconds` and `Translation__RunPod__TimeoutSeconds`
as optional parameters (non-secret, with default value `150`).
