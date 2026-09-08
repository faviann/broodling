# V1-P4 explicit original-B1 replacement — issue #22

This boundary follows #21's durable abandonment, proven physical cessation and
owned retirement. It creates an explicit fresh Attempt under the same Work Unit,
immutable Contract and original admitted B1. It does not resume the abandoned run.

## Public seam

The host supplies fresh validated profile directories and an explicit retry ID:

```python
profile = QualifiedCodexProfile(real_codex, empty_home, auth_only_codex_home)
submitter = ZeroshotSubmitter(runtime_state_dir, codex_profile=profile)
coordinator = RetryCoordinator(
    store, AttemptProvisioner(store, durable_workspace_root), submitter
)
replacement = coordinator.retry(abandoned_attempt_id, explicit_retry_id)
```

`allocate` commits retry identity, original bindings and exclusive worktree
allocation before host work. `prepare` uses the existing recorded-B1 provisioner
and `prepare_assurance`. `retry` additionally crosses the existing public SDK
submission/correlation boundary. The product neither creates credentials nor
copies them into fresh homes; those are explicit host-provisioned inputs.

Repeating the same retry ID returns its same successor. Changing predecessor,
workspace root or runtime target conflicts. A different request cannot replace
that predecessor again. Historical duplicate allocation can identify old A2,
but currentness checks prevent reprovisioning or submitting an abandoned A2.
Continuing after A2 abandonment requires a separate explicit retry naming A2,
after its own safe retirement. There is no automatic retry policy.

## Durable facts and exclusion

Schema 7 adds `attempt_retries`: immutable retry ID, unique predecessor/successor,
workspace root, target JSON and request time. Its SQL gate requires an abandoned,
ceased and retired predecessor and no current competitor. Original Work Unit,
Contract revision, repository/commit/material digest and revision description
must match. Existing schemas and historical rows migrate without identity changes.

The new HOME is canonical and empty; CODEX_HOME is canonical and contains only a
regular `auth.json`. Both are disjoint from retained homes, runtime directories,
other Attempt enclosures and shared Git/store paths. Reservations apply before
submission exists. Ordinary admission/provisioning/submission cannot consume
reserved homes through workspace, runtime-state or declared launch paths.

Git creation and retirement share the existing enclosure lock. The creation
child inherits it through caller death; repository checkout hooks are disabled.
The exact original commit is used with replacement refs disabled. No live HEAD
lookup chooses replacement B1. Missing original objects or frozen material fail.

## Frozen instructions and supported checkout behavior

The initial typed `admittedInstructions` array comes only from verified snapshots
attributed to the Attempt's original Contract. Records contain source ID, kind,
locator, media type, SHA-256, encoding and content. UTF-8 content preserves exact
bytes; non-UTF-8 content is explicit base64. New issue snapshots do not participate.
Only the implementer receives this field, and no worker can write it. Repair and
assurance roles retain their existing narrow inputs. The frozen Contract governs
the admitted source material; no candidate, evidence, directive, acceptance or
provider history from the predecessor initializes the successor.

The single-host V1 source profile excludes Git checkout transformations. Effective
configuration and attributes are inspected against the pinned tree before
admission status, provisioning and initial clean-B1 checks. External conversion
drivers, active byte-conversion attributes, incompatible conversion/symlink/sparse
settings, fsmonitor and conditional configuration are refused. The implementation
does not normalize files or seal/prove later candidate versions. The #21 `/tmp`
shared-Git restriction and containment launcher remain in force.

The exact pre-instruction graph accepted in #21 remains stoppable through a
pinned graph digest plus unchanged runtime/target and physical cessation checks.
This exception only permits administration. It does not authorize replaying old
graph state into A2 or recovering old semantic results.

## Fault and completion boundaries

Allocation, provisioning, submission preparation and accepted acknowledgement
loss have retained crash controls. Repetition after valid dispatch correlates
the same run even after graph-authorized mutation; clean B1 is required before
first dispatch, not after that run has advanced its candidate.

Late predecessor callbacks recheck durable currentness. Old custody remains
readable for diagnosis, but cannot restore eligibility or alter A2. Direct races
against unfinished stop/retirement and ordinary provisioning are tested with
independent stores and held boundaries.

See [acceptance evidence](../../qualification/v1-p4/issue-22-replacement.md).
This issue does not decide Work Unit disposition, promote retained custody to
success after restart, deliver effects, recover completed-run history, or add a
scheduler, session manager, candidate seal or second runtime authority.
