# Production checklist

Use this as the evidence-based sign-off for the course. An item is complete only
when its proof exists, not when the setting merely looks plausible.

## Preflight (0.1)

- [ ] I understand the course outcome: deploy and operate TaskFlow on one VPS
      with private administration, HTTPS, verified releases, backups and alerts.
- [ ] On Windows I use PowerShell locally and Bash only after SSH; `ssh`, `scp`,
      `git`, `curl.exe`, `Resolve-DnsName`, and `Test-NetConnection` are available.
- [ ] VPS, domain/DNS, GitHub, Tailscale, Cloudflare, and external-monitor
      accounts are ready; Module 8 creates the R2 bucket.
- [ ] The server choice is Ubuntu 24.04 x86 with at least 2 GB RAM and 30 GB
      disk, and I verified today's checkout price.
- [ ] TaskFlow is treated as a disposable fixture with no real or customer
      data; its demo JWT identities and workspace authorization gate board/data access but are not a substitute
      for keeping valuable data out of it.

## Access and hardening (2.1-4.3)

- [ ] The production ed25519 private key has a passphrase and an off-device
      backup; only its public half was uploaded.
- [ ] A default-deny provider firewall was attached when the VPS was created.
- [ ] Public TCP 80/443 are open; TCP 22 was initially restricted to my source
      IP; UDP 41641 is either deliberately enabled or deliberately omitted.
- [ ] A non-allowed external source cannot reach TCP 22.
- [ ] Daily administration uses the sudo-capable `deploy` user, not root.
- [ ] `/etc/ssh/sshd_config.d/00-course-hardening.conf` disables root,
      password, and keyboard-interactive login.
- [ ] `sudo sshd -t` passes and `sudo sshd -T` proves all three effective
      values before reload.
- [ ] A second, fresh deploy-user key session worked before the bootstrap root
      session was closed.
- [ ] Automatic security updates are enabled.
- [ ] The fresh image was upgraded once and any post-upgrade reboot requirement
      was handled before application installation.
- [ ] `ssh prod` uses a tailnet hostname/IP, public TCP 22 is deleted, and
      public-IP SSH now fails.
- [ ] Key expiry is disabled only for the stable server, and stale/compromised
      server-device revocation is part of the operator checklist.
- [ ] The provider web console is documented and tested as break-glass access.

## Dokploy control plane (5.1-5.3)

- [ ] Dokploy was installed on the supported Ubuntu 24.04 path with the current
      official `curl -fsSL https://dokploy.com/install.sh | sudo sh` command.
- [ ] The installed Dokploy version meets the current security gate (v0.30.0
      minimum for the 2026-08 course baseline; current patch rechecked).
- [ ] Port 3000 fails through the public IP and works through the tailnet.
- [ ] The first admin account is protected by 2FA and recovery material.
- [ ] No generic "pin the previous Ubuntu codename" workaround was applied.
- [ ] I can locate Project, Service, Domains, and Deployments without exposing
      the panel.

## Application and HTTPS (6.1-6.5)

- [ ] The app image runs as a non-root user and listens on internal port 8080.
- [ ] `DB_PASSWORD`, `JWT_SECRET`, and `ADMIN_PASSWORD` are independently
      generated and stored in Dokploy, never Git, with no insecure defaults.
- [ ] Runtime inspection shows the three services — app, Postgres, and Redis —
      on their generated project network. After routing, only the app additionally
      joins `dokploy-network`;
      Postgres and Redis remain project-only.
- [ ] Postgres and Redis have no host `ports:` mapping.
- [ ] All three services use `restart: unless-stopped` and Compose's rotating
      `logging: { driver: local }`.
- [ ] Redis runs with persistence off, a memory ceiling, LRU eviction, and no
      volume; the app was seen serving with the cache stopped (`X-Cache: DOWN`)
      while readiness stayed green.
- [ ] `/health/live` proves process liveness and `/health/ready` checks the
      database.
- [ ] I can explain that a healthcheck reports state but does not restart a
      merely unhealthy container.
- [ ] Stopping TaskFlow's Postgres container in Dokploy left direct liveness
      at 200 and readiness at 503. Starting it restored readiness and task writes
      without restarting the app. The database is running again.
- [ ] First-boot migration/seed stdout was captured immediately. The course
      baseline has one `InitialCreate` history row and native UUID keys. A restart
      leaves that row unchanged and skips the existing seed.
- [ ] The direct DNS A record resolves, HTTPS is valid and auto-renewing, and
      HTTP redirects to HTTPS.
- [ ] A disposable task write survives refresh.

## Private CI/CD and rollback (7.1-7.5)

