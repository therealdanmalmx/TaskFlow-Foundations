---
name: diagnose
description: Diagnose a TaskFlow Foundations deployment symptom using bounded health, version, container and log evidence. Use for failed releases, unavailable boards, or cache degradation before choosing a repair.
---

# Diagnose before changing state

Read [learner context](../../../AGENTS.md),
[troubleshooting](../../../README.md#troubleshooting), and
[container selection](../../../ops/agent-workflows.md#select-the-right-container).
Identify the symptom, target and first failure time. Preserve unrelated services.
Start with read-only evidence; do not turn a diagnosis request into a restart.

Probe the confirmed URL's `/health/live`, `/health/ready` and `/version.json`
using connect/overall timeouts. Then inspect only the selected project's services.
For each exact selected container, these server Bash commands are useful:

```bash
sudo docker inspect --format '{{.Name}} {{.State.Status}} restart-count={{.RestartCount}}' "$CONTAINER"
sudo docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}no-healthcheck{{end}}' "$CONTAINER"
sudo docker logs --since 10m --tail 120 "$CONTAINER"
```

Inspect logs in a trusted local tool and redact before summarizing. Do not paste
credentials, session tokens, connection strings or a full environment dump.
Widen the time window only when a specific missing event justifies it.

Use the application's actual failure boundaries:

- Live fails: process, routing, DNS or TLS may be involved. Compare container
  state with the public route before deciding which boundary failed.
- Live works, ready fails: inspect database state, connection errors and startup
  migration logs. Do not claim Redis caused a readiness failure.
- Ready works, cache is DOWN: PostgreSQL still serves data. Redis/cache or its
  connection is degraded; its activity feed is intentionally disposable.
- Ready works, SHA differs: the expected release has not been proved. Inspect
  the selected deployment's evidence; do not report success from HTTP 200 alone.
- Login fails after an `ADMIN_PASSWORD` edit: that variable seeds first boot;
  it does not update the persisted account. Do not reset volumes to change it.
- Container is unhealthy but running: healthchecks report state; Docker restart
  policies do not restart an otherwise running process solely for that status.

For a cache-header probe, prefer an already authenticated browser request. If a
CLI bearer probe is needed, use the learner's protected local mechanism and never
place its token in a saved command, receipt or message. Do not request passwords
just to diagnose public health.

Report a supported cause or a ranked hypothesis with the next discriminating
check. Prepare the smallest repair with target, expected effect, interruption,
recovery and verification. Execute it only when that change is within the current
request or existing authorization; otherwise present the concrete decision.
Do not delete volumes, flush all Redis keys, disable TLS checks, or open public
database/SSH ports as generic fixes.
