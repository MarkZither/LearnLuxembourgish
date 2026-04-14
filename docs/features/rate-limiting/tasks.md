# Rate Limiting: Task Decomposition

**Feature**: API Rate Limiting  
**Date**: 2026-04-14  
**Spec**: `docs/features/rate-limiting/spec.md`  
**Plan**: `docs/features/rate-limiting/plan.md`

## Phase 1 — Setup

- [ ] Create project folder structure `src/LearnLuxembourgish.Api/RateLimiting/`

## Phase 2 — Foundational (blocking prerequisites)

- [ ] [P] Create `IRateLimitStore` interface with `TryConsumeAsync` and `IsAvailableAsync` in `src/LearnLuxembourgish.Api/RateLimiting/IRateLimitStore.cs`
- [ ] [P] Create `TokenBucketResult` record in `src/LearnLuxembourgish.Api/RateLimiting/TokenBucketResult.cs`
- [ ] [P] Create `RateLimitOptions` configuration POCO in `src/LearnLuxembourgish.Api/RateLimiting/RateLimitOptions.cs`

## Phase 3 — US1 + US2: Anonymous Rate Limiting

- [ ] Implement `InMemoryRateLimitStore` (token bucket, `ConcurrentDictionary`, continuous refill) in `src/LearnLuxembourgish.Api/RateLimiting/InMemoryRateLimitStore.cs`
- [ ] Write unit tests for `InMemoryRateLimitStore` in `tests/LearnLuxembourgish.Tests/RateLimiting/InMemoryRateLimitStoreTests.cs`
- [ ] Implement `RateLimitResponseWriter` helper in `src/LearnLuxembourgish.Api/RateLimiting/RateLimitResponseWriter.cs`
- [ ] Implement `TranslationRateLimitMiddleware` (store availability, identity resolution, global-before-per-identity, Owner/Admin bypass, response headers) in `src/LearnLuxembourgish.Api/RateLimiting/TranslationRateLimitMiddleware.cs`
- [ ] Register `IRateLimitStore`, `RateLimitOptions`, and `TranslationRateLimitMiddleware` in `src/LearnLuxembourgish.Api/Program.cs`
- [ ] Add `RateLimiting` section with defaults to `src/LearnLuxembourgish.Api/appsettings.json`
- [ ] Write unit tests for `TranslationRateLimitMiddleware` in `tests/LearnLuxembourgish.Tests/RateLimiting/TranslationRateLimitMiddlewareTests.cs`

## Phase 4 — US3 + US4: Authenticated Rate Limiting and Owner/Admin Bypass

- [ ] Extend middleware tests to cover authenticated per-user quota (5 req/hr), global authenticated quota (50 req/hr), and Owner/Admin bypass in `tests/LearnLuxembourgish.Tests/RateLimiting/TranslationRateLimitMiddlewareTests.cs`

## Phase 5 — US5: Outbound LLM Budget

- [ ] Create `IOutboundCallBudget` interface in `src/LearnLuxembourgish.Api/RateLimiting/IOutboundCallBudget.cs`
- [ ] Implement `OutboundCallBudget` wrapping `IRateLimitStore` with key `outbound:llm` in `src/LearnLuxembourgish.Api/RateLimiting/OutboundCallBudget.cs`
- [ ] Create `OutboundBudgetExceededException` in `src/LearnLuxembourgish.Api/RateLimiting/OutboundBudgetExceededException.cs`
- [ ] Inject `IOutboundCallBudget` into `GrammarService` and call `TryConsumeAsync` before each LLM call in `src/LearnLuxembourgish.Api/Services/GrammarService.cs`
- [ ] Catch `OutboundBudgetExceededException` in `TranslationsController` and return HTTP 429 in `src/LearnLuxembourgish.Api/Controllers/TranslationsController.cs`
- [ ] Register `IOutboundCallBudget` / `OutboundCallBudget` in `src/LearnLuxembourgish.Api/Program.cs`
- [ ] Write unit tests for outbound budget in `tests/LearnLuxembourgish.Tests/RateLimiting/OutboundCallBudgetTests.cs`

## Phase 6 — Polish (integration tests)

- [ ] Write integration tests covering all 11 spec acceptance scenarios in `tests/LearnLuxembourgish.Tests/RateLimiting/RateLimitIntegrationTests.cs`
