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

- When running in a docker container, the default URL http://+:80 is just fine because normally you would use a reverse proxy in front of Nexus which terminates the TLS connection.

- In case the end user needs to change that port he could clear the environment variable `ASPNETCORE_URLS` and set `NEXUS_KESTREL__ENDPOINTS__HTTP__URL` instead (resulting in the warning above), or simply override `ASPNETCORE_URLS` with the desired URLs.
