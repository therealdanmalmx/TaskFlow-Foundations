---
name: preflight
description: Check prerequisites and observed state for the TaskFlow Foundations self-hosting lab before a lesson or repeated operation. Use for readiness checks, not provisioning or deployment.
---

# Check the lab before continuing

Read [learner context](../../../AGENTS.md), the learner's supplied private
context file, and the relevant section of [the checklist](../../../ops/checklist.md).
When no context exists, use [the template](../../../ops/agent-context.example.md)
to identify missing targets; do not fill them from course examples.

First establish the requested checkpoint. A learner before Tailscale setup should
not fail a later checkpoint. Separate local tools, configured resources and
observed server state. Continue independent checks while a target is unknown.

For local inspection, use `git status --short`, `git rev-parse --show-toplevel`,
`docker version` and `docker compose version` as relevant. On Windows,
`Get-Command ssh,scp,git,curl.exe` checks the course tools. Docker alone supports
the packaged local app; .NET 10 and Node 24 are needed only for source/test work.
Do not install tools, start containers, or run a full test suite merely to report
that the prerequisites exist.

If the learner requests an existing-server check, use the confirmed SSH alias
and inspect these in server Bash:

```bash
hostname
cat /etc/os-release
uname -m
df -h /
free -h
tailscale ip -4
sudo docker version --format '{{.Server.Version}}'
```

The taught server baseline is Ubuntu 24.04 x86 with at least 2 GB RAM and 30 GB
disk. Report differences; do not upgrade the OS to make the check pass. Account
for the lesson stage before treating an absent Docker/Tailscale binary as a fault.
If testing the published app is in scope, request `/health/live`,
`/health/ready` and `/version.json` with bounded timeouts at the confirmed URL.

Before operational work, check that the learner can identify the provider console,
private access path, exact Compose project, and relevant recovery receipt. A named
backup destination proves configuration only. A successful restore needs evidence.

Return a short checkpoint report: proved now, declared but unverified, missing,
and the next action. Name the exact check that failed. Do not mark the lab ready
from a configuration file alone or include secret values in the report.
