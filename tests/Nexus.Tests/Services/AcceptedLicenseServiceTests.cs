// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Options;
using Moq;
using Nexus.Core;
using Nexus.Services;
using System.Security.Claims;
using Xunit;

namespace Services;

public class AcceptedLicenseServiceTests
{
    [Fact]
    public async Task CanPersistAcceptedLicenseAsync()
    {
        var configPath = GetTempPath();

        try
        {
            var service = GetService(configPath);

            await service.AcceptAsync("user", "/A/B", "license");

            Assert.True(service.HasAccepted("user", "/A/B", "license"));
        }
        finally
        {
            Delete(configPath);
        }
    }

    [Fact]
    public async Task ChangedLicenseRequiresNewAcceptanceAsync()
    {
        var configPath = GetTempPath();

        try
        {
            var service = GetService(configPath);

            await service.AcceptAsync("user", "/A/B", "license 1");

            Assert.True(service.HasAccepted("user", "/A/B", "license 1"));
            Assert.False(service.HasAccepted("user", "/A/B", "license 2"));
        }
        finally
        {
            Delete(configPath);
        }
    }

    [Theory]
    [InlineData(HeaderAuthenticationDefaults.AuthenticationScheme)]
    [InlineData(PersonalAccessTokenAuthenticationDefaults.AuthenticationScheme)]
    public async Task AcceptedLicenseIsMatchedByAuthenticatedSubjectAsync(string authenticationType)
    {
        var configPath = GetTempPath();

        try
        {
            var service = GetService(configPath);
            var user = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(NexusClaimTypes.Subject, "user")],
                authenticationType));

            await service.AcceptAsync("user", "/A/B", "license");

            Assert.True(service.HasAccepted(user, "/A/B", "license"));
        }
        finally
        {
            Delete(configPath);
        }
    }

    private static AcceptedLicenseService GetService(string configPath)
    {
        var options = Options.Create(new PathsOptions()
        {
            Config = configPath
        });

        return new AcceptedLicenseService(options, Mock.Of<IDatabaseService>());
    }

    private static string GetTempPath()
    {
        return Path.Combine(Path.GetTempPath(), "Nexus", Guid.NewGuid().ToString());
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
