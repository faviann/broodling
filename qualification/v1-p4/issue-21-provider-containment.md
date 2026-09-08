# Issue #21 actual-provider containment requalification

This record covers only the actual-provider/physical-profile probes. It does not
complete #21, authorize retirement based on a runtime label, or record G4 PASS.
The product administration and five fault windows require separate evidence.

The new [harness](issue21_provider.py) submits the exact product graph/runtime
through Contract admission, Attempt provisioning and the public pinned SDK.
The selected named role runs actual Codex 0.153.4; other model roles are explicitly
controlled test fixtures. Product deterministic evidence is unchanged. The
provider dispatcher adds a precise qualification command to the selected role's
prompt and records actual streaming CLI events, including command output.
This is confinement evidence, not semantic model-reliability evidence.

## Established positive and sensitivity control

[Controller loss](evidence/issue-21-provider-loss.json) used actual provider run
`01a081c8-94bf-7c40-99d8-79d69a4e69a1`. The actual mutator invoked a checked-in
helper that double-forked, called `setsid()`, redirected descriptors, and wrote
numbered heartbeat lines every 200 ms while the foreground command remained
active. Host observations retained concrete process IDs/start times and PID
namespaces as well as increasing heartbeat bytes. After controller SIGKILL,
one-second and two-second observations found identical heartbeat bytes and no
remaining writer processes. These observations preceded administrative launch
closure/cessation confirmation. The physical API subsequently confirmed
cessation; the SDK's `runtime_lost` result is retained only as diagnosis.

The [negative control](evidence/issue-21-provider-negative.json) ran the identical
helper deliberately outside product containment. Killing its disposable parent
left the detached writer continuing to append. Exact recorded PID/start-time
identities were used for cleanup. Thus the heartbeat test demonstrably detects
an uncontained survivor. This negative is controlled, not a second actual-model
execution.

## Actual confinement observations

The actual read-only [reviewer](evidence/issue-21-provider-reviewer.json),
[adjudicator](evidence/issue-21-provider-adjudicator.json), and
[final assessor](evidence/issue-21-provider-final.json) execute the probe rather
than merely refuse its instructions. Each retained record checks own/sibling
write denials, shared Git config/ref stability, local network denial, empty-HOME
GitHub credential unreadability, and successful completion of the exact product
graph. These runs use a source repository under `/tmp`; read-only roles deny
its shared metadata writes. The initial local remote was under `/dev/shm` and
not visible within the inner sandbox, so those remote failures alone do not
prove filesystem write denial to an otherwise visible local remote.

The [actual mutator counterexample](evidence/issue-21-provider-mutation.json)
**fails** shared-Git containment: the durable Attempt worktree points to a
fixture source repository under `/tmp`, and workspace-write's existing writable
`/tmp` permission permits shared `git config` and `git tag` mutations. Host
config and refs changed; own-worktree writes succeeded; sibling writes and
network were denied. This is retained as a product/profile gap, not hidden by
moving the failing source. It existed in the inner profile independently of
parent-death containment. The parent surfaced the admission/profile decision.

A separately labeled `--durable-source` mutator control uses G1's non-temporary
source placement and a visible local bare remote in a durable sibling directory.
[Its PASS result](evidence/issue-21-provider-mutation-durable-source.json), run
`01a081cd-244e-74e3-a19f-153c2a6f7b04`, includes actual shared-metadata EROFS and
a remote unpack failure while creating temporary objects. It cannot erase the temporary-source failure
or establish that admission rejects unsafe source configurations.

## Explained preliminary failures

An initial outer `--dev /dev` mount hid `/dev/shm` provider executables/auth homes;
[the initial failure](evidence/issue-21-provider-mutation-initial-failure.json)
never reached actual Codex. Public SDK diagnostics identified `FileNotFoundError`
in the namespace entry. The product containment agent restored the existing
host device/shared-memory view with `--dev-bind /dev /dev`, independently tested
a `/dev/shm` executable regression, and froze the launch profile before the
positive records above. The corresponding initial loss probe timed out waiting
for a writer for the same reason; it supplied no positive evidence.

Two intermediate probes relocating runtime state to long durable paths were
rejected before provider invocation because the SDK controller Unix socket path
was too long. Final runs restored the original short `/dev/shm` profile paths.

Early controlled model fixtures returned `diagnostic: null`, incompatible with
the product's record-shaped diagnostic. These failures are retained as
`*-fixture-failure.json`. The fixture now returns `{}`; product schema and graph
were not changed. The mutator's temporary-source metadata violation remains
real even though its subsequent controlled review also hit this fixture bug.

## Reproduction

Use the pinned SDK Python:

```sh
/home/faviann/.cache/broodling-zeroshot-venv/bin/python qualification/v1-p4/issue21_provider.py --case loss --output /path/to/loss.json
```

Cases are `negative`, `loss`, `mutation`, `reviewer`, `adjudicator`, and `final`.
Add `--durable-source` for the explicitly separate source-placement control.
The harness provisions a private auth-only home and deletes it on cleanup;
authentication contents are never retained. Runtime diagnostic logs and actual
provider command events are qualification evidence, never semantic input to any
Attempt. Source hashes in each record describe its invocation-time harness;
checks added afterward are explicitly marked as such. Product profile identity,
graph/runtime hashes, SDK/sidecar identity, host and occurrence/run references
remain in the machine records.
