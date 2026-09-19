# Issue #83: fresh v6 inputs and tested baseline

Non-provider preparation on 2026-09-19 for frozen `p5-native-pr-v6`, commit
`4a2b0b8a0340b747e805f51da218a77ad81278f9`. This package records fresh actual
bindings; it grants no admission or dispatch authorization.

The private disposable repository is
[faviann/broodling-p5-v6-20260919](https://github.com/faviann/broodling-p5-v6-20260919),
with [issue #1](https://github.com/faviann/broodling-p5-v6-20260919/issues/1)
containing the exact frozen `P5 v1 / T1 / R01` text. Its sole branch `p5-eval` is
at original B1 `884bd64264df1515bee76a63f548db9cabe25a35`. Only `README.md` and
`tiny.py` enter the fixture. `b1.bundle` was verified and independently cloned;
both recovered B1 files match the frozen inputs.

Fresh durable inputs live at
`/home/faviann/.local/share/broodling-p5-v6/r01-inputs`: `broodling.sqlite3`,
`source/`, and empty `workspaces/`. [Input records](R01/input-records.json) bind
the new entitled source, Work Unit and canonical criteria-only Contract. The
[store readback](store-readback.json) confirms one of each, zero admissions,
Attempts, submissions and dispositions. All eight slots remain `NOT_STARTED`.

[Baseline verification](baseline-verification.json) records the actual committed
helper/test revision and fresh supported full-suite result. Product/dependency/
configuration bytes still match selected #82 baseline
`da5db3167eda0ef458cf0875b5a1c64138637b9d`; the v6 helper/test additions receive
their own validation. [Dependency verification](dependency-verification.json)
compares every installed SDK package file to the pinned official wheel, including
the native executable. No SDK/native coverage is omitted and no provider task is
part of the suite. The old 356-test result is historical provenance.

[Operational readback](operational-readback.json) records current stop access,
quarantine capacity, input isolation and independent exact-revision judging/Git
retention arrangements. The [live preparation package](../2026-09-19-v6-r01/README.md)
records the selected running target, actual-container GitHub CLI, current
credentials and reachability. `SHA256SUMS` seals this setup package for later
read-only preflight. Future run accounting belongs in the separate live package.

Preparation used the v6 setup helper first offline, published exact B1 to the new
private repository, created the issue from `R01/issue-body.md`, then ran:

```bash
.venv/bin/python evaluation/p5/v6/setup_r01.py \
  --evidence /home/faviann/repos/broodling/evaluation/p5/runs/2026-09-19-v6-setup \
  --state-dir /home/faviann/.local/share/broodling-p5-v6/r01-inputs \
  --repository faviann/broodling-p5-v6-20260919 \
  --direct-target-origin http://127.0.0.1:18768 --github-issue 1
```

This creates/reads unadmitted input records only. V1-v5 protocols, helpers,
evidence, stopped v5 target/quarantine and partial PR #2 remain preserved.
