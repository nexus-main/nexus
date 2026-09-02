# nexus-api

A REST client for Nexus.

High-level `load` uses v2 batch streaming and requires an HTTPS base URL so HTTP/2 can be negotiated. `NexusClient.create` and `NexusAsyncClient.create` enable HTTP/2; injected `httpx` clients must be configured accordingly.
