using Microsoft.AspNetCore.Components.Authorization;
using Nexus.Api;
using System.Security.Claims;

namespace Nexus.UI.Services;

public class NexusAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly INexusClient _client;

    public NexusAuthenticationStateProvider(INexusClient client)
    {
        _client = client;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        ClaimsIdentity identity;

        const string NAME_CLAIM = "name";
        const string ROLE_CLAIM = "role";

        try
        {
            var meResponse = await _client.Users.GetMeAsync();

            var claims = new List<Claim>
            {
                new Claim(NAME_CLAIM, meResponse.User.Name)
            };

            if (meResponse.IsAdmin)
                claims.Add(new Claim(ROLE_CLAIM, "Administrator"));

            identity = new ClaimsIdentity(
                claims,
                authenticationType: meResponse.UserId.Split(new[] { '@' }, count: 2)[1],
                nameType: NAME_CLAIM,
                roleType: ROLE_CLAIM);
        }
        catch (Exception)
        {
            identity = new ClaimsIdentity();
        }

        var principal = new ClaimsPrincipal(identity);

        return new AuthenticationState(principal);
    }
}
