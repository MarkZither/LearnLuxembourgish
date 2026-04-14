# Rate Limit Backing Store

**Status**: Proposed
**Date**: 2026-04-14

## Context

The rate limiting feature requires a state store to track request counters per caller identity (IP address, user ID) and per scope (global unauthenticated, global authenticated). Each counter must support atomic increment-and-read, expiry, and retrieval of current token count.

The initial deployment runs as a single API instance. A future migration to multi-instance hosting (e.g., Azure Container Apps with horizontal scaling) would require a distributed backing store to prevent each instance from maintaining independent, uncoordinated counters.

## Priorities and Requirements (ordered)

1. **No new infrastructure dependencies for v1** — The first implementation must not require standing up Redis or any external service. Operational complexity grows with each required dependency; for a feature that is primarily about correctness and not throughput, the simplest store that works correctly on a single instance is the right starting point.
2. **Correct counter isolation** — Per-identity counters must not interfere with each other. Global counters must be consistent within a single process.
3. **Explicit Redis migration path** — When horizontal scaling becomes necessary, replacing the store must not require changes to the rate limiting middleware or policy logic. The store must be accessible through an interface.
4. **Read and write performance** — Counter checks and updates are on the hot path of `POST /api/translations`. Operations must complete in microseconds, not milliseconds.

## Options Considered

### Option 1: In-memory store behind IRateLimitStore interface

A custom `IRateLimitStore` interface isolates the backing store from the middleware. The initial implementation uses `ConcurrentDictionary<string, TokenBucketEntry>` with per-key locking for atomic check-and-consume operations. Entries expire naturally via the token bucket's time-based refill logic; no explicit eviction is needed for correctness.

**Evaluation against priorities**:

- **No new infrastructure dependencies**: No network dependency, no external service, no deployment configuration. Satisfied by design.
- **Correct counter isolation**: `ConcurrentDictionary` provides thread-safe per-key access. Lock scope can be narrowed to individual bucket operations. Correct for single-instance.
- **Redis migration path**: The `IRateLimitStore` interface is the exact seam. A `RedisRateLimitStore` implementation can be registered in `Program.cs` via configuration flag without touching middleware or policy code.
- **Performance**: In-process dictionary lookups are sub-microsecond. No serialisation overhead.

**Limitation**: Does not work correctly across multiple instances without a distributed store. Documented constraint, not a defect.

### Option 2: Redis (StackExchange.Redis with Lua scripts)

Redis provides atomic operations via Lua scripting, making it the canonical distributed rate limit store. StackExchange.Redis is the standard .NET client.

**Evaluation against priorities**:

- **No new infrastructure dependencies**: Requires a Redis instance. In development this means a Docker container; in production it means a managed service (e.g., Azure Cache for Redis). Adds infrastructure cost and operational overhead from day one.
- **Correct counter isolation**: Atomic Lua scripts guarantee consistency across instances. Correct for both single and multi-instance.
- **Redis migration path**: Not applicable — Redis is the initial choice, not the destination.
- **Performance**: Network round-trip to Redis adds 0.5–2 ms per request locally, 1–5 ms in a datacenter. Acceptable but slower than in-process.

**Limitation**: Unjustified infrastructure cost at current scale. The application does not yet run in a multi-instance configuration.

### Option 3: SQL database (EF Core)

Use the existing PostgreSQL or SQLite database to store rate limit counters in a dedicated table, with row-level locking or optimistic concurrency.

**Evaluation against priorities**:

- **No new infrastructure dependencies**: Reuses an existing dependency.
- **Correct counter isolation**: Achievable with row-level locking, but requires careful transaction design to avoid deadlocks or lost updates under concurrent requests.
- **Redis migration path**: Replaces the SQL store implementation with Redis when scaling is needed; interface stays the same.
- **Performance**: Database round-trips (even to SQLite) are 1–10 ms per request. Unnecessary overhead for a counter that lives only 60 minutes.

**Limitation**: High cardinality of counter keys (one per IP, one per user ID) causes table growth and index pressure. Relational databases are not optimised for this access pattern.

## Decision

In-memory store behind the `IRateLimitStore` interface (Option 1).

The current deployment is single-instance. The primary risk of in-memory rate limiting (counters not shared across instances) does not exist yet, and introducing Redis now adds infrastructure and operational cost without providing correctness benefits.

The `IRateLimitStore` interface is the deliberate migration seam: when horizontal scaling is introduced, a `RedisRateLimitStore` can be registered in place of `InMemoryRateLimitStore` without any changes to the middleware, policy resolution, or tests.

Entry memory growth is bounded: at most one entry per active IP or user ID within the refill window. For the expected traffic volume (tens of requests per hour), memory footprint is negligible.

## Implementation Notes

The `IRateLimitStore` interface must expose:

- `TryConsumeAsync(string key, int maxTokens, TimeSpan window, CancellationToken ct) -> (bool Allowed, int Remaining, DateTimeOffset ResetAt)`
- `IsAvailableAsync(CancellationToken ct) -> bool` — used by the fail-closed circuit breaker (see ADR 0002)

The in-memory implementation initialises entries lazily on first access. The `ResetAt` return value is derived from the token bucket's computed time-to-next-token, not from a fixed clock boundary.

When replacing the in-memory store with Redis, the Redis implementation must use a Lua script for the atomic check-and-consume operation to maintain correctness under concurrent requests from multiple instances.

## References

- Feature spec: `docs/features/rate-limiting/spec.md` FR-010, FR-011
- ADR 0002: Rate Limit Failure Policy (references this interface's `IsAvailableAsync`)
