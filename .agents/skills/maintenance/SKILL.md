---
name: maintenance
description: Review the Foundations server's recovery evidence, resource headroom and update needs, then prepare or perform explicitly scoped maintenance. Use for the monthly operating routine, without assuming permission for upgrades or cleanup.
---

# Maintain a server whose recovery path is known

Read [learner context](../../../AGENTS.md), the day-2 section of
[the checklist](../../../ops/checklist.md), and section 7 of
[the backup runbook](../../../ops/backup-restore.md).
Distinguish a requested review from an authorized maintenance operation. Preserve
authorization already given; prepare precise missing decisions after inspection.

Begin with evidence: latest application and control-plane backup objects, UTC
timestamps and nonzero sizes; last successful scratch restore; known-good SHA;
private SSH and provider-console recovery; external-monitor alert receipts.
If access to a backup store or monitor is unavailable, mark it unverified.
Do not turn a configured schedule or a green dashboard into a fabricated receipt.

Collect a dated, read-only snapshot over the confirmed SSH alias, in server Bash:

```bash
date -u '+maintenance snapshot: %FT%TZ'
df -h /
df -i /
free -h
sudo docker system df
sudo docker stats --no-stream
apt list --upgradable
apt-mark showhold
test -f /var/run/reboot-required && echo 'reboot required' || echo 'no current reboot marker'
sudo docker image ls --filter dangling=true
```

The update list uses existing package indexes and may be stale. Refreshing them
with `sudo apt update` changes local package metadata; distinguish that step from
an inspection-only request. For log-driver proof, select the exact project's
containers and inspect only `{{.Name}} {{.HostConfig.LogConfig.Type}}`.
Compare dated snapshots where available; a single observation is not a trend.

Prepare useful changes individually: package update list, held/phased items,
reviewed Dokploy release and required migrations, a reboot window if needed, or
the exact cleanup candidates. Include the target, effect, recovery evidence and
verification. An authorized package upgrade uses the taught `sudo apt upgrade`
path; inspect remaining updates and `/var/run/reboot-required` afterward. Do not
force phased/held updates or reboot automatically to make a checklist green.

Before an authorized Dokploy upgrade, use current official release notes and
fresh application/control-plane backups. Preserve volumes and the known-good app
artifact. After any live change, prove private access, panel login, HTTPS,
readiness and the expected full SHA; verify backup behavior when affected.

Pruning is deletion. A preview is not authorization. If cleanup is requested,
keep it scoped to the reviewed candidates and preserve tagged rollback images.
Never substitute `docker system prune`, volume pruning or `down -v` for the
course's bounded image cleanup. If an update fails, preserve evidence and diagnose
the incident rather than rerunning changes blindly.

Return checks performed, actual changes, deferred work, elapsed time and the next
review date. Create a reminder only when the learner requests one; otherwise
provide a date to put in their calendar. Explain one actionable capacity signal
without claiming a larger single VPS provides high availability.
