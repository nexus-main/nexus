using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Nexus.Core;
using Nexus.Utilities;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Microsoft.Extensions.DependencyInjection;

internal static class NexusAuthExtensions
{
    public static OpenIdConnectProvider DefaultProvider { get; } = new OpenIdConnectProvider()
    {
        Scheme = "nexus",
        DisplayName = "Nexus",
        Authority = NexusUtilities.DefaultBaseUrl,
        ClientId = "nexus",
        ClientSecret = "nexus-secret"
    };

    public static IServiceCollection AddNexusAuth(
        this IServiceCollection services,
        PathsOptions pathsOptions,
        SecurityOptions securityOptions)
    {
        /* https://stackoverflow.com/a/52493428/1636629 */

        JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(pathsOptions.Config, "data-protection-keys")));

        var builder = services

            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })

            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.ExpireTimeSpan = securityOptions.CookieLifetime;
                options.SlidingExpiration = false;

                options.LoginPath = "/login";
            })

            .AddScheme<AuthenticationSchemeOptions, PersonalAccessTokenAuthHandler>(
                PersonalAccessTokenAuthenticationDefaults.AuthenticationScheme, default);

        var providers = securityOptions.OidcProviders.Any()
            ? securityOptions.OidcProviders
            : [DefaultProvider];

        foreach (var provider in providers)
        {
            if (provider.Scheme == CookieAuthenticationDefaults.AuthenticationScheme)
                continue;

            builder.AddOpenIdConnect(provider.Scheme, provider.DisplayName, options =>
            {
                options.Authority = provider.Authority;
                options.ClientId = provider.ClientId;
                options.ClientSecret = provider.ClientSecret;

                options.CallbackPath = $"/signin-oidc/{provider.Scheme}";
                options.SignedOutCallbackPath = $"/signout-oidc/{provider.Scheme}";

                options.ResponseType = OpenIdConnectResponseType.Code;

                options.TokenValidationParameters.AuthenticationType = provider.Scheme;
                options.TokenValidationParameters.NameClaimType = Claims.Name;
                options.TokenValidationParameters.RoleClaimType = Claims.Role;

                /* user info endpoint is contacted AFTER OnTokenValidated, which requires the name claim to be present */
                options.GetClaimsFromUserInfoEndpoint = false;

                var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

                if (environmentName == "Development")
                    options.RequireHttpsMetadata = false;

                options.Events = new OpenIdConnectEvents()
                {
                    OnTokenResponseReceived = context =>
                    {
                        /* OIDC spec RECOMMENDS id_token_hint (= id_token) to be added when
                         * post_logout_redirect_url is specified 
                         * (https://openid.net/specs/openid-connect-rpinitiated-1_0.html)
                         * 
                         * To be able to provide that parameter the ID token must become
                         * part of the auth cookie. The /connect/logout endpoint in 
                         * NexusIdentityProviderExtensions.cs is then getting that logout_hint
                         * query parameter automatically (this has been tested!).
                         * This parameter is then part of the httpContext.Request.Query dict.
                         *
                         * Why do we enable this when this is just recommended? Because newer
                         * version of Keycloak REQUIRE it, otherwise we get a 
                         * "Missing parameters: id_token_hint" error.
                         *
                         * Problem is very large size (> 8 kB) of cookie when setting
                         * options.SaveTokens = true; because then ALL OIDC tokens are stored
                         * in the cookie then.
                         *
                         * Solution: https://github.com/dotnet/aspnetcore/issues/30016#issuecomment-786384559
                         * 
                         * Cookie size is ~3.9 kB now. Unprotected cookie size is 2.2 kB
                         * (https://stackoverflow.com/a/69047119/1636629) where 1 kB, or 50%, 
                         * comes from the id_token.
                         */
                        context.Properties!.StoreTokens(new[]
                        {
                            new AuthenticationToken
                            {
                                Name = "id_token",
                                Value = context.TokenEndpointResponse.IdToken
                            }
                        });

                        return Task.CompletedTask;
                    },

                    OnTokenValidated = async context =>
                    {
                        // scopes
                        // https://openid.net/specs/openid-connect-basic-1_0.html#Scopes

                        var principal = context.Principal 
                            ?? throw new Exception("The principal is null. This should never happen.");
                        var userId = principal.FindFirstValue(Claims.Subject)
                            ?? throw new Exception("The subject claim is missing. This should never happen.");

                        var username = principal.FindFirstValue(Claims.Name)
                            ?? throw new Exception("The name claim is required.");

                        using var dbContext = context.HttpContext.RequestServices.GetRequiredService<UserDbContext>();
                        var uniqueUserId = $"{Uri.EscapeDataString(userId)}@{Uri.EscapeDataString(context.Scheme.Name)}";

                        // user
                        var user = await dbContext.Users
                            .Include(user => user.Claims)
                            .SingleOrDefaultAsync(user => user.Id == uniqueUserId);

                        if (user is null)
                        {
                            var newClaims = new List<NexusClaim>();
                            var isFirstUser = !dbContext.Users.Any();

                            if (isFirstUser)
                                newClaims.Add(new NexusClaim(Guid.NewGuid(), Claims.Role, NexusRoles.ADMINISTRATOR));

                            newClaims.Add(new NexusClaim(Guid.NewGuid(), Claims.Role, NexusRoles.USER));

                            user = new NexusUser(
                                id: uniqueUserId,
                                name: username)
                            {
                                Claims = newClaims
                            };

                            dbContext.Users.Add(user);
                        }

                        else
                        {
                            // user name may change, so update it
                            user.Name = username;
                        }

                        await dbContext.SaveChangesAsync();

                        // oidc identity
                        var oidcIdentity = (ClaimsIdentity)principal.Identity!;
                        var subClaim = oidcIdentity.FindFirst(Claims.Subject);

                        if (subClaim is not null)
                            oidcIdentity.RemoveClaim(subClaim);

                        oidcIdentity.AddClaim(new Claim(Claims.Subject, uniqueUserId));

                        // app identity
                        var claims = user.Claims.Select(entry => new Claim(entry.Type, entry.Value));

                        var appIdentity = new ClaimsIdentity(
                            claims,
                            authenticationType: context.Scheme.Name,
                            nameType: Claims.Name,
                            roleType: Claims.Role);

                        principal.AddIdentity(appIdentity);
                    }
                };
            });
        }

        var authenticationSchemes = new[]
        {
            CookieAuthenticationDefaults.AuthenticationScheme,
            PersonalAccessTokenAuthenticationDefaults.AuthenticationScheme
        };

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireRole(NexusRoles.USER)
                .AddAuthenticationSchemes(authenticationSchemes)
                .Build())
            .AddPolicy(NexusPolicies.RequireAdmin, policy => policy
                .RequireRole(NexusRoles.ADMINISTRATOR)
                .AddAuthenticationSchemes(authenticationSchemes));

        return services;
    }
}
