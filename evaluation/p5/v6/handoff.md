# Issue #83: R01 consumed, settled FAIL / FA

The [final evidence](../runs/2026-09-19-v6-r01/README.md) settles completed run
`01a0b9ee-476e-7b02-a216-ae885ac1db4b`. Its accepted revision
`248d67d35fc8fe6ac5dba9a0fb8cae831ae22631` passed the frozen automated checks
but failed two independent full-criteria reviews. Native outcome and Broodling
disposition remain SUCCEEDED; evaluator class is FA.

Do not run `start`, `finalize`, `replay` or native/provider work for this consumed
slot. Do not remove its start marker, reset the store, replace its Attempt,
repair/publish its PR, or use its output as another trial's input. R02–R08 remain
NOT_STARTED and no v6 continuation is eligible. The
[skeptical #66 review](../reviews/2026-09-19-issue-66/README.md) is COMPLETE
with scoped P5 verdict FAIL; broader release readiness remains NOT ASSESSED.

The original store/source/workspace, stopped corrected target container/image,
target state/home and dispatched Attempt remain quarantined. The actual PR
repository/base/head were corroborated. B1 and the exact receipt head survive
in the retained bundle and recovered bare Git repository outside the execution
checkout; a consistent store backup is retained separately. V5's stopped
resources remain retained; its container state and partial PR were reverified
unchanged, and its original state/home were not modified.

Read the retained receipt/disposition, GitHub readback, frozen judge output,
independent reviews and classification. The existing completed-result/reopened-
store records need not be replayed. Offline reproduction commands and isolation
settings are in [judge-execution.json](../runs/2026-09-19-v6-r01/R01/judge-execution.json).
The original pre-admission handoff remains in Git history at `a39445a`.
