// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Configuration;
using Nexus.Core;
using Xunit;

namespace Other;

public class OptionsTests
{
    private static readonly object _lock = new();

    [InlineData(GeneralOptions.Section, typeof(GeneralOptions))]
    [InlineData(DataOptions.Section, typeof(DataOptions))]
    [InlineData(PathsOptions.Section, typeof(PathsOptions))]
    [InlineData(SecurityOptions.Section, typeof(SecurityOptions))]
    [Theory]
    public void CanBindOptions<T>(string section, Type optionsType)
    {
        var configuration = NexusOptionsBase
            .BuildConfiguration([]);

        var options = (NexusOptionsBase)configuration
            .GetSection(section)
            .Get(optionsType)!;

        Assert.Equal(section, options.BlindSample);
    }

    [Fact]
    public void CanReadAppsettingsJson()
    {
        var configuration = NexusOptionsBase
            .BuildConfiguration([]);

        var options = configuration
            .GetSection(DataOptions.Section)
            .Get<DataOptions>()!;

        Assert.Equal(0.99, options.AggregationNaNThreshold);
    }

    [Fact]
    public void CanOverrideAppsettingsJson_With_Json()
    {
        lock (_lock)
        {
            Environment.SetEnvironmentVariable("NEXUS_PATHS__SETTINGS", "myappsettings.json");

            var configuration = NexusOptionsBase
                .BuildConfiguration([]);

            var options = configuration
                .GetSection(DataOptions.Section)
                .Get<DataOptions>()!;

            Environment.SetEnvironmentVariable("NEXUS_PATHS__SETTINGS", null);

            Assert.Equal(0.90, options.AggregationNaNThreshold);
        }
    }

    [Fact]
    public void CanOverrideIni_With_EnvironmentVariable()
    {
        lock (_lock)
        {
            Environment.SetEnvironmentVariable("NEXUS_PATHS__SETTINGS", "myappsettings.ini");

            var configuration1 = NexusOptionsBase
               .BuildConfiguration([]);

            var options1 = configuration1
                .GetSection(DataOptions.Section)
                .Get<DataOptions>()!;

            Environment.SetEnvironmentVariable("NEXUS_DATA__AGGREGATIONNANTHRESHOLD", "0.90");

            var configuration2 = NexusOptionsBase
               .BuildConfiguration([]);

            var options2 = configuration2
                .GetSection(DataOptions.Section)
                .Get<DataOptions>()!;

            Environment.SetEnvironmentVariable("NEXUS_PATHS__SETTINGS", null);
            Environment.SetEnvironmentVariable("NEXUS_DATA__AGGREGATIONNANTHRESHOLD", null);

            Assert.Equal(0.80, options1.AggregationNaNThreshold);
            Assert.Equal(0.90, options2.AggregationNaNThreshold);
        }
    }

    [InlineData("DATA:AGGREGATIONNANTHRESHOLD=0.99")]
    [InlineData("/DATA:AGGREGATIONNANTHRESHOLD=0.99")]
    [InlineData("--DATA:AGGREGATIONNANTHRESHOLD=0.99")]

    [InlineData("data:aggregationnanthreshold=0.99")]
    [InlineData("/data:aggregationnanthreshold=0.99")]
    [InlineData("--data:aggregationnanthreshold=0.99")]

    [Theory]
    public void CanOverrideEnvironmentVariable_With_CommandLineParameter(string arg)
    {
        lock (_lock)
        {
            Environment.SetEnvironmentVariable("NEXUS_DATA__AGGREGATIONNANTHRESHOLD", "0.90");

            var configuration = NexusOptionsBase
                .BuildConfiguration([arg]);

            var options = configuration
                .GetSection(DataOptions.Section)
                .Get<DataOptions>()!;

            Environment.SetEnvironmentVariable("NEXUS_DATA__AGGREGATIONNANTHRESHOLD", null);

            Assert.Equal(0.99, options.AggregationNaNThreshold);
        }
    }
}