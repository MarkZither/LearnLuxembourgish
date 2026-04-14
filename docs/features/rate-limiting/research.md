# Rate Limiting: Research and Decision Consolidation

**Feature**: API Rate Limiting
**Date**: 2026-04-14
**Spec**: `docs/features/rate-limiting/spec.md`

## Summary

This document consolidates the architectural decisions and implementation research for the rate limiting feature. Three ADRs govern the core design choices. All decisions were reached through explicit trade-off evaluation.

## Architectural Decisions

| Decision | Choice | ADR |
|---|---|---|
| Backing store | In-memory behind `IRateLimitStore` interface; Redis is the documented migration path | ADR 0001 |
| Failure policy | Fail-closed: reject all requests when store is unavailable | ADR 0002 |
| Rate limit algorithm | Token bucket with continuous refill | ADR 0003 |

## Rate Limit Policies (from spec, confirmed)

| Scope | Key Type | Max Tokens | Window |
|---|---|---|---|
| Unauthenticated per-IP | Caller IP address | 1 | 60 minutes |
| Unauthenticated global | Fixed key `global:anon` | 10 | 60 minutes |
| Authenticated per-user | User ID claim | 5 | 60 minutes |
| Authenticated global | Fixed key `global:auth` | 50 | 60 minutes |
| Outbound LLM calls | Fixed key `outbound:llm` | Configurable (default: 100) | Configurable (default: 60 minutes) |
| Owner/Admin role | N/A — bypass all checks | Unlimited | N/A |

FR-012 requires: global limit is evaluated before per-identity. A request rejected at the global level does not consume per-identity tokens.

## Third-Party Package Evaluation: AspNetCoreRateLimit

`AspNetCoreRateLimit` (Stefan Prodan) is a community package that predates the ASP.NET Core built-in rate limiter. It was evaluated against this feature's requirements.

**What it provides**: per-IP and per-client-ID limits, multiple endpoint rules, in-memory and `IDistributedCache` (Redis) backing stores, `appsettings.json` configuration.

**Why it was not selected**:

- No aggregate cross-client bucket. It limits *per IP*, not across all anonymous callers simultaneously. FR requires a shared 10 req/hr budget for all unauthenticated traffic and a shared 50 req/hr budget for all authenticated traffic. This is architecturally impossible to express in the package's rule model.
- Fixed window / leaky bucket only. Token bucket (ADR 0003) is not supported.
- No ordered multi-policy evaluation. The global-before-per-identity check ordering required by FR-012 cannot be expressed.
- Inbound HTTP only. Outbound LLM call budgeting is out of scope for any HTTP middleware package.
- Unmaintained for modern .NET. Last meaningful release was 2022; no .NET 8/9/10 support; open issues unaddressed.

The aggregate global limits (anonymous and authenticated combined buckets) are the requirements that no off-the-shelf package covers. Custom code is required for those regardless of which other package is chosen, which makes adopting a third-party package a net complexity increase rather than a simplification.

## ASP.NET Core Built-in Rate Limiting Evaluation

ASP.NET Core 8+ ships `Microsoft.AspNetCore.RateLimiting` with `AddRateLimiter` and built-in `TokenBucketRateLimiter`. This was evaluated before deciding on a custom implementation.

**Why the built-in was not selected**:

- `PartitionedRateLimiter<HttpContext>` does not natively support checking a global counter before a per-identity counter in the same request pipeline (FR-012). Workarounds require chaining two separate middleware registrations, which breaks the atomicity of the check-before-consume sequence.
- The built-in `RateLimiter` base class is sealed to in-memory state. There is no supported extension point for a distributed backing store. Implementing Redis support requires implementing `RateLimiter` from scratch, at which point the built-in type no longer simplifies anything.
- Owner/Admin bypass with outbound LLM budget tracking requires reading caller identity and performing conditional logic that the policy-based registration API does not expose without heavy use of `EnableRateLimitingAttribute` overrides.

**Custom `IRateLimitStore` + middleware are chosen** because they map directly to the feature's requirements without workarounds or hidden coupling to framework internals.

## Component Architecture

