# Feature Specification: API Rate Limiting

**Created on**: 2026-04-14  
**Status**: Draft

## Executive Summary

- **Objective**: Protect the translation endpoint from abuse and prevent unexpected LLM API cost overruns by enforcing tiered inbound and outbound rate limits
- **Primary user**: All API consumers — anonymous visitors, authenticated users, and the owner/admin
- **Value delivered**: Bounds external LLM API spending at a predictable level while providing a fair, transparent request quota to each caller tier
- **Scope**: Inbound rate limiting on `POST /api/translations` only; outbound call limiting to Groq/Mistral LLM APIs; excludes all other endpoints, subscription tier implementation, and IP blocking
- **Primary success criterion**: LLM API costs remain within defined budget thresholds regardless of traffic volume or user behaviour

## Non-Scope

- Rate limiting on any endpoint other than `POST /api/translations`
- Subscription tier implementation (noted as a future concern; see [FR-010](#functional-requirements))
- IP banning, blocking, or deny-listing
- Rate limiting applied at the Blazor Web frontend or MAUI mobile app layer
- User-facing quota management UI (querying or resetting one's own quota)
- Adaptive or dynamic rate limiting based on real-time cost signals

## Assumptions

- Unauthenticated caller identity is determined by IP address; session cookies are not used as the primary discriminator
- The rate limit window is a **sliding window** of 60 minutes (more predictable user experience than a fixed-clock window)
- "Owner/admin" is an existing role or claim in the authentication system and does not need to be created as part of this feature
- When the global limit for a tier is reached, all subsequent callers in that tier receive HTTP 429 regardless of their individual quota status
- Outbound call limits to Groq/Mistral represent an independent safety net; they do not grant or revoke inbound request budget
- The outbound limit applies to the total number of LLM calls within a configurable window, regardless of which user triggered each call
- Rate limit thresholds are configurable values, not hardcoded constants

## User Scenarios & Tests

### User Story 1 — Anonymous User Makes First Translation Request (Priority: P1)

An anonymous visitor submits a translation request for the first time within a 1-hour window. The request is accepted and they see how many requests they have remaining.

**Why this priority**: This is the baseline happy path for the most common caller type and directly validates the core protection mechanism.

**Independent Test**: Submit a single `POST /api/translations` from a new IP address; verify HTTP 200 and that rate limit headers indicate 0 remaining requests.

**Acceptance Scenarios**:

1. **Given** an unauthenticated caller whose IP has made no requests in the current window, **When** they submit `POST /api/translations`, **Then** the response is HTTP 200 and includes `X-RateLimit-Limit: 1`, `X-RateLimit-Remaining: 0`, and a `X-RateLimit-Reset` header indicating the window expiry
2. **Given** the same caller submits a second request within the same window, **When** the request reaches the API, **Then** the response is HTTP 429 with a `Retry-After` header and a human-readable message explaining the limit and the benefit of creating an account

---

### User Story 2 — Global Unauthenticated Limit Protects Against Distributed Abuse (Priority: P1)

Even if many different IP addresses each make one request per hour, the system stops accepting unauthenticated translation requests once the global cap of 10 per hour is reached.

**Why this priority**: Distributed abuse from many IPs could exhaust LLM budget even if each IP stays within its individual quota. This is the primary cost-protection mechanism.

**Independent Test**: Simulate 11 requests from 11 distinct IP addresses within a 1-hour window; verify the 11th request receives HTTP 429 citing the global limit.

**Acceptance Scenarios**:

1. **Given** 10 unauthenticated requests have already been processed globally within the current window, **When** a new IP address (which has made no prior request) submits `POST /api/translations`, **Then** the response is HTTP 429 indicating global capacity is exhausted
2. **Given** the global window resets, **When** a new unauthenticated request arrives, **Then** the request is accepted normally

---

### User Story 3 — Authenticated User Uses Their Per-User Quota (Priority: P2)

A logged-in user can make up to 5 translation requests per hour, with each response informing them of their remaining budget.

**Why this priority**: Authenticated access is the product's intended usage mode; its rate limits directly shape the user experience and monetisation foundation.

**Independent Test**: Authenticate as a test user, submit 5 requests, verify all succeed; submit a 6th, verify HTTP 429.

**Acceptance Scenarios**:

1. **Given** an authenticated user has made 4 requests in the current window, **When** they submit their 5th request, **Then** the response is HTTP 200 with `X-RateLimit-Remaining: 0`
2. **Given** the same user submits a 6th request within the same window, **Then** the response is HTTP 429 with `Retry-After` header
3. **Given** the global authenticated limit of 50 per hour has been reached, **When** a different authenticated user who has used 0 of their personal quota submits a request, **Then** the response is HTTP 429

---

### User Story 4 — Owner/Admin Has Unlimited Access (Priority: P2)

The owner/admin can submit translation requests without restriction, even when global limits are reached.

**Why this priority**: The developer must be able to test and operate the system freely; blocking the owner degrades operational utility.

**Independent Test**: Sign in as owner/admin, exhaust global authenticated limits with other accounts, confirm owner/admin still receives HTTP 200.

**Acceptance Scenarios**:

1. **Given** the global authenticated limit has been reached and individual user limits are exhausted, **When** the owner/admin submits `POST /api/translations`, **Then** the response is HTTP 200 with no rate limit headers (or headers indicating unlimited)
2. **Given** the owner/admin account, **When** any number of requests are submitted in any window, **Then** no HTTP 429 is ever returned due to rate limiting

---

### User Story 5 — Outbound LLM Call Limiter Prevents Cost Overruns (Priority: P2)

Regardless of how inbound requests are distributed across users, the total number of outbound calls to Groq/Mistral within a configured window is capped independently.

**Why this priority**: Inbound limits alone do not guarantee cost bounds if limits are misconfigured or bypassed; outbound limiting is the last line of defence.

**Independent Test**: Configure an outbound limit of N calls per hour; trigger N+1 inbound requests (via owner/admin if needed); verify the (N+1)th outbound call is suppressed and the caller receives an appropriate error.

**Acceptance Scenarios**:

1. **Given** the outbound LLM call limit has been reached within the current window, **When** an additional inbound translation request would trigger an LLM call, **Then** the request fails with HTTP 429 (or HTTP 503 with a distinct error code) and no outbound call is made
2. **Given** the outbound window resets, **When** a new translation request arrives, **Then** the outbound call is permitted normally

---

### Edge Cases

- What happens when a request arrives at the exact moment the window resets (boundary condition)?
- What happens if the rate limit state store is temporarily unavailable — does the system fail open (allow) or fail closed (deny)?
- What happens when an IPv6 address with a large prefix makes many requests across its subnet?
- How does the system handle a user whose authentication token expires mid-session — are they reclassified as unauthenticated?

### Failure Modes

- **Rate limit store unavailable**: If the backing store used to track counters is unreachable, the system must fail in a defined, consistent way (preferably fail open with logging, to avoid blocking legitimate users during infrastructure issues — [DEFERRED: fail-open vs fail-closed policy to be decided during planning])
- **Outbound LLM API timeout**: If Groq/Mistral does not respond within the configured timeout, the inbound request must not remain blocking; the outbound counter must still be incremented to prevent retry amplification
- **Clock skew**: Distributed instances must agree on the current window; inconsistent clocks can lead to limit bypasses or false rejections

## Requirements

### Functional Requirements

- **FR-001**: The system MUST enforce a per-IP rate limit of **1 request per 60-minute sliding window** for unauthenticated callers on `POST /api/translations`
- **FR-002**: The system MUST enforce a global rate limit of **10 requests per 60-minute sliding window** across all unauthenticated callers on `POST /api/translations`
- **FR-003**: The system MUST enforce a per-user rate limit of **5 requests per 60-minute sliding window** for authenticated callers on `POST /api/translations`
- **FR-004**: The system MUST enforce a global rate limit of **50 requests per 60-minute sliding window** across all authenticated callers on `POST /api/translations`
- **FR-005**: The system MUST exempt callers with the Owner/Admin role from all inbound rate limits on `POST /api/translations`
- **FR-006**: The system MUST include rate limit response headers (`X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset`) on all responses from `POST /api/translations`
- **FR-007**: The system MUST return HTTP 429 Too Many Requests with a `Retry-After` header and a human-readable error body when any inbound rate limit is exceeded
- **FR-008**: The system MUST enforce an independent outbound call limit to Groq/Mistral LLM APIs within a configurable time window, regardless of user tier
- **FR-009**: The system MUST NOT apply rate limiting logic to any endpoint other than `POST /api/translations`
- **FR-010**: The rate limiting system MUST be designed so that subscription tiers (e.g., Free, Pro, Premium) can be introduced as future policy sources without requiring changes to the limiting mechanism itself — the tier-to-policy mapping is a future concern and is explicitly out of scope for this feature
- **FR-011**: All rate limit thresholds MUST be configurable without redeployment
- **FR-012**: The system MUST evaluate the global limit before the per-identity limit; a caller rejected by the global limit MUST NOT consume their individual quota

### Key Entities

- **Rate Limit Policy**: Defines the request threshold, time window, and scope (per-identity or global) for a specific caller tier; future tiers are added by introducing new policies
- **Rate Limit Counter**: Tracks the number of requests consumed within the current sliding window for a given identity key (IP address, user ID, or a global scope key)
- **Caller Tier**: Classification of an API caller that determines which policies apply: `Unauthenticated`, `Authenticated`, `Owner/Admin`; reserved extension point: `Subscription` (future)
- **Outbound Call Budget**: A global counter tracking the number of LLM API calls made within the current outbound window, shared across all user tiers

## Success Criteria

### Measurable Outcomes

- **SC-001**: LLM API costs remain within the defined budget threshold under any realistic traffic scenario — the outbound call limiter prevents overruns even when inbound limits are misconfigured
- **SC-002**: Rate limit enforcement adds no perceptible latency to non-limited requests (limit check overhead is not user-visible)
- **SC-003**: Callers who are rate-limited receive a response that clearly communicates the remaining wait time and, for unauthenticated callers, the benefit of creating an account for higher limits
- **SC-004**: The implementation can accommodate the introduction of subscription-tier policies without changes to the core limiting mechanism
- **SC-005**: All rate limit thresholds can be adjusted in configuration and take effect without a service restart or redeployment

## Compliance Criteria

### Compliance Cases

| ID | Scenario | Input | Expected Output |
|----|----------|-------|-----------------|
| CC-001 | Anon user — first request | `POST /api/translations` from IP `1.2.3.4` with no prior requests this window | HTTP 200; `X-RateLimit-Limit: 1`, `X-RateLimit-Remaining: 0` |
| CC-002 | Anon user — second request | `POST /api/translations` from same IP `1.2.3.4` within the same window | HTTP 429; `Retry-After` header present; human-readable body referencing account creation |
| CC-003 | Anon global limit — new IP blocked | `POST /api/translations` from IP `9.9.9.9` (no prior requests) when 10 unauthenticated requests have already been processed globally this window | HTTP 429; individual IP quota is NOT consumed |
| CC-004 | Auth user — within quota | 3rd `POST /api/translations` from authenticated user `alice` (3 of 5 consumed) | HTTP 200; `X-RateLimit-Remaining: 2` |
| CC-005 | Auth user — quota exhausted | 6th `POST /api/translations` from `alice` within the same window | HTTP 429; `Retry-After` header present |
| CC-006 | Auth global limit — new user blocked | `POST /api/translations` from authenticated user `bob` (0 of 5 personal quota used) when 50 authenticated requests have already been processed globally this window | HTTP 429; `bob`'s personal counter is NOT incremented |
| CC-007 | Owner/admin — always allowed | `POST /api/translations` from owner/admin when global authenticated limit is exhausted | HTTP 200; no rate limit restriction applied |
| CC-008 | Outbound limit reached | Outbound call budget exhausted; inbound request arrives from owner/admin (who bypasses inbound limits) | HTTP 429 or HTTP 503; no outbound LLM call is made |
| CC-009 | Must NOT affect other endpoints | `POST /api/some-other-endpoint` from a caller that has exceeded the translation limit | Must NOT receive HTTP 429 due to rate limiting |
| CC-010 | Rate limit headers always present | Any `POST /api/translations` response (200 or 429) | Response MUST include `X-RateLimit-Limit`, `X-RateLimit-Remaining`, and `X-RateLimit-Reset` headers |

## Invariants

- The global limit for a tier is checked and decremented **before** the per-identity limit; a request rejected at the global level does not consume the caller's individual quota
- Owner/Admin callers bypass all inbound rate limit checks and never receive HTTP 429 due to rate limiting, even when all global limits are exhausted
- The outbound call budget is global and shared across all user tiers; it is not affected by user tier exemptions
- Rate limit counters accurately reflect the state **after** processing the current request; headers in a response are never stale relative to the outcome of that response
- A caller's individual quota is never decremented when the request is rejected due to a global limit being reached
