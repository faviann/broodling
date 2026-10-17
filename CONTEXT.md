# Broodling

Broodling's language for supervised software-change proposals and the planned
homelab HTTP intake.

## Language

**Work Unit**:
One software-change request identified by a repository and its primary issue.
Its identity persists across Issue submissions and Contract revisions.

**Issue submission**:
A durably accepted request for Broodling to prepare and seek admission for a
Work Unit's work, retaining any resulting Contract and Attempt lineage. A later
Issue submission explicitly names its predecessor and leaves the earlier frozen
authority unchanged.

**Submission cancellation**:
The durable withdrawal of permission for an Issue submission to progress. It
does not prove physical cessation of any execution that has already started.

**Executable Request**:
The complete, current description of a Work Unit's requested outcome, completion
criteria and scope, from which a Contract is constructed. For an Issue
submission it is the primary issue's whole body. The request defines the work
and can be edited as it is clarified.

**RequestBundle**:
The immutable request environment containing an exact Executable Request, the
primary issue it was taken from and its starting source revision.

**Contract**:
An immutable, source-attributed statement of what a Work Unit must satisfy,
including its criteria, obligations, prerequisites and required effects.

**PR authority**:
Permission to deliver one proposal as a pull request to a named branch of the
Work Unit's repository, including the proposal's commit, push and PR creation or
update.

**Execution asset**:
The immutable workflow and runtime material Broodling approves for native
execution of an Attempt. It is distinct from that Attempt's task, source,
credentials and permission to perform effects.

**Environment**:
An operator-approved name for one adopted image release in which an Attempt's
native execution runs. Repositories select an environment by its exact name.

**Execution target**:
The one DirectTarget serving one environment at its own origin, with its own
native state and home. Work selecting the same environment shares it.

**Environment declaration**:
The versioned repository file, read at an Attempt's original starting state,
that names the environment its work requires. It supplies execution context,
never Contract authority.

**Environment resolution**:
The mapping, fixed once at an Attempt's preparation and retained with its
Prepared submission, of its environment requirement to an exact adopted
environment and execution target. Later catalog or repository changes do not
alter it.

**Starting revision**:
The exact repository revision selected as the source starting point for the
requested work, independent of later branch movement.

**PR target branch**:
The named branch to which a proposal is addressed; its tip may advance after the
starting revision has been selected.

**Starting state (B1)**:
The original admitted Git commit and entitled instruction snapshots from which
an Attempt starts.

**Git custody**:
Broodling's retention of the exact starting revision and any accepted revision,
independent of moving branches and execution workspaces.

**Attempt**:
One execution of an admitted Contract, bound to its original starting state.
Its identity does not depend on an execution workspace having been created.

**Execution workspace**:
The working checkout used to carry out an Attempt.

**Prepared submission**:
The complete, frozen request retained for an Attempt's later submission to its
execution target. Preparation can finish while that target is offline and does
not mean the target has accepted the request.

**Intended run identity**:
The native execution identity reserved for an Attempt before submission. Knowing
it does not establish that the execution target accepted the prepared submission.

**Dispatch intent**:
The durable fact that initiation of an Attempt's native submission was authorized
and may have begun. It does not establish native acceptance.

**Native correlation**:
Retained confirmation that an execution target accepted an Attempt's complete
prepared submission as a particular native run. It does not restore withdrawn
authority or establish successful completion.

**Unresolved dispatch**:
An Attempt whose native submission may have begun but whose native correlation
has not been retained. A submission conflict or an unknown-run observation does
not resolve that uncertainty.

**Native observation**:
A read of an Attempt's current execution state. It does not itself establish
retained acceptance, successful completion or physical cessation.

**Native result**:
The terminal outcome reported by an Attempt's execution target. A successful
native result still requires Broodling's authority, receipt and accepted-revision
checks before successful completion can be retained.

**Attempt retirement**:
A recorded end to an Attempt's resource lifecycle after the applicable cessation
checks and permitted cleanup are complete. Its authority history and retained
revisions remain available.

**Replacement Attempt**:
A new Attempt for the same immutable Contract, RequestBundle and original
starting state as a preceding Attempt. It is a fresh execution, distinct from
reconnecting to the preceding Attempt.

**Accepted revision**:
The exact Git commit delivered by an Attempt and identified by its validated PR
receipt. It remains the identity of that result when branches later move or
disappear.
