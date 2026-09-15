# Nexus UI New

Prototype Angular 21 + PrimeNG + Tailwind client for Nexus. This is intentionally not a port of `Nexus.UI`; it explores a denser Grafana/InfluxDB-style direction for catalog-heavy workflows.

Use PrimeNG styled components for new buttons, form controls, and overlays; keep Tailwind for layout. The Aura-based preset in `src/app/theme.ts` defines the cyan/slate palette and compact sizing. PrimeNG follows the existing `data-theme` dark/light setting. CSS layers place Tailwind's reset before PrimeNG and layout utilities after it, without per-control appearance overrides.

PrimeNG provides the action buttons, search/range inputs, range menu, catalog tree, sidebar view switch, resource table and checkboxes, mobile catalog drawer, and export/readme/preview dialogs. Native UTC date fields retain their existing string conversion; catalog loading, search, and selection remain owned by the application state.

The current PrimeNG tree uses Down to enter an already-expanded branch; Home/End navigation is not supported by the library. Overlay focus restoration and drawer-local Escape handling bridge gaps in PrimeNG's default behavior.

Implemented prototype flows:
- Browse and search catalog branches from the live Nexus API.
- Inspect a selected catalog, resource groups, representations, units, time range, metadata and attachments.
- Select resources and show a placeholder telemetry graph wired to the selection state.
- Configure exports through live writer metadata and create an export job with the generated TypeScript Nexus client.

Credentials are not stored in this repo or exposed to browser JavaScript. To run with the local credentials file:

```sh
./dev-with-local-credentials.sh
```

The launcher reads `$HOME/.config/nexus-ui-new/credentials.env` by default. You can override the path with `NEXUS_UI_NEW_CREDENTIALS`.

Use `.env.example` as a template for that external credentials file. The Angular dev proxy injects `NEXUS_TOKEN` server-side for `/api` requests.

Run `npm run build` for a production build, or `npm run lint` for a strict development build (there is no dedicated linter or application test target yet).
