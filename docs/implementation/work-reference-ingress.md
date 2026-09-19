# Work-reference to Contract ingress

`ContractIngress` implements [#75](https://github.com/faviann/broodling/issues/75)
up to a stored admission decision. It creates no Attempt, workspace or execution.
The [current architecture](../governing/current.md) and
[#74 first-use decision](https://github.com/faviann/broodling/issues/74) govern this
path. The latter permits limited first-use implementation while retaining the
scoped P5 **FAIL**; it does not establish reliable semantic acceptance.

## Three responsibilities

1. `acquire_github_issue` reads one explicit `WorkReference` using the installed,
   authenticated `gh api` CLI. It validates the issue locator, repository locator,
   number and stable issue identity, rejects pull requests, and preserves the
   exact REST response bytes. `ContractIngress` resolves the Work Unit and records
   the primary issue snapshot under existing Broodling entitlement policy.
2. A caller-supplied callback receives `ContractProposalInput`: immutable Work
   Unit/source records, source pins, the caller's exact required effects, and the
   declared producer. It returns the existing typed `Contract`. This may be caller
   code, a structured Contract supplied by a caller, or a model adapter. There is
   no bundled model, provider selection or general Markdown parser.
3. Ingress checks the proposal's structure, producer, Work Unit, complete source
   attribution and exact effects, then uses `record_contract_revision` and `admit`.
   Existing Closability policy determines supported capabilities. Unsupported
   effects, missing PR targets and unsatisfied prerequisites stay in rejected
   Contracts. Ingress never repairs a proposal by deleting a requirement.

The required `required_effects` argument is trusted caller authority, including
when it is explicitly `()`. It is snapshotted as an entitled caller statement
before extraction. A model cannot add, remove or retarget an effect, even to
another otherwise supported PR branch. This uses the current required-effect
model; it adds no separate permission ontology. Repository authority comes from
the Work Unit; B1 is still established by later Attempt admission.

Every input snapshot, including the caller statement, must be attributed at its
exact digest. The statement also preserves that the complete source governs scope
and acceptance: extracted criteria supplement it and cannot waive its
requirements. A different, previously entitled snapshot cannot be substituted.
Source acquisition and semantic proposal remain separate; the model output has
no source-entitlement channel.

## Use

For an issue already reviewed to require only a candidate change, local checks
and the explicitly authorized PR, a caller can use its complete prose as one
criterion. A more detailed proposer can lift the existing Completion/Acceptance
sections verbatim and represent other obligations and prerequisites using the
existing Contract fields. It must inspect the complete request, including scope
restrictions; unsupported requirements must remain visible to admission.

```python
import json

from broodling import (
    BroodlingStore,
    Contract,
    ContractIngress,
    Criterion,
    RequiredEffect,
    WorkReference,
)


def propose_reviewed_local_change(inputs):
    primary = next(source for source in inputs.sources if source.kind == "primary_issue")
    issue = json.loads(primary.content)
    return Contract(
        work_unit_id=inputs.work_unit.work_unit_id,
        source_attribution=inputs.source_attribution,
        criteria=(Criterion("request", issue["body"]),),
        required_effects=inputs.required_effects,
        constructed_by=inputs.constructed_by,
    )


with BroodlingStore.open("/srv/broodling/state/broodling.sqlite3") as store:
    result = ContractIngress(store).from_github(
        WorkReference.parse("acme/widget", 123),
        propose_reviewed_local_change,
        constructed_by="caller",
        required_effects=(
            RequiredEffect("pr", "Deliver a PR targeting main.", "pull_request", "main"),
        ),
    )
    print(result.revision.contract_revision_id, result.decision.outcome)
    for finding in result.decision.findings:
        print(finding.code, finding.preserved_obligation)
```

`from_sources(reference, sources, propose, required_effects=...)` accepts exact
`SourceSubmission` bytes directly, with no JSON/Markdown dependency in Contract
construction. The current Work Unit profile still requires one primary issue.
Additional sources in either method need their own explicit `SourceEntitlement`;
links, issue comments and repository guidance are never fetched or entitled
implicitly. This allows callers to freeze a relevant decision or prerequisite
record without granting authority to everything the issue links to.

An external model adapter must decode its output to the existing dataclasses;
ingress validates their runtime types as well as nonempty, unique identifiers and
nonempty statements. It cannot claim a different `constructed_by` value than the
caller selected (the default is `model_extraction`). A caller supplying a typed
Contract can return it from the callback after binding these source pins.

## Reproduction and limits

The result exposes the Work Unit, all entitled snapshots, immutable revision and
stored decision. Repeating identical source bytes and proposal meaning resolves
the same source/revision/decision identities, including after reopening the store.
Changed issue bytes or proposal meaning create new records; they cannot amend an
earlier admitted revision or its Attempt. Replaying a recorded decision needs no
GitHub fetch or extraction. Re-running a model is not promised to reproduce its
earlier proposal. Even a metadata-only change to the captured REST response is a
new source snapshot.

Acquisition, entitlement and malformed/authority-changing proposal failures
raise before Contract admission. Already captured sources remain durable facts.
A valid structured proposal lacking supported capabilities receives the existing
durable rejected decision. An interruption after revision storage but before its
decision leaves a non-admitted revision that `store.admit(revision_id)` can decide.

Deterministic admission validates the declared structure and capabilities; it
cannot establish that natural-language extraction is complete or correct. The
caller/proposer must expose unsupported obligations and unresolved prerequisites,
and conflicting source requirements require handback. Keeping complete sources
prevents a summary from replacing authority, but does not prove model obedience.
Under #74, any subsequently delivered PR still needs independent human/operator
review of its exact revision against the frozen request and Contract, with
appropriate tests/CI, before a separate merge decision. `SUCCEEDED` is not semantic
certification. No-effect admission retains the existing unsupported stable-result
limitation. Invocation composition (#76), deployment packaging (#77), execution
and P5 evaluation are outside this change.

The offline ingress tests use retained current Broodling issues
[#75 and #82](../../tests/fixtures/ingress/README.md), with explicit fixture
proposals, to demonstrate reproducible admitted/rejected results and the authority
boundaries. They make no provider-quality or live-delivery claim.
