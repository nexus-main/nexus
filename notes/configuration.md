Nexus is being configured using an `IConfiguration` instance which enables multiple ways to add or override application settings. You can find the general documentation [here](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-6.0).

The following configuration providers are supported (last one has highest precedence):

1. [JSON](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-9.0#jcp): Loads the [settings.json](https://github.com/malstroem-labs/Nexus/blob/master/src/Nexus/settings.json) that provides default settings for the application.

2. [JSON](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-6.0#jcp): Loads a custom json file if the environment variable `NEXUS_PATHS__SETTINGS` points to an existing JSON file. If the variable is not set, the default path is `/etc/nexus/appsettings.json` (Linux) or `%appdata%\Nexus\appsettings.json` (Windows).

3. [Environment Variables](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-6.0#environment-variables): All environment variables must be prefixed with `NEXUS_`, e.g. `NEXUS_PATHS__SETTINGS`.

4. [Command Line](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-6.0#command-line): Add or override settings using the command line.