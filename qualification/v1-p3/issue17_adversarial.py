"""Read-only #17 reviewer probes through the public pinned SDK; run from repo root."""
import asyncio
import hashlib
import json
import shutil
import sys
import tempfile
from datetime import UTC, datetime
from pathlib import Path

OUTPUT = Path('/dev/shm/issue17_adversarial_results.json')

async def main():
    ROOT = Path.cwd()
    sys.path.insert(0, str(ROOT))
    sys.path.insert(0, str(ROOT / 'tests'))
    import assurance_support as a
    from support import durable_test_root

    from broodling.assurance_graph import assurance_graph
    from broodling.zeroshot_sdk import assert_qualified_integration

    build = assert_qualified_integration()
    run_root = Path(tempfile.mkdtemp(prefix='b17ra-', dir='/dev/shm'))
    workspace_root = durable_test_root('b17-adversarial-')
    launcher = durable_test_root('b17-adversarial-launcher-')
    source = (a.LEAF_BIN / 'codex').read_text()
    needle = 'message = {"response": response}'
    assert source.count(needle) == 1
    variants = {
        'no-envelope': 'message = {} if selected == node else {"response": response}',
        'no-message': 'message = {"response": response}\n    if selected == node:\n        emit({"type": "turn.completed", "usage": {"input_tokens": 1, "cached_input_tokens": 0, "output_tokens": 0}})\n        return 0',
        'null-response': 'message = {"response": None if selected == node else response}',
    }
    record = {
        'schema': 'broodling.v1-p3.issue17-adversarial-controls/v1',
        'recordedAt': datetime.now(UTC).isoformat(),
        'build': build,
        'graphSha256': a.canonical_hash(assurance_graph()),
        'controlledRuntimeSha256': a.canonical_hash(a.controlled_runtime()),
        'fixtureSourceSha256': hashlib.sha256(source.encode()).hexdigest(),
        'fixtureSource': source,
        'probeSourceSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        'scope': 'Controlled-provider mechanics through public SDK submit/wait/status; no product changes, containment/model-quality claim, or gate verdict.',
        'fixtureModifications': {'needle': needle, 'replacements': variants},
        'cases': {},
    }
    async def run(name, node, scenario, expect_success=False):
        case = await a.run_case(run_root, workspace_root, name, scenario)
        actual = case['result']
        passed = (actual['succeeded'] and actual['failure'] is None) if expect_success else (not actual['succeeded'] and actual['failure'] == 'execution_unusable')
        sequence = a.nodes(case)
        # Ensure an earlier fixture error cannot masquerade as the selected fault.
        passed = bool(passed and node in sequence and (expect_success or sequence[-1] == node))
        record['cases'][name] = {
            'runId': case['runId'], 'scenario': scenario, 'selectedNode': node,
            'expectedSuccess': expect_success, 'passed': passed,
            'result': actual, 'nodes': sequence,
            'graphSha256': case['graphSha256'],
            'selectedEvent': next((e for e in case['events'] if e['node'] == node), None),
        }
        OUTPUT.write_text(json.dumps(record, indent=2, sort_keys=True) + '\n')
        print(name, passed, flush=True)
    try:
        for node in [*a.CLEAN_ORDER, *a.ROUND_ORDER, 'final_assessment_authority_repaired']:
            base = 'clean' if node in a.CLEAN_ORDER else 'repair-resolve'
            for fault in ['missing', 'malformed', 'default']:
                await run(f'{fault}-{node}', node, f'{base};{fault}:{node}')
        a.LEAF_BIN = launcher
        for variant, replacement in variants.items():
            leaf = launcher / 'codex'
            leaf.write_text(source.replace(needle, replacement))
            leaf.chmod(0o755)
            for node in ['implement', 'repair', 'final_assessment_authority_clean', 'round_complete']:
                base = 'clean' if node in a.CLEAN_ORDER else 'repair-resolve'
                await run(f'{variant}-{node}', node, f'{base};malformed:{node}', variant == 'null-response' and node in {'implement', 'repair'})
        record['verdict'] = 'PASS' if all(c['passed'] for c in record['cases'].values()) else 'FAIL'
        OUTPUT.write_text(json.dumps(record, indent=2, sort_keys=True) + '\n')
        print('FINAL', record['verdict'], len(record['cases']), str(OUTPUT), flush=True)
    finally:
        shutil.rmtree(run_root, ignore_errors=True)
        shutil.rmtree(workspace_root, ignore_errors=True)
        shutil.rmtree(launcher, ignore_errors=True)

if __name__ == "__main__":
    asyncio.run(main())