- [ ] CI tests first, builds once, and smoke-tests the exact local image that is
      later pushed.
- [ ] GHCR receives `:latest` plus a non-overwritten full commit-SHA tag, and the
      workflow summary records the immutable registry digest.
- [ ] A Tailscale OAuth client has writable `auth_keys` scope, uses only `tag:ci`, and
      creates an ephemeral node per job; its credentials are Actions secrets.
- [ ] The course HuJSON replaced the default allow-all grant, and its tests prove
      `tag:ci` can reach production TCP 3000 but not SSH, Postgres, or other devices.
- [ ] `DOKPLOY_API_KEY` is dedicated, expiring, secret, and least-privileged
      where supported; `DOKPLOY_HOST`, `DOKPLOY_COMPOSE_ID`, and
      `APP_URL` plus `DEPLOY_ENABLED` are repository variables.
- [ ] Dokploy and its deploy trigger stay private: CI calls authenticated
      `POST /api/compose.deploy` over the tailnet; no public webhook is used.
- [ ] Provider/native auto-deploy is disabled so CI gates are authoritative.
- [ ] The production release concurrency group lets an active release finish
      verification. Pending runs are not a FIFO queue.
- [ ] The branch-head gate rejects stale runs before publication. Deliberate
      rollback uses an explicit full-SHA selection or a new revert commit.
- [ ] The live footer's short SHA matches the deployed commit.
- [ ] CI finishes only after readiness and `/version.json` both prove the full
      expected SHA, not merely after Dokploy accepts the API request.
- [ ] An older full-SHA image was actually redeployed by setting `IMAGE_TAG`,
      and the footer proved the rollback.
- [ ] The rollback plan accounts for schema compatibility; image rollback never
      assumes a database migration was reversed.

## Application data recovery (8.1-8.4)

- [ ] The R2 bucket's location choice is explicit: a hint is best effort, while
      the selected EU jurisdiction is a residency guarantee and uses the
      jurisdiction endpoint with region `auto`.
- [ ] The R2 token is scoped to the backup bucket and stored in the password
      manager plus Dokploy, never Git; I know its write permission can delete
      objects and a bucket lock protects the required retention window.
- [ ] Backup credentials were pasted with capture paused and masked; anything
      exposed in a screen recording, error detail, or support log was rotated
      and the destination retested.
- [ ] The TaskFlow Compose backup specifies PostgreSQL, service `postgres`,
      database/user `taskflow`, a schedule, requested prefix, keep count, and
      enabled state; the scheduler's UTC timezone was proved.
- [ ] The daily schedule is treated as a near-24-hour RPO target whose fresh
      objects must be monitored, not as proof that a backup completed.
- [ ] A pre-dump UTC timestamp, grouped task counts, and migration IDs were recorded, then the
      exact generated off-site `*.sql.gz` key, UTC timestamp, and non-zero size
      were copied from R2 rather than inferred from the requested prefix.
- [ ] The exact production `com.docker.compose.project` label is recorded; all
      CLI restore lookups filter by it and abort unless exactly one container matches.
- [ ] That exact object restored through Dokploy into `taskflow_verify` without
      touching the live database.
- [ ] Schema, migration history, and a non-zero task count were verified before
      the scratch database was dropped.
- [ ] Any CLI fallback decompresses gzip before `pg_restore` and uses
      fail-fast, single-transaction options.
- [ ] I distinguish RPO, scratch restore-test time, and the RTO recovery target.
- [ ] A provider snapshot, if enabled, is treated as an additional layer rather
      than the tested database restore path.
- [ ] Redis is recorded as rebuildable and outside the backup surface.
      Losing its cache and recent activity must be acceptable.

## Control-plane recovery and day 2 (8.1, 9.1-9.3)

- [ ] A separate Dokploy Web Server backup exists as a recent, non-empty
      off-site `.zip`; **Include encryption key** is enabled for recoverability,
      so the archive is treated as especially sensitive.
- [ ] Before a Dokploy upgrade I read release notes, create fresh app and
      control-plane backups, and record the current version and good app SHA.
- [ ] After an upgrade I prove login/2FA, routing, deploy, and backup tests.
- [ ] The monthly reminder covers backups, disk space and CPU/memory use,
      reviewed updates, a reboot check when needed, and verification of the
      app and private access afterward. Findings and follow-up work are saved.
- [ ] An external monitor checks `/health/ready`; a green check plus delivered
      test DOWN and UP alerts are recorded.
- [ ] I know the measured resize trigger and accept that one larger box is not
      high availability.
- [ ] The teardown plan removes the VPS/IPv4, DNS, credentials, and tailnet
      device and confirms billing stopped.
