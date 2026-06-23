# Basis Luau performance evaluation

Basis Luau has two benchmark tiers:

- **IL2CPP Player benchmarks** are the release-quality evidence for Unity
  performance discussions and PR review.
- **Editor/TestRunner benchmarks** are quick local regression signals only. They
  are useful while iterating, but Editor/Mono numbers must not be scaled into an
  IL2CPP conclusion.

Run `Tools > Basis Luau > Benchmarks > IL2CPP Player > Build and Run` to build a
Windows x64 IL2CPP Player with the self-running benchmark scene. The Player
writes:

- `Application.persistentDataPath/BasisLuauBenchmarks/results.json`
- `Application.persistentDataPath/BasisLuauBenchmarks/summary.md`

The JSON file is the machine-readable artifact. The Markdown file is the PR and
review summary. The build menu creates and deletes a temporary scene under
`Assets/Temp`; no scene asset is committed.

To compare against a known-good run, copy a previous `results.json` to
`Application.persistentDataPath/BasisLuauBenchmarks/baseline.json` before running
the Player benchmark. The primary acceptance gate then rejects p99 or aggregate
Luau memory increases above 5%.

For quick development checks, run
`Tools > Basis Luau > Benchmarks > Quick Regression > Run UGC Scale Play Mode`.
Results are logged with the `[BasisLuau.Benchmark]` prefix and appended to
`Temp/luau-benchmark-results.txt`.

## Target envelope

- Windows x64 PCVR
- 1,000 concurrently loaded UGC Luau scripts
- strict 90 Hz frame deadline: 11.111 ms
- Luau main-thread budget: 2.000 ms

The 2 ms budget leaves the rest of the 90 Hz frame for rendering, physics,
networking, animation, audio, and Basis itself. The 11.111 ms value is the hard
deadline, not an acceptable Luau budget.

## IL2CPP workload model

The Player benchmark uses three layers so reviewers can distinguish native
runtime cost from Unity bridge cost and real UGC scale cost:

- Native: command ring push/pop, snapshot publish, buffer allocation/read/release,
  and scheduler tick.
- Bridge: Luau scripts writing transform commands, reading snapshots, dispatching
  through events, and sending large text payloads through the native buffer pool.
- Scenario: mixed UGC populations at 32, 128, 512, and 1,000 hosts.

Scenario diagnostics also split the mixed workload by cohort, topology, and
host-pump phase. These diagnostic cases explain the primary scenario result; the
release gate still uses `scenario.mixed-ugc-1000`.

The fixed payload matrix is:

- small: handle plus scalar/vector command
- medium: transform snapshot batch and quaternion-like snapshot data
- large: 1 KiB, 4 KiB, and 16 KiB string/buffer payloads
- mixed: 80% small/light work, 15% compute work, 5% bridge or large payload work

The cohort cases isolate the same population sizes with only one workload type:
`dormant`, `light`, `compute`, `bridge`, and `large-payload`. The topology cases
compare one VM/host containing many proxies against many independent hosts with
one proxy each.

Full-profile IL2CPP runs use at least 10,000 iterations for microbenchmarks and
10,000 pump samples for frame-distribution cases. The light native command-ring
case uses 100,000 iterations. The smoke profile uses fewer samples and exists
only to validate that artifact generation and the benchmark harness work.

## Editor workload model

The scale suite uses a deterministic mix intended to represent a populated UGC
session rather than a synthetic all-heavy worst case:

- 50% dormant scripts: loaded but no per-frame callback
- 30% light scripts: small state update
- 15% compute scripts: 500-iteration numeric loop
- 5% bridge scripts: snapshot read and deferred transform command

Two topologies are measured:

- single host with 32, 128, and 256 scripts; 256 is the current native registry
  limit and measures VM sharing density
- 32, 128, 512, and 1,000 independent hosts with one script each; this models
  separately loaded UGC Props

The load test creates 1,000 independent hosts/scripts and reports load time,
managed allocation when the active runtime supports the counter, and aggregate
Luau memory across every VM.

## Metrics

`mean` is useful for throughput but is not the acceptance metric. Use:

- `p50`: normal frame cost
- `p95`: sustained tail cost
- `p99`: primary regression and capacity metric
- `max`: worst observed sample; investigate large separation from p99
- `budgetMiss`: samples above the 2 ms Luau budget
- `deadlineMiss`: samples above the 11.111 ms 90 Hz deadline
- `throughput`: script updates processed per second for the measured population
- `gcAlloc`: managed bytes allocated per measured pump, or `unavailable` when
  Mono cannot provide a functioning thread allocation counter
- `managedRetained`: live managed heap change after forced collection; this is
  not total allocation traffic
- `luau`: aggregate live Luau bytes and aggregate configured cap
- `phaseBreakdown`: diagnostic-only pump phase distribution for
  `flushCommandsBefore`, `processTickets`, `publishSnapshots`, `drainEvents`,
  `kickScheduler`, and `flushCommandsAfter`

Each percentile result measures the complete synchronous host pump: command
flush, tickets, snapshots, events, and the Luau update scheduler. It does not
include an Editor `yield return null` or unrelated PlayerLoop work.

The Player JSON schema includes:

- result fields: `id`, `layer`, `workload`, `iterations`, `warmup`, `meanMs`,
  `p50Ms`, `p95Ms`, `p99Ms`, `maxMs`, `budgetMisses`, `deadlineMisses`,
  `gcAllocBytesPerIter`, `managedRetainedBytes`, `luauBytes`,
  `throughputUnitsPerSec`, `tags`, `hostCount`, `proxyCount`, `topology`,
  `cohort`, `phaseBreakdown`
- environment fields: `unityVersion`, `platform`, `scriptingBackend`, `il2cpp`,
  `developmentBuild`, `cpu`, `coreCount`, `systemMemory`, `graphicsDevice`,
  `incrementalGc`, `packageVersion`, `gitCommit`, `nativeDllHash`,
  `workloadHash`

## Acceptance gates

The 1,000-host mixed UGC case is the release capacity gate:

1. p99 at or below 2.000 ms
2. zero 2 ms budget misses in the full-profile sample run
3. zero 11.111 ms deadline misses
4. max at or below 11.111 ms
5. no unexplained increase above 5% in p99 or aggregate memory against a baseline
   captured on the same machine, Unity version, build type, and profiler state

The canonical case ID is `scenario.mixed-ugc-1000`.
Baseline comparison is performed only when `baseline.json` is present. A missing
baseline is reported as `passed without baseline`, not as a regression failure.

Large-payload bridge cases are reported separately and must not be hidden inside
small command-ring results. Use the mixed payload scenario for final judgment and
use large-only cases to explain string/buffer transport cost.

The IL2CPP Player runner fails fast for unsafe conditions: missing native
runtime, non-IL2CPP build, development build, or unsupported platform. Editor
measurements are valid only for comparing the directly timed synchronous pump on
the same machine.
