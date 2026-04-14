# Rate Limit Failure Policy

**Status**: Proposed
**Date**: 2026-04-14

## Context

When the rate limit backing store is unavailable (e.g., unhandled exception in `IsAvailableAsync`, internal corruption, or a future Redis connection failure), the rate limit middleware cannot verify whether the incoming request is within quota. The system must define consistent, predictable behaviour for this failure mode.

The primary business risk this feature guards against is unexpected LLM API cost overruns. This context is the decisive factor when weighing the two failure policies.

## Priorities and Requirements (ordered)

1. **Protect LLM API budget** — Cost overruns are the explicitly stated primary risk in the spec. If the store is unavailable and requests are allowed through, every unmetered request may generate an outbound LLM call with a direct monetary cost.
2. **Consistent behaviour across instances** — All threads and (in future) all instances must behave identically when the store is unavailable. Inconsistent handling creates unpredictable partial-rate-limiting that is harder to observe and debug than a clean fail state.
3. **Observability** — The system must emit structured log entries and (where applicable) metrics when the fail policy is triggered, so the operator can detect and respond to store availability issues.
4. **Fairness to legitimate users** — Legitimate users should not be permanently blocked by transient failures. The failure state must be distinguishable from a standard quota rejection so clients can understand why they were rejected.

## Options Considered

### Option 1: Fail-closed — reject all requests when store is unavailable

When `IRateLimitStore.IsAvailableAsync` returns false or throws, the middleware returns HTTP 429 with a distinct `X-RateLimit-Error: store-unavailable` header and logs a structured warning. No request is forwarded to the translation endpoint or the LLM service.

**Evaluation against priorities**:

- **Protect LLM API budget**: No unmetered request can reach the LLM service during a store outage. Budget protection is absolute.
- **Consistent behaviour**: All requests from all callers, including Owner/Admin, are rejected uniformly while the store is unavailable. No ambiguity.
- **Observability**: Each rejection logs `RateLimitStore unavailable — request rejected (fail-closed)` with request metadata. The response header distinguishes store failures from quota exhaustion.
- **Fairness**: Legitimate users receive a 429 with a header indicating a system issue rather than a per-user limit. This is more disruptive than fail-open but is temporary and bounded to the store outage window.

**Limitation**: If the outage is prolonged, the service is effectively offline for translation requests. Acceptable for the current scale; a later ADR can introduce a short grace window (e.g., allow requests for the first N seconds of a store outage) if needed.

### Option 2: Fail-open — allow all requests when store is unavailable

When the store is unavailable, the middleware skips rate limit checks and forwards all requests normally, logging a warning.

**Evaluation against priorities**:

- **Protect LLM API budget**: Budget protection is lost entirely during the outage window. Any traffic volume, including automated abuse, reaches the LLM service with no throttle.
- **Consistent behaviour**: All requests are forwarded, which is consistent, but it is consistently unsafe under the budget-protection requirement.
- **Observability**: Log entries are still emitted, but the monitoring signal is informational rather than actionable — there is no measurable impact on the system to correlate with the alert.
- **Fairness**: Legitimate users are unaffected. No disruption.

**Limitation**: A store outage is operationally indistinguishable from no rate limiting being present. This directly contradicts the primary purpose of the feature.

## Decision

Fail-closed (Option 1).

The spec's primary success criterion is bounding LLM API spend. A fail-open policy defeats that criterion entirely during any store outage window, however short. The cost of a transient false 429 to a legitimate user is lower than the cost of an uncapped LLM billing event during an outage.

The response header `X-RateLimit-Error: store-unavailable` distinguishes store failures from quota rejections in client error handling and in log correlation. Owner/Admin callers receive the same rejection during a store outage and can observe the header to detect the failure mode.

If a future operational requirement demands partial availability during transient failures (e.g., a 10-second grace window), that constraint should be addressed in a new ADR that extends this policy rather than replacing it.

## Implementation Notes

The middleware checks `IRateLimitStore.IsAvailableAsync()` at the start of request processing. If it returns false or throws, the middleware sets the response to:

```
HTTP 429 Too Many Requests
X-RateLimit-Error: store-unavailable
Retry-After: 60
```

The body should contain a human-readable message distinguishing this from a quota exhaustion event.

The `InMemoryRateLimitStore.IsAvailableAsync()` always returns true. The method exists solely to satisfy the circuit breaker seam for the future Redis implementation, where TCP connectivity failures are detectable.

## References

- Feature spec: `docs/features/rate-limiting/spec.md` Failure Modes section
- ADR 0001: Rate Limit Backing Store (defines `IRateLimitStore.IsAvailableAsync`)
