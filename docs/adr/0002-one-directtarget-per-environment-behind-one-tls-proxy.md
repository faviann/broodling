---
status: accepted
---

# One DirectTarget per approved environment behind one TLS proxy

[ADR 0001](0001-directtarget-https-origin-and-compose-topology.md) fixed one
`zeroshot` target behind one `zeroshot-tls` at the single origin
`https://zeroshot.dev.faviann.com`. Repositories can need incompatible OS
libraries, and each target keeps its own native ledger, so #235 resolves every
Attempt to an operator-approved environment and retains the target serving it
(owner decision Q2 on #235). This ADR supersedes ADR 0001's single target: its
service count, the `zeroshot` service and alias, and its one origin. The rest of
ADR 0001 still holds as recorded there: why the origin is HTTPS through Caddy,
the once-created TLS root and its key separation, Traefik's LAN hop trusting
that root, the service users and capabilities, root rotation, and the rejected
options.

- **Targets.** One DirectTarget serves each environment in the operator's
  catalog. An entry maps the environment name to the target's origin, its
  control token file, the adopted image digest, the target container and its
  host state and home mounts. No two entries share a name, origin or container,
  and no state, home or token paths overlap. Repositories that select the same
  name share its target. There is no alias layer, no load balancing between
  native ledgers behind one origin, no automatic placement or failover, and no
  on-demand container creation.
- **Names.** The operator chooses each name: a lowercase DNS label of at most 54
  characters, so `zeroshot-<name>` is a label too. One mechanism covers a stable
  name such as `dotnet`, which repositories normally select and which is
  upgraded by swapping its image in drained maintenance, and a release-specific
  name such as `dotnet-r3`, added as a separate target for testing and removed
  once the stable name is promoted.
- **Services and origins.** Each target is the Compose service and
  project-network alias `zeroshot-<name>`. Each entry sets its origin
  explicitly, so the domain stays out of Broodling's code; the convention is
  `https://zeroshot-<name>.dev.faviann.com`, which Traefik's `*.dev.faviann.com`
  wildcard covers. The origin is canonical HTTPS on port 443.
- **TLS.** One `zeroshot-tls` (Caddy) and one root serve every origin.
  [`zeroshot-tls.Caddyfile`](../../deployment/zeroshot-tls.Caddyfile) holds only
  the global block and `import /etc/caddy/sites/*.caddy`. Each origin has one
  site file there, `<host> { tls internal; reverse_proxy zeroshot-<name>:18770 }`,
  and `zeroshot-tls` carries each origin host as a network alias. Broodling
  keeps one `directRootCertificate`. The single Caddy is an accepted common
  point of failure, the same failure mode as one target.
- **Mounts.** In-container paths stay fixed: `/state`, `/home/node`, the
  read-only public root at `/tls-root` and the bootstrap key at
  `/run/secrets/zeroshot-bootstrap-key`. Host paths are per target, by
  convention `<root>/targets/<name>/{state,home}`, and each target has its own
  bootstrap key file and control token.
- **Retained target.** Each Attempt's Prepared submission retains its resolved
  environment and target identity: environment name, origin, container, state
  mount and home mount. Dispatch, replay, reads, wait and stop use only that
  target, and a stopped-target check must name it exactly. A configuration
  change never rebinds an Attempt. Moving a target's storage or changing its
  container or origin is therefore a stopped-target transition after which its
  unfinished Attempts are retired and replaced.
- **Image swap.** Changing an entry's image is drained maintenance. An Attempt
  prepared for the old image is replaced onto the new one only with the
  operator's `replace-attempt --update-environment`
  ([environment selection](../implementation/zeroshot-native-integration.md#environment-selection)).

## Contract for homelab-iac

Broodling owns this contract; homelab-iac renders and operates it. Release and
operator details are in the
[operations guide](../../deployment/README.md#several-targets-235).

- **homelab-iac#353** (installation): for each catalog entry, render a
  `zeroshot-<name>` service from the adopted image digest with the supported
  target profile and its alias, its own host state and home and bootstrap key,
  and the shared public root read-only; one site file and one `zeroshot-tls`
  alias per origin; the catalog entry and default in Broodling's invocation
  configuration with one control token file per target; and one readiness
  inventory `targets` element per entry.
- **homelab-iac#354** (routes): LAN DNS and one Traefik route per origin to
  `zeroshot-tls`'s published port, each trusting the one shared root.
- **homelab-iac#355** (reconciliation stop): the exclusion covers every target
  service, `zeroshot-tls`, every site file and the catalog configuration, so
  adding a target opens no path around it.
- **homelab-iac#356** (maintenance): take the affected targets from unfinished
  Attempts' retained `environment.target`, including targets that are no longer
  the default or in the catalog. Supply each `retire-attempt` with a fresh
  check naming exactly that Attempt's retained `environment`, origin, container,
  state and home mounts; never share or substitute a check between targets.
  After starting targets: `bootstrap-target` for each environment, then
  `check-target`, then release.
- **homelab-iac#357** (backup): include every target's state and home,
  including targets still retained by unfinished Attempts, with the store, the
  root pair and Caddy's data, the catalog configuration, the site files and the
  inventory. Resolutions are retained in the store; never reconstruct them from
  the catalog.
- **homelab-iac#358** (upgrades): adding an environment or changing the default
  leaves retained Attempts on their targets. Swapping an entry's image is
  drained maintenance followed by readiness, and Replacements of affected
  Attempts need `--update-environment`. Do not remove or repurpose an origin or
  storage pair while unfinished work retains it.

## Consequences

- Readiness checks the whole installation: every catalog target, overlap
  across targets, and exactly one container per target alias and per origin
  alias. Maintenance stays whole-installation.
- Broodling cannot check tools at startup itself, since it has no Docker
  access. The documented order pause, start, `bootstrap-target`,
  `check-target`, release ensures no agent starts on an unchecked target.
- A catalog entry removed while Attempts retain it leaves them unreadable
  through native (`environment_unavailable`) until the entry returns or they
  are retired.

## Considered Options

- **One TLS proxy per target.** More services and roots for no isolation gain
  that the one root does not already decide.
- **Aliases or families over names.** A second mechanism beside image swap and
  release-specific names, and a hidden rebinding path.
- **Host-only mount verification.** Without container and mounts in the
  catalog and the retained target, Broodling could not refuse a check that
  names another target or other mounts.
