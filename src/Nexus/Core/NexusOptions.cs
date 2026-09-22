// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Runtime.InteropServices;

namespace Nexus.Core;

// TODO: Records with IConfiguration: wait for issue https://github.com/dotnet/runtime/issues/43662 to be solved

// template: https://grafana.com/docs/grafana/latest/administration/configuration/

internal abstract record NexusOptionsBase()
{
    // for testing only
    public string? BlindSample { get; set; }

    internal static IConfiguration BuildConfiguration(string[] args)
    {
        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        var builder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json");

        if (!string.IsNullOrWhiteSpace(environmentName))
        {
            builder
                .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true);
        }

        var settingsPath = Environment.GetEnvironmentVariable("NEXUS_PATHS__SETTINGS");

        settingsPath ??= PathsOptions.DefaultSettingsPath;

        if (settingsPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            builder.AddJsonFile(settingsPath, optional: true, /* for serilog */ reloadOnChange: true);

        else if (settingsPath.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
            builder.AddIniFile(settingsPath, optional: true, /* for serilog */ reloadOnChange: true);

        builder
            .AddEnvironmentVariables(prefix: "NEXUS_")
            .AddCommandLine(args);

        return builder.Build();
    }
}

internal record GeneralOptions() : NexusOptionsBase
{
    public const string Section = "General";

    public string? ApplicationName { get; set; } = "Nexus";

    public string? HelpLink { get; set; }
}

internal record DataOptions() : NexusOptionsBase
{
    public const string Section = "Data";

    public string? CachePattern { get; set; }

    public long TotalBufferMemoryConsumption { get; set; } = 1 * 1024 * 1024 * 1024; // 1 GB

    public double AggregationNaNThreshold { get; set; } = 0.99;
}

internal record PathsOptions() : NexusOptionsBase, IPackageManagementPathsOptions
{
    public const string Section = "Paths";

    public string Config { get; set; } = Path.Combine(PlatformSpecificRoot, "config");

    public string Cache { get; set; } = Path.Combine(PlatformSpecificRoot, "cache");

    public string Catalogs { get; set; } = Path.Combine(PlatformSpecificRoot, "catalogs");

    public string Artifacts { get; set; } = Path.Combine(PlatformSpecificRoot, "artifacts");

    public string Packages { get; set; } = Path.Combine(PlatformSpecificRoot, "packages");

    #region Support

    public static string DefaultSettingsPath { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nexus", "settings.json")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "nexus", "settings.json");

    private static string PlatformSpecificRoot { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nexus")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "nexus");

    #endregion
}

internal partial record SecurityOptions() : NexusOptionsBase
{
    public const string Section = "Security";

    public string UserHeader { get; set; } = "X-Forwarded-User";

    public string NameHeader { get; set; } = "X-Forwarded-Preferred-Username";

    public string GroupsHeader { get; set; } = "X-Forwarded-Groups";

    public string AdministratorGroup { get; set; } = "nexus-admin";

    public string EnabledCatalogsPattern { get; set; } = DEFAULT_ENABLED_CATALOGS_PATTERN;

    public string EnabledCatalogsPatternHeader { get; set; } = "X-Forwarded-EnabledCatalogsPattern";

    public string CanReadCatalogHeader { get; set; } = "X-Forwarded-CanReadCatalog";

    public string CanWriteCatalogHeader { get; set; } = "X-Forwarded-CanWriteCatalog";

    public string CanReadCatalogGroupHeader { get; set; } = "X-Forwarded-CanReadCatalogGroup";

    public string CanWriteCatalogGroupHeader { get; set; } = "X-Forwarded-CanWriteCatalogGroup";

    public string? LogoutUrl { get; set; }

    public const string DEFAULT_ENABLED_CATALOGS_PATTERN = "" /* == match all */;
}
