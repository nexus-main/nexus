// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Nexus.Core;

namespace Microsoft.Extensions.DependencyInjection;

internal static class NexusAuthExtensions
{
    public static IServiceCollection AddNexusAuth(
        this IServiceCollection services
    )
    {
        var builder = services

            .AddAuthentication(options =>
            {
                options.DefaultScheme = HeaderAuthenticationDefaults.AuthenticationScheme;
            })

            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(
                HeaderAuthenticationDefaults.AuthenticationScheme, default)

            .AddScheme<AuthenticationSchemeOptions, PersonalAccessTokenAuthHandler>(
                PersonalAccessTokenAuthenticationDefaults.AuthenticationScheme, default);

        var authenticationSchemes = new[]
        {
            HeaderAuthenticationDefaults.AuthenticationScheme,
            PersonalAccessTokenAuthenticationDefaults.AuthenticationScheme
        };

        services.AddAuthorizationBuilder()

            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddAuthenticationSchemes(authenticationSchemes)
                .Build())

            .AddPolicy(NexusPolicies.RequireAdmin, policy => policy
                .RequireRole(nameof(NexusRoles.Administrator))
                .AddAuthenticationSchemes(authenticationSchemes));

        return services;
    }
}
