# Notes:
- When no URLs are specified anywhere, the default URLs http://localhost:5000 and https://localhost:5001 are used.

# Problem Description:

- The docker image sets `ENV ASPNETCORE_URLS=http://+:80`.
- When in addition `NEXUS_KESTREL__ENDPOINTS__HTTP__URL=http://0.0.0.0:5000`
is set, this results in the following warning:
```
overriding address(es) '"http://+:80"'. Binding to endpoints defined via IConfiguration and/or UseKestrel() instead. 
```

# Solution

- `launchSettings.json` defines the URL http://localhost:5000 which is used by VSCode and Visual Studio during development.

- When running in a docker container, the default URL http://+:80 is sufficient for older single-resource streaming and regular HTTP API usage behind a TLS-terminating reverse proxy. For v2 batch streaming, configure a Kestrel HTTP/2 endpoint such as `NEXUS_KESTREL__ENDPOINTS__HTTP2__URL=http://+:5000` and `NEXUS_KESTREL__ENDPOINTS__HTTP2__PROTOCOLS=Http2` so the proxy can use HTTP/2 upstream.

- In case the end user needs to change that port he could clear the environment variable `ASPNETCORE_URLS` and set `NEXUS_KESTREL__ENDPOINTS__HTTP__URL` instead (resulting in the warning above), or simply override `ASPNETCORE_URLS` with the desired URLs.
