# Controlled stock DirectTarget

`stock-target` layers a controlled provider and forge over the actual
DirectTarget image. The pinned native binary, its HTTP/OECP server and the
approved execution asset are unchanged. The layer replaces only the hosted
target's fixed dependency paths:

- `codex` answers each node from its response schema. Its worker writes known
  candidate bytes to `README.md`, verifiers accept, and a corrected reply
  resumes the requested thread. When the task lists a RequestBundle, the worker
  also reads each listed reference through the installed `broodling-reference`
  helper and writes its output to `references/<index>`. While the forge holds a
  `hold` file, the worker waits, so the image demonstration's run stays active
  until it is stopped. The delivery repair node appends its input's `outcome`
  and `deliveryFeedback` as one line of `delivery-repairs.jsonl`, rewrites
  `README.md` and stages everything. Each repair is therefore a new commit that
  records what delivery reported, and a materialized conflict is resolved.
- `git` maps `https://github.com/acme/widget.git` to a mounted host bare
  repository and marks only that repository as a safe directory, because the
  target's isolated identities do not own it. Native clears the Git environment
  and global configuration, so this shim is the only way to add either setting.
- `gh` implements only the `gh api` calls native pull-request delivery makes:
  pull-request list/create/read/update, branch references, the paginated
  discussion, review and inline-comment reads, the readiness policy query, the
  branch-update mutation and job logs. It appends every invocation, with the
  state a readiness answer or branch update came from, to `gh-trace.jsonl` and
  refuses anything else as unexpected, including `gh pr merge`. Mergeability
  comes from the forge repository: a head that does not merge cleanly with main
  is `DIRTY`. Where the scenario requires up-to-date branches, as GitHub
  branch protection can, one that does not contain main is `BEHIND`. The branch
  update merges main into the head, as GitHub does. A test may script the rest
  in `scenario.json`, which the shim rereads on every call: check runs for each
  successive PR head, the target branch's rule and whether it requires
  up-to-date branches, the review decision, feedback pages, a change to main
  once the PR opens, and a foreign head repository.
  Without a scenario it reports an open, clean PR on an unprotected branch with
  no checks and no comments or reviews, so native reports it `ready`.

- `access-probe`, installed as `/usr/local/bin/broodling-access-probe`, runs
  only when the forge supplies `probe/hashes.json` (the
  [image demonstration](../README.md#image-demonstration)): the worker then
  runs it as the native-spawned execution agent before anything else, with its
  task prompt on stdin. Given only SHA-256 hashes of the installation's
  synthetic control secrets, it hashes every 64-character lowercase hexadecimal
  string it can read (environment, arguments, prompt, readable `/proc` entries
  and files outside `/proc`, `/sys`, `/dev` and `/usr`), and records its
  identity and capabilities, which secret paths it can open, whether it can
  create a raw socket, and the target's answers to unauthenticated,
  wrong-bearer and forged-bootstrap control requests over loopback and the
  routed origin. It writes the result to the forge.

A successful run produces a controlled PR receipt, not a real GitHub PR or
semantic-quality result. Credentials are fixed fake values. The only network
call a fixture makes is the helper's read from the test's own Broodling reader
on the host.

This is deliberately not an independent implementation of review, repair, or
graph semantics. Historical custom-graph, evidence and supervisor fixtures and
qualification campaigns are available only in Git history.

