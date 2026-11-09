# TaskFlow learner context

This repository is the disposable TaskFlow application for the Foundations
self-hosting course. Read [README.md](README.md) for the deployment contract and
[ops/agent-workflows.md](ops/agent-workflows.md) when using the course skills.

## What the application does

- React is built into a .NET 10 API image. The API serves one origin on internal
  port 8080. PostgreSQL 18 holds durable application state; Redis 8 is disposable.
- Production uses `docker-compose.prod.yml`, a CI-built GHCR image, and
  Dokploy/Traefik for HTTPS. PostgreSQL and Redis have no public host ports.
- Tailscale is part of Foundations. Operator SSH and Dokploy stay private;
  public ingress is HTTP/HTTPS, with optional UDP 41641 for a direct tailnet path.
- `/health/live` checks the process. `/health/ready` checks the database, not
  Redis. A healthcheck reports state; the restart policy responds to process exit.
- `/version.json` contains the full image commit SHA. The footer abbreviates it.
  A green old instance or an accepted deployment request is insufficient proof.
- Startup migrations and first-boot seeding are controlled by `RUN_MIGRATIONS`.
  Changing `ADMIN_PASSWORD` later does not reset a stored password.
- TaskFlow is a learning fixture. Do not use personal, customer, or valuable data.

## Working with the learner

Use the selected skill and any learner-supplied context. Separate declared
configuration, fresh observations, and unknowns. Old receipts and example values
are not current evidence or permission to operate a server.

On first use, briefly explain what the command changes and what result proves it
worked. When repeating an understood procedure, perform the authorized work and
report the evidence. Preserve authorization already given in the conversation;
do not repeatedly ask before routine inspection or reversible local preparation.

Prepare the exact target, proposed change, expected interruption, recovery path,
and verification before requesting any missing authorization. A diagnosis request
does not authorize a live restart, deploy, upgrade, firewall change, or restore.
Access changes that could lock out the learner and destructive actions require
specific current scope and a recovery path. Do not infer either from this file.

## Repository and command boundaries

- Preserve unrelated changes. No commits, pushes, deployments, or new resources
  unless requested or already authorized by the active task.
- Keep secrets out of Git, messages, screenshots, receipts, and command arguments.
  Use the learner's existing secret store. Do not print `.env`, private keys,
  full Docker inspect output, or Dokploy environment documents.
- Shell blocks identify local PowerShell versus server Bash. Do not paste Bash
  into PowerShell. Validate real targets; never run placeholder values.
- Use the exact Compose project label and service label when selecting containers;
  stop if selection is ambiguous. Do not use the first name that contains `postgres`.
- Existing code/content attribution belongs to Milan Jovanović. Do not add agent
  attribution, co-author trailers, or generated-by footers.
- New repository automation uses C# single-file apps, not shell scripts.

Skills live in `.agents/skills/`. Discovery depends on the agent; explicitly open
the relevant `SKILL.md` when automatic discovery is unavailable.
