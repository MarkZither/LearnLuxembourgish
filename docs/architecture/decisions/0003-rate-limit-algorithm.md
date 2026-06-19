# Rate Limit Algorithm

**Status**: Proposed
**Date**: 2026-04-14

## Context

The rate limiting feature must enforce per-identity and global request quotas over a 60-minute window. The choice of algorithm determines how the quota is measured, when it resets, and how boundary conditions at window edges behave.

The spec specifies a "sliding window of 60 minutes" as the desired UX behavior — callers should not experience abrupt quota resets at fixed clock boundaries (e.g., every caller's quota resetting simultaneously at the top of each hour).

The three most commonly used algorithms are: fixed window counter, token bucket, and sliding window log.

## Priorities and Requirements (ordered)

1. **Predictable user experience** — Callers must not experience sudden quota resets at deterministic clock boundaries. A user who exhausted their quota at 10:59 should not immediately get a full quota at 11:00.
2. **Resistance to boundary exploitation** — At the boundary between two fixed windows, a caller must not be able to send double their quota by timing requests to straddle the reset.
3. **Implementation simplicity** — The algorithm will be implemented in a custom `IRateLimitStore` whose backing store will migrate to Redis. The algorithm must be expressible as simple arithmetic that translates directly to both in-memory and Redis Lua script implementations.
4. **Per-entry memory footprint** — The state stored per counter key must be bounded and small. High-cardinality keys (one per IP) require compact per-entry state.

## Options Considered

### Option 1: Fixed window counter

Divide time into fixed-duration buckets (e.g., 10:00–11:00, 11:00–12:00). Each bucket holds a counter per identity key. When the window expires, the counter resets to zero. The bucket boundary is derived from `floor(utcNow / window)`.

**Evaluation against priorities**:

- **Predictable user experience**: Window resets are abrupt and synchronised to clock time. All counters reset simultaneously, which creates a thundering herd at window boundaries and can confuse callers whose request was accepted at 10:59:59 but whose quota resets at 11:00:00.
- **Resistance to boundary exploitation**: A caller can send up to 2x their quota by splitting requests: first half in the last seconds of window N, second half at the start of window N+1. This is the well-known fixed window double-spend problem.
- **Implementation simplicity**: Very simple — a hash map from `(key, window_id)` to counter. No timestamps beyond the window ID.
- **Per-entry memory footprint**: Minimal. One integer per key per window.

### Option 2: Token bucket

Each counter key maintains a bucket of tokens with a maximum capacity equal to the quota (`maxTokens`) and a continuous refill rate of `maxTokens / window`. On each request, the system computes how many tokens have accrued since the last interaction (using elapsed time), adds them to the bucket (capped at `maxTokens`), then attempts to deduct one token. If the bucket has at least one token, the request is allowed; otherwise it is rejected.

State per entry: `(double tokensRemaining, DateTimeOffset lastInteractionTime)`. This is two values, regardless of request history length.

**Evaluation against priorities**:

- **Predictable user experience**: Refill is continuous and relative to each key's own interaction history, not to a shared clock boundary. A user whose bucket emptied at 10:59 will have tokens again 60 minutes after that point (11:59), not at a fixed hour. This matches the spec's "sliding window" intent.
- **Resistance to boundary exploitation**: No fixed boundary exists to exploit. The boundary is per-key and based on the time of each request.
- **Implementation simplicity**: Two values per entry and arithmetic involving elapsed time. Directly expressible in a Redis Lua script (available via `redis.call('TIME')` for current time).
- **Per-entry memory footprint**: Two values per key (double + timestamp). Compact.

**Trade-off**: Token bucket allows short bursts. With `maxTokens = 5` (authenticated tier), a user could send all 5 requests in rapid succession if the bucket was full. This is acceptable for this domain because: (a) the quota is already small, (b) the LLM call cost is the real constraint, and (c) burst granularity does not meaningfully change the cost bound.

### Option 3: Sliding window log

For each key, store the timestamps of all requests within the last window duration. On each request, remove timestamps older than `now - window`, count remaining entries, and reject if the count equals the quota.

**Evaluation against priorities**:

- **Predictable user experience**: True sliding window. Exactly consistent with the spec's description.
- **Resistance to boundary exploitation**: No boundary exists.
- **Implementation simplicity**: Requires a sorted list or ring buffer per key. In Redis, this is a sorted set with `ZREMRANGEBYSCORE` + `ZCARD`; this is more complex than the token bucket Lua script.
- **Per-entry memory footprint**: Grows linearly with request count per window per key. For an authenticated user with 5 req/hour, this is 5 timestamps. Acceptable but larger than token bucket.

**Limitation**: Memory per key is proportional to quota size; for high-quota future tiers or the global counters (50 entries), this grows. Token bucket stores two fixed values regardless of quota.

## Decision

Token bucket (Option 2).

Token bucket satisfies the primary UX requirement (no fixed boundary resets) and is resistant to boundary exploitation. Its per-entry state of two values is more compact than the sliding window log and is directly expressible as both in-memory arithmetic and a Redis Lua script, which satisfies the migration path requirement from ADR 0001.

The burst allowance inherent in token bucket is not a concern at the quota levels defined in the spec (max 5 per-identity, 50 global). If future subscription tiers introduce larger quotas where burst control matters, the token bucket can be extended with a burst multiplier parameter without changing the interface.

Fixed window was discarded primarily because of the double-spend exploit at window boundaries, which directly undermines the cost-protection goal of the feature.

## Implementation Notes

The token bucket entry state is:

```
struct TokenBucketEntry
{
    double TokensRemaining;
    DateTimeOffset LastInteractionAt;
}
```

The refill computation on each `TryConsume` call:

```
elapsed = now - entry.LastInteractionAt
refillRate = maxTokens / window.TotalSeconds   // tokens per second
accrued = min(elapsed.TotalSeconds * refillRate, maxTokens - entry.TokensRemaining)
entry.TokensRemaining += accrued
entry.LastInteractionAt = now

if entry.TokensRemaining >= 1:
    entry.TokensRemaining -= 1
    allowed = true
else:
    secondsToNextToken = (1 - entry.TokensRemaining) / refillRate
    resetAt = now + TimeSpan.FromSeconds(secondsToNextToken)
    allowed = false
```

For the in-memory implementation, the `ConcurrentDictionary` should use `AddOrUpdate` with a local lock per key (via a lightweight per-key `SemaphoreSlim` or `lock` on the entry object) to prevent lost-update races when two threads read and modify the same entry concurrently.

For the Redis implementation, this entire computation must be expressed as a single Lua script to preserve atomicity.

## References

- Feature spec: `docs/features/rate-limiting/spec.md` Assumptions section (sliding window), FR-001 through FR-004
- ADR 0001: Rate Limit Backing Store (defines the `IRateLimitStore` interface this algorithm operates through)
