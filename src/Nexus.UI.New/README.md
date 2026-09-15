# Nexus UI New

Prototype Angular 21 + PrimeNG + Tailwind client for Nexus. This is intentionally not a port of `Nexus.UI`; it explores a denser Grafana/InfluxDB-style direction for catalog-heavy workflows.

Use PrimeNG styled components for new buttons, form controls, and overlays; keep Tailwind for layout. The Aura-based preset in `src/app/theme.ts` defines the cyan/slate palette and compact sizing. PrimeNG follows the existing `data-theme` dark/light setting. CSS layers place Tailwind's reset before PrimeNG and layout utilities after it, without per-control appearance overrides.

PrimeNG provides the action buttons, search/range inputs, range menu, catalog tree, sidebar view switch, resource table and checkboxes, mobile catalog drawer, and export/readme/preview dialogs. Native UTC date fields retain their existing string conversion; catalog loading, search, and selection remain owned by the application state.

The current PrimeNG tree uses Down to enter an already-expanded branch; Home/End navigation is not supported by the library. Overlay focus restoration and drawer-local Escape handling bridge gaps in PrimeNG's default behavior.

Implemented prototype flows:
- Browse and search catalog branches from the live Nexus API.
- Inspect a selected catalog, resource groups, representations, units, time range, metadata and attachments.
- Pin individual representations with Original, aggregation, or Resampled methods and show a placeholder telemetry graph wired to the selection state.
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

Run `npm run build` for a production build, or `npm run lint` for a strict development build (not a dedicated linter). Run `npm test` with Node 24+ for type-checked selection, period, request-path, and persistence tests using Node's built-in test runner.
