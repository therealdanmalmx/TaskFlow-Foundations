---
name: deploy
description: Prepare TaskFlow Foundations deployment configuration and repeat an explicitly authorized release through the existing GitHub Actions, GHCR and private Tailscale Dokploy path. Use for configuration adaptation or shipping the taught app, not new infrastructure.
---

# Prepare and ship a known release

Read [learner context](../../../AGENTS.md), the learner's private context,
[the deployment contract](../../../README.md#how-a-deploy-works),
[production Compose](../../../docker-compose.prod.yml),
[configuration names](../../../.env.example), and the actual
[deployment workflow](../../../.github/workflows/deploy.yml).
Keep the existing app, single VPS and production pipeline. Prepare deployment
configuration and use the supplied release workflow within the requested scope.

## Prepare the target and local changes

Inspect the working tree, current branch, requested diff and confirmed GitHub
repository. Preserve unrelated work. Identify the intended full commit SHA,
expected production branch, application URL, Dokploy Compose target, lowercase
image repository and currently selected `IMAGE_TAG`. Do not assume that a local
commit has been pushed or that `latest` still points to the requested commit.

Perform authorized local configuration edits without repeatedly requesting
permission: adapt non-secret placeholders and preserve the existing Compose and
workflow behavior. Keep secrets blank in examples. Use existing private runtime
configuration and secret-store entries; never copy secret values into Git, a
prompt, command arguments or evidence. An agent may prepare exact panel field
names and expected values without reading the full environment document.

Check these prerequisites against current evidence:

- Production Compose uses the published GHCR image; the VPS does not build it.
  `IMAGE_REPOSITORY` matches CI's lowercase `ghcr.io/<owner>/<repository>`.
  The taught deployment uses `IMAGE_TAG=latest`; a prior rollback pin can keep
  another SHA running even after this workflow succeeds at publishing.
- Dokploy can read the private source repository and pull its private GHCR
  package. Native push auto-deploy is disabled so the tested workflow owns the
  release. Preserve the existing project, volumes and domain routing.
- Dokploy contains `DB_PASSWORD`, `JWT_SECRET` and `ADMIN_PASSWORD`; existence
  and configuration checks do not establish that each credential is valid.
- Actions secret names are `TS_OAUTH_CLIENT_ID`, `TS_OAUTH_SECRET` and
  `DOKPLOY_API_KEY`. `gh secret list --repo $repo --json name` lists names only.
- Repository variables are `DEPLOY_ENABLED`, `DOKPLOY_HOST`,
  `DOKPLOY_COMPOSE_ID` and `APP_URL`. Inspect the selected variables with
  `gh variable get <name> --repo $repo`; credentials do not belong in variables.
  The live-deploy flag must be `true` for the deployment and verification steps.
- The private Tailscale path is proven. The ephemeral `tag:ci` runner can reach
  Dokploy TCP 3000; operator access uses SSH over the tailnet. Do not fix a
  missing CI route by publishing the panel or weakening the tailnet policy.

If access, credentials or tools are unavailable, continue local preparation and
state the exact missing prerequisite. Never invent a Dokploy API patch. In
particular, do not treat a replace-all environment API as a single-variable edit.

## Validate the prepared result

Render affected Compose files with `docker compose -f docker-compose.prod.yml
config --quiet` and the equivalent dev command if changed. Supply required dummy
values in an isolated process for syntax checks; never print a rendered secret
environment. Verify the core app/Postgres/Redis shape, internal port 8080, no
published data ports, retained volume identity and required secret guards.
Rendering does not prove registry access or live runtime configuration.

Use [.github/workflows/ci.yml](../../../.github/workflows/ci.yml) as the test
reference. Run relevant tests for changed behavior: API tests for backend changes;
frontend lint/tests/build for frontend changes; image build/smoke checks for an
image contract change. Do not rerun unrelated suites for a placeholder-only edit
or remove failing checks. The deployment workflow runs its own test/build/smoke
gate before publishing. Present the diff, test evidence, target, intended SHA,
expected interruption and known-good rollback/schema position as one reviewable
result. Commit/push only when those actions are included in the current task.

## Trigger the existing workflow when authorized

The shipped workflow is named `deploy`, in `deploy.yml`. It triggers on pushes
to `main` and supports `workflow_dispatch` with **no inputs**. Inspect the current
file in the learner's repository before relying on that contract. Do not invent
`-f sha=...` or choose an arbitrary branch to bypass the production path.

A push to the configured production branch already starts this workflow. If a
matching run exists, follow it instead of launching a duplicate. Also inspect
active runs: the production concurrency group lets the active release finish
verification. A new dispatch can replace a pending run; pending runs are not a
FIFO queue. Cancelling Actions cannot undo a Dokploy deployment already accepted.

Before a manual dispatch, in local PowerShell, fill the confirmed values:

```powershell
$repo = '<owner>/<repository>'
$branch = 'main'
$expectedSha = '<intended-full-commit-sha>'
if ($repo.Contains('<') -or $expectedSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'Set the actual repository and intended full SHA.'
}
$remoteSha = gh api "repos/$repo/commits/$branch" --jq .sha
if ($LASTEXITCODE -ne 0 -or $remoteSha -ne $expectedSha) {
    throw 'The expected branch does not currently identify the intended release.'
}
gh run list --repo $repo --workflow deploy.yml --branch $branch --limit 10 --json databaseId,headSha,event,status,createdAt,url
```

Record existing run IDs and check queued/running releases before proceeding.
The following command is a remote mutation, even when `DEPLOY_ENABLED=false`:
the workflow still builds and publishes images. Run it once only when the user
has authorized this release or artifact publication to the confirmed target:

```powershell
gh workflow run deploy.yml --repo $repo --ref $branch
```

Reuse authorization already given. If it is missing, ask only after the prepared
result is concrete. Do not silently turn `DEPLOY_ENABLED` on or change a pinned
`IMAGE_TAG` to obtain a green result. Those are target configuration changes.
Do not automatically retry an ambiguous dispatch response; inspect runs first.

Dispatch resolves a moving branch, not an atomic expected-SHA input. Avoid
concurrent pushes during dispatch and immediately locate the new run by event,
creation time, ID and full `headSha`; do not assume the newest run is yours.
If identity is ambiguous or its SHA differs, report that uncertainty and do not
issue another release. A caller requiring an atomic commit pin needs a separate
workflow change; this skill must not claim the existing workflow provides it.

## Follow through to live proof

Inspect the selected run with `gh run view $runId --repo $repo --json
headSha,status,conclusion,url,jobs`. Poll with a deadline aligned with its
30-minute job timeout. If a pending run is replaced, identify the superseding run
and do not equate cancellation with rollback or success. Never retry a Dokploy
deployment POST directly as a shortcut around a failed workflow.

After successful deployment, use
[release-check](../release-check/SKILL.md) to match the exact
intended full SHA to `/version.json` and database readiness. Preserve the registry
digest receipt. If deploy was disabled, report artifact publication only.
Return the changed files, checks, selected run URL/SHA, actual deployment proof
and anything still unverified. Do not call the release complete at API acceptance.
