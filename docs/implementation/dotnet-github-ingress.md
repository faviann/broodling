# .NET explicit GitHub acquisition and caller proposal input

[D #136](https://github.com/faviann/broodling/issues/136) composes the named GitHub
issue with [A2 admission](dotnet-contract-admission.md). It creates no Attempt,
worktree or native execution. See the
[release/cutover guide](../../deployment/README.md) for current operations.

## Callable acquisition and admission

`GitHubIssueSource.AcquireAsync(reference, cancellationToken)` invokes the
operator's authenticated `gh api` once, using GitHub's issue REST resource and
the retained `2022-11-28` API version. It reads only the explicitly named issue.
Comments, links, repository guidance and prerequisite documents are neither
fetched nor entitled implicitly. The executable defaults to `gh`; an explicit
executable path allows host configuration and a controlled CLI boundary in tests.

Acquisition validates the issue number, issue/repository locators, title/body
shape and stable issue node ID, and refuses pull requests. Complete stdout bytes,
including metadata and formatting, become the primary source without rewriting.
The source records JSON media type, retrieval time and Broodling-policy origin.
The acquired reference retains caller repository pins and gains the verified
issue pin. Supplied or already-retained conflicting upstream identities refuse;
there is no extra repository or comments request to invent additional pins.
Transport failure and malformed responses report safe diagnostics, never CLI
stderr or raw response fragments. A timed-out read refuses after 30 seconds;
caller cancellation cancels acquisition without admitting authority.

`BroodlingStore.AdmitGitHubAsync` accepts the same typed caller proposal and exact
required effects as A2, plus optional explicitly granted supplementary sources.
Only the primary issue actually acquired and validated by `GitHubIssueSource`
receives implicit policy entitlement. All supplementary bytes need caller origin
and an explicit caller grant; these are checked before acquisition. The complete
input-source pins, producer and caller effect fields/order are checked by the
same admission implementation used for supplied sources. Mutable caller
collections are captured before the asynchronous read.

Repeating an identical snapshot and proposal converges after reopening. Changed
source bytes, even metadata-only edits, create a new immutable source/revision;
an earlier admission is never amended. Acquisition and malformed-proposal
failures grant no admission. Source facts captured before a proposal refuses may
remain, and valid unsupported proposals retain their rejected decision/findings.
Use A2 `Status` and `History` for retained observation without a new acquisition.

## Service-owned repository preparation

`BroodlingStore.PrepareRequestBundleRepositoryAsync` is the #107 preparation
seam. It resolves the Work Unit through authenticated `gh api`, validates the
returned GitHub identity and default branch, and acquires a durable bare
repository under the configured service repository root. The accepted endpoint
is derived as the exact canonical
`https://github.com/OWNER/REPOSITORY.git` URL; clone metadata containing another
host, a non-default port, user information, query/fragment, or a non-HTTPS
scheme is refused. A Work Unit's retained repository identity pin is checked before the
preparation is committed, and a newly verified identity is retained with the
Work Unit.

An existing destination is first checked without credentials: it must be a bare
repository whose `origin` is that exact canonical endpoint. A contradictory
directory or origin is refused before any credential-bearing Git operation or
modification. A new bare repository is initialized with that canonical origin,
and every acquisition refresh forwards the explicit
`+refs/heads/*:refs/broodling/upstream/*` refspec, including for bare
repositories. That acquisition-owned namespace is separate from
`refs/heads/broodling/*`, which materialized Attempts own, and from
`refs/broodling/starting/*`, which existing Git custody retains. Refresh pruning
therefore cannot delete or move an active Attempt or a B1 pin. Git HTTPS
credentials are supplied only to the child process as a process-only,
process-scoped Basic `http.extraHeader` using `x-access-token:<token>`; token
values are not persisted in SQLite, Git config or the retained preparation.

Metadata is read again after repository initialization/fetch and before Git
custody retention. A
changed repository identity, default branch or canonical endpoint refuses the
acquisition, so the fetched content cannot be retained under stale metadata.
`GitHubRepositoryError.Retryable` is false for a mismatched identity, pin or clone
URL and an unsupported default branch. It is also false for local configuration:
a misconfigured or unwritable root, a `git` or `gh` that cannot be started, and a
conflicting local repository path or origin. Those need attention. Failed or
changing acquisition is retryable.
Caller cancellation is preserved; an active acquisition process and its owned
process tree are terminated, and the root plus inherited output pipes are
reaped before the operation returns. Git and `gh` acquisition helpers are
synchronously owned by those commands, so no general process-group supervisor
is introduced. Each `gh` metadata read has a 30-second deadline, as issue reads
do. The fetch has no total time limit, so a large transfer is not cut off, but
Git fails it once no data arrives for two minutes (`http.lowSpeedLimit=1`,
`http.lowSpeedTime=120`). An expired metadata read kills its process, and both
end as retryable errors. Acquisitions into the same local repository run one at
a time within the process, so concurrent fetches cannot contend for its ref
locks.

The resulting immutable `RepositoryPreparation` separately retains the default
branch, requested branch ref and exact starting commit. `RegisterRequestBundleRepositoryFile`
constructs the existing Git-blob capture input from that exact commit, so later
refreshes, branch/default changes and worktree changes cannot retarget a
completed bundle. Replaying an already prepared bundle returns its durable
selection without re-acquisition. [Bundle-bound admission](dotnet-contract-admission.md#bundle-bound-admission)
grants the pull-request effect to the retained target branch, and
`AdmitHttpAttempt(submissionId)` consumes this state for that bound Contract. It
visibly refuses an unbound Contract, missing preparation or unsupported retained
Git state. Explicit local `AdmitAttempt` remains unchanged.

## Trusted reviewed-source proposal

`ReviewedIssueProposal` is a small .NET replacement for the existing operator's
reviewed-issue example. Its input is an exact GitHub response file already
reviewed by the operator, not an executable Python import or a plugin. It checks
the acquired bytes against that immutable copy, then uses the complete title and
body as one criterion while preserving all input pins and exact caller effects:

```csharp
var reviewed = new ReviewedIssueProposal(File.ReadAllBytes(reviewedIssuePath));
using var store = new BroodlingApplication().OpenStore(storePath);
var status = await store.AdmitGitHubAsync(
    WorkReference.Parse("acme/widget", 123), reviewed.Propose,
    requiredEffects: [new RequiredEffect("deliver", "Open the selected PR.", "pull_request", "main")],
    constructedBy: "caller");
```

This convenience proposer is only for one self-contained, operator-reviewed
request: prerequisites already met, candidate change/local checks and only the
explicitly selected supported effect. It requires exactly one primary source,
without supplementary sources. For structured obligations/prerequisites or
additional sources, supply an ordinary typed .NET callback instead. Neither path
is a bundled model, general Markdown parser or proof that extraction is complete.
The complete sources remain authority alongside the Contract.

Any byte change—including JSON reformatting or metadata—requires fresh operator
review; the proposer does not silently refresh its reviewed input. F consumes
this boundary for composed operator submission. D adds no separate command or
public HTTP intake. The callable pre-Contract `RequestBundle` capture seam now
retains acquisition inputs, policy and limits with the #105 submission identity,
supports registering references as they are discovered, and seals completed
membership for bundle-scoped reads. #107's repository preparation is limited to
the Work Unit's service-owned repository, default branch and retained starting
commit. The v1 request convention and reference traversal follow.

## Executable Request capture

`BroodlingStore.CaptureRequestBundleAsync(submissionId, repositoryRoot, credentials)`
processes one accepted Issue submission after durable acceptance. It uses the
checkpoints above and #107 preparation, so it adds no second capture lifecycle.
Every GitHub read uses the configured credentials. Durable acceptance never
waits for this operation.

The primary issue body must contain exactly one `<!-- broodling-request:v1 -->`
line directly beneath an ATX Markdown heading of any name or level. Blank lines
may separate them. The section runs from that heading to the next heading of the
same or higher level, or to the body end, and includes nested subsections.
Only a whole, visible line indented by at most three spaces is a marker, so an
inline mention or an indented code example is ignored; indented code also never
opens or forms an HTML comment.
Lines inside fenced code or HTML comments are never headings, markers or
declarations. The exact section text is
retained as the `request` member (`executable_request` source kind), next to the
exact primary issue response (`primary`):

```markdown
## Executable Request
<!-- broodling-request:v1 -->
Add CSV export. Conform to `export-schema`.

### Available references
- export-schema: repo:docs/export-schema.md
- export-example: https://github.com/acme/widget/issues/123#issuecomment-456
```

The optional Available references subsection may contain only
`- label: target` lines. A target is `repo:PATH` (a file at the retained
starting commit) or `https://github.com/OWNER/REPOSITORY/issues/N`, optionally
with `#issuecomment-ID`. Any other non-blank line, a repeated label or target, a
declaration of the primary issue, or a second such subsection is an invalid
declaration. Labels are compared without regard to case. Links anywhere else in the request do not select material.

Traversal is breadth-first in registration order, starting with the
declarations in their written order. An issue reference captures GitHub's exact
issue response (title and body, no comments). A comment reference captures that
exact comment response. Links in captured GitHub reference bodies are followed
only when they are absolute issue or comment URLs of that form, not embedded in
another URL and not continued by a path, query, fragment or file extension.
Pull-request URLs, shorthand such as `#12`, `.`/`..` repository segments,
external links and links inside repository files are ordinary text. Reference IDs are normalized
identities (`repo:PATH`, `github:owner/repository/issues/N[#issuecomment-ID]`),
so repeats and cycles are captured once. The primary issue itself is never
re-captured. Each member's selector records its role and its label or the first
reference that linked to it.

The retained plan records the convention, the traversal and the limits:
50 available references, 1 MiB per captured member, and 8 MiB in total,
including `primary` and `request`. Callers cannot choose other bounds. Capture
resumes or returns only a bundle frozen under exactly this v1 plan. A bundle
begun with any other inputs, policy or limits, even through the generic
checkpoint API, is a `RequestBundleConflict`.
A repository file's size is read from Git first; a file over the per-member
limit or the remaining total is refused without being read or captured. Other
acquired sources are measured before their first capture, so an oversized
source is refused without being captured either.
The following retain a refusal instead of an incomplete bundle:

| Finding code | Cause |
| --- | --- |
| `request_section_missing`, `request_section_multiple`, `request_section_ambiguous`, `request_section_unsupported` | No usable v1 section |
| `invalid_reference_declaration` | A declaration outside the grammar |
| `reference_unavailable` | GitHub reports 410, or 404 while the same credentials can read the object's repository; the response names another object (including a pull request for an issue); or a repository path is not a file at the starting commit |
| `reference_limit_exceeded` | Count, per-member or total size |

A refusal seals the bundle as `refused` with its findings and marks the
submission `rejected`, so Contract association is refused. `GetRequestBundle`
exposes the state, `Findings` and captured membership, and
`ReadRequestBundleReference` reads the members captured before refusal.
GitHub also answers 404 for private objects that the credentials cannot see. So
after an issue or comment 404, capture reads `/repos/OWNER/REPOSITORY` with the
same credentials. The absence is deterministic only when that repository is
readable. Any failure of that read leaves the 404 retryable, with no finding and
no rejection. Other GitHub failures, including 403, rate limiting, 5xx, timeouts
and malformed or incomplete responses, throw `GitHubSourceError` with `Retryable`
true. A `gh` that cannot be started throws it with `Retryable` false: that local
configuration needs attention and is not a refusal. #107 preparation
errors propagate unchanged. A later call resumes without refetching committed
members. A completed or refused bundle is returned without acquisition.

## Validation

`RequestCaptureTests` owns the request grammar, deterministic closure, bounds and
retained refusals, through the same controlled `gh` and real Git/SQLite. It
covers one composed capture and replay, a 404 in an unreadable repository resumed
as retryable after upstream edits, and one refusal case per grammar and
acquisition policy, including 404 in a readable repository and 410.
`GitHubAdmissionTests` and `RepositoryPreparationTests` run controlled local
`gh` and Git executables through the real process boundary and real SQLite
admission, with no network or provider calls. Preparation tests cover canonical
metadata/identity refusal, pre-credential origin validation, explicit bare
refspec refresh, process-only Basic Git auth, durable replay, a concurrent
identity-pin race and a bound Attempt that keeps its retained target and
starting commit after upstream moves. `RequestAdmissionTests` owns bundle-bound
admission; see [admission](dotnet-contract-admission.md#bundle-bound-admission).
It checks exact captured bytes and one explicit request, identity and response
refusals, safe transport errors/cancellation, explicit supplementary grants,
reviewed-source byte pins, immutable replay after reopen, changed-source lineage
and effect snapshots across the asynchronous read. It also replays the retained
[#75/#82 issue fixtures](../../tests/fixtures/ingress/README.md) for rejected and
accepted admission without execution. Detailed Contract refusals remain in A2.

On 22 September 2026, `dotnet test --solution Broodling.sln --no-restore` passed
**69 tests, 0 failed, 0 skipped**, using .NET SDK 10.0.401. These checks establish
acquisition/admission behavior, not live GitHub delivery or model quality. The
governing operator-supervised internal-use and independent human review
limitations remain unchanged.
