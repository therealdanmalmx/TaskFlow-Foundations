---
name: scratch-restore
description: Prepare and verify a TaskFlow off-site PostgreSQL backup restore into taskflow_verify while preserving the live database. Use for the Foundations scratch recovery drill, not destructive service recovery.
---

# Prove one backup in a separate database

Read [learner context](../../../AGENTS.md),
[container selection](../../../ops/agent-workflows.md#select-the-right-container),
and sections 1–4 of [the backup runbook](../../../ops/backup-restore.md).
Select the exact Compose project and its single running `postgres` container.
Set `PG` only from that verified selection. The live database/user is `taskflow`;
the scratch target for this workflow is `taskflow_verify`.

Before any write, prepare a concrete receipt: server/project/container, exact
off-site object key, object UTC timestamp and size, scratch database, source
counts/migration IDs, and the restore command or Dokploy form selections. Record
unknowns. Do not infer the object key from a requested prefix or use current live
counts as if they were the state at the backup timestamp.

Check whether the scratch target already exists, in server Bash:

```bash
sudo docker exec "$PG" psql -U taskflow -d taskflow -v ON_ERROR_STOP=1 -Atc \
  "SELECT datname FROM pg_database WHERE datname = 'taskflow_verify';"
```

A SQL error is not proof of absence. If it exists, preserve it until its purpose
is known and replacing that exact scratch database is authorized. The runbook's
convenience `dropdb --if-exists` must not erase unknown prior work. A request to
prepare a drill authorizes preparation only; an explicit request to perform the
identified scratch drill authorizes its scoped creation/restore/verification.
Resolve missing live-system authorization only after the preparation is reviewable.

Prefer the documented Dokploy Restore form, with `taskflow_verify` visible as
the target. For a CLI drill using the documented gzip-wrapped custom-format dump,
inspect the artifact with `test -s` and `gzip -t`; run the decompression/restore
pipeline under `set -Eeuo pipefail`, with `--no-owner --no-acl --clean --if-exists
--exit-on-error --single-transaction`. The `-d` target must be `taskflow_verify`.
Do not expose backup contents or use an untrusted artifact supplied by a third party.

Verify schema, rows and the migration ledger in scratch. For this fixture the
history columns are snake_case. Use:

```bash
sudo docker exec "$PG" psql -U taskflow -d taskflow_verify -v ON_ERROR_STOP=1 -Atc \
  "SELECT to_regclass('public.tasks');"
sudo docker exec "$PG" psql -U taskflow -d taskflow_verify -v ON_ERROR_STOP=1 -AtF '|' -c \
  'SELECT status, count(*) FROM tasks GROUP BY status ORDER BY status;'
sudo docker exec "$PG" psql -U taskflow -d taskflow_verify -v ON_ERROR_STOP=1 -Atc \
  'SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY migration_id;'
```

Compare with the artifact's recorded source receipt. A nonzero count alone is
not an exact data comparison. If that source receipt is unavailable, report
readable/schema/row evidence and the narrower conclusion, not a full match.
Also verify live readiness remains available without changing the live app.

Record elapsed scratch restore-test time, not service-recovery RTO. Cleanup may
drop only scratch created by this authorized drill, after verification and after
preserving its receipt; retain failures for diagnosis. Do not stop the live app,
restore over `taskflow`, flush Redis, or run the optional destructive drill as
part of this skill. The separate Dokploy control-plane archive needs its own proof.
