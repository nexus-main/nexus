using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.Services;
using Serilog;
using Serilog.Extensions.Logging;
using System.Text.RegularExpressions;
using Xunit;

namespace Other;

// Information from Microsoft:
// https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-2.2#log-level

// Best practices:
// https://blog.rsuter.com/logging-with-ilogger-recommendations-and-best-practices/

// Attaching a large state might lead to very large logs. Nicholas Blumhardt recommends to 
// simply send a single verbose message with identifier and all other messages should contain
// that identifier, too, so they can be correlated later ("log once, correlate later").

public class LoggingTests
{
    [Fact]
    public void CanSerilog()
    {
        // create dirs
        var root = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
        Directory.CreateDirectory(root);

        // 1. Configure Serilog
        Environment.SetEnvironmentVariable("NEXUS_SERILOG__MINIMUMLEVEL__OVERRIDE__Nexus.Services", "Verbose");

        Environment.SetEnvironmentVariable("NEXUS_SERILOG__WRITETO__1__NAME", "File");
        Environment.SetEnvironmentVariable("NEXUS_SERILOG__WRITETO__1__ARGS__PATH", Path.Combine(root, "log.txt"));
        Environment.SetEnvironmentVariable("NEXUS_SERILOG__WRITETO__1__ARGS__OUTPUTTEMPLATE", "[{Level:u3}] {MyCustomProperty} {Message}{NewLine}{Exception}");

        Environment.SetEnvironmentVariable("NEXUS_SERILOG__WRITETO__2__NAME", "GrafanaLoki");
        Environment.SetEnvironmentVariable("NEXUS_SERILOG__WRITETO__2__ARGS__URI", "http://localhost:3100");
        Environment.SetEnvironmentVariable("NEXUS_SERILOG__WRITETO__2__ARGS__LABELS__0__KEY", "app");
        Environment.SetEnvironmentVariable("NEXUS_SERILOG__WRITETO__2__ARGS__LABELS__0__VALUE", "nexus");
        Environment.SetEnvironmentVariable("NEXUS_SERILOG__WRITETO__2__ARGS__OUTPUTTEMPLATE", "{Message}{NewLine}{Exception}");

        Environment.SetEnvironmentVariable("NEXUS_SERILOG__WRITETO__3__NAME", "Seq");
        Environment.SetEnvironmentVariable("NEXUS_SERILOG__WRITETO__3__ARGS__SERVERURL", "http://localhost:5341");

        Environment.SetEnvironmentVariable("NEXUS_SERILOG__ENRICH__1", "WithMachineName");

        // 2. Build the configuration
        var configuration = NexusOptionsBase.BuildConfiguration(Array.Empty<string>());

        var serilogger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.WithProperty("MyCustomProperty", "MyCustomValue")
            .CreateLogger();

        var loggerFactory = new SerilogLoggerFactory(serilogger);

        // 3. Create a logger
        var logger = loggerFactory.CreateLogger<DataService>();

        // 3.1 Log-levels
        logger.LogTrace("Trace");
        logger.LogDebug("Debug");
        logger.LogInformation("Information");
        logger.LogWarning("Warning");
        logger.LogError("Error");
        logger.LogCritical("Critical");

        // 3.2 Log with exception
        try
        {
            throw new Exception("Something went wrong?!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error");
        }

        // 3.3 Log with template
        var context1 = new { Amount = 108, Message = "Hello" };
        var context2 = new { Amount2 = 108, Message2 = "Hello" };

        logger.LogInformation("Log with template with parameters {Text}, {Number} and {AnonymousType}", "A", 2.59, context1);

        // 3.4 Log with scope with template
        using (var scopeWithTemplate = logger.BeginScope("My templated scope message with parameters {ScopeText}, {ScopeNumber} and {ScopeAnonymousType}", "A", 2.59, context1))
        {
            logger.LogInformation("Log with scope with template with parameters {Text}, {Number} and {AnonymousType}", "A", 2.59, context1);
        }

        // 3.5 Log with double scope with template
        using (var scopeWithTemplate1 = logger.BeginScope("My templated scope message 1 with parameters {ScopeText1}, {ScopeNumber} and {ScopeAnonymousType}", "A", 2.59, context1))
        {
            using var scopeWithTemplate2 = logger.BeginScope("My templated scope message 2 with parameters {ScopeText2}, {ScopeNumber} and {ScopeAnonymousType}", "A", 3.59, context2);
            logger.LogInformation("Log with double scope with template with parameters {Text}, {Number} and {AnonymousType}", "A", 2.59, context1);
        }

        // 3.6 Log with scope with state
        using (var scopeWithState = logger.BeginScope(context1))
        {
            logger.LogInformation("Log with scope with state with parameters {Text}, {Number} and {AnonymousType}", "A", 2.59, context1);
        }

        // 3.7 Log with double scope with state
        using (var scopeWithState1 = logger.BeginScope(context1))
        {
            using var scopeWithState2 = logger.BeginScope(context2);
            logger.LogInformation("Log with double scope with state with parameters {Text}, {Number} and {AnonymousType}", "A", 2.59, context1);
        }

        // 3.8 Log with scope with Dictionary<string, object> state
        using (var scopeWithState1 = logger.BeginScope(new Dictionary<string, object>()
        {
            ["Amount"] = context1.Amount,
            ["Message"] = context1.Message
        }))
        {
            logger.LogInformation("Log with scope with Dictionary<string, object> state");
        }

        // 3.9 Log with named scope
        using (var namedScope = logger.BeginNamedScope("MyScopeName", new Dictionary<string, object>()
        {
            ["Amount"] = context1.Amount,
            ["Message"] = context1.Message
        }))
        {
            logger.LogInformation("Log with named scope");
        }

        serilogger.Dispose();

        // assert
        var expected = Regex.Replace(Regex.Replace(File.ReadAllText("expected-log.txt"), "\r\n", "\n"), @"in .*?tests.*?LoggingTests.cs", "");
        var actual = Regex.Replace(Regex.Replace(File.ReadAllText(Path.Combine(root, "log.txt")), "\r\n", "\n"), @"in .*?tests.*?LoggingTests.cs", "");

        Assert.Equal(expected, actual);

        // clean up
        try
        {
            Directory.Delete(root, true);
        }
        catch
        {
            //
        }
    }
}