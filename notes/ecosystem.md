# Extension Types Overview

| Extension Type                | Complexity | Performance | Hosting type                               | Dependencies               | Catalog Paths                    | Example                                                                                                                     |
| ----------------------------- | ---------- | ----------- | ------------------------------------------ | -------------------------- | -------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| C#                            | High       | Highest     | Git repostory with tag (e.g. v1.0.0)       | all                        | all (Admin approves)             | [Example](https://github.com/Apollo3zehn/nexus-sources-gantner)                                                             |
| Python Remote Data Source     | Medium     | High/Medium | Git repostory with tag (e.g. v1.0.0)       | all (requirements.txt)     | all (Admin approves)             | [Example](https://github.com/nexus-main/nexus-sources-remote/blob/master/tests/Nexus.Sources.Remote.Tests/python/remote.py) |
| Python Playground Data Source | Low        | High/Medium | Single folder per user (VSCode SSH access) | only preinstalled packages | /PLAYGROUND/%USERNAME%/CATALOG_X | [Example](https://github.com/Apollo3zehn/nexus-remoting-python-playground)                                                  |

# Extension Ecosystem

```mermaid
flowchart LR

    Nexus.Sources.Federation[Nexus.Sources.Federation]

    subgraph Nexus
        nexus-extensibility["nexus-extensibility (.NET, Python)"]
        nexus-api["nexus-api (.NET, Python)"]
    end

    subgraph Remote data sources
        nexus-remoting-python-playground[nexus-remoting-python-playground]

        subgraph nexus-sources-remote
            nexus-remoting["nexus-remoting (.NET, Python)"]
            Nexus.Sources.Remote[Nexus.Sources.Remote]
        end

        nexus-remoting --> nexus-remoting-python-playground
    end

    subgraph File-based data sources
        nexus-sources-structured-file[Nexus.Sources.StructuredFile]

        nexus-sources-windcube[Nexus.Sources.Windcube]
        nexus-sources-famos[Nexus.Sources.Famos]
        nexus-sources-gantner[Nexus.Sources.Gantner]
        nexus-sources-leospherewindiris[Nexus.Sources.LeosphereWindIris]
        nexus-sources-csv[Nexus.Sources.Csv]
        nexus-sources-campbell[Nexus.Sources.Campbell]

        nexus-sources-structured-file --> nexus-sources-windcube
        nexus-sources-structured-file --> nexus-sources-famos
        nexus-sources-structured-file --> nexus-sources-gantner
        nexus-sources-structured-file --> nexus-sources-leospherewindiris
        nexus-sources-structured-file --> nexus-sources-csv
        nexus-sources-structured-file --> nexus-sources-campbell
    end

    subgraph nexus-agent[Nexus Agent]
        nexus-extensibility["nexus-extensibility (.NET, Python)"]
        nexus-api["nexus-api (.NET, Python)"]
    end

    nexus-api --> Nexus.Sources.Federation

    nexus-extensibility --> Nexus.Sources.Federation
    nexus-extensibility --> Nexus.Sources.Transform
    nexus-extensibility --> Nexus.Sources.Remote
    nexus-extensibility --> nexus-remoting
    nexus-extensibility --> nexus-sources-structured-file

    nexus-remoting --> nexus-agent

```
