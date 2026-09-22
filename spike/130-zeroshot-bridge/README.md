# Spike: C# to pinned Zeroshot, issue #130

Disposable experiment. Not production code, not a migration slice, not a design
this repository has adopted. It exists so the report on issue #130 rests on
observed behavior instead of a reading of the SDK source.

## What it is

- `bridge/zsbridge.py` — one JSON request on stdin, one JSON response on stdout,
  one process per call, four operations: `version`, `submit`, `wait`, `stop`.
- `dotnet/Spike.Zeroshot/ZeroshotBridge.cs` — the C# caller.
- `dotnet/Spike.Zeroshot/Scenario.cs` — the controlled dispatch: a real Git
  workspace, the repository's no-effect Codex launcher profile, a private
  native state directory.
- `dotnet/Spike.Zeroshot/BridgeTests.cs` — the scenarios.
- `provider/slow-codex` — the repository's controlled Codex fixture with a
  per-occurrence delay, so a wait can be cancelled while a run is still moving.
- `stub/zeroshot/` — a controlled stand-in for the pinned SDK. Used by two tests
  only, because the released engine has no offline path that produces a
  pull-request delivery receipt.

## Run

```bash
dotnet test spike/130-zeroshot-bridge/dotnet/Spike.Zeroshot/Spike.Zeroshot.csproj
```

Twelve of the fourteen tests drive the real pinned SDK and its bundled native
engine. Only the Codex provider is controlled. No network, no credentials, no
paid provider.

The project is deliberately outside `Broodling.sln`, so the supported
`dotnet test` and `python -m pytest tests` commands do not pick it up.
