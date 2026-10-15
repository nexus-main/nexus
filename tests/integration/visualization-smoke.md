# Live Visualization Smoke

Opt-in cross-stack verification using a real local Nexus host and the production
`decodeVisualization` / `decodeCursor` frontend parsers. No deployed server URL
is accepted. Only built-in, in-process sample catalogs are requested.

From the repository root:

```sh
ls artifacts
dotnet build src/Nexus/Nexus.csproj --no-restore -v minimal
node tests/integration/visualization-smoke.ts
```

Node 24 runs the TypeScript harness directly. Install the UI dependencies first
(`npm ci --prefix src/Nexus.UI`); the production decoders use its Apache Arrow package.

Typecheck the harness with the application's path aliases:

```sh
node src/Nexus.UI/node_modules/typescript/bin/tsc -p tests/integration/tsconfig.json
```

The default parent is the repository's ignored `artifacts/` directory.
`NEXUS_SMOKE_PARENT` may select a writable directory under `artifacts/` or
`/tmp/opencode`; the harness rejects paths outside these roots. It preserves its unique run directory and
`host.log` for diagnosis.

## Isolation

- Copies the debug build to the unique run directory, excluding all appsettings
  files, and launches that DLL directly without launch profiles on an OS-assigned
  loopback port. Build first to avoid testing stale binaries; the copy also avoids
  changing assemblies under the running host during another agent's build.
- Uses an allowlisted child environment, isolated HOME, settings, config,
  catalogs, caches, packages, and artifacts. Generates a minimal `appsettings.json`
  beside the copied DLL rather than loading build-output settings.
  User and system Git configuration are disabled. No user Nexus settings or Git
  remote configuration are inherited. This is application isolation, not an OS sandbox.
- Uses Development authentication, which supplies an admin identity by default.
- The two-minute watchdog and SIGINT/SIGTERM abort active requests and signal only
  the spawned child. SIGKILL escalation after five seconds is scheduled immediately,
  independently of reaching `finally`. Cleanup waits at most ten seconds for closure.
  Every fetch and decoder also has a 30-second deadline, including the cancellation case.
- Sets an 8 MiB shared buffer-accounting budget and one source-read worker. With the
  current memory tracker's one-eighth grant policy, each reservation is at most 1 MiB.
  This is not an RSS cap or a fixed 64 KiB slice size. Correlated timing aggregates
  must confirm more than one cold source read; fractional progress is not required.

## Assertions

- Cold seven-day full-domain request: 604,800 samples per resource, two resources,
  512-point budget, Arrow content type, no-store, progress, ranges, completed
  replacement views, and explicit terminal completion enforced by the real parser.
- Warm overview, 20,000-sample zoom, and 32-sample detail in one request. The full
  overview must exactly match the cold response. Correlated server timing logs must
  report a cold miss and warm hit in the memory-only summary cache, plus successful
  request completion. Warm raw-detail reads are expected; their exact count remains
  covered by instrumented service unit tests, not this smoke.
- Raw `POST /api/v2/data` Float32 cursor read through the real cursor parser:
  all 32 samples of both resources must exactly equal the detail view.
- Unix-time values and full-domain minimum/maximum are checked against an independent
  timestamp formula. Every point must lie within its reported range, and overview
  extrema must equal that range. Float32 Unix times in 2020 have 128-second spacing,
  so the timestamp oracle alone cannot distinguish every adjacent source index.
- Invalid point budget and configuration header: 422; missing resource: 404;
  non-admin access to an unlicensed synthetic catalog: 403.
- Cancellation after receiving real progress: AbortError and successful subsequent
  visualization request. Does not prove that every backend worker immediately stops.

Development automatically supplies an authenticated identity even when no bearer
token is valid, so this harness does not claim unauthenticated 401 coverage.
Browser rendering is separate from this transport check; desktop/mobile rendering,
pointer interaction and mode switching are not asserted by this harness.

## Verification

The October 7 run used an earlier cache/read-budget implementation and does not
validate this harness. Current runs print their unique artifact directory, individual
checks and final status; retain `host.log` with the tested revision when recording a
result. Elapsed times include transport, decoding, assertions and timing-log delivery.
They are smoke diagnostics for a sample plugin, not storage or browser benchmarks.

On October 15, 2026, the revised harness passed against the worktree based on
`50a33b6`, using a freshly built isolated debug host and Node 24.14.1. All listed
checks ran, including multi-slice/cold-miss timing, warm-hit overview equality,
cursor equality, HTTP errors and cancellation/recovery. The owned host exited 0.
Local evidence: `artifacts/nexus-visualization-W0fYsZ/host.log`.

A separate interruption check suspended only that check's newly spawned host after
startup, then sent SIGTERM to its harness. The request aborted, SIGKILL escalation
stopped the unresponsive owned host after five seconds, and the harness exited
nonzero with no child remaining. Local evidence:
`artifacts/nexus-visualization-l0vNRj/host.log`. This exercises the shared watchdog/
signal stop path, not a full two-minute deadline wait. No deployed service or
container was involved in either verification.
