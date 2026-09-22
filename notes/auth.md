# Authentication

Nexus does not perform OpenID Connect, cookie authentication, or user
database management itself. Authentication is delegated to a trusted proxy in
front of Nexus, for example an `oidc-proxy` backed by Keycloak.

The proxy authenticates the user and forwards identity data to Nexus through
HTTP headers. Nexus trusts these headers and creates its `ClaimsPrincipal` from
them.

Nexus does not store users or user claims. There is no SQLite user database,
no `users.json`, and no per-user claim store.

## Header Claims

The header names are configured with `SecurityOptions` in
`src/Nexus/Core/NexusOptions.cs`.

- `ForwardedUserHeaderName`: subject header, default `X-Forwarded-User`, mapped to claim `sub`
- `ForwardedPreferredUsernameHeaderName`: display name header, default `X-Forwarded-Preferred-Username`, mapped to claim `name`
- `ForwardedGroupsHeaderName`: groups header, default `X-Forwarded-Groups`
- `ForwardedClaimsHeaderName`: Nexus claim header for roles and Nexus permissions

Claim headers contain comma-separated lists. Nexus expands each list value into
repeated claims of the corresponding type. Values are trimmed and empty segments
are dropped. This matches the format emitted by oauth2-proxy.

If a claim header is missing, Nexus treats that claim type as empty.

## Development Identity

In development mode, when no auth headers are present, Nexus creates a fixed
development identity for `Star Lord` with administrator privileges.

## Logout

`SecurityOptions.LogoutUrl` configures the browser URL used by the UI sign-out
button. Nexus does not perform provider logout itself.

When `SecurityOptions.LogoutUrl` is not set, the UI hides the sign-out button.

## Authorization

Roles come from Keycloak through the forwarded claims.

- The default authorization policy requires an authenticated user.
- `NexusPolicies.RequireAdmin` requires the `Administrator` role.
- Nexus does not bootstrap administrators. Administrators are configured in Keycloak.

Pipeline management requires administrator access because pipelines can expose
resources reachable by the Nexus server.

## Enabled Catalogs

`SecurityOptions.EnabledCatalogsPattern` defines the default regular expression
for enabled catalog IDs. The trusted proxy can override it per request with the
`X-Forwarded-EnabledCatalogsPattern` header.

Nexus stores the effective value as a transient claim on the current principal.
Catalog read and write checks require the catalog ID to match this pattern unless
the current user is an administrator. The root catalog and built-in sample
catalogs keep their existing special handling.

The enabled-catalogs header is a single string regular expression. It is not a
JSON array claim header.

## Personal Access Tokens

Personal access tokens are supported for non-browser clients.

Endpoints:

- `GET /api/users/tokens`
- `POST /api/users/tokens/create`
- `DELETE /api/users/tokens/{tokenId}`
- `POST /api/users/tokens/delete`

When a PAT is created, Nexus stores:

- `Claims`: the claims requested for the token
- `GrantClaims`: a snapshot of the creator's claims at creation time

The requested `Claims` must be a subset of `GrantClaims`. PAT authentication
uses the token's stored `Claims` and does not consult Keycloak or a Nexus user
database.

PATs also use the enabled-catalogs pattern from their stored `GrantClaims`. If no
pattern is present, Nexus falls back to `SecurityOptions.EnabledCatalogsPattern`.
Changing the pattern in Keycloak or the proxy does not change existing PATs; the
affected PATs must expire or be deleted.

PATs are stored in `tokens.json` under the configured users path.

## Catalog License Acceptance

Catalog licenses are accepted with:

- `POST /api/v1/catalogs/{catalogId}/accept-license`

The endpoint is available only through trusted header authentication. PATs cannot
accept licenses interactively.

Nexus resolves the current license on the server from `LICENSE.md` or the
catalog license property. The client does not send license text or a license
hash.

Accepted licenses are stored per user under the configured users path. The
stored acceptance is keyed by the user subject, catalog ID, and current license
text. If the catalog license text changes, the previous acceptance no longer
matches and the user must accept the new license.

Catalog read checks grant access when either the user's claims permit reading
the catalog or the user has accepted the catalog's current license. This applies
to browser/header requests and PAT requests because both authenticate with the
same user subject claim.

## Revoking Access After Keycloak Changes

Changing or deleting a user in Keycloak affects browser/proxy authentication
immediately, but it does not change existing Nexus PATs.

Existing PATs keep their stored `GrantClaims` until they expire or are deleted.

Required operational flow after demoting or deleting a user in Keycloak:

1. Demote or delete the user in Keycloak.
2. Delete that user's Nexus PATs from `tokens.json`.

Step 2 is required to revoke non-browser access immediately.
