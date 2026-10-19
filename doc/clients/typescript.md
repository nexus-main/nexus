# TypeScript client

The TypeScript client package is published as [`@apollo3zehn/nexus-api`](https://www.npmjs.com/package/@apollo3zehn/nexus-api).

```bash
npm install @apollo3zehn/nexus-api
```

The package is ESM-only and exports the client plus versioned API model namespaces:

```ts
import { NexusClient, V1, V2 } from "@apollo3zehn/nexus-api";
```

Use the TypeScript client for browser applications, Node.js services, or integration tests that should use the same generated API contracts as the Nexus UI.

There are no dedicated TypeScript examples yet. Until they are added, the Python samples are still useful as a guide because the generated clients expose the same API concepts: authenticate, browse catalogs, select resources, request time ranges, and stream or export data.

## Notes

- The package depends on Apache Arrow for streamed data handling.
- Keep the package version aligned with the server version.
- Use the versioned namespaces when referring to request and response models explicitly.
