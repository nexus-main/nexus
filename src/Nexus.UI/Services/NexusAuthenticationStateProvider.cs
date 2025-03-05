// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Components.Authorization;
using Nexus.Api;
using System.Security.Claims;

namespace Nexus.UI.Services;

public class NexusAuthenticationStateProvider(INexusClient client) : AuthenticationStateProvider
{
    private readonly INexusClient _client = client;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        ClaimsIdentity identity;

        const string NAME_CLAIM = "name";
        const string ROLE_CLAIM = "role";

        try
        {
            var meResponse = await _client.V1.Users.GetMeAsync();

            List<Claim> claims = [
                new(NAME_CLAIM, meResponse.UserName),
                .. meResponse.Claims.Select(x => new Claim(x.Value.Type, x.Value.Value))
            ];

            identity = new ClaimsIdentity(
                claims,
                authenticationType: meResponse.UserId.Split('@', count: 2)[1],
                nameType: NAME_CLAIM,
                roleType: ROLE_CLAIM
            );
        }
        catch (Exception)
        {
            identity = new ClaimsIdentity();
        }

        var principal = new ClaimsPrincipal(identity);

        return new AuthenticationState(principal);
    }
}
