# P5 v5 setup and R01 handoff

The corrected protocol freeze is
`52eb3569b3671baa37426792a67b50058e2d223f`. Its selected gateway base URL is exactly
`https://cliproxy.local.faviann.com/v1`, without a trailing slash. The
[retained setup and baseline](../runs/2026-09-19-v5-setup/README.md) record compatible
product/dependency/test identities. The frozen v5 protocol, v1-v4 material and
earlier evidence packages are unchanged.

## Prepare or verify the inputs

Run the tracked v5 helper with the supported Python environment:

```bash
.venv/bin/python evaluation/p5/v5/setup_r01.py \
  --evidence "$PWD/evaluation/p5/runs/2026-09-19-v5-setup" \
  --state-dir /home/faviann/.local/share/broodling-p5-v5/r01-inputs \
  --repository faviann/broodling-p5-v3-20260917 \
  --github-issue 1 \
  --direct-target-origin http://127.0.0.1:18767
```

Without `--github-issue`, preparation is entirely offline: reconstruct the frozen
two-file B1, retain the frozen R01 issue text and fixed execution profile, and
initialize eight `NOT_STARTED` slots. With that option, read-only GitHub checks
verify the existing private repository, target branch and issue before creating
local entitled source, Work Unit and criteria-only Contract records. The helper
never creates or changes a GitHub issue, repository, branch or PR. Its local
source contains only `README.md` and `tiny.py`; judging material stays outside it.

Setup never admits a Contract, provisions an Attempt, dispatches a run, contacts
the gateway or starts a DirectTarget. It does not inspect or retain credential
values; GitHub authentication stays with `gh`. A recorded DirectTarget origin is an intended destination, not a running
target verification. New records use v5 identities; prior run directories and
started slots cannot be reused. Identical setup may be verified again before
admission; changed material is refused rather than overwritten.

The historical v4 setup/driver scripts bind the obsolete v4/OpenAI profile and baseline.
Do not execute them for v5. No historical record is migrated or rescored.

Keep the retained #81 setup package immutable. #79 uses a new live-evidence
directory for trial accounting and results, referencing/copying the verified
input records and zero-start allocation. The prepared durable store/source and
Contract remain the inputs; no new Attempt is substituted after admission.

## Remaining live prerequisites for #79

1. Separately authorize the one R01 execution and keep the owner actively
   supervising with external stop access. This setup and closure of #81 do not
   authorize admission. There is no added P5 dollar or wall-time ceiling.
2. Start or supply a compatible isolated DirectTarget with durable state and
   sufficient quarantine capacity. The previously inspected target was stopped.
   Preserve historical target state/home; a new v5 target can use the existing
   pinned image with fresh state/home directories. Verify the selected origin's
   native discovery and empty run inventory using the SDK with `environment={}`.
   Check native/Codex compatibility and target DNS/TLS reachability to the gateway;
   the operator's host-side `/v1/models` result does not establish these facts.
   Do not launch an extra provider task as a preflight check.
3. Supply exact `GATEWAY_BASE_URL=https://cliproxy.local.faviann.com/v1`, a current
   nonempty `GATEWAY_API_KEY` and a current GitHub delivery credential as `GH_TOKEN`
   with authority for the designated private repository's native branch/PR
   effect. The existing authenticated `gh` session provides read access, but its
   delivery authority must be checked before using its token ephemerally. Pass
   the three values only through the product's dispatch arguments/SDK environment;
   never retain values in command transcripts, setup records or evidence.
4. Recheck the recorded baseline against the actual product, dependencies and
   tests, and verify the private fixture, open issue's exact frozen body, target
   branch at B1, no prior PRs, zero admission/Attempt/submission rows and all eight
   `NOT_STARTED` slots immediately before admission. A source or profile change
   requires compatible evidence, not relabeling this record.

After those checks, #79 owns the live execution through the current Broodling
Python APIs. Retain its fresh preflight, record R01's start immediately before the
first admission attempt, then admit/provision/submit once. A setup failure before
that boundary consumes no slot; a started Attempt cannot be replaced or rerun.
Use the fixed gateway `ZeroshotSubmitter` inputs, standard `software-change` and
native PR delivery. Reconnect from the durable correlation without credentials,
retain the exact native receipt/head revision and Broodling disposition, and
independently judge that exact revision with the frozen judge. Preserve the
dispatched Attempt's quarantine and stop before R02. #68 remains gated on
compatible successful R01 evidence.
