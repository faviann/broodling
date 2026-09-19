# Issue #79: last point before R01 admission

The [preparation record](../runs/2026-09-19-v5-r01/pre-admission-verification.json)
records the current readiness decision. Preparation grants no live authorization.
R01-R08 stay `NOT_STARTED` until a separately authorized start action below.

Use this checkout's supported `.venv` and the existing ephemeral environment:
exact `GATEWAY_BASE_URL=https://cliproxy.local.faviann.com/v1`, current nonempty
`GATEWAY_API_KEY`, and `GH_TOKEN`. Do not put credential values in command lines,
files, transcripts or evidence. The driver rejects conflicting legacy credentials.
The gateway URL and DirectTarget origin `http://127.0.0.1:18767` have different roles.

Read-only final verification (safe before authorization):

```bash
.venv/bin/python evaluation/p5/v5/run_r01.py check
```

Only after separate authorization for #79's one R01 execution, with the owner
actively supervising and able to stop the target, start it with:

```bash
.venv/bin/python evaluation/p5/v5/run_r01.py start --authorize-r01
```

This performs another read-only preflight, records the counting boundary,
admits the original Contract, provisions its one Attempt and submits the frozen
gateway/native-PR invocation. It writes only the new v5 live evidence directory
and original prepared durable store/workspace. The preserved #81 package stays
immutable. No R02 operation exists in this driver.

Once `R01/execution-start.json` exists, R01 counts as started even if admission
fails or the process crashes. Never rerun `start`, replace its Attempt, delete
the marker, reset its slot or reuse a historical v4 driver. If acknowledgement
is lost, inspect the original durable store and retained invocation before any
reconciliation. This driver intentionally provides no dispatch replay command;
the frozen protocol permits at most two separately recorded same-request/key
acknowledgement reconciliations, never a new invocation or Attempt.

After successful durable correlation, a fresh process can observe/finalize the
same run without credentials:

```bash
env -u GATEWAY_BASE_URL -u GATEWAY_API_KEY -u GH_TOKEN \
  -u OPENAI_API_KEY -u OPENAI_BASE_URL -u GITHUB_TOKEN \
  -u ANTHROPIC_API_KEY -u GEMINI_API_KEY -u GOOGLE_API_KEY \
  .venv/bin/python evaluation/p5/v5/run_r01.py finalize
```

The driver resolves the sole Attempt and persisted origin from the original
store, logs each caller reattachment before contacting the target, and refuses
a third reattachment. It retains the native result separately from Broodling's
disposition/receipt and stops with independent judgment still pending. Raw
provider failure text is omitted from driver evidence to avoid retaining secrets;
failure type and the durable product state remain available for classification.

For an operator stop, use the same credential-free prefix with `stop` replacing
`finalize`. This abandons the original Attempt and requests the known native run's
stop without replay. A refusal reporting unconfirmed physical cessation is
expected for a dispatched Attempt: retain its quarantine. External containment
is available with `docker stop broodling-p5-v5-r01-target`; preserve its target
state/home and the original source/store/workspace. Neither native stop nor
external containment authorizes deletion or a replacement.

After a receipt-backed disposition, archive the exact `headRevision` Git object
and original B1 outside the execution checkout. Verify the actual GitHub PR's
repository, base branch and exact receipt revision, then run the frozen judge
against that archived repository and retained receipt. Independently review all
original scope/behavior criteria; automated checks alone are insufficient.
Retain judgment, classification, relevant database readbacks and quarantine
evidence, and update the live allocation honestly. Stop before R02; only
compatible successful R01 evidence can unlock #68.
