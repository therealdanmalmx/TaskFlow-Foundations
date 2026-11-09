# Backup and restore drill

The default drill restores a real off-site Dokploy backup into a scratch
database. It does not delete live data. Run the destructive variant only on the
explicitly disposable TaskFlow fixture and never copy it unchanged to a real
production system.

All server commands run as the `deploy` user after `ssh prod`. Docker access is
always explicit through `sudo docker`.

The backup surface is deliberately exactly one store: the TaskFlow Postgres
database (plus the separate Dokploy control-plane archive in section 7). The
Redis cache is excluded on purpose — it has no volume, persistence is off, and
the app treats it as optional; the activity feed it holds is disposable by
design. When adapting this application, inventory every additional durable store
and add its backup and restore procedure.

## 1. Configure the off-site backup

For an EU-jurisdiction R2 bucket, configure Dokploy under **Settings -> S3
Destinations -> Create** with endpoint
`https://<account-id>.eu.r2.cloudflarestorage.com` and region `auto`. A Cloudflare
location hint is only best-effort placement; selecting the EU jurisdiction is
the residency guarantee. Use an **Object Read & Write** token scoped only to
this bucket. That scope removes access to other buckets but can still delete
objects here, so add a seven-day, all-object bucket lock and keep enough daily
generations that normal expiry happens after the lock window.

Paste credentials with recording paused, show only masked fields and the
successful **Test** result, and rotate/retest if a key appears in a recording,
error detail, or copied support log.

In TaskFlow Compose -> **Backups -> Create Backup**, configure:

| Field | Value |
| --- | --- |
| Destination | bucket-scoped R2 destination |
| Database type | PostgreSQL |
| Service name | `postgres` |
| Database | `taskflow` |
| Database user | `taskflow` |
| Schedule | `0 3 * * *` |
| Prefix | `taskflow/postgres` (requested prefix, not a promised final key) |
| Keep Latest | `8` |
| Enabled | yes |

Do not trigger it yet. First record the source state and scheduler clock in the
next section.

## 2. Select the exact production project and database container

For this controlled disposable-fixture drill, pause other demo sessions and any
background writers. Keep the fixture free of writes from the source-count queries
until the test backup completes. The app can remain running. These separate queries
are not a transaction shared with the later dump. If a write occurs between them,
take a new source receipt and a new test backup rather than claiming an exact match.

```bash
ssh prod

sudo docker ps --format \
  'table {{.Names}}\t{{.Label "com.docker.compose.project"}}\t{{.Label "com.docker.compose.service"}}'

PROJECT=<exact-production-compose-project-label>
test "$PROJECT" != '<exact-production-compose-project-label>'

mapfile -t PG_CANDIDATES < <(sudo docker ps \
  --filter "label=com.docker.compose.project=$PROJECT" \
  --filter label=com.docker.compose.service=postgres \
  --format '{{.Names}}')

if (( ${#PG_CANDIDATES[@]} != 1 )); then
  printf 'ABORT: expected one production postgres container, found %s\n' \
    "${#PG_CANDIDATES[@]}" >&2
  printf '%s\n' "${PG_CANDIDATES[@]}" >&2
  exit 1
fi

PG=${PG_CANDIDATES[0]}
test "$(sudo docker inspect -f '{{ index .Config.Labels "com.docker.compose.project" }}' "$PG")" = "$PROJECT"
printf 'Production project: %s; Postgres container: %s\n' "$PROJECT" "$PG"

date -u '+source receipt: %FT%TZ'
sudo docker exec "$PG" psql \
  -U taskflow -d taskflow -v ON_ERROR_STOP=1 -AtF '|' -c \
  'SELECT status, count(*) FROM tasks GROUP BY status ORDER BY status;'

sudo docker exec "$PG" psql \
  -U taskflow -d taskflow -v ON_ERROR_STOP=1 -Atc \
  'SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY migration_id;'

DOKPLOY=$(sudo docker ps \
  --filter 'name=dokploy.1.' --format '{{.Names}}' | head -n 1)
test -n "$DOKPLOY"
sudo docker exec "$DOKPLOY" date '+scheduler: %Z %z'
```

Copy `PROJECT` from the production TaskFlow Preview Compose/deployment evidence
recorded in Module 6. Select both the project label and `postgres` service name
to identify the intended database. If this aborts, return to
Dokploy and identify the production project. Do not guess or use the first match.
Record the UTC timestamp, exact grouped counts, and migration IDs. The scheduler
proof must show `UTC +0000` before describing `0 3 * * *` as 03:00 UTC.

Now return to TaskFlow **Backups** and click **Test**. In R2, record the generated
object's actual full key, UTC timestamp, and non-zero size. Dokploy may prepend
generated Compose/service identity to the requested prefix, so never construct
or shorten the key in the recovery note. A daily cron is a near-24-hour RPO
target only while monitoring proves fresh objects continue to arrive.

After the object and its receipt are saved, normal fixture writes can resume.
The restore comparison uses the saved source receipt, not current live counts.
If that receipt is unavailable, report readability, schema and row evidence with
that limitation rather than claiming a full match.

Use that exact test object for the drill. Current Dokploy Postgres artifacts are
`*.sql.gz` files containing:

```text
pg_dump -Fc --no-acl --no-owner ... | gzip
```

The gzip layer matters: `pg_restore` consumes the decompressed custom-format
archive, not the `.sql.gz` bytes directly.

## 3. Safe default: restore through Dokploy into scratch

The scratch target is a separate database in the same PostgreSQL instance. It
shares disk, memory and CPU with the live database. Check resource headroom before
the drill. A different database name is a data-target boundary, not a separate
failure domain.

First prove the scratch target does not already exist, then create it:

```bash
SCRATCH_EXISTS=$(sudo docker exec "$PG" psql -U taskflow -d taskflow \
  -v ON_ERROR_STOP=1 -Atc \
  "SELECT datname FROM pg_database WHERE datname = 'taskflow_verify';") || {
  echo "ABORT: could not inspect the scratch target" >&2
  exit 1
}
if [[ -n "$SCRATCH_EXISTS" ]]; then
  echo "ABORT: taskflow_verify already exists. Preserve it until its purpose and replacement permission are known." >&2
  exit 1
fi
sudo docker exec "$PG" createdb \
  -U taskflow -O taskflow taskflow_verify || {
  echo "ABORT: scratch creation failed. Do not start the restore." >&2
  exit 1
}
date -u '+scratch restore started: %FT%TZ'
```

A SQL error is not proof of absence. If scratch exists, stop and identify its
purpose. Preserve unknown prior work. Only after the exact target is known to be
disposable and its replacement is explicitly authorized, remove it with
`sudo docker exec "$PG" dropdb -U taskflow taskflow_verify`, then repeat the guarded
creation above. A request to prepare a restore does not authorize this deletion.

In Dokploy, open TaskFlow Compose -> Backups -> Restore and select:

- the same R2 destination;
- the exact recorded `*.sql.gz` object key;
- database type `PostgreSQL`;
- service `postgres`;
- database user `taskflow`;
- target database `taskflow_verify`.

Start Restore and keep the log open until Dokploy reports success. The target
name is the safety boundary. Stop if the form says `taskflow` instead of
`taskflow_verify`.

Verify both schema and data:

```bash
set -Eeuo pipefail

TABLE=$(sudo docker exec "$PG" psql \
  -U taskflow -d taskflow_verify -v ON_ERROR_STOP=1 -Atqc \
  "SELECT to_regclass('public.tasks');")
ROWS=$(sudo docker exec "$PG" psql \
  -U taskflow -d taskflow_verify -v ON_ERROR_STOP=1 -Atqc \
  'SELECT count(*) FROM tasks;')

test "$TABLE" = "tasks"
test "$ROWS" -gt 0

sudo docker exec "$PG" psql \
  -U taskflow -d taskflow_verify -v ON_ERROR_STOP=1 -c \
  'SELECT status, count(*) FROM tasks GROUP BY status ORDER BY status;'

sudo docker exec "$PG" psql \
  -U taskflow -d taskflow_verify -v ON_ERROR_STOP=1 -c \
  'SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY migration_id;'

date -u '+scratch queries completed: %FT%TZ'
```

