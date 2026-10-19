# Configuration

Nexus configuration connects installed plugins to actual data, controls access, and stores administrative state such as pipelines, package references, tokens, and catalog metadata.

Configuration can be changed through the UI and REST API. The Git-backed history feature records changes in the configuration directory so administrators can review and restore previous states.

## Configuration files and paths

The default per-user settings path is:

- Linux: `~/.local/share/nexus/settings.json`
- Windows: `%LOCALAPPDATA%/nexus/settings.json`

Important storage paths are configured under `Paths`:

- `Config`: administrative configuration,
- `Cache`: cache files,
- `Catalogs`: catalog attachments,
- `Artifacts`: generated export artifacts,
- `Packages`: restored extension packages.

Settings can be supplied from JSON/INI files, environment variables prefixed with `NEXUS_`, and command-line arguments.

For the full list of server options, defaults, JSON paths, and environment-variable names, see the [configuration options reference](options.md).
