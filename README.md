# TaskFlow

The companion app for **Pragmatic Self-Hosting: Foundations**. It is an
approachable workspace task manager — React frontend, .NET minimal API,
Postgres, and a Redis cache — that every student deploys to their own server.
The course is about the deployment flow, but the fixture is still a useful
production-shaped reference: vertical feature slices, workspace tenancy,
role-scoped boards, a durable database, a disposable cache, and health checks.

> **Disposable fixture:** TaskFlow has local registration, self-contained JWT
> sessions, workspace roles, multiple boards, and user profile management. It
> deliberately has no verified-email delivery, password-recovery channel, MFA,
> or external identity provider. Never put personal, customer, or otherwise
> valuable data in it.

By the end of the course this repo runs in production on one VPS with
HTTPS, private SSH and Dokploy access through **Tailscale**, automatic deployments,
scheduled database backups with a verified scratch restore, and external alerts.
Once the deployment contract works, you can adapt TaskFlow's five deployment
requirements to your own app (see below).

## Repeat the work with your agent

The course teaches the decisions and verification steps. The included seven skills
let your agent repeat preflight, private-access checks, deployment, release verification,
diagnosis, scratch restores, and maintenance using the same operational contract.
Read [the workflow guide](ops/agent-workflows.md), prepare your local environment
context from [the template](ops/agent-context.example.md), and invoke a skill with
a concrete task. Credentials stay in your existing secret store. Readiness and a
matching release SHA are observed results, never assumptions in the context file.
This path is optional; the manual course remains complete.

## Run it locally

One command from a clean clone (you only need Docker):

```bash
docker compose -f docker-compose.dev.yml up --build
```

Open http://localhost:8080. You get the app, a seeded course workspace and
deployment board, Postgres, and Redis — the same shape as production. Log in
as the throwaway seeded owner with **admin / taskflow-dev-password**, or
register another throwaway user to create an isolated personal workspace.

The interface uses Tailwind CSS with light, dark, and system appearance modes.
Use the sun, moon, and monitor buttons in the top-right corner. Your choice is
saved in this browser and applies to the login screen, boards, and settings.

Boards offer **Compact** and **Detailed** card views, also saved in this browser.
Compact cards hide descriptions and tighten spacing while keeping priorities,
due dates, assignees, and task controls. Open a card to read or edit its full details.
Use a card's grip to drag it between Todo, Doing, and Done. On touch screens,
hold the grip briefly before dragging. With a keyboard, focus the grip and press
Space, use the arrow keys, then Space to drop or Escape to cancel. The move
buttons remain available in both views. The dragged card follows your pointer
while the other cards slide apart to show its position. Drop between any two
cards, at either end, or into an empty column. Escape or dropping outside the
board cancels the preview. The order saves when you release and survives a page
refresh. If saving fails, the original order returns. Archived boards stay read-only.

<details>
<summary>Inner dev loop (hot reload, only if you want to change the app)</summary>

From the repository root, use these three local PowerShell terminals. Start only
the dependencies here so the containerized app does not compete for port 8080.

```powershell
# Terminal 1: Postgres and Redis
docker compose -f docker-compose.dev.yml up postgres redis

# Terminal 2: the API on :8080, with the optional cache enabled
cd src/TaskFlow.Api
$env:ConnectionStrings__Redis = "localhost:6379"
dotnet run

# Terminal 3: the frontend with hot reload (proxies API routes to the API)
cd src/TaskFlow.Web
npm ci
npm run dev
```

The frontend proxies API calls to `http://localhost:8080` by default. If the API
is on another local port, set `$env:TASKFLOW_API_URL = "http://127.0.0.1:8081"`
in terminal 3 before starting Vite. The `/auth` page stays in React while
`/auth/` API requests are forwarded to the backend.

If you omit the Redis connection, the API deliberately runs with cache `OFF`
and no recent activity feed. Use the full Compose path above for the course
rehearsal so it matches the three-service deployment.