```
┌─────────────────────────────────────────────────────┐
│                  HTTP Pipeline                       │
│                                                      │
│  TranslationRateLimitMiddleware                      │
│  - Only activates on POST /api/translations          │
│  - Reads caller identity (IP or user ID)             │
│  - Checks Owner/Admin claim: bypass all checks       │
│  - Check global limit for tier (via IRateLimitStore) │
│  - If rejected: 429 (GlobalLimitExceeded)            │
│  - Check per-identity limit (via IRateLimitStore)    │
│  - If rejected: 429 (PerIdentityLimitExceeded)       │
│  - Sets X-RateLimit-* response headers               │
│  - Calls next()                                      │
└────────────────────┬────────────────────────────────┘
                     │
┌────────────────────v────────────────────────────────┐
│           TranslationsController                     │
│  - Calls _translationService (DeepL)                 │
│  - Calls _grammarService (Groq/Mistral) ←────────┐  │
└─────────────────────────────────────────────────────┘
                                                    │
┌───────────────────────────────────────────────────── │
│  GrammarService                                    │  │
│  - Checks IOutboundCallBudget before LLM call      │  │
│  - If budget exhausted: throws OutboundBudget-     │  │
│    ExceededException (controller → 429/503)        │  │
└────────────────────────────────────────────────────  │
```

## Inbound Middleware: Check-Then-Consume Order

FR-012 requires global before per-identity evaluation, with no per-identity consumption if global rejects. The implementation uses a two-phase approach:

1. Call `IRateLimitStore.IsAvailableAsync()`. If false: fail-closed, return 429 with `X-RateLimit-Error: store-unavailable`.
2. Call `TryConsumeAsync` on global key for the caller's tier. If rejected: return 429, do not touch per-identity counter.
3. Call `TryConsumeAsync` on per-identity key. If rejected: return 429 (global token was consumed for this request; this is expected behavior per spec — the request was legitimately submitted but the individual was over quota).
4. Both checks passed: set `X-RateLimit-*` headers using the per-identity result and call `next()`.

**Note on Owner/Admin bypass**: The bypass applies to all inbound and outbound limits. Owner/Admin requests skip steps 1 through 4 entirely and pass directly to the controller without any header modification.

## Outbound LLM Budget

The outbound budget is enforced inside `GrammarService` via an `IOutboundCallBudget` dependency. This places the check at the exact point where the LLM call is about to be made, preventing any path through the controller from accidentally bypassing it.

`IOutboundCallBudget` is backed by the same `IRateLimitStore` with a dedicated key (`outbound:llm`). This means:
- Both inbound and outbound counters share the same in-memory store instance.
- When migrating to Redis, both counters migrate together with no additional changes.

The outbound budget is intentionally independent from inbound limits. An Owner/Admin who bypasses inbound limits does NOT bypass the outbound LLM budget. Per the spec (User Story 5, accepted Acceptance Scenario): the outbound limit is the last line of defence against cost overruns regardless of caller tier.

**Outstanding design question**: When the outbound budget is exhausted, should `GrammarService` return HTTP 429 (rate limited) or HTTP 503 (service temporarily unavailable)? The spec permits both. A 503 is semantically more accurate (the service is overloaded by its own cost constraints, not by the caller's individual rate), but 429 is more consistent with the rest of the middleware responses. This should be clarified during implementation; the outbound response code should be configurable.

## Response Headers

All non-rejected, non-owner-bypass responses from `POST /api/translations` include:

| Header | Value |
|---|---|
| `X-RateLimit-Limit` | Max tokens for the per-identity policy |
| `X-RateLimit-Remaining` | Tokens remaining for the per-identity policy after this request |
| `X-RateLimit-Reset` | Unix epoch timestamp when the per-identity bucket will next have a full token |

429 responses include:

| Header | Value |
|---|---|
| `Retry-After` | Seconds until the next token becomes available |
| `X-RateLimit-Error` | `per-identity-limit-exceeded` | `global-limit-exceeded` | `store-unavailable` |

## Configuration Schema

Rate limit thresholds are configurable via `appsettings.json` without redeployment (FR-011):

```json
"RateLimiting": {
  "Unauthenticated": {
    "PerIp": { "MaxTokens": 1, "WindowMinutes": 60 },
    "Global": { "MaxTokens": 10, "WindowMinutes": 60 }
  },
  "Authenticated": {
    "PerUser": { "MaxTokens": 5, "WindowMinutes": 60 },
    "Global": { "MaxTokens": 50, "WindowMinutes": 60 }
  },
  "Outbound": {
    "Llm": { "MaxTokens": 100, "WindowMinutes": 60 }
  }
}
```

## Identified Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| In-memory counters lost on app restart | High (by design) | Acceptable for v1; restart frequency is low and quota window is 60 minutes |
| IPv6 prefix abuse (edge case from spec) | Low | Out of scope for v1; IP is used as-is; can be addressed by a future ADR on caller identity resolution |
| Clock drift between API instances (future multi-instance) | Low (irrelevant for single-instance v1) | Mitigated by Redis migration (ADR 0001) before scaling |
| Token expiry not actively evicted from MemoryCache | Low | Token bucket entries are naturally bounded; entries not accessed for more than one window are stale and their memory is recoverable by GC |
