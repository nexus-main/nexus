# Agent Notes

## Repo Shape
- This is a .NET 9 solution, not a Node workspace; `tailwind.config.js` is only for CSS generation.
- `Nexus.sln` contains the server, Angular UI, generated clients, extensibility contracts/analyzers, and their test projects.
- Root MSBuild policy lives in `Directory.Build.props`: .NET target is `net9.0`, central artifacts go under `artifacts/`, code style is enforced during build, and an extra MyGet source is configured.
- Central .NET package versions are in `Directory.Packages.props`; project files intentionally omit package versions.

## Important Paths
- `src/Nexus/Nexus.csproj` / `src/Nexus/Program.cs`: ASP.NET Core host, REST API, auth, OpenAPI, extension package management, SQLite user DB setup, and app initialization. Serves the Angular production bundle from `wwwroot/` as static files with SPA fallback.
- `src/Nexus/API/v1/`: REST controllers; API surface changes here usually require regenerating clients and `openapi.json`.
- `src/Nexus/Core/`: shared server models/options/auth/OpenAPI helpers; `Models_Public_v1.cs` affects generated client contracts.
- `src/Nexus/Services/`: server-side application services for catalogs, data, jobs, cache, processing, tokens, packages, and upgrades.
- `src/Nexus/Extensibility/`: server glue for runtime data source and data writer extension loading.
- `src/Nexus/Extensions/Sources/` and `src/Nexus/Extensions/Writers/`: built-in extension implementations shipped with the server.
- `src/Nexus.UI/`: Angular 21 + PrimeNG + Tailwind client. `Pages/`, `Components/`, `Controls/`, `Charts/`, `ViewModels/` hold UI code. `src/charts/` contains local copies of the chart renderer JS scripts; `public/js/` contains the synthetic worker.
- `src/Nexus.UI/angular.json`: Angular CLI config; script refs point to `src/charts/chart*.js`.
- `src/Nexus.UI/proxy.conf.cjs`: dev-server proxy, defaults `/api` to `http://localhost:5000`.
- `src/clients/dotnet/`: generated C# REST client package; `NexusClient.g.cs` is generated.
- `src/clients/python/`: generated Python REST client package; generated module lives in `nexus_api/`, packaging metadata in `setup.py`.
- `src/clients/matlab/`: Matlab client assets/samples are separate from generated .NET/Python clients.
- `src/Nexus.ClientGenerator/`: executable that starts the API, reads `/openapi/v1.json`, regenerates C# and Python clients, and writes `openapi.json`.
- `src/extensibility/dotnet-extensibility/`: .NET package with data model and extension contracts for external data sources/writers.
- `src/extensibility/dotnet-extensibility-analyzers/`: Roslyn analyzer for extensibility constraints.
- `src/extensibility/python-extensibility/`: Python extension contract package, with source in `nexus_extensibility/`.
- `tests/Nexus.Tests/`: server unit/integration tests, including controller, service, options, logging, data source, and data writer coverage.
- `tests/clients/dotnet-tests/` and `tests/clients/python-tests/`: generated client behavior tests.
- `tests/extensibility/dotnet-extensibility-tests/` and `tests/extensibility/python-extensibility-tests/`: extension contract/data model tests.
- `build/`: release/version metadata scripts used by CI and package builds (`print_solution.py`, `print_version.py`, `release.py`).
- `notes/`: design notes for auth, configuration, data model/source/writer, permissions, logging, versioning, and memory behavior; use these when changing those subsystems.
- `samples/`: language-specific usage examples for C#, Matlab, and Python.
- `openapi.json`: checked-in generated API document; CI verifies it is fresh.

## Setup And Run
- Run the app with `dotnet run --project src/Nexus/Nexus.csproj`, then open `http://localhost:5000`.
- For UI development, run the Angular dev server: `(cd src/Nexus.UI && npm ci && npm start)`, then open `http://localhost:4200`.
- The Docker image expects a published `app/` folder from `dotnet publish`; the Dockerfile copies `app .` and uses the .NET SDK image so runtime extension compilation works.

## Verification
- Full CI-equivalent core checks: `dotnet test -c Release /p:BuildProjectReferences=false`, then `pyright`, then `pytest`.
- Focus a single .NET test project with `dotnet test tests/Nexus.Tests/Nexus.Tests.csproj` or the specific project under `tests/`.
- Python tests are discovered by `pytest.ini`: files must be `*-tests.py`, classes `*Tests`, functions `*_test`; `pythonpath` is set to both Python source packages.
- `pytest` covers only `tests/clients/python-tests` and `tests/extensibility/python-extensibility-tests`; `src/` is intentionally excluded from recursion.

## Generated Artifacts
- If API surface changes, regenerate clients and `openapi.json` with `dotnet run --project src/Nexus.ClientGenerator/Nexus.ClientGenerator.csproj -- ./ openapi.json`.
- CI verifies OpenAPI freshness with `dotnet run --project src/Nexus.ClientGenerator/Nexus.ClientGenerator.csproj -- ./ openapi_new.json` followed by `diff --strip-trailing-cr openapi.json openapi_new.json`.
- The Angular production bundle is built during `dotnet publish` via an MSBuild target in `Nexus.csproj` that runs `npm ci` + `npm run build` and copies `dist/nexus-ui/browser/` into `wwwroot/`.

## Packaging Notes
- Python package builds rely on metadata environment variables emitted by `python build/print_solution.py` and version values from `python build/print_version.py`.
- CI builds wheels with `python -m build --wheel --outdir artifacts/package --no-isolation src/clients/python` and the same command for `src/extensibility/python-extensibility`.
- Central .NET package versions are in `Directory.Packages.props`; projects intentionally omit package versions.

## Configuration Quirks
- App configuration loads `appsettings.json`, optional `appsettings.{ASPNETCORE_ENVIRONMENT}.json`, optional settings from `NEXUS_PATHS__SETTINGS`, then `NEXUS_` environment variables, then command-line args.
- Default per-user settings path is `~/.local/share/nexus/settings.json` on Linux and `%LOCALAPPDATA%/nexus/settings.json` on Windows.

## Style
- C# uses file-scoped namespaces; `IDE0161` and `IDE1006` are build errors because `EnforceCodeStyleInBuild` is enabled.
- Private instance fields use `_camelCase`; `var` is preferred only when the type is apparent or not a built-in type.