</details>

### Running the tests

The inner development loop and test suites require the .NET 10 SDK, Node.js 24
with npm, and a running Docker daemon.

```bash
dotnet test TaskFlow.slnx
```

Docker needs to be running: the integration suite starts production-parity
PostgreSQL and Redis containers once, then gives every test a cloned private
database and a fresh application host. Migration tests still start from a truly
empty database. Nothing to install and nothing to clean up; Testcontainers
removes the services after the run. The CI pipeline runs the same command before
it builds the image. See `tests/TaskFlow.Api.Tests/README.md` for the fixture and
scenario-helper conventions.

## What's in the box

```
├── src/TaskFlow.Api/        .NET 10 minimal API + EF Core, organized by feature slice
├── src/TaskFlow.Web/        React + Vite + Tailwind CSS, with feature and shared UI folders
├── tests/                   API, tenancy, cache, and health regressions against real services
├── Dockerfile               multi-stage build → one runtime image, non-root
├── docker-compose.dev.yml   local: builds from source (+ Postgres + Redis)
├── docker-compose.prod.yml  server: pulls the CI-built image (+ Postgres + Redis)
├── .github/workflows/       tests/lint → one build → smoke → GHCR → private Dokploy API
└── ops/                     backup-restore drill + production checklist
```

In production the API serves the built React app from `wwwroot` — one
container, one origin, one domain, one TLS certificate.

The API keeps domain entities in `Domain/`, one use case per folder in
`Features/`, and persistence, authentication, caching, and other
technical helpers in `Infrastructure/`. Classes and endpoint contracts have
their own files; concrete helpers such as `PasswordHasher` stay concrete when
an interface would add no useful boundary. The React app follows the same
feature-oriented shape and consolidates styling through Tailwind CSS and the
components in `src/TaskFlow.Web/src/shared/`.

## The deployment-facing parts

These are the pieces the course is actually about:

| Piece | Where | Why it exists |
|---|---|---|
| Identity and sessions | `Features/Authentication/`, `Features/Users/`, `Infrastructure/Authentication/` | Registration and login issue an 8-hour HS256 session in an HttpOnly cookie; the same response also exposes a bearer token for the course's curl probes, but the browser never stores it. Login, registration, and password changes have separate rate limits; passwords use PBKDF2. Profile edits, password changes, logout, and token-version checks provide a compact account lifecycle |
| Workspace authorization | `Features/Workspaces/`, `Features/Boards/`, `Features/Tasks/` | Every board and task is reached through a workspace membership. Owner/Admin/Member permissions and tenant-scoped queries prevent one workspace from reading or mutating another; transaction-scoped PostgreSQL locks preserve owner, membership, assignment, and archive invariants during concurrent writes |
| `GET /health/live` | API | "Is the process up?" — no dependencies checked |
| `GET /health/ready` | API | "Can it serve traffic?" — verifies the DB connection; compose healthchecks hit this |
| `RUN_MIGRATIONS=true` | env var | Applies EF migrations on startup and logs what happened; flip off to run migrations as a deploy step instead |
| Redis cache-aside | `Infrastructure/Caching/BoardCache.cs` | Board and stats reads report `X-Cache: HIT/MISS/OFF/DOWN`. Keys include the board ID, and generation-checked fills prevent stale repopulation after writes. A dead cache degrades to Postgres reads rather than an outage |
| Activity feed | `GET /api/boards/{boardId}/activity` | Lives only in Redis, scoped by board. This ephemeral state is not part of the backup surface |
| Stats | `GET /api/boards/{boardId}/stats` | Aggregates done-today and oldest-active values for the selected board. Cached for 30 seconds |
| Application logs | Container stdout, viewed with `docker logs` | Startup and request logs support diagnosis alongside health checks; Compose rotates the container logs |
| Seed data | `Infrastructure/Seeding/SeedData.cs` | Ensures the configured course owner and initial workspace exist. Creates the deployment board and 10 tasks only when that workspace has no boards. Existing board data is preserved |
| Version marker + accent color | label passed from `src/TaskFlow.Web/src/App.tsx` into `src/TaskFlow.Web/src/app/DeploymentFooter.tsx`, plus the Tailwind theme/CSS | The auto-deploy demo: edit, push, watch it ship |
| Commit SHA in the footer | `VITE_GIT_SHA` build arg (Dockerfile + workflow) | CI bakes the commit SHA into the build; the footer tells you exactly which commit is live |
| Full SHA receipt | `GET /version.json` | Machine-readable artifact identity used by CI to prove the requested deploy actually became ready |

