# Work-reference to Contract ingress

The .NET store exposes two admission paths, both ending at an immutable
decision without creating an Attempt or dispatching execution:

- `BroodlingStore.AdmitSources`: [supplied-source API and proposal validation](dotnet-contract-admission.md).
- `BroodlingStore.AdmitGitHubAsync`: [explicit acquisition and reviewed-source input](dotnet-github-ingress.md).

Both accept a typed `ContractProposalInput` → `Contract` callback and mandatory
exact caller `requiredEffects`, including an explicit empty collection for no
effect. The callable examples and detailed refusal behavior live in those
references. For the composed submit/resume path, use [Invocation](invocation.md).

## Work-reference identity

`WorkReference.Parse` accepts supported HTTPS, SSH, scp-like,
schemeless `host/owner/repository`, and bare `owner/repository` forms. Host, owner
and repository name are case-insensitive; an optional `.git` suffix or trailing
slash does not change identity. The issue can be a positive signed 64-bit integer,
decimal string, `#n`, or canonical issue URL. Optional opaque upstream repository
and issue IDs pin independently and conflicting known identities refuse.
See [identity and custody](dotnet-identity-custody.md) for persistence and Unicode
boundaries.

## Authority and evidence

Only the explicitly acquired and validated primary issue receives policy
entitlement. Supplied sources and supplementary material need explicit caller
grants. On these admission paths, links, comments and repository guidance are
not implicitly fetched or entitled; the separate
[RequestBundle capture](dotnet-github-ingress.md#executable-request-capture)
acquires only declared references and their bounded GitHub link closure. Every exact source digest, the declared producer and all caller effect
fields must remain in the proposal. Unsupported obligations/prerequisites remain
visible to admission; a proposer cannot delete them to obtain acceptance.

`ReviewedIssueProposal` compares the complete acquired response with the
operator-reviewed file byte for byte, then preserves its title/body as a criterion.
It is for a self-contained reviewed request; richer obligations or additional
sources use the typed callback. There is no bundled model or general prose parser.

Equivalent source bytes and Contract meaning converge after reopen. Changed
bytes, including metadata, create new facts. Already captured sources survive
a later malformed proposal. A valid but unsupported Contract retains a rejected
decision. An undecided revision grants no admission authority.

Deterministic admission does not prove complete natural-language extraction or
semantic correctness. Complete frozen sources remain authoritative alongside
the Contract. [P5's scoped FAIL](../../evaluation/p5/README.md) and independent
human review of the exact delivered revision still govern subsequent execution.

The [retained issue fixtures](../../tests/fixtures/ingress/README.md) are replayed
offline by `GitHubAdmissionTests`; they establish neither live delivery nor
provider quality. The prior Python ingress is
[dated baseline history](https://github.com/faviann/broodling/blob/b3f61a96c40401722ec16fc361958d1690982e02/docs/implementation/work-reference-ingress.md).
