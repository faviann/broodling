# Issue #18 — trusted evidence seam decision

**Status: BLOCKED pending a boundary decision.**
#17 is complete at `25c496df9ed33715d3cbbff18d77f07a581ba565` on `origin/main`.
This record does not complete #18 or begin #19/#20.

## Established facts

The governing [target v0.5](../../docs/governing/broodling-target-responsibility-boundary-design-v0.5.md)
section 2.1 states that evidence availability comes from admitted graph state
and trusted producers. Section 7.2 requires known mechanical evidence to be
produced or verified by graph-local trusted/controlled operations. Semantic
sufficiency remains a separate designated judgment.

The exact historical [W3 leaf](../v1-p1/w3-bin/codex) implemented availability
using a deterministic `evidence/raw.txt` existence check. The
[W4/W6 leaf](../v1-p1/issue10-bin/codex) likewise checked that fixed file and
selected wrong-population/host/mode/artifact/contradiction final gaps by fixture
scenario. Those controlled substitutions established the recorded graph
mechanics, not a product mapping from arbitrary frozen Contract obligations to
trusted observations.

The current product has read-only evidence agent occurrences with a typed
availability signal. Its qualified launcher applies sandbox/session settings
and forwards the Codex invocation; it does not implement trusted artifact
selection or validation execution.

The pinned native runtime declaration at Zeroshot
`d0909615d6ba3c179b58bce15a059f40400ec995`,
`crates/openengine-cluster-protocol/src/native_v2_run/runtime.rs`, defines only
`Agent` and `GitDelivery` node bindings. The parent also exercised the public
submission API on an isolated P2 test Attempt with a proposed `command` binding.
The Python wrapper accepted the opaque RuntimePlan dictionary, but the actual
sidecar rejected it before a run was returned:

```text
InvalidRequestError: ... unknown variant `command`, expected `agent` or `git_delivery`
```

The installed SDK/sidecar assertion passed for SDK `0.1.0.dev0`, SDK source hash
`0263b63cb6c6991703f699919ea974ba502da23e3a14ab7d5ab8c5d5ac3b256e` and sidecar hash
`9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86`.

Current Contract admission accepts nonempty `validationSeam` and
`validationAction` strings and finite `enumerated`/`declared_surface` populations.
It defines no command grammar or required artifact-path field. For example,
the admitted fixture's population member is
`tests/test_work_unit_identity.py::CanonicalIngressTests`, its seam is a Python
API name, and its action is a unittest command. Treating every population member
as a pathname, or every action as shell syntax, would assign meaning that the
Contract model did not establish.

## Unresolved question and options

How should the product supply the trusted graph-local evidence operation and
its explicit check/artifact inputs without silently changing existing admitted
obligations or the qualified runtime boundary?

The bounded proposed option surfaced to the user is a deterministic read-only
evidence leaf inside the existing runtime-dispatched agent-harness invocation,
with explicit frozen check/artifact declarations and affected containment and
mechanical controls. The runtime would still dispatch the occurrence, validate
its typed response and own its lifecycle. Raw bytes and observed check/context
would flow directly into the existing graph, not an archive or observer.
This is a proposal awaiting authorization, not a selected implementation.

Another option is to qualify a native trusted runtime evidence operation first;
that would require a new runtime capability rather than the current pinned
configuration. A governing decision may instead be required before selecting
either approach.

Model-reported availability, a new interpretation of free-form fields, or a
controlled test fixture alone is not silently substituted for the governing
trusted-producer requirement. No admitted obligation is dropped or amended.

## Independent work and stop boundary

Reviewer instruction and context-isolation work can proceed independently on
the existing profile. Its pre-integration controls substitute the evidence
producer and cannot establish completed #18 evidence integration. They must not
be presented as a G3 verdict or as proof that the missing producer exists.

The completed [partial reviewer record](issue-18-reviewer-preintegration.md)
retains ten passing real-reviewer controls and the initial failed fixture control.
The parent independently inspected the actual typed responses, verified source
hashes, and compared the product graph with #17: only the two reviewer instruction
strings differ; topology, types, bindings, routing, runtime and sandbox/session
settings are unchanged. [Focused regression checks](evidence/issue-18-focused-tests.txt)
passed: **33 tests and 1725 subtests**. Changed-file Ruff checks also passed.
No broad reviewer-correctness or evidence-production claim follows from that
partial result; its report records a false extra finding in the contaminated case.

Evidence implementation and #18 completion remain blocked on the question above.
#19, #20, P4 and later work remain unstarted. No native runtime extension,
launcher evidence interceptor, new Contract evidence grammar, semantic store,
response validator, archive, observer or recovery behavior was implemented by
this investigation.
