# Security Review — API Rate Limiting

**Mode**: Architectural
**Date**: 2026-04-15
**Reviewer**: Copilot Security Agent
**Feature**: `docs/features/rate-limiting/spec.md`

## Executive Summary

**Verdict**: APPROVED_WITH_CONTROLS

The design is sound. Three ADRs cover the critical architectural choices (store, failure policy, algorithm) and the reasoning is correct. No redesign is required. Four medium-to-high findings were identified that must be addressed during implementation; none require architecture changes.

## Attack Surface

| Component | Exposure | Data | Initial Risk |
|-----------|----------|------|--------------|
| `POST /api/translations` middleware | Public internet | Client IP, JWT sub claim, role claims | High |
| `InMemoryRateLimitStore` | In-process only | Per-IP / per-user counters | Low |
| `IRateLimitStore.TryConsumeAsync` | Internal (hot path) | Counter state | Medium |
| `GrammarService` + `IOutboundCallBudget` | Internal | Outbound LLM call count | Medium |
| `X-RateLimit-*` response headers | Public | Remaining quota, reset time, error type | Low |

## STRIDE Analysis


### Findings

| ID | Threat | Component | Severity | Status |
|----|--------|-----------|----------|--------|
| SEC-001 | Spoofing | IP identity resolution | **High** | OPEN |
| SEC-002 | EoP | Middleware ordering | **Medium** | OPEN |
| SEC-003 | DoS | Global token bleed | **Medium** | RESOLVED — see `research.md` Identified Risks |
| SEC-004 | Spoofing | XFF without trusted proxy | **High** | OPEN — linked to SEC-001 |
| SEC-005 | DoS | Unhandled `TryConsumeAsync` exception | **Medium** | OPEN |
| SEC-006 | Info Disclosure | `X-RateLimit-Error` distinguishes error types | **Low** | ACCEPTED |

---

### SEC-001 / SEC-004: IP Identity Resolution Behind Reverse Proxy

**Severity**: High
**Component**: `TranslationRateLimitMiddleware` — anonymous caller identity

The plan specifies `HttpContext.Connection.RemoteIpAddress` as the source for anonymous caller IP. The API runs containerised (Dockerfile present) and in any non-local deployment it sits behind a reverse proxy. In those environments `RemoteIpAddress` is the proxy's IP, not the client's.

Two failure modes:

1. **Ignored forwarded headers**: every anonymous caller shares one bucket. The per-IP limit of 1/hr becomes a broken global limit; the first anon request blocks all others.
2. **Naive XFF reading** (SEC-004): if the middleware reads `X-Forwarded-For` without allowlisting the trusted proxy CIDR, any client can forge the header and get a fresh bucket on every request — per-IP limit entirely bypassed.

**Remediation**: Call `app.UseForwardedHeaders()` in `Program.cs` **before** `UseAuthentication()`, configured with `KnownProxies` or `KnownNetworks` restricted to the actual proxy CIDR per environment. Then read `HttpContext.Connection.RemoteIpAddress` as planned — it will be correct after the forwarded-headers middleware has rewritten it. Do **not** read `X-Forwarded-For` directly in `TranslationRateLimitMiddleware`.

**Implementation task**: "Register `IRateLimitStore`, `RateLimitOptions`, and `TranslationRateLimitMiddleware` in `Program.cs`" annotated with SEC-001/004 in `tasks.md`.

---

### SEC-002: Middleware Ordering Has No Enforcement Guard

**Severity**: Medium
**Component**: `TranslationRateLimitMiddleware` registration in `Program.cs`

The middleware must run after `UseAuthentication()` + `UseAuthorization()` so `HttpContext.User` is populated. If it is accidentally placed before authentication (e.g., during a future refactor), the middleware silently classifies all requests as anonymous. No exception is thrown; the 5/hr auth quota and Owner/Admin bypass are silently lost.

**Remediation**: In `TranslationRateLimitMiddleware`'s constructor or in a `UseTranslationRateLimiting()` extension method, assert that `IAuthenticationSchemeProvider` is registered in DI. This is a startup-time check; if the guard fires the service fails fast with a descriptive message.

**Implementation task**: "Implement `TranslationRateLimitMiddleware`" annotated with SEC-002 in `tasks.md`.

---

### SEC-003: Exhausted-Identity Requests Bleed the Global Pool

**Severity**: Medium
**Component**: Inbound middleware evaluation order
**Status**: RESOLVED

A caller whose personal quota is exhausted still consumes one global token per request before being rejected. Detailed decision recorded in `research.md` Identified Risks table: accepted as known limitation for v1.

---

### SEC-005: `TryConsumeAsync` Exceptions Not Covered by Fail-Closed Policy

**Severity**: Medium
**Component**: `TranslationRateLimitMiddleware` error handling

ADR 0002 defines fail-closed as: "if `IsAvailableAsync` returns false or throws → 429". The plan does not specify what happens if `TryConsumeAsync` itself throws (e.g., `OperationCanceledException`, `ObjectDisposedException` on shutdown). An unhandled exception propagates as HTTP 500, not 429, and budget protection is lost for that request.

**Remediation**: Wrap the entire middleware body (from `IsAvailableAsync` through both `TryConsumeAsync` calls) in a `try/catch(Exception)` block. On any exception, log the full error context and return HTTP 429 with `X-RateLimit-Error: store-unavailable` — identical to the `IsAvailableAsync = false` path.

**Implementation task**: "Implement `TranslationRateLimitMiddleware`" annotated with SEC-005 in `tasks.md`.

---

### SEC-006: `X-RateLimit-Error` Header Information Disclosure (Accepted)

**Severity**: Low
**Component**: `RateLimitResponseWriter`

The distinct header values (`store-unavailable`, `global-limit-exceeded`, `per-identity-limit-exceeded`) let an attacker probe global pool status. At current scale this is low-signal information. Accepted as a deliberate trade-off for operational visibility.

---

## Security Requirements

Before merging the implementation:

- [ ] **SEC-001/004**: `UseForwardedHeaders()` registered in `Program.cs` before `UseAuthentication()`, with `KnownNetworks`/`KnownProxies` set per environment.
- [ ] **SEC-002**: Startup guard in `TranslationRateLimitMiddleware` verifies authentication middleware is registered.
- [ ] **SEC-003**: Decision recorded in `research.md` (global token bleed accepted for v1).
- [ ] **SEC-005**: `TranslationRateLimitMiddleware` wraps all store calls in a `try/catch(Exception)` and returns 429 `store-unavailable` on any exception.

## ADR Decisions with Security Implications

| ADR | Decision | Implication | Recommendation |
|-----|----------|-------------|----------------|
| ADR 0001 | In-memory store | No cross-instance consistency. Correct for single instance; per-identity limits fragment silently if scaled horizontally without Redis. | Document pod-count constraint; enforce before enabling horizontal scaling. |
| ADR 0002 | Fail-closed | Correct for budget protection. | Ensure ALL exceptions in the store path are covered, not just `IsAvailableAsync = false` (SEC-005). |
| ADR 0003 | Token bucket | Burst allowance equals the full quota at current limits. Not exploitable. | Acceptable. |
## Reasoning Log

**Discarded threats**

| Threat | Component | Reasoning |
|--------|-----------|-----------|
| JWT `sub` claim forgery | JWT validation | `ValidateIssuerSigningKey = true` with symmetric key. Forgery requires the server signing secret. |
| Owner/Admin role claim injection | `ClaimsPrincipal` | Role claims are inside the signed JWT body. Cannot be injected without the signing key. |
| Token bucket burst exploitation | `InMemoryRateLimitStore` | At 1 anon / 5 auth quota, a full burst equals the entire quota — no amplification beyond the configured limit. |
| `X-Forwarded-For` forge from browser | CORS | CORS policy restricts allowed origins and requires credentials; a browser attacker cannot set arbitrary request headers cross-origin. Does not apply to non-browser clients (hence SEC-001/004 remain findings). |
| `global:anon` / `global:auth` key collision with user IDs | `IRateLimitStore` | User IDs are GUIDs from the JWT `sub` claim. Fixed keys use a colon-prefixed namespace; GUIDs never contain colons. |