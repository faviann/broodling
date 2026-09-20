# Agent guidance

Start every architecture, feature or bug task with
[`docs/governing/current.md`](docs/governing/current.md). Production code and the
current tests decide implemented behavior when prose disagrees.

Read the implementation document for the seam being changed:

- native execution and lifecycle: `docs/implementation/zeroshot-native-integration.md`
- caller invocation/recovery: `docs/implementation/invocation.md`
- work-reference/source ingress: `docs/implementation/work-reference-ingress.md`
- installation and operations: `deployment/README.md`
- validation scope: `tests/README.md`

`evaluation/p5/README.md` records the one historical result that still limits
current use. It is evidence, not a protocol or authorization for another run.

Git history contains superseded plans, qualification campaigns and evaluation
artifacts. Historical material defines only its recorded revision and profile;
use current authority and open issues for present requirements. Restore or rerun
archived machinery only under an explicit current scope.

Run `python -m pytest tests` for the supported suite. Preserve unrelated working
tree state.
