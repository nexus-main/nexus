# Note
The text below does not fully apply anymore to Nexus because we have switched from refresh tokens + access tokens to personal access tokens that expire only optionally and are not cryptographically signed but checked against the database instead. The negible problem of higher database load is acceptible to get the benefit of not having to manage refresh tokens which are prone to being revoked as soon as the user uses it in more than a single place.

The new personal access tokens approach allows fine-grained access control to catalogs and makes many parts of the code much simpler. Current status is:
- User can manage personal access tokens in the web interface and specify read or read/write access to specific catalogs.
- The token the user gets is a string which consists of a combination of the token secret (a long random base64 encoded number) and the user id.
- Tokens are stored on disk in the folder configured by the `PathsOptions.Users` option in a files named `tokens.json`. They loaded lazily into memory on first demand and kept there for future requests.
- When the token is part of the Authorization header (`Authorization: Bearer <token>`) it is being handles by the `PersonalAccessTokenAuthenticationHandler` which creates a `ClaimsPrincipal` if the token is valid.
- The claims that are associated with the token can be anything but right now only the claims `CanReadCatalog` and `CanWriteCatalog` are being considered. To avoid a token to be more powerful than the user itself, the user claims are also being checked (see `AuthUtilities.cs`) on each request.
- The lifetime of the tokens can be choosen by the users or left untouched to produce tokens with unlimited lifetime.

# Authentication and Authorization

Nexus exposes resources (data, metadata and more) via HTTP API. Most of these resources do not have specific owners - they are owned by the system itself. Most of these resources need to be protected which makes an `authorization` mechanism necessary.

The first thing that comes into mind with HTTP services and authorization is `OAuth (v2)`. However, studying the specs reveals the following statement:
"In OAuth, the client requests access to resources `controlled by the resource owner` and hosted by the resource server [...]" [[RFC 6749](https://datatracker.ietf.org/doc/html/rfc6749)]

According to the RFC, OAuth is a mechanism for a user to grant a client (an application) access to their resources without revealing their own credentials and with the option to only grant a limited set of permissions.

**Example**: A user with a Microsoft account and access to OneDrive wants to allow another application (in terms of OAuth it's the _client_) to access the files that are on their drive. The user does not want to give this application a password and the user only wants to grant read permissions.

For this scenario, OAuth is a good option since every Microsoft user is a resource owner with the same set of rights to their own OneDrive files. OAuth helps limit the set of permissions to those actually required for that particular task (using the "scope" claim or other claims).

With Nexus, the situation is different. It's not like a user owns a resource that a client should access (here the client would be the front-end single page application (SPA) or a console application). Instead, the user himself wants to gain access to resources owned by the system.

When an API request is made, Nexus needs to know the user's identity to determine which resources can be accessed. Instead of using OAuth for authorization, Nexus will rely on [OpenID Connect](https://openid.net/specs/openid-connect-core-1_0.html) (based on OAuth) for `authentication` and will then perform authorization itself.

This works by first authenticating the user, then consulting a database to find the user's claims (e.g. a claim which describes which resource catalogs the user can access) and finally setting a cookie with the user's identity information.

## Summary

Using OAuth means that permissions are managed by the authorization server (in the form of scopes), so you need control over that server, which isn't always the case. Instead, OpenID connect allows authentication via a single sign-on (SSO) provider, while authorization is still controlled by Nexus.

## Disadvantages

- the cookie might become very large
- the SPA cannot extract the username from the encrypted cookie, an additional API call is required

## Non-browser clients
Clients without the ability to follow redirects and show a browser-based login screen could use the device code flow but that is not supported by Open ID Connect. Therefore, Nexus offers an API for authenticated users to obtain an access and refresh token as a json file stream. This file should be stored somewhere where the non-browser application can access it. Nexus adds bearer authentication along with the cookie authentication middleware to support both scenarios.

## Why refresh tokens?

Some web services like GitHub offer the possibility to create an API key with a limited or sometimes unlimited lifetime. These keys tend to be long-lived, which means they can be used for a very long time in case they have been stolen. In addition, the server must be able to manage them in a database and associate the API key with a user, which means frequent database access.

A solution to this is a short-lived and asymmetrically signed access token that contains all information about the user. The token can be verified as long as the signing key is known. The validation process is an in-memory operation and does not require database access.

An access token alone has the disadvantage that it cannot be revoked in the event of theft, since the token's signature is still valid. And now that the token is short-lived, there needs to be a mechanism to refresh it automatically.

The solution is a refresh token that is issued along with the access token. The refresh token is long-lived and stored in a database. When the access token expires, the refresh token can be silently exchanged for a new access/refresh token pair. It differs from the API key in that the database needs to be consulted only when the access token expires, rather than every time the API is accessed.

A refresh token is very powerful. Once an attacker gets their hands on that token, they can forever issue new access tokens. It is therefore advisable to limit the lifetime of the first and all subsequently issued refresh tokens to an absolute point in time in the future. After this time, the user has to authenticate again. 

> [OAuth 2.0 for Browser-Based Apps](
https://datatracker.ietf.org/doc/html/draft-ietf-oauth-browser-based-apps-08): _"[...] MUST NOT extend the lifetime of the new refresh token beyond the lifetime of the initial refresh token"_

In order to detect a compromised token, it is recommended to implement token rotation, i.e. always issue new refresh tokens and invalidate the old ones. If an attacker or the user both use the refresh token, it will fail for one of them. In this case, Nexus can invalidate all tokens in the same token family (all refresh tokens descending from the original refresh token issued for the client) and thus force the user to re-authenticate. 

> See also [Auth0](https://auth0.com/docs/secure/tokens/refresh-tokens/refresh-token-rotation#:~:text=Refresh%20token%20rotation%20is%20a,that%20goes%20beyond%20silent%20authentication.&text=Refresh%20tokens%20are%20often%20used,issue%20long%2Dlived%20access%20tokens.) for a concise explanation about refresh tokens.

## Implementation details

The backend of Nexus is a confidential client and upon user request, it will perform the authorization code flow to obtain an ID token to authenticate and sign-in the user.

Nexus supports multiple OpenID Connect providers. See [Configuration] on how to add configuration values.

Nexus configuration does not offer any `audience` or `scope` property available because it does not use the access token to access a resource server (which would validate the `aud` claim) and the ID token is not allowed to be sent anywhere. 

According to this [tutorial](https://auth0.com/blog/backend-for-frontend-pattern-with-auth0-and-dotnet/), it might be required to attach an audience value to get a token using `context.ProtocolMessage.SetParameter("audience", value)`. Support for this will be implemented when this becomes an issue for anyone.

## Alternative approach (and why it did not work):

Since there are many examples on the web for SPA scenarios (and Nexus offers a SPA), it was considered to follow and apply them to Nexus. The approach is to run the authorization code flow in the SPA to obtain an access token. This access token is then forwarded to the resource server (here: the backend of Nexus) where only a JWT bearer token middleware is configured (no cookies required).

The problem now is that although the access token contains the subject claim, it is missing more information about the user like its name. This makes it hard to manage user specific claims from within Nexus.

Another problem is that Nexus cannot add these user-specific claims to the access token, which means that the user database must be consulted for every single request, resulting in a high disk load.

Also, a such client would be public which means it is possible to copy the `client_id` and use them in other clients, which might be problematic when there is limited traffic allowed.

The last problem with refresh tokens is that _"for public clients [they] MUST be sender-constrained or use
   refresh token rotation [...]"_ [[OAuth 2.0 Security Best Current Practice](https://datatracker.ietf.org/doc/html/draft-ietf-oauth-security-topics-19#section-2.2.2), [RFC 6749](https://datatracker.ietf.org/doc/html/rfc6749#section-4.13)].

To solve the first issue, one might think that the ID token could be used instead of the access token but that is forbidden: _"Access tokens should only be used to make requests to the resource server. Additionally, ID tokens must not be used to make requests to the resource server."_ [[oauth.net](https://oauth.net/2/access-tokens/)]. Additionally the access token claims (e.g. scope) would be missing.

In the end it's clear that while there is nice OpenID connection support for Blazor wasm SPA, this approach doesn't suit Nexus.

## Other findings (informative)

### Scopes / Audience:

>_Scopes represent what a client application is allowed to do. They represent the scoped access [...] mentioned before. In IdentityServer, scopes are typically modeled as resources, which come in two flavors: identity and API._

>_An identity resource allows you to model a scope that will permit a client application to view a subset of claims about a user. For example, the profile scope enables the app to see claims about the user such as name and date of birth._

>_An API resource allows you to model access to an entire protected resource, an API, with individual permissions levels (scopes) that a client application can request access to."_ [[Source](https://www.scottbrady91.com/identity-server/getting-started-with-identityserver-4)]

When during an OAuth flow an API scope is requested, it will become part of the scope claim of the returned access token. Additionally, the audience claim will be set to the resource the scope belongs to. This is important to understand the audience validation. This is an important security measure as described [here](https://www.keycloak.org/docs/11.0/server_admin/#_audience).

When an API scope is requested during an OAuth flow, it becomes part of the `scope` claim of the returned access token. Also, the audience (`aud`) claim [[RFC 7519](https://www.rfc-editor.org/rfc/rfc7519#section-4.1.3)] is set to the resource that the scope belongs to. This is important to understand audience validation. The audience value should identify the recipient of the access token, i.e. the resource server.

### Bearer token validation:

Bearer token validation does not necessarily require a manually provided token signing key for validation. The .NET middleware tries to get the public key from the authorization server on the first request [[Source](https://stackoverflow.com/questions/58758198/does-addjwtbearer-do-what-i-think-it-does)].