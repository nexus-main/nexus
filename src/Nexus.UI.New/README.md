# Nexus UI New

Angular 21 + PrimeNG + Tailwind client for Nexus. It explores a denser Grafana/InfluxDB-style direction for catalog-heavy workflows and includes the time-series chart port from `Nexus.UI`.

Use PrimeNG styled components for new buttons, form controls, and overlays; keep Tailwind for layout. The Aura-based preset in `src/app/theme.ts` defines the cyan/slate palette and compact sizing. PrimeNG follows the existing `data-theme` dark/light setting. CSS layers place Tailwind's reset before PrimeNG and layout utilities after it, without per-control appearance overrides.

PrimeNG provides the action buttons, search/range inputs, range menu, catalog tree, sidebar view switch, mobile catalog drawer, and editing/export/readme/preview dialogs. The resource matrix uses Angular CDK virtual scrolling with native selection checkboxes. Native UTC date fields retain their existing string conversion; catalog loading and selection remain owned by the application state.

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

Selections are stored in `nexus.selectedResources` as a versioned document containing Period, automatic-period policy, and ordered native-representation references with methods. Restore resolves references against catalog data, preserves unavailable entries for retry, and drops only references confirmed missing in successfully loaded catalogs. Legacy resource-only entries migrate to their first native representation. Parameterized representations cannot be newly selected or exported yet; their restored references remain removable from the pinned list. Catalog bundles are cached for the browser session and refreshed after saving metadata; reload to see external metadata changes.

Run `npm run build` for a production build, or `npm run lint` for a strict development build (not a dedicated linter). Run `npm test` with Node 24+ for type-checked grouping/search, metadata-merge, selection, period, request-path, persistence, Arrow streaming, and chart tests using Node's built-in test runner. These tests do not measure browser layout or virtual-scroll geometry. The shared renderer's tests run from the repository root with `node --test tests/js/chart.webgpu.test.js tests/js/chart.interactions.test.js`.

## Resource Matrix

Choose a group from the horizontally scrollable strip; expand it into a bounded wrapping list for more groups. Counts refer to matching representations. Search spans the entire catalog, including groups, resource IDs/paths, units, descriptions, warnings, and formatted sample periods. Only matching groups remain. The current group stays selected when possible, and the last group is remembered per catalog during the session.

Every representation remains independently selectable, with no 450-row cap. Fixed-height CDK rows switch to a stacked layout below 760px of matrix width. Only visible rows and a buffer are rendered. Full metadata opens in a dialog, or a bottom sheet in narrow layouts. Short screens allow the surrounding matrix controls to scroll while reserving space for resource rows. Activating a pin reveals its representation and group, clearing any obstructing search.

Readable, writable live catalogs expose **Edit metadata**. Unit, description, and warning edits are resource-level drafts shared across representations and groups. Wide panes have inline inputs; the detail editor supports long text and a **Keep draft** action for editing multiple resources before saving. Search uses saved values during editing so rows do not disappear mid-edit. **Cancel** discards drafts; catalog navigation offers Save, Discard, or Stay, and leaving the page warns about unsaved edits.

Saving reads current metadata and updates only changed override fields through the generated V1 client. Unrelated overrides, contact, and authorization group memberships are preserved. **Blank fields remove their override and restore the source value, which may not be blank.** Failed writes retain drafts. A successful write followed by a failed refresh is reported separately; reselect the catalog to retry loading. There is no server revision/ETag protection against simultaneous writes by multiple users.

## Time-Series Visualization

Visualize snapshots the pinned methods, Period, and UTC From/To range. Data loads with decoded-sample progress and cancellation; changing the selection or range requires Reload. Moving between the inline view and expanded dialog retains CPU data without another HTTP request. Closing an in-progress visualization, hiding its inline panel by resizing, or encountering a GPU failure cancels the load. A line chart requires at least two samples; shorter requests are rejected before downloading.

The Angular build includes the existing `Nexus/wwwroot/js/chart*.js` renderer directly, avoiding a second copy of the GPU shaders, reduction algorithms, and interaction handlers. TypeScript replaces the C# chart orchestration; Canvas 2D replaces only the SkiaSharp axes and text. The original seven series colors, fills, legend, crosshairs, unit axes, overview, precision navigator, and Begin at zero setting are retained. The embedded Courier New Bold font is shared with Blazor. Text rasterization can differ slightly between Canvas 2D and SkiaSharp.

Arrow IPC is decoded incrementally into aligned 16 MiB Float32 chunks, not a whole-response Arrow table or boxed number arrays. Published chunks are uploaded directly with `queue.writeBuffer`; WebGPU must copy CPU data into GPU memory, just as in Blazor. CPU chunks remain available for cursor lookup and detailed zoom uploads. GPU reductions create persistent min/max/gap overviews, while a bounded raw-data cache supplies detailed views. There is a 2048 MiB visualization payload limit and a separately adjustable GPU cache budget (default 2048 MiB). The original renderer publishes each series for drawing after its overview upload completes.

Time calculations retain .NET ticks as bigint, including 100 ns fractions and the year-1 epoch. GPU geometry uses relative floating-point coordinates, as before. WebGPU requires browser support and a secure context (HTTPS or localhost); unsupported browsers show an actionable error and retry button, not a CPU fallback. No C#/WebAssembly runtime is required by the migrated chart.

Chart controls: drag to zoom, middle-button or modifier-drag to pan, wheel to zoom time, Shift-wheel to zoom values, double-click to reset. Both navigators support dragging, resizing, wheel zoom, and arrow keys. Legend buttons toggle individual series.
