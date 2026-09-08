# Issue #23 — bounded repair-machinery diagnosis

Status: **Diagnostic chain supported; #23 OPEN/BLOCKED; #24 not begun.**
Product baseline: `a661948455cffe52c3d7d87b023b27f3b4d11d68`.
No integrated real-provider witness or G4 PASS is claimed.

## Question and conclusion

The user requested inexpensive diagnosis before another full real-provider
fixture: is the downstream repair machinery defective, or do initial
implementations simply keep satisfying the Contract before review?

One bounded diagnostic chain completed in approximately 217 seconds. Actual
review, adjudication, repair, fresh review, resolution and repaired final
assessment successfully handled a real, predeclared duplicate-header defect.
No specific review/adjudication/repair/resolution weakness appeared in this case.
The prior natural CSV implementations also satisfy the visible Contract; their
clean reviews were substantively justified. Taken together with existing
controlled routing and disposition tests, the evidence narrows the remaining
blocker primarily to the required **all-real integrated repair witness**.

Another full attempt now has limited incremental diagnostic value. It could
still establish the missing conjunction: an actual initial implementation leaves
a real defect and the same run repairs it through durable disposition. This
investigation cannot estimate how often that happens, guarantee semantic
reliability, or waive that conjunction. Repeated fixture fishing is not justified
by a newly discovered technical defect. No additional provider runs were started.

## Method and provenance

The [diagnostic driver](issue23_repair_diagnostic.py) reuses the frozen
[natural fixture](issue23_natural_provider.py) admission, observer, finalization,
retention and physical cleanup seams. Its predeclared B1 is almost-complete CSV
code missing only normalized duplicate-header rejection. The same frozen CSV
Contract and three valid-input smokes apply. The defect predates admission.

A [diagnostic executable shim](issue23-diagnostic-bin/codex) supplies only a
controlled no-op implementer response, leaving B1 intact. Every subsequent model
node receives its original product prompt and arguments through the actual
`/home/faviann/.local/bin/codex`, version 0.153.4, using the existing launcher,
model/effort settings, containment and read-only/mutation separation. Official
pinned SDK/sidecar, product graph and runtime definitions remain unchanged.
No downstream finding, directive, repair or authority response is substituted;
no post-admission candidate tampering occurs.

The shim is an explicit diagnostic executable substitution. The inherited
`actualProvider`/profile executable path identifies that shim. Reuse of qualified
launch settings does **not** make this an all-real qualified product witness or
requalify the shim. The driver hardcodes `integratedRealRepairWitness: false` and
`passed: false`, even when inherited structural checks all pass. In particular,
`allRequiredActualProductRolesObserved` only checks role presence and cannot
establish actual-provider provenance despite its inherited name.

Execution was bounded to one chain with a 900-second total wait and the existing
product repair bound. It used one repair occurrence and seven actual model-node
occurrences, plus the controlled implementer and two deterministic evidence
occurrences. No second chain or full natural fixture was launched.

## Retained result

Run: `01a082c0-c2c3-7d53-9629-c3c991a8f707`.
Contract: `cr-41ed72fb809d4287a9125a7ea6462e98e3d26fd6da183c6dd425726eeb647bd3`.
[Raw JSON](evidence/issue-23-repair-diagnostic.json),
[retained database](evidence/issue-23-repair-diagnostic.sqlite3),
[independent executable audit](issue23_diagnostic_audit.py),
[audit results and hashes](evidence/issue-23-repair-diagnostic-audit.json).

| Boundary | Actual diagnostic observation |
| --- | --- |
| Initial evidence | All three fixed smokes pass despite the duplicate-header defect. |
| Initial review | `found`: reviewer identifies normalized duplicates and silent dictionary key overwrite; explicitly recognizes smoke insufficiency. |
| Adjudication | `open_d1`: substantive directive requires duplicate normalized headers to raise ValueError before data processing; preserves other behavior and checker. |
| Repair | One actual mutation occurrence adds the two-line set-size uniqueness guard after normalization. Two null response log messages share that same execution; they are not two repairs. |
| Renewed evidence | The unchanged checker passes all three cases on newly captured repaired material. |
| Fresh review | Distinct occurrence returns clean and explains satisfaction beyond the smoke population. |
| Resolution | `resolved_d1`: retained raw command logs show additional duplicate/empty-header, width, blank-row, cell-preservation and quoted-newline checks, all successful. |
| Final authority | Repaired final assessor returns accepted with criterion-level rationale. |
| Disposition and custody | Normal durable SUCCEEDED; exact custody/disposition reread after physical cessation and disposable fixture cleanup. |

The parent independently executes inspected retained source on 12 focused inputs.
Before repair, all three normalized-duplicate probes demonstrate the real
violation (data, header-only and Unicode-whitespace collision); the other nine
pass. After repair, all 12 pass. Both declared smoke populations pass. Checker
and README remain unchanged. This demonstrates a semantic correction beyond
nonempty text or an AST difference. These extra diagnostic checks confer no
product acceptance authority and do not amend the Contract's evidence declaration.

## Independent review, compatibility and limits

A fresh read-only subagent inspected graph routing, governing role prompts,
previous records, diagnostic methodology and final raw evidence. It independently
confirmed the objective before/after violation, actual supplemental resolution
commands, one structural repair, unchanged checker/README and durable readback.
All 32 retained source hashes match current files. No execution, diagnostic-log
or cleanup error is recorded. Stores remain outside candidate and ambient `/tmp`.

Static inspection found no route barrier: findings reach adjudication, an open
directive reaches repair, and renewed evidence and independent review precede
explicit resolution. The [existing disposition controls](issue-23-disposition.md)
already cover controlled repair and fault/race behavior. Historical
[actual reviewer evidence](../v1-p3/issue-18-reviewer-preintegration.md) also contains
genuine insufficiency findings; those records are not relabeled current proof.

One positive seeded case does not measure error rates, establish robust negative
resolution behavior with actual models, exclude correlated model judgment errors,
or prove every repair situation. Most critically, no real initial implementation
created the defect here. This diagnostic therefore earns **no credit** toward
#23's integrated actual-repair requirement.

Only new qualification files and the README status link changed. Product,
tests, graph, runtime and qualified launcher/profile code remain unchanged;
historical qualification and governing records are preserved. Ruff checking,
format checking and `git diff --check` pass. The focused retained-source audit
passes; no full regression rerun was needed for this diagnostic-only addition.
Previously retained regression results remain scoped as originally reported.

Recommendation: keep the requirement and blocker explicit; do not launch another
full fixture merely to search for a machinery defect this investigation did not
find. Deciding to change the gate requirement would be a governing decision for
the user, not an inference from this diagnostic. #23 remains open and #24 blocked.