Compare grouped counts and migration IDs with the saved receipt from that exact
object. A nonzero count alone does not prove a match. These checks establish basic
restore integrity, not equality of every field in every table. Record the key,
timestamp, object size, counts, migration IDs and elapsed restore-test time.
Preserve a failed or mismatched restore for diagnosis. After successful verification
and retaining the receipt, remove only scratch created by this authorized drill:

```bash
sudo docker exec "$PG" dropdb -U taskflow taskflow_verify
```

Check live `/health/ready` and the board independently. Record whether the live
data stayed unchanged and the application stayed available. The shared instance
means a scratch operation can still create resource pressure. Do not describe
scratch restore-test time as service downtime or full-rebuild recovery time.

## 4. CLI fallback for the same gzip artifact

Prefer the current Dokploy Restore UI because it selects the stored destination
and generated format directly. If you need to validate the mechanics, download
the exact recorded object from R2 to your workstation, then copy it over the
tailnet alias:

```bash
scp ~/Downloads/<exact-dokploy-object>.sql.gz \
  prod:/home/deploy/taskflow-restore.sql.gz
```

Select the exact project/container using section 2 and create the empty scratch
target using section 3's guard. Do not bypass that check for the CLI path. Then,
on the server:

```bash
set -Eeuo pipefail

ARTIFACT=/home/deploy/taskflow-restore.sql.gz
test -s "$ARTIFACT"
gzip -t "$ARTIFACT"

gzip -dc "$ARTIFACT" |
  sudo docker exec -i "$PG" pg_restore \
    -U taskflow \
    -d taskflow_verify \
    --no-owner \
    --no-acl \
    --clean \
    --if-exists \
    --exit-on-error \
    --single-transaction

sudo docker exec "$PG" psql \
  -U taskflow -d taskflow_verify -v ON_ERROR_STOP=1 -c \
  'SELECT status, count(*) FROM tasks GROUP BY status ORDER BY status;'
```

`--single-transaction` and `--exit-on-error` make failure explicit and avoid a
half-applied scratch restore. They do not make an incompatible backup safe.
Complete section 3's schema, migration and exact-source count checks, retain the
receipt and check the live app before the authorized scratch cleanup. A failed
pipeline leaves scratch in place for diagnosis instead of dropping its evidence.
Keep the downloaded copy only if you will run the optional drill next.

## 5. Optional destructive drill: disposable TaskFlow only

Do this only when all of the following are true:

- the scratch restore above passed;
- TaskFlow contains no real or customer data;
- you have announced a maintenance window;
- the exact off-site `*.sql.gz` artifact is already on the server;
- you accept losing changes newer than that artifact.

If you used the Dokploy UI for the scratch test, use the download and `scp`
steps in section 4 now to place that same object at
`/home/deploy/taskflow-restore.sql.gz`.

First take an extra safety dump without stopping the app:

```bash
set -Eeuo pipefail
mkdir -p ~/backups
STAMP=$(date -u +%Y%m%dT%H%M%SZ)

sudo docker exec "$PG" pg_dump \
  -U taskflow -d taskflow \
  -Fc --no-owner --no-acl \
  > "$HOME/backups/pre-drill-$STAMP.dump"

test -s "$HOME/backups/pre-drill-$STAMP.dump"
```

Find the exact app container inside that same production project and set the
downloaded artifact path:

