---
status: accepted
---

# DirectTarget HTTPS origin and single-project Compose topology

Pinned Zeroshot 10.3.0 accepts only an HTTPS origin or literal-loopback HTTP,
records that origin in native state at initialization, and its clients refuse
any endpoint that differs from it; `target serve` provides no TLS. The homelab
installation therefore runs one Compose project with three services: `broodling`,
`zeroshot` (the DirectTarget) and `zeroshot-tls`, a Caddy container that forwards
to `zeroshot` over the project network. The single, permanent DirectTarget origin
is `https://zeroshot.dev.faviann.com`. Inside the project a network alias
resolves that name to `zeroshot-tls` on port 443, where Caddy serves a
certificate from its `tls internal` authority. The stack's explicit first
initialization creates that authority's root once: the key in a location
readable only by `zeroshot-tls`'s user, the certificate in a separate directory
of public material only. Caddy signs with the configured root (`pki { ca local
{ root { cert, key } } }`) and still issues and renews its intermediate and leaf
certificates itself. Broodling mounts only the public directory read-only and
re-reads the root for each new connection; no key reaches it. Outside the
project, LAN DNS resolves the same name to Traefik, which serves the public
wildcard certificate and forwards to `zeroshot-tls`'s port on the LXC. The
stack's internal communication thus depends on nothing outside it, while the
operator can use the Zeroshot CLI, HTTP requests or a browser from the LAN
without tunnels or trust setup.

Every container runs as its production user. Broodling is non-root.
`zeroshot-tls` runs Caddy as a non-root user that owns the key directory and
binds 443 inside its container via `NET_BIND_SERVICE` (Docker's unprivileged-port
sysctl also permits it); a high inner port would break in-project clients, which
reach Caddy directly through the alias on the origin's port. Only `zeroshot` runs
as container root, as native's process-identity allocation requires.

## Consequences

- Zeroshot is deliberately reachable from LAN and VPN callers admitted by
  Traefik's allow-list, and on its published LXC port, with no authentication
  until Zeroshot's private (bearer-token) mode is adopted. This supersedes the
  earlier "unpublished target, off the proxy network" requirement.
- Broodling has no Compose dependency on `zeroshot` or `zeroshot-tls`; it starts
  and serves retained history while either is down. Native agents reach
  Broodling's read-only reader by service name on the project network.
- Running jobs are protected from Broodling deploys by deploy policy (update by
  service, never `down` the project), not by separate Compose projects.
- The TLS hop adds disconnect sources and Broodling performs no automatic
  transport retry; that is an accepted limitation until retry is designed.
- Changing the origin name later requires a stopped-target transition of native
  state.
- Caddy only loads the provided root and never generates one: missing or
  mismatched root files stop it from starting. A stored intermediate that no
  longer chains to the root (after a rotation or restore that skipped Caddy's
  data) fails readiness discovery through the origin with the configured root.
  No separate startup check of the root is added.
- Rotating the root is a deliberate, documented step: replace the key and
  certificate together and remove Caddy's stored intermediate and leaf.
  Broodling picks up the new root on its next connection.
- The published LXC port is free to choose; exact-origin native clients on the
  LAN reach 443 through Traefik.

## Considered Options

- **Broodling on the LXC host network, target published on LXC loopback.**
  Cheapest now, but ties Broodling to one host's network and has no cloud mapping.
- **Only through Traefik.** Makes the stack depend on the portal LXC for its own
  internal traffic.
- **Shared network namespace for all services (pod style) with a loopback
  origin.** Removes TLS, but the Zeroshot CLI then needs an SSH tunnel and a
  Kubernetes mapping forces Broodling and the target into one pod.
- **Publicly issued certificate via Cloudflare DNS challenge in the stack.**
  Adds an external token dependency to internal communication.
- **Loosening Zeroshot's origin rule.** Removes the only safeguard of an
  unauthenticated server.
- **Caddy's auto-generated root, read from its data volume.** Caddy writes the
  root `0600 root:root`, so a non-root Broodling cannot read it, and the same
  volume holds the root and intermediate keys
  ([#155 evidence](https://github.com/faviann/broodling/issues/155#issuecomment-5837214235)).
