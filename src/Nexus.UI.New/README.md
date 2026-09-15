# Nexus UI New

Angular 21 + PrimeNG + Tailwind client for Nexus. It explores a denser Grafana/InfluxDB-style direction for catalog-heavy workflows and includes the time-series chart port from `Nexus.UI`.

Use PrimeNG styled components for new buttons, form controls, and overlays; keep Tailwind for layout. The Aura-based preset in `src/app/theme.ts` defines the cyan/slate palette and compact sizing. PrimeNG follows the existing `data-theme` dark/light setting. CSS layers place Tailwind's reset before PrimeNG and layout utilities after it, without per-control appearance overrides.

PrimeNG provides the action buttons, search/range inputs, range menu, catalog tree, sidebar view switch, resource table and checkboxes, mobile catalog drawer, and export/readme/preview dialogs. Native UTC date fields retain their existing string conversion; catalog loading, search, and selection remain owned by the application state.

The current PrimeNG tree uses Down to enter an already-expanded branch; Home/End navigation is not supported by the library. Overlay focus restoration and drawer-local Escape handling bridge gaps in PrimeNG's default behavior.

Implemented prototype flows:
- Browse and search catalog branches from the live Nexus API.
- Inspect a selected catalog, resource groups, representations, units, time range, metadata and attachments.
- Pin individual representations with Original, aggregation, or Resampled methods and visualize their time series through the V2 Arrow stream and WebGPU.
- Configure exports through live writer metadata and create an export job with the generated TypeScript Nexus client.
- Administrators can list, create, edit, and delete local/Git-tag package references from the Administrator menu. Role detection uses the generated client's users/me call; package operations use its packageReferences API. Tokens also need administrator permission. Changes save references only, without reloading running extensions.

Credentials are not stored in this repo or exposed to browser JavaScript. To run with the local credentials file:

```sh
./dev-with-local-credentials.sh
```

The launcher reads `$HOME/.config/nexus-ui-new/credentials.env` by default. You can override the path with `NEXUS_UI_NEW_CREDENTIALS`.

Use `.env.example` as a template for that external credentials file. The Angular dev proxy injects `NEXUS_TOKEN` server-side for `/api` requests.

The first pinned representation initializes Period unless the user has already changed it. Subsequent selections default to Original at the same period, Mean at a slower period, or Resampled at a faster period. The methods popup supports multiple methods. Changing Period preserves existing methods; incompatible methods turn red and block export until removed or made compatible. Periods must divide exactly for aggregation/resampling.

Selections are stored in `nexus.selectedResources` as a versioned document containing Period, automatic-period policy, and ordered native-representation references with methods. Restore resolves references against catalog data, preserves unavailable entries for retry, and drops only references confirmed missing in successfully loaded catalogs. Legacy resource-only entries migrate to their first native representation. Parameterized representations cannot be newly selected or exported yet; their restored references remain removable. Catalog bundles are cached for the browser session; reload to refresh catalog metadata.

Run `npm run build` for a production build, or `npm run lint` for a strict development build (not a dedicated linter). Run `npm test` with Node 24+ for type-checked selection, period, request-path, persistence, Arrow streaming, and chart tests using Node's built-in test runner. The shared renderer's tests run from the repository root with `node --test tests/js/chart.webgpu.test.js tests/js/chart.interactions.test.js`.

## Time-Series Visualization

Visualize snapshots the pinned methods, Period, and UTC From/To range. Data loads with decoded-sample progress and cancellation; changing the selection or range requires Reload. Moving between the inline view and expanded dialog retains CPU data without another HTTP request. Closing an in-progress visualization, hiding its inline panel by resizing, or encountering a GPU failure cancels the load. A line chart requires at least two samples; shorter requests are rejected before downloading.

The Angular build includes the existing `Nexus/wwwroot/js/chart*.js` renderer directly, avoiding a second copy of the GPU shaders, reduction algorithms, and interaction handlers. TypeScript replaces the C# chart orchestration; Canvas 2D replaces only the SkiaSharp axes and text. The original seven series colors, fills, legend, crosshairs, unit axes, overview, precision navigator, and Begin at zero setting are retained. The embedded Courier New Bold font is shared with Blazor. Text rasterization can differ slightly between Canvas 2D and SkiaSharp.

Arrow IPC is decoded incrementally into aligned 16 MiB Float32 chunks, not a whole-response Arrow table or boxed number arrays. Published chunks are uploaded directly with `queue.writeBuffer`; WebGPU must copy CPU data into GPU memory, just as in Blazor. CPU chunks remain available for cursor lookup and detailed zoom uploads. GPU reductions create persistent min/max/gap overviews, while a bounded raw-data cache supplies detailed views. There is a 2048 MiB visualization payload limit and a separately adjustable GPU cache budget (default 2048 MiB). The original renderer publishes each series for drawing after its overview upload completes.

Time calculations retain .NET ticks as bigint, including 100 ns fractions and the year-1 epoch. GPU geometry uses relative floating-point coordinates, as before. WebGPU requires browser support and a secure context (HTTPS or localhost); unsupported browsers show an actionable error and retry button, not a CPU fallback. No C#/WebAssembly runtime is required by the migrated chart.

Chart controls: drag to zoom, middle-button or modifier-drag to pan, wheel to zoom time, Shift-wheel to zoom values, double-click to reset. Both navigators support dragging, resizing, wheel zoom, and arrow keys. Legend buttons toggle individual series.
