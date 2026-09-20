# Retained P5 outcome

P5 is complete. Its only current role is to state the quality limitation on the
supported authorized-PR profile; this directory contains no executable campaign,
prospective protocol or authorization for another run.

## Result carried forward

The v5 R01 attempt ended `NOT_DELIVERED / IF` because its DirectTarget GitHub CLI
could not execute Zeroshot's required `gh api graphql --paginate --slurp` query.
That dependency is now pinned and checked by the deployment package, but the
historical run remains an infrastructure failure.

The separately accounted v6 R01 used the corrected target. Native execution and
Broodling disposition succeeded, including exact PR receipt binding, detached
consumption of an already-completed result and credential-free reopened-store
replay. Independent full-criteria review nevertheless classified the accepted
revision as **false acceptance (FA)**: `parse_port()` rejected a nonempty
ASCII-digit `str` subclass solely because its overridden truth value was false.
Both native verifier stages had accepted the defective implementation.

The frozen automated judge also missed that case. A later offline `p5-judge-v2`
added the regression and corrected its calibration reference; it did not change
the native workflow, rescore v6 or qualify a new cohort.

Therefore:

- the authorized-PR delivery/disposition/reconnection boundary is demonstrated;
- autonomous semantic correctness and general reliability are not demonstrated;
- the scoped P5 verdict remains **FAIL**;
- no P5 cohort is active or authorized; and
- supported use requires independent human review before any merge/deployment
  decision, as stated by current governing and deployment documentation.

## Archive references

Git history is the archive for the removed protocols, drivers and raw evidence:

- [v5 R01 settlement](https://github.com/faviann/broodling/blob/445c6d773ab772cb00f98a274d4bb8fce3e21b84/evaluation/p5/runs/2026-09-19-v5-r01/README.md)
- [v6 R01 evidence and classification](https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/README.md)
- [skeptical review and scoped verdict](https://github.com/faviann/broodling/blob/c5632ddd8896bfe8ffd2defafc7777d68dafbc5a/evaluation/p5/reviews/2026-09-19-issue-66/README.md)
- [calibration-gap analysis](https://github.com/faviann/broodling/blob/24855ea8c59c852558553ce1bd6b0a72f992aeef/evaluation/p5/analyses/2026-09-19-t1-calibration-gap/README.md)
- [`p5-judge-v2` correction](https://github.com/faviann/broodling/blob/e0a595c93ebef92432d7e1bf55eec03d7bbe5a8a/evaluation/p5/judges/v2/README.md)
- [complete pre-cleanup tree](https://github.com/faviann/broodling/tree/348e1f469c04fecbc24f4088e6eb438a3934e872)

These records justify the limitation above; their old next steps, resource
instructions and issue states are historical rather than current requirements.
