# Rate Limiting: Task Decomposition

**Feature**: API Rate Limiting  
**Date**: 2026-04-14  
**Spec**: `docs/features/rate-limiting/spec.md`  
**Plan**: `docs/features/rate-limiting/plan.md`  
**Security Review**: `docs/features/rate-limiting/security-review-architecture.md` — four open findings (SEC-001/002/004/005); see annotated tasks below

## Phase 1 — Setup

- [x] Create project folder structure `src/LearnLuxembourgish.Api/RateLimiting/`

## Phase 2 — Foundational (blocking prerequisites)

- [x] [P] Create `IRateLimitStore` interface with `TryConsumeAsync` and `IsAvailableAsync` in `src/LearnLuxembourgish.Api/RateLimiting/IRateLimitStore.cs`
- [x] [P] Create `TokenBucketResult` record in `src/LearnLuxembourgish.Api/RateLimiting/TokenBucketResult.cs`
- [x] [P] Create `RateLimitOptions` configuration POCO in `src/LearnLuxembourgish.Api/RateLimiting/RateLimitOptions.cs`

## Phase 3 — US1 + US2: Anonymous Rate Limiting

- [x] Implement `InMemoryRateLimitStore` (token bucket, `ConcurrentDictionary`, continuous refill) in `src/LearnLuxembourgish.Api/RateLimiting/InMemoryRateLimitStore.cs`
- [x] Write unit tests for `InMemoryRateLimitStore` in `tests/LearnLuxembourgish.Tests/RateLimiting/InMemoryRateLimitStoreTests.cs`
- [x] Implement `RateLimitResponseWriter` helper in `src/LearnLuxembourgish.Api/RateLimiting/RateLimitResponseWriter.cs`
- [x] Implement `TranslationRateLimitMiddleware` (store availability, identity resolution, global-before-per-identity, Owner/Admin bypass, response headers) in `src/LearnLuxembourgish.Api/RateLimiting/TranslationRateLimitMiddleware.cs` — **SEC-002**: constructor must assert `IAuthenticationSchemeProvider` is in DI (fail-fast if middleware is registered before `UseAuthentication`); **SEC-005**: wrap all store calls in `try/catch(Exception)` and return 429 `store-unavailable` on any exception
- [x] Register `IRateLimitStore`, `RateLimitOptions`, and `TranslationRateLimitMiddleware` in `src/LearnLuxembourgish.Api/Program.cs` — **SEC-001/004**: call `app.UseForwardedHeaders()` with `KnownProxies`/`KnownNetworks` restricted to the proxy CIDR **before** `app.UseAuthentication()`; do not read `X-Forwarded-For` directly in middleware
- [x] Add `RateLimiting` section with defaults to `src/LearnLuxembourgish.Api/appsettings.json`
- [x] Write unit tests for `TranslationRateLimitMiddleware` in `tests/LearnLuxembourgish.Tests/RateLimiting/TranslationRateLimitMiddlewareTests.cs`

## Phase 4 — US3 + US4: Authenticated Rate Limiting and Owner/Admin Bypass

- [x] Extend middleware tests to cover authenticated per-user quota (5 req/hr), global authenticated quota (50 req/hr), and Owner/Admin bypass in `tests/LearnLuxembourgish.Tests/RateLimiting/TranslationRateLimitMiddlewareTests.cs`

## Phase 5 — US5: Outbound LLM Budget

- [x] Create `IOutboundCallBudget` interface in `src/LearnLuxembourgish.Api/RateLimiting/IOutboundCallBudget.cs`
- [x] Implement `OutboundCallBudget` wrapping `IRateLimitStore` with key `outbound:llm` in `src/LearnLuxembourgish.Api/RateLimiting/OutboundCallBudget.cs`
- [x] Create `OutboundBudgetExceededException` in `src/LearnLuxembourgish.Api/RateLimiting/OutboundBudgetExceededException.cs`
- [x] Inject `IOutboundCallBudget` into `GrammarService` and call `TryConsumeAsync` before each LLM call in `src/LearnLuxembourgish.Api/Services/GrammarService.cs`
- [x] Catch `OutboundBudgetExceededException` in `TranslationsController` and return HTTP 429 in `src/LearnLuxembourgish.Api/Controllers/TranslationsController.cs`
- [x] Register `IOutboundCallBudget` / `OutboundCallBudget` in `src/LearnLuxembourgish.Api/Program.cs`
- [x] Write unit tests for outbound budget in `tests/LearnLuxembourgish.Tests/RateLimiting/OutboundCallBudgetTests.cs`

## Phase 6 — Polish (integration tests)

- [x] Write integration tests covering all 11 spec acceptance scenarios in `tests/LearnLuxembourgish.Tests/RateLimiting/RateLimitIntegrationTests.cs`
