# TaskFlow API tests

Tests follow the application's feature folders so a behavior and its regression
coverage can be reviewed together:

- `Features/Authentication/`, `Features/Users/`, `Features/Workspaces/`,
  `Features/Boards/`, and `Features/Tasks/` contain the corresponding API scenarios,
  authorization rules, and concurrency regressions.
- `Infrastructure/` groups authentication primitives, caching, persistence,
  routing, health checks, and seed configuration tests.
- `Support/` contains the shared collection, application hosts, scenario helpers,
  and test logging. It does not contain feature tests.

Namespaces match these folders. Keep a scenario that exercises several features
with the behavior it protects, such as workspace membership authorization or
board cache invalidation. Split a suite when its tests belong to unrelated
features. Shared infrastructure remains in `Support/`.

## Shared test infrastructure

Integration tests should join `TaskFlowApiCollection` and derive from
`IntegrationTestBase`. The collection starts one production-parity PostgreSQL 18
container and one Redis 8 container. The environment builds a migrated, seeded
PostgreSQL template once, then clones a private database and starts a private
`WebApplicationFactory` for every test. This keeps data, dependency state, and
in-memory rate limiters isolated without starting containers or running PBKDF2
seeding for every test.

Pass `authenticateAsAdmin: true` to mint a real application JWT for the seeded
admin without exercising the intentionally slow login endpoint. Authentication
tests should still call `/auth/login` directly. `CreateUserScenarioAsync` can
arrange users and personal workspaces directly for tests that are not testing
registration; registration tests should continue to use `/auth/register`.

Board scenarios must select a board explicitly: create it through
`/api/workspaces/{workspaceId}/boards`, use the `BoardId` returned by
`CreateUserScenarioAsync(..., createPersonalWorkspace: true)`, or select a named
seeded board from the workspace's board list. Exercise board reads, task creation,
stats, and activity through `/api/boards/{boardId}` and task mutations through
`/api/tasks/{taskId}`. Helpers must not infer a global default board.

Pass `useRedis: true` for cache tests. Redis is flushed before each such test.
Pass `startFromEmptyDatabase: true` only when a test must prove first-start
migration and seeding behavior; ordinary tests should use the fast template clone.

Dependency outage tests use `IsolatedPostgresApplication` to pause and recover a
dedicated PostgreSQL container. Never pause the collection container: it owns the
template database used by every other integration test.

All integration tests use the collection/base class so they receive strict
per-test isolation and shared container lifecycle. Application logs are attached
to xUnit output, and container logs are written beneath the system temporary
directory if infrastructure or application startup fails.
