# Nexus UI New

Prototype React 19 + Vite + Tailwind client for Nexus. This is intentionally not a port of `Nexus.UI`; it explores a denser Grafana/InfluxDB-style direction for catalog-heavy workflows.

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

Use `.env.example` as a template for that external credentials file. The Vite dev proxy injects `NEXUS_TOKEN` server-side for `/api` requests.