### Configuration

The image runs with these environment variables:

```
ConnectionStrings__Db      Host=postgres;Port=5432;Database=taskflow;Username=taskflow;Password=...
ConnectionStrings__Redis   redis:6379   (optional — omit it and the cache is simply off)
RUN_MIGRATIONS             true | false
ASPNETCORE_URLS            http://+:8080   (already the image default)
Auth__JwtSecret            signs the login tokens; 32+ characters, startup-validated
Auth__AdminUsername        username for the seeded course owner (default: admin)
Auth__AdminPassword        seeded owner password, hashed and stored on first boot only
```

Production secrets are set in the Dokploy UI, outside source control; Dokploy
stores them on the control-plane host and injects the selected values into the
containers. Root, Docker, and Dokploy administrators can read them. See
`.env.example` and `docker-compose.prod.yml`. There are no default passwords:
the stack refuses to start if `DB_PASSWORD`, `JWT_SECRET`, or `ADMIN_PASSWORD`
is empty or absent.

Four identity operations worth knowing:

Keep `ADMIN_USERNAME` and the seed email fixed after first boot. They identify
the seed account. Renaming that configuration is not an account-rename operation
and can conflict with the persisted unique email. Change the display name in the
profile UI. Rehearse account recovery only with disposable data.

- **Rotate the JWT secret** — change `JWT_SECRET` and redeploy. Every
  outstanding token stops verifying, so everyone is signed out; nothing else
  breaks.
- **Change a password** — use the signed-in profile flow. It verifies the
  current password, increments the account's token version, invalidates older
  sessions, and issues a replacement session. `ADMIN_PASSWORD` is seed input
  only, so changing it in Dokploy does not modify the persisted course owner.
- **Log out everywhere** — the logout endpoint also increments the account's
  token version, so every outstanding cookie or bearer token is revoked. The
  fixture intentionally does not model separate remembered-device sessions.
- **Recover an account** — TaskFlow intentionally has no email-based recovery
  service. For this disposable fixture, reset it or restore a known-good fixture
  backup. Do not treat manual database edits as an account-recovery design for
  a real application.

Users, workspaces, boards, and tasks use random UUIDv4 identifiers, including
foreign keys, API responses, URLs, and JWT subjects. IDs make enumeration harder;
workspace membership and role checks still enforce access to every resource.

The course uses one `InitialCreate` migration. With `RUN_MIGRATIONS=true`, it
creates native PostgreSQL UUID primary and foreign keys in an empty database,
then seeds the course workspace once. The history table contains one row,
`20260829184834_InitialCreate`. Restarting checks the same baseline and skips the
existing seed. Both course v1/v2 images use this schema.

For development or practice with an older course database, prepare a separate,
fresh disposable database for this baseline. This is not an in-place upgrade.
Keep the old data and matching image if they are still needed. Do not delete an
existing volume or rewrite its migration ledger to make it match. Real systems
require new forward migrations and a tested data-preserving upgrade plan.

Compose also requires `IMAGE_REPOSITORY`, copied exactly from CI's lowercase
`ghcr.io/<owner>/<repository>` output. `IMAGE_TAG` selects `latest` normally or
a previously published full-SHA tag during rollback.

## How a deploy works

