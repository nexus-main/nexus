// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;

namespace Nexus.Core;

internal static class SpaStaticFileOptions
{
    /// <summary>
    /// The <c>Cache-Control</c> value applied to the SPA entry document.
    /// </summary>
    /// <remarks>
    /// The Angular entry document (<c>index.html</c>) must be revalidated on
    /// every navigation. It is not content-hashed and it is authentication
    /// gated by the reverse proxy in front of Nexus. After a logout the browser
    /// is redirected back to Nexus, and that document navigation has to reach
    /// the proxy so the proxy can send the user to the login page. Without an
    /// explicit cache policy browsers apply heuristic caching and reuse the
    /// cached document without contacting the proxy, so the SPA boots with no
    /// session and only surfaces "401 Unauthorized" errors instead of the
    /// login page. <c>no-cache</c> still allows the document to be stored but
    /// requires revalidation, which keeps conditional requests working while
    /// guaranteeing that the proxy always gets a chance to answer with a login
    /// redirect.
    /// </remarks>
    public const string DOCUMENT_CACHE_CONTROL = "private, no-cache";

    /// <summary>
    /// Creates the static file options used for both the default document and
    /// the SPA fallback route.
    /// </summary>
    public static StaticFileOptions Create()
    {
        return new StaticFileOptions
        {
            OnPrepareResponse = ApplyCachePolicy
        };
    }

    internal static void ApplyCachePolicy(StaticFileResponseContext context)
    {
        // Only the HTML entry document needs an explicit policy. Content-hashed
        // Angular bundles and static assets keep their default caching.
        if (context.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers.CacheControl = DOCUMENT_CACHE_CONTROL;
        }
    }
}
