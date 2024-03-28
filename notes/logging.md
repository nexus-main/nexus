Nexus relies on [Serilog](https://serilog.net/) to produce logs. Serilog completely replaces the standard logging mechanism, which means that the `Logging` configuration section is being ignored and the `Serilog` section is used instead. Therefore, the default `Logging` section has been removed from the [appsettings.json file](https://github.com/nexus-main/Nexus/blob/master/src/Nexus/appsettings.json).

Currently, the following sinks and enrichers are supported by Nexus:

**Sinks**
- [Console](https://github.com/serilog/serilog-sinks-console) *enabled by default*
- [Debug](https://github.com/serilog/serilog-sinks-debug)
- [File](https://github.com/serilog/serilog-sinks-file)
- [Grafana.Loki](https://github.com/serilog-contrib/serilog-sinks-grafana-loki)
- [Seq](https://github.com/serilog/serilog-sinks-seq)

**Enrichers**
- [ClientInfo](https://github.com/mo-esmp/serilog-enrichers-clientinfo)
- [CorrelationId](https://github.com/ekmsystems/serilog-enrichers-correlation-id)
- [Environment](https://github.com/serilog/serilog-enrichers-environment)

The default log level is `Information`, which can be easily modified using one of the methods shown in [Configuration](configuration.md). For example, you could set the environment variable `NEXUS_SERILOG__MINIMUMLEVEL__OVERRIDE__Nexus` to `Verbose` to also receive `Trace` and `Debug` message.

[Here](https://github.com/nexus-main/Nexus/blob/master/tests/Nexus.Core.Tests/Other/LoggingTests.cs) you can find some more examples to enable the `File`, `GrafanaLoki` or `Seq` using environment variables. You can do the same using your own configuration file or command line args as shown in [Configuration](configuration.md).

Logging follows the [best practices guide](https://benfoster.io/blog/serilog-best-practices/).

# Change Log Level at Runtime
To change the log level at runtime, make sure you have an `settings.json` file created before the application starts. The location of the file can be defined by the environment variable `NEXUS_PATHS__SETTINGS`, otherwise the [default path](https://github.com/nexus-main/Nexus/blob/c69425659927956cb12efcd0749ca64d00464509/src/Nexus.Core/Core/NexusOptions.cs#L67) is used. The contents of the file should look like:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.Hosting.Lifetime": "Information",
        "Microsoft": "Warning",
        "Nexus": "Verbose",
        "Serilog": "Information"
      }
    }
  }
}
```

The log level is updated when the file is saved to disk.

# Async and Multithreading Support

`ILogger` scopes support multithreading and async context flow using `AsyncLocal` state variable (https://stackoverflow.com/a/63852241). The following examples shows that a logger scope applies to all log messages not matter which logger instance is invoked and which one is used to create the scope. It also demonstrates that parallel task execution is no problem:

```cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace logging
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var serviceProvider = new ServiceCollection()
                .AddTransient<Service1>()
                .AddTransient<Service2>()
                .AddLogging(logging => logging.AddSystemdConsole(configure => configure.IncludeScopes = true))
                .BuildServiceProvider();

            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var dummyLogger = loggerFactory.CreateLogger("dummy-logger");
            var tasks = new List<Task>();

            for (int i = 0; i < 10; i++)
            {
                var index = i;

                tasks.Add(Task.Run(() =>
                {
                    Service1 service1;

                    using (var scope = dummyLogger.BeginScope("constructor-scope =>"))
                    {
                        service1 = serviceProvider.GetRequiredService<Service1>();
                    }

                    using (var scope = dummyLogger.BeginScope($"Scope for Service1 ({index}) =>"))
                    {
                        service1.SayHello();
                    }

                    Service2 service2;

                    using (var scope = dummyLogger.BeginScope("constructor-scope =>"))
                    {
                        service2 = serviceProvider.GetRequiredService<Service2>();
                    }

                    using (var scope = dummyLogger.BeginScope($"Scope for Service2 ({index}) =>"))
                    {
                        service2.SayHello();
                    }
                }));
            }

            await Task.WhenAll(tasks);
        }
    }
}

```

Output:

```
<6>logging.Service1[0] => constructor-scope => Constructed Service 1 on thread 10
<6>logging.Service1[0] => constructor-scope => Constructed Service 1 on thread 7
<6>logging.Service1[0] => constructor-scope => Constructed Service 1 on thread 8
<6>logging.Service1[0] => constructor-scope => Constructed Service 1 on thread 11
<6>logging.Service1[0] => constructor-scope => Constructed Service 1 on thread 4
<6>logging.Service1[0] => constructor-scope => Constructed Service 1 on thread 9
<6>logging.Service1[0] => constructor-scope => Constructed Service 1 on thread 12
<6>logging.Service1[0] => constructor-scope => Constructed Service 1 on thread 5
<6>logging.Service1[0] => Scope for Service1 (1) => Hello from Service 1 on thread 10
<6>logging.Service1[0] => Scope for Service1 (4) => Hello from Service 1 on thread 5
<6>logging.Service1[0] => Scope for Service1 (6) => Hello from Service 1 on thread 11
<6>logging.Service1[0] => Scope for Service1 (0) => Hello from Service 1 on thread 8
<6>logging.Service1[0] => Scope for Service1 (7) => Hello from Service 1 on thread 12
<6>logging.Service1[0] => Scope for Service1 (3) => Hello from Service 1 on thread 4
<6>logging.Service1[0] => Scope for Service1 (5) => Hello from Service 1 on thread 7
<6>logging.Service1[0] => Scope for Service1 (2) => Hello from Service 1 on thread 9
<6>logging.Service2[0] => constructor-scope => Constructed Service 2 on thread 12
<6>logging.Service2[0] => constructor-scope => Constructed Service 2 on thread 5
<6>logging.Service2[0] => constructor-scope => Constructed Service 2 on thread 9
<6>logging.Service2[0] => constructor-scope => Constructed Service 2 on thread 10
<6>logging.Service2[0] => constructor-scope => Constructed Service 2 on thread 4
<6>logging.Service2[0] => constructor-scope => Constructed Service 2 on thread 11
<6>logging.Service2[0] => constructor-scope => Constructed Service 2 on thread 8
<6>logging.Service2[0] => constructor-scope => Constructed Service 2 on thread 7
<6>logging.Service2[0] => Scope for Service2 (0) => Hello from Service 2 on thread 8
<6>logging.Service2[0] => Scope for Service2 (5) => Hello from Service 2 on thread 7
<6>logging.Service2[0] => Scope for Service2 (7) => Hello from Service 2 on thread 12
<6>logging.Service2[0] => Scope for Service2 (4) => Hello from Service 2 on thread 5
<6>logging.Service2[0] => Scope for Service2 (2) => Hello from Service 2 on thread 9
<6>logging.Service2[0] => Scope for Service2 (1) => Hello from Service 2 on thread 10
<6>logging.Service2[0] => Scope for Service2 (3) => Hello from Service 2 on thread 4
<6>logging.Service2[0] => Scope for Service2 (6) => Hello from Service 2 on thread 11
<6>logging.Service1[0] => constructor-scope => Constructed Service 1 on thread 7
<6>logging.Service1[0] => Scope for Service1 (8) => Hello from Service 1 on thread 7
<6>logging.Service2[0] => constructor-scope => Constructed Service 2 on thread 7
<6>logging.Service1[0] => constructor-scope => Constructed Service 1 on thread 10
<6>logging.Service1[0] => Scope for Service1 (9) => Hello from Service 1 on thread 10
<6>logging.Service2[0] => Scope for Service2 (8) => Hello from Service 2 on thread 7
<6>logging.Service2[0] => constructor-scope => Constructed Service 2 on thread 10
<6>logging.Service2[0] => Scope for Service2 (9) => Hello from Service 2 on thread 10
```