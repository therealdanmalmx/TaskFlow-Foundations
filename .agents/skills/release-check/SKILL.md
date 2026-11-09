---
name: release-check
description: Verify that the intended TaskFlow release is live by matching the full commit SHA and database readiness to workflow evidence. Use after a deployment or rollback, without triggering another release.
---

# Prove the intended release

Read [learner context](../../../AGENTS.md), the deployment section of
[README.md](../../../README.md), and the actual
[production workflow](../../../.github/workflows/deploy.yml).
Obtain the intended full SHA from the requested release or selected workflow.
Do not silently substitute local HEAD, the newest run, or the moving `latest` tag.

When GitHub CLI access is available, set `$repo` to the confirmed `owner/repository`
and use `gh run list --repo $repo --workflow deploy.yml --limit 5` to locate a run.
Inspect its commit, completion
state, and artifact digest receipt. A failed/cancelled run and a healthy app are
different observations. Without access, prepare the exact run-page checks and
continue the public endpoint checks that are possible.

In local PowerShell, fill the confirmed HTTPS URL and expected 40-character SHA:

```powershell
$appUrl = 'https://<confirmed-application-host>'
$expectedSha = '<expected-full-commit-sha>'
if ($appUrl.Contains('<') -or $expectedSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'Set the actual URL and full expected SHA before probing.'
}
$appUrl = $appUrl.TrimEnd('/')
$receipt = curl.exe --connect-timeout 3 --max-time 5 -fsS "$appUrl/version.json"
$versionExit = $LASTEXITCODE
$liveSha = $null
if ($versionExit -eq 0) {
    try { $liveSha = ($receipt | ConvertFrom-Json -ErrorAction Stop).sha }
    catch { $liveSha = $null }
}
$readyHttp = curl.exe --connect-timeout 3 --max-time 5 -sS --output NUL --write-out '%{http_code}' "$appUrl/health/ready"
$readyExit = $LASTEXITCODE
[pscustomobject]@{
    ObservedAtUtc = [DateTime]::UtcNow.ToString('o')
    ExpectedSha = $expectedSha
    ObservedSha = $liveSha
    VersionExit = $versionExit
    ReadyHttp = $readyHttp
    ReadyExit = $readyExit
}
if ($versionExit -ne 0 -or $liveSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'The version receipt could not be read or contained no valid full SHA.'
}
if ($liveSha -ne $expectedSha) { throw "Release mismatch: expected $expectedSha; observed $liveSha" }
if ($readyExit -ne 0 -or $readyHttp -ne '200') { throw 'The expected release is not database-ready.' }
```

Use equivalent `curl` and JSON parsing in Bash. TLS failures are failures, not a
reason to add `-k`. If rollout is in progress, repeat these read-only checks with
a deadline of five minutes, consistent with the fixture workflow. Record status,
time and last observed SHA on timeout. Do not retry the deploy POST or silently
switch the expected commit to whatever became live.

The visible footer is a useful short-SHA cross-check; `/version.json` provides
the full receipt. Do not claim end-user functionality unless an actual check was
performed. A disposable task write requires that functional check to be in scope.

If rollback is requested separately, prepare the known-good full SHA/digest,
schema compatibility and verification first. An image rollback does not reverse
migrations. Dokploy environment APIs can replace an entire environment document;
do not send a guessed single-key patch. Prefer the taught explicit `IMAGE_TAG`
panel edit for a scoped manual rollback. Release checking alone makes no changes.

Return intended SHA, observed full SHA, readiness result, workflow/digest evidence,
UTC time and unresolved checks. Distinguish release proof from a guarantee of uptime.