```bash
test -n "${PROJECT:-}"
test -n "${PG:-}"

mapfile -t APP_CANDIDATES < <(sudo docker ps -a \
  --filter "label=com.docker.compose.project=$PROJECT" \
  --filter label=com.docker.compose.service=app \
  --format '{{.Names}}')

if (( ${#APP_CANDIDATES[@]} != 1 )); then
  printf 'ABORT: expected one production app container, found %s\n' \
    "${#APP_CANDIDATES[@]}" >&2
  printf '%s\n' "${APP_CANDIDATES[@]}" >&2
  exit 1
fi

APP=${APP_CANDIDATES[0]}

# The cache must be flushed after the restore, so resolve it under the same
# project filter and with the same abort-on-ambiguity rule.
mapfile -t REDIS_CANDIDATES < <(sudo docker ps \
  --filter "label=com.docker.compose.project=$PROJECT" \
  --filter label=com.docker.compose.service=redis \
  --format '{{.Names}}')

if (( ${#REDIS_CANDIDATES[@]} != 1 )); then
  printf 'ABORT: expected one production redis container, found %s\n' \
    "${#REDIS_CANDIDATES[@]}" >&2
  printf '%s\n' "${REDIS_CANDIDATES[@]}" >&2
  exit 1
fi

REDIS=${REDIS_CANDIDATES[0]}
ARTIFACT=/home/deploy/taskflow-restore.sql.gz
EXPECTED_COUNTS=$(cat <<'COUNTS'
<paste exact pipe-delimited grouped-count lines from the source receipt>
COUNTS
)
test "$EXPECTED_COUNTS" != \
  '<paste exact pipe-delimited grouped-count lines from the source receipt>'

APP_PROJECT=$(sudo docker inspect -f '{{ index .Config.Labels "com.docker.compose.project" }}' "$APP")
PG_PROJECT=$(sudo docker inspect -f '{{ index .Config.Labels "com.docker.compose.project" }}' "$PG")
test "$APP_PROJECT" = "$PROJECT"
test "$PG_PROJECT" = "$PROJECT"
test -s "$ARTIFACT"
gzip -t "$ARTIFACT"
printf 'DESTRUCTIVE TARGET CONFIRMED: project=%s app=%s postgres=%s\n' \
  "$PROJECT" "$APP" "$PG"
```

Run the procedure as the bounded subshell shown below, not as loose interactive
commands. Its timer starts at application shutdown and stops only after
database, readiness, and browser proof. The trap is armed before the app is
stopped. Before a restore attempt starts, failure restores the app's prior
running/stopped state. Once the restore command starts, its result is treated as
ambiguous until every proof passes: any failure or interruption leaves the app
stopped so it cannot serve unverified data.