```
git push → tests + lint → build once → smoke exact SHA image → push to GHCR
                                                                  ↓
VPS pulls + replaces ← private Dokploy Compose API ← ephemeral tailnet CI runner
```

The server never builds anything. CI pushes the same local image it smoke-tested,
under `:latest` and a full commit-SHA tag. The workflow never overwrites an
existing commit tag; a rerun pulls and reuses it, and records the immutable
registry digest in the job summary. A short-lived `tag:ci` GitHub runner
joins the tailnet and calls Dokploy's authenticated API; port 3000 remains
private. After Dokploy accepts the request, CI waits until readiness and
`/version.json` prove the full expected SHA. The whole pipeline is one heavily commented file:
[.github/workflows/deploy.yml](.github/workflows/deploy.yml).

### Rolling back a bad deploy

The footer identifies the running commit. Two supported choices are:

1. **`git revert` (normal path).** Revert the bad change and push. CI creates a
   new audited release from the corrected history.

   ```bash
   git revert HEAD && git push
   ```

2. **Commit-artifact rollback (incident path).** In Dokploy, set `IMAGE_TAG` to
   the full last-good SHA and redeploy. Verify the footer and recorded registry
   digest, then investigate.
   Restore `IMAGE_TAG=latest` only after the release channel is good again.

An image rollback does **not** undo a database migration. Keep migrations
backward-compatible (expand, deploy, then contract later); use the tested
database recovery path for data loss, not an older application image.

TaskFlow also uses its Postgres initialization owner for runtime access and
startup migrations. That keeps this disposable, single-instance teaching
fixture understandable; a real application should use a restricted runtime
role and a separate serialized migration/admin role.

## Swapping in your own app

The server and private control plane carry over. Your app must deliberately
satisfy the course's five-part deployment contract:

1. Build a small, non-root container on a declared internal port.
2. Expose separate process liveness and dependency-aware readiness checks.
3. Build once in CI; publish a moving release tag, a non-overwritten full-SHA tag,
   and record the immutable registry digest.
4. Define runtime configuration, secrets, and a migration/release strategy.
5. Inventory every durable write (database, uploads, queues, volumes), back it
   up off-site, and prove each restore path.

That normally changes the Dockerfile, Compose file, workflow, health code, and
operations docs. TLS and the hardened host remain reusable; backups do not
magically carry over when an app's durable state changes.

## Troubleshooting

- **Port 8080, 5432 or 6379 already in use locally** — override the host ports:
  `APP_PORT=8081 DB_PORT=15432 REDIS_PORT=16379 docker compose -f docker-compose.dev.yml up`
  (PowerShell: `$env:APP_PORT='8081'; docker compose ...`).
- **Dokploy can't pull the image (`denied` / `manifest unknown`)** — GHCR
  packages are **private by default**. Keep the package private and add a
  package-read credential in Dokploy → Registries, then run the registry test.
  Making this disposable fixture public is an optional simplification, not the
  course's primary path.
- **ARM server (Hetzner CAX, Raspberry Pi, …)** — the supported course path is
  x86/`linux/amd64`. A multi-platform adaptation must build a manifest and
  smoke-test each platform's exact artifact on a matching or emulated runner;
  do not delete the central artifact gate merely to make an ARM build pass.
- **The UI shows `cache DOWN` / "Activity feed offline"** — the Redis
  container is stopped or unreachable. The board keeps working from Postgres
  by design; check `sudo docker ps` and the redis container's logs. Readiness
  stays green on purpose: a degraded cache must not page anyone.
- **App container unhealthy right after deploy** — check
  `sudo docker logs <app-container>`. An unhealthy status is a diagnostic
  signal; Docker restart policies react to process exit, not health alone. If it's a connection string problem, the
  startup log tells you; if migrations are off, you'll see
  `RUN_MIGRATIONS is not 'true' — skipping database migrations.`

## License

MIT — do whatever you like with it.
