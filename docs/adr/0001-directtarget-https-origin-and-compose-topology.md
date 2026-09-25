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
resolves that name to `zeroshot-tls`, which serves a certificate from its own
`tls internal` authority; Broodling trusts that root from Caddy's read-only data
volume and re-reads it per connection. Outside the project, LAN DNS resolves the
same name to Traefik, which serves the public wildcard certificate and forwards
to `zeroshot-tls`'s port on the LXC. The stack's internal communication thus
depends on nothing outside it, while the operator can use the Zeroshot CLI,
HTTP requests or a browser from the LAN without tunnels or trust setup.

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