```bash
(
set -Eeuo pipefail

RESTORE_STARTED=0
FINAL_PROOF=0
APP_WAS_RUNNING=$(sudo docker inspect -f '{{.State.Running}}' "$APP")

on_drill_exit() {
  rc=$?
  trap - EXIT
  if (( rc != 0 || FINAL_PROOF == 0 )); then
    if (( RESTORE_STARTED == 0 )); then
      if [[ "$APP_WAS_RUNNING" == 'true' ]]; then
        echo 'Restore never started; restoring the previously running app.' >&2
        sudo docker start "$APP" >/dev/null 2>&1 || true
      else
        echo 'Restore never started; preserving the previously stopped app.' >&2
      fi
    else
      echo 'Restore was attempted but proof is incomplete; leaving the app stopped.' >&2
      sudo docker stop "$APP" >/dev/null 2>&1 || true
    fi
    (( rc != 0 )) || rc=1
  fi
  exit "$rc"
}

START=$(date +%s)
trap on_drill_exit EXIT
sudo docker stop "$APP"

RESTORE_STARTED=1
gzip -dc "$ARTIFACT" |
  sudo docker exec -i "$PG" pg_restore \
    -U taskflow \
    -d taskflow \
    --no-owner \
    --no-acl \
    --clean \
    --if-exists \
    --exit-on-error \
    --single-transaction

# Prove the restored durable state while the application is still stopped.
ACTUAL_COUNTS=$(sudo docker exec "$PG" psql \
  -U taskflow -d taskflow -v ON_ERROR_STOP=1 -AtF '|' -c \
  'SELECT status, count(*) FROM tasks GROUP BY status ORDER BY status;')
test "$ACTUAL_COUNTS" = "$EXPECTED_COUNTS"

MIGRATIONS=$(sudo docker exec "$PG" psql \
  -U taskflow -d taskflow -v ON_ERROR_STOP=1 -Atqc \
  'SELECT count(*) FROM "__EFMigrationsHistory";')
test "$MIGRATIONS" -gt 0

# Drop every cached read before the app serves again. Cache invalidation only
# fires for writes that go through the API, and this restore went underneath
# it: without this flush the board you are about to verify could be a cached
# copy of the data you just replaced, and the drill would prove nothing. The
# keys also carry short TTLs, but do not race a TTL during a recovery.
sudo docker exec "$REDIS" redis-cli FLUSHALL

sudo docker start "$APP" >/dev/null

READY=0
for _ in $(seq 1 150); do
  if curl --connect-timeout 3 --max-time 5 -fsS \
    https://app.<domain>/health/ready >/dev/null; then
    READY=1
    break
  fi
  sleep 2
done
test "$READY" -eq 1

printf '%s\n' "$ACTUAL_COUNTS"
printf 'Open the board and compare it with the source receipt.\n'
read -r -p 'Type PROVED only after the expected board renders: ' CONFIRM
test "$CONFIRM" = 'PROVED'

END=$(date +%s)
FINAL_PROOF=1
trap - EXIT
printf 'fixture service-recovery time through final proof: %ss\n' \
  "$((END - START))"
)
```

Keep the safety dump until the result is reviewed. The `PROVED` prompt keeps the
timer and safety trap active through the browser check. If the board still shows
post-artifact data, do not confirm; interrupting the script leaves the app
stopped. Confirm the flush targeted the right container before rerunning.
This is the general rule for any cache in front of a restored database — the
restore happens beneath the application, so nothing invalidates the cache for
you. `--clean` removes and recreates the fixture's objects
inside the restore transaction, so a restore failure rolls the transaction back
instead of leaving a deliberately emptied database. The successful drill still
demonstrates the backup's recovery point: changes made after the artifact
timestamp are intentionally absent.

After review, remove the temporary server copy with
`rm /home/deploy/taskflow-restore.sql.gz`. Keep the off-site object and safety
dump according to your retention policy.

## 6. Record the right recovery numbers

Do not collapse these into one claim:

- **RPO:** maximum acceptable data loss measured in time. A daily schedule sets
  a near-24-hour target only while monitoring proves fresh successful objects.
  The artifact timestamp proves the actual recovery point used in this drill.
- **Restore-test time:** time to restore and verify `taskflow_verify`. It proves
  the artifact is readable; it is not user-visible downtime.
- **Fixture service-recovery time:** the optional timer from app stop through
  restored counts, readiness, and final browser proof. It covers only the
  disposable database exercise.
- **RTO:** the target time to restore service. A scratch restore proves only the
  database restore step; it does not establish the time needed to recover the
  whole server and control plane.

## 7. Back up the Dokploy control plane

TaskFlow's `*.sql.gz` does not protect Dokploy configuration. Open **Web Server
-> Backups -> Create Backup**, choose the S3 destination, set a schedule and
keep count, enable **Include encryption key**, trigger one now, and verify the
non-empty `.zip` object. It contains Dokploy's own Postgres data,
`/etc/dokploy`, and the key material needed to recover encrypted control-plane
values. Treat it as especially sensitive and record its actual generated key.

Before every Dokploy upgrade:

1. read the release notes and required migration steps;
2. trigger and verify a fresh TaskFlow database backup;
3. trigger and verify a fresh Dokploy control-plane backup;
4. record the current version and a known-good app SHA;
5. after upgrading, prove login/2FA, domain routing, deployment, and both backup
   tests.

Record the control-plane backup object and the checks completed after maintenance.
An archive that exists has not yet been proved by a control-plane restore.
