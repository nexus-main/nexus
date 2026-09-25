// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Nexus.Utilities;

namespace Nexus.Core;

internal static class HeaderAuthenticationDefaults
{
    public const string AuthenticationScheme = "header";
    public const string DevelopmentRoleHeader = "X-Nexus-Dev-Role";
}

internal class HeaderAuthenticationHandler(
    IOptions<SecurityOptions> securityOptions,
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private readonly SecurityOptions _securityOptions = securityOptions.Value;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        try
        {
            return Task.FromResult(HandleAuthenticate());
        }

        catch (Exception ex)
        {
            return Task.FromResult(AuthenticateResult.Fail(ex.Message));
        }
    }

    private AuthenticateResult HandleAuthenticate()
    {
        var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        var userId = Request.Headers[_securityOptions.UserHeader].FirstOrDefault();

        if (string.IsNullOrEmpty(userId))
        {
            if (isDevelopment)
            {
                var devClaims = new List<Claim>()
                {
                    new(NexusClaimTypes.Subject, "star-lord"),
                    new(NexusClaimTypes.Name, "Star Lord")
                };

                var devRole = Request.Headers[HeaderAuthenticationDefaults.DevelopmentRoleHeader].FirstOrDefault();

                if (!string.Equals(devRole, "user", StringComparison.OrdinalIgnoreCase))
                    devClaims.Add(new(NexusClaimTypes.Role, nameof(NexusRoles.Administrator)));

                var devIdentity = new ClaimsIdentity(
                    devClaims,
                    Scheme.Name,
                    nameType: NexusClaimTypes.Name,
                    roleType: NexusClaimTypes.Role
                );

                var devPrincipal = new ClaimsPrincipal(devIdentity);
                AuthUtilities.SetEnabledCatalogPatternClaim(devPrincipal, _securityOptions.EnabledCatalogsPattern);

                return AuthenticateResult.Success(new AuthenticationTicket(devPrincipal, Scheme.Name));
            }

            return AuthenticateResult.NoResult();
        }

        var claims = new List<Claim>
        {
            new(NexusClaimTypes.Subject, userId)
        };

        var name = Request.Headers[_securityOptions.NameHeader].FirstOrDefault();

        if (name is not null)
            claims.Add(new Claim(NexusClaimTypes.Name, name));

        var groups = ParseClaimHeader(_securityOptions.GroupsHeader);
        var isAdmin = isDevelopment
            ? IsDevelopmentAdminMode()
            : groups.Any(group => group == _securityOptions.AdministratorGroup);

        if (isAdmin)
            claims.Add(new Claim(NexusClaimTypes.Role, nameof(NexusRoles.Administrator)));

        AddClaimArrayHeader(claims, _securityOptions.CanReadCatalogHeader, nameof(NexusClaims.CanReadCatalog));
        AddClaimArrayHeader(claims, _securityOptions.CanWriteCatalogHeader, nameof(NexusClaims.CanWriteCatalog));
        AddClaimArrayHeader(claims, _securityOptions.CanReadCatalogGroupHeader, nameof(NexusClaims.CanReadCatalogGroup));
        AddClaimArrayHeader(claims, _securityOptions.CanWriteCatalogGroupHeader, nameof(NexusClaims.CanWriteCatalogGroup));

        var identity = new ClaimsIdentity(
            claims,
            Scheme.Name,
            nameType: NexusClaimTypes.Name,
            roleType: NexusClaimTypes.Role
        );

        var principal = new ClaimsPrincipal(identity);

        var pattern = Request.Headers[_securityOptions.EnabledCatalogsPatternHeader].FirstOrDefault();

        if (string.IsNullOrEmpty(pattern))
            pattern = _securityOptions.EnabledCatalogsPattern;

        AuthUtilities.SetEnabledCatalogPatternClaim(principal, pattern);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    private string[] ParseClaimHeader(string headerName)
    {
        var headerValue = Request.Headers[headerName].FirstOrDefault();

        if (string.IsNullOrEmpty(headerValue))
            return [];

        return headerValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private void AddClaimArrayHeader(List<Claim> claims, string headerName, string claimType)
    {
        foreach (var value in ParseClaimHeader(headerName))
            claims.Add(new Claim(claimType, value));
    }

    private bool IsDevelopmentAdminMode()
    {
        var devRole = Request.Headers[HeaderAuthenticationDefaults.DevelopmentRoleHeader].FirstOrDefault();

        return !string.Equals(devRole, "user", StringComparison.OrdinalIgnoreCase);
    }
}
