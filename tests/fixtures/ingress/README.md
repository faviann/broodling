# Representative ingress fixtures

These files preserve the exact stdout bytes returned by read-only `gh api
repos/faviann/broodling/issues/{number}` calls. They include the complete REST
issue response, including title and body, without reformatting or newline changes.
The source locator is the linked GitHub issue; acquisition uses its REST endpoint.

| Issue | Snapshot | Retrieved at (UTC) | SHA-256 |
| --- | --- | --- | --- |
| [#75](https://github.com/faviann/broodling/issues/75) | `issue-75.json` | `2026-09-19T19:38:00.584537+00:00` | `bcff9ebd1e926e7bba543b3ae9f69ca9be628fe06a4f6494becdbf8194f313cd` |
| [#82](https://github.com/faviann/broodling/issues/82) | `issue-82.json` | `2026-09-19T19:38:01.120837+00:00` | `9d907caa1f7f3f1bf10cf94386c379a5052620473d87db8ffeb10119ddc859e3` |

The tests replay these bytes offline through the production GitHub adapter and
supply small explicit Contract proposal callbacks. Completion prose is copied
verbatim, and the full issue snapshot stays attributed as governing input.
These are representative admission/non-admission checks, not live implementation,
provider runs, semantic-quality qualification, or authorization to execute either
issue. The #75 fixture deliberately demonstrates a missing prerequisite receipt;
it does not dispute #74's actual first-use decision. GitHub comments, linked
material and today's mutable issue state are not part of these snapshots.
