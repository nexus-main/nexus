# Package references

Package references tell Nexus which extension packages are available. They are administrative configuration because extension code runs inside the Nexus process or as part of the configured extension infrastructure.

Only trusted packages should be added.

## What package references are used for

Package references are used to install and load plugins such as:

- data sources,
- data writers,
- supporting extension packages.

When Nexus starts, it restores package references and discovers extension implementations.

## Administrative workflow

Administrators can manage package references through the Nexus UI. After changing package references, Nexus reloads extension state so newly available sources and writers can be configured.

## Security considerations

Extension packages are code. Treat them like server-side deployments:

- use trusted repositories,
- pin versions where possible,
- review package provenance,
- avoid broad access to secrets,
- keep plugin code focused on reading or writing the intended data.

Use catalog permissions and token restrictions to control what users can access through the configured sources.
