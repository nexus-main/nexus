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

Node 24 runs the TypeScript harness directly without additional packages.

Typecheck the harness with the application's path aliases:

```sh
node src/Nexus.UI/node_modules/typescript/bin/tsc -p tests/integration/tsconfig.json
```

The default parent is the repository's ignored `artifacts/` directory.
`NEXUS_SMOKE_PARENT` may select a writable directory under `artifacts/` or
`/tmp/opencode`; the harness rejects paths outside these roots. It preserves its unique run directory and
`host.log` for diagnosis.

## Isolation

- Launches the debug DLL directly, without launch profiles, on an OS-assigned
  loopback port. Build first to avoid testing stale binaries.
- Uses an allowlisted child environment, isolated HOME, settings, config,
  catalogs, caches, packages, and artifacts. No user Nexus settings or Git remote
  configuration are inherited.
- Uses Development authentication, which supplies an admin identity by default.
- Stops only the spawned child, in `finally`, with a bounded SIGTERM/SIGKILL
  shutdown and a two-minute watchdog. SIGINT/SIGTERM also stop that child.
- Uses a 64 KiB visualization read budget to exercise multi-slice transport.

## Assertions

- Cold seven-day full-domain request: 604,800 samples per resource, two resources,
  512-point budget, Arrow content type, no-store, progress, ranges, completed
  replacement views, and explicit terminal completion enforced by the real parser.
- Warm overview, 20,000-sample zoom, and 32-sample detail in one request. Checks
  that the same on-disk summary remains unchanged. This is cache reuse evidence,
  not instrumentation proving an exact number of backend source reads.
- Raw `POST /api/v2/data` Float32 cursor read through the real cursor parser:
  all 32 samples of both resources must exactly equal the detail view.
- Unix-time resource values are checked against an independent timestamp formula.
- Invalid point budget and configuration header: 422; missing resource: 404;
  non-admin access to an unlicensed synthetic catalog: 403.
- Cancellation after receiving real progress: AbortError and successful subsequent
  visualization request. Does not prove that every backend worker immediately stops.

Development automatically supplies an authenticated identity even when no bearer
token is valid, so this harness does not claim unauthenticated 401 coverage.
Browser rendering is separate from this transport check; tool availability is
reported, but desktop/mobile rendering is not asserted by this harness.

## Verification

On 2026-10-07 the live smoke passed with an isolated debug host: cold full domain
420 ms, warm overview/zoom/detail 19 ms, and post-cancellation recovery 6 ms.
These are smoke observations for the sample plugin, not storage benchmarks.
The actual frontend decoders accepted all streams and exact cursor values matched
the corresponding detail points. Validation returned 422, missing data 404, and
denied catalog access 403 even with an Arrow-only Accept header. The host stopped
successfully after the test. Browser desktop/mobile rendering remains unverified;
Playwright is not installed.
