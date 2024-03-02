using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nexus.Core;
using System.Security.Claims;
using System.Security.Cryptography;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Services
{
    // https://jasonwatmore.com/post/2021/06/15/net-5-api-jwt-authentication-with-refresh-tokens
    // https://github.com/cornflourblue/dotnet-5-jwt-authentication-api

    internal interface INexusAuthenticationService
    {
        Task<string> GenerateRefreshTokenAsync(
            NexusUser user,
            string description);

        Task<TokenPair> RefreshTokenAsync(
            RefreshToken token);

        Task RevokeTokenAsync(
            RefreshToken token);
    }

    internal class NexusAuthenticationService : INexusAuthenticationService
    {
        #region Fields

        private readonly IDBService _dbService;
        private readonly SecurityOptions _securityOptions;
        private readonly SigningCredentials _signingCredentials;

        #endregion

        #region Constructors

        public NexusAuthenticationService(
            IDBService dbService,
            IOptions<SecurityOptions> securityOptions)
        {
            _dbService = dbService;
            _securityOptions = securityOptions.Value;

            var key = Convert.FromBase64String(_securityOptions.Base64JwtSigningKey);
            _signingCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature);
        }

        #endregion

        #region Methods

        public async Task<string> GenerateRefreshTokenAsync(
            NexusUser user, string description)
        {
            // new refresh token
            var newRefreshToken = GenerateRefreshToken(
                description);

            user.RefreshTokens.Add(newRefreshToken);

            // add token

            /* When the primary key is != Guid.Empty, EF thinks the entity
             * already exists and tries to update it. Adding it explicitly
             * will correctly mark the entity as "added".
             */
            await _dbService.AddOrUpdateRefreshTokenAsync(newRefreshToken);

            return newRefreshToken.Token;
        }

        public async Task<TokenPair> RefreshTokenAsync(RefreshToken token)
        {
            var user = token.Owner;

            // new token pair
            var newAccessToken = GenerateAccessToken(
                user: user,
                accessTokenLifeTime: _securityOptions.AccessTokenLifetime);

            var newRefreshToken = GenerateRefreshToken(
                description: token.Description,
                ancestor: token);

            // change token content
            await _dbService.AddOrUpdateRefreshTokenAsync(newRefreshToken);

            return new TokenPair(newAccessToken, newRefreshToken.Token);
        }

        public async Task RevokeTokenAsync(RefreshToken token)
        {
            // revoke token
            token.Owner.RefreshTokens.Remove(token);

            // save changes
            await _dbService.SaveChangesAsync();
        }

        #endregion

        #region Helper Methods

        private string GenerateAccessToken(NexusUser user, TimeSpan accessTokenLifeTime)
        {
            var mandatoryClaims = new[]
            {
                new Claim(Claims.Subject, user.Id),
                new Claim(Claims.Name, user.Name)
            };

            var claims = user.Claims
                .Select(entry => new Claim(entry.Type, entry.Value));

            var claimsIdentity = new ClaimsIdentity(
                mandatoryClaims.Concat(claims),
                authenticationType: JwtBearerDefaults.AuthenticationScheme,
                nameType: Claims.Name,
                roleType: Claims.Role);

            // TODO: We will encounter the year 2038 problem if this is not solved (https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/issues/92)
            var utcNow = DateTime.UtcNow;
            var year2038Limit = new DateTime(2038, 01, 19, 03, 14, 07, DateTimeKind.Utc);

            var combinedAccessTokenLifeTime = AddWillOverflow(utcNow.Ticks, accessTokenLifeTime.Ticks)
                ? DateTime.MaxValue
                : utcNow + accessTokenLifeTime;

            var limitedAccessTokenLifeTime = combinedAccessTokenLifeTime > year2038Limit
                ? year2038Limit
                : utcNow + accessTokenLifeTime;

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = claimsIdentity,
                NotBefore = DateTime.UtcNow,
                Expires = limitedAccessTokenLifeTime,
                SigningCredentials = _signingCredentials
            };

            var tokenHandler = new JsonWebTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);

            return token;
        }

        private RefreshToken GenerateRefreshToken(string description, RefreshToken? ancestor = default)
        {
            var expires = ancestor is null
                ? _securityOptions.RefreshTokenLifetime == TimeSpan.MaxValue
                    ? DateTime.MaxValue
                    : DateTime.UtcNow.Add(_securityOptions.RefreshTokenLifetime)
                : ancestor.Expires;

            var id = ancestor is null
                ? Guid.NewGuid()
                : ancestor.InternalRefreshToken.Id;

            var randomBytes = RandomNumberGenerator.GetBytes(64);

            var token = new InternalRefreshToken(
                Version: 1,
                Id: id,
                Value: Convert.ToBase64String(randomBytes)
            );

            var serializedToken = InternalRefreshToken.Serialize(token);

            return new RefreshToken(id, serializedToken, expires, description);
        }

        private static bool AddWillOverflow(long x, long y)
        {
            var willOverflow = false;

            if (x > 0 && y > 0 && y > (long.MaxValue - x))
                willOverflow = true;

            if (x < 0 && y < 0 && y < (long.MinValue - x))
                willOverflow = true;

            return willOverflow;
        }

        #endregion
    }
}
