---
name: private-access
description: Verify or prepare the Foundations SSH and private Dokploy access path over Tailscale, including the evidence required before closing public SSH. Use for access checks and scoped access changes.
---

# Keep a working private administration path

Read [learner context](../../../AGENTS.md) and the access/control-plane sections
of [the checklist](../../../ops/checklist.md). Tailscale belongs to Foundations.
Use the learner's actual alias, public IP and tailnet address. Check the current
SSH configuration's selected `hostname`, `user` and `identityfile` fields with
`ssh -G <alias>`; do not read the private-key file.

Prove network reachability and authentication separately. In local PowerShell,
after assigning the actual addresses and alias:

```powershell
Test-NetConnection -ComputerName $tailnetIp -Port 22
ssh $sshAlias
```

Inside that server Bash session, check `hostname`, `tailscale ip -4`, and the
relevant peer's state in `tailscale status`. Direct and relay connections both
provide a valid private path. Do not invent a direct connection from successful
ping/SSH alone. On Linux/macOS use bounded TCP probes available on that machine
and `ssh`; do not send PowerShell cmdlets to the remote server.

For an installed Dokploy panel, test TCP 3000 at the tailnet address and public
address from the workstation. The private endpoint should be reachable and the
public endpoint should fail. A failed TCP test alone does not identify whether
the cause is the provider firewall, routing or a stopped service.

If the request includes hardening, prepare the exact proposed rule/configuration
delta, affected host and recovery route first. Preserve a working session, prove
a second fresh `deploy` key session over Tailscale, and identify the provider
console before any change that could remove access. Validate SSH configuration
with `sudo sshd -t` and selected `sudo sshd -T` fields before an authorized reload.
Use the current supported installer/policy documentation for bootstrap work;
never invent a provider API or replace the whole tailnet policy from memory.

After an authorized removal of the public TCP 22 rule, repeat the public probe
from the formerly allowed source and open a fresh private SSH session. Keep
public HTTP/HTTPS and the learner's deliberate optional UDP 41641 choice.
Do not reopen SSH globally, disable host-key checking, log out the server from
Tailscale, or rotate keys as a speculative repair.

An inspection request authorizes inspection. If a live access change still lacks
authorization, finish the checks and present the prepared change for that
decision. Preserve authorization already given for the specific change.
Report separate evidence for TCP reachability, authenticated SSH, private panel,
public closure and provider-console recovery; mark untested items explicitly.
