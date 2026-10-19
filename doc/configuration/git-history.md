# Git-backed configuration history

Nexus can track its configuration directory with Git. This gives administrators a history of configuration changes, file-level diffs, and a way to restore an earlier configuration state.

The feature is for Nexus configuration, not for the measurement data itself.

## What is tracked

Git history tracks files in the configured `Paths:Config` directory. This includes administrative configuration such as data source pipelines, package references, user token metadata, and catalog metadata stored there.

Cache files, export artifacts, package caches, and raw measurement data are not part of this history unless a deployment explicitly places them in the configuration directory, which should be avoided.

## How it works

On startup, Nexus checks whether Git is available. If it is, Nexus initializes or opens a repository in the configuration directory. A file watcher observes configuration changes and commits them after a throttle interval.

Administrators can use the UI or API to:

- view effective Git configuration,
- see current status,
- inspect history,
- view file-level diffs for a commit,
- restore a previous configuration by creating a new restore commit.

The administrative API is under `/api/v1/git` and requires administrator permissions.

## Settings

Git settings are configured under `Git`:

| Setting | Meaning | Default |
| --- | --- | --- |
| `Branch` | Branch used for the configuration repository. | `main` |
| `CommitThrottleSeconds` | Delay used to group rapid configuration changes into one commit. | `300` |
| `RemoteUrl` | Optional remote repository URL for pushing configuration history. | empty |
| `Username` | Optional username for remote authentication. | empty |
| `Token` | Optional token for remote authentication. | empty |
| `SshPrivateKey` | Optional SSH private key for remote authentication. | empty |
| `CommitAuthorName` | Author name used for automatic commits. | `Nexus` |
| `CommitAuthorEmail` | Author email used for automatic commits. | `nexus@localhost` |

## Restore behavior

Restoring a previous version does not rewrite history. Nexus checks out the selected configuration state and commits that restored state as a new commit. This keeps the audit trail intact.

## Deployment guidance

- Ensure `git` is installed in the runtime environment if history should be enabled.
- Configure a remote repository if configuration history should survive host replacement.
- Do not store large generated files or raw measurement data below `Paths:Config`.
- Treat remote credentials as secrets and provide them through secure deployment configuration.
