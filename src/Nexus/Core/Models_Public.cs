using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Nexus.DataModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Core
{
    /// <summary>
    /// Represents a user.
    /// </summary>
    public class NexusUser
    {
#pragma warning disable CS1591

        public NexusUser(
            string id,
            string name)
        {
            Id = id;
            Name = name;

            RefreshTokens = new();
            Claims = new();
        }

        [JsonIgnore]
        [ValidateNever]
        public string Id { get; set; } = default!;

#pragma warning restore CS1591

        /// <summary>
        /// The user name.
        /// </summary>
        public string Name { get; set; } = default!;

#pragma warning disable CS1591

        [JsonIgnore]
        public List<RefreshToken> RefreshTokens { get; set; } = default!;

        [JsonIgnore]
        public List<NexusClaim> Claims { get; set; } = default!;

#pragma warning restore CS1591

    }

    /// <summary>
    /// Represents a claim.
    /// </summary>
    public class NexusClaim
    {
#pragma warning disable CS1591

        public NexusClaim(Guid id, string type, string value)
        {
            Id = id;
            Type = type;
            Value = value;
        }

        [JsonIgnore]
        [ValidateNever]
        public Guid Id { get; set; }

#pragma warning restore CS1591

        /// <summary>
        /// The claim type.
        /// </summary>
        public string Type { get; init; }

        /// <summary>
        /// The claim value.
        /// </summary>
        public string Value { get; init; }

#pragma warning disable CS1591

        // https://learn.microsoft.com/en-us/ef/core/modeling/relationships?tabs=fluent-api%2Cfluent-api-simple-key%2Csimple-key#no-foreign-key-property
        [JsonIgnore]
        [ValidateNever]
        public NexusUser Owner { get; set; } = default!;

#pragma warning restore CS1591
    }

    /// <summary>
    /// A refresh token.
    /// </summary>
    public class RefreshToken
    {
#pragma warning disable CS1591

        public RefreshToken(Guid id, string token, DateTime expires, string description)
        {
            Id = id;
            Token = token;
            Expires = expires;
            Description = description;

            InternalRefreshToken = InternalRefreshToken.Deserialize(token);
        }

        [JsonIgnore]
        [ValidateNever]
        public Guid Id { get; init; }

        [JsonIgnore]
        public string Token { get; init; }

#pragma warning restore CS1591

        /// <summary>
        /// The date/time when the token expires.
        /// </summary>
        public DateTime Expires { get; init; }

        /// <summary>
        /// The token description.
        /// </summary>
        public string Description { get; init; }

        [JsonIgnore]
        #warning Remove this when https://github.com/RicoSuter/NSwag/issues/4681 is solved
        internal bool IsExpired => DateTime.UtcNow >= Expires;

        [JsonIgnore]
        #warning Remove this when https://github.com/RicoSuter/NSwag/issues/4681 is solved
        internal InternalRefreshToken InternalRefreshToken { get; }

#pragma warning disable CS1591

        // https://learn.microsoft.com/en-us/ef/core/modeling/relationships?tabs=fluent-api%2Cfluent-api-simple-key%2Csimple-key#no-foreign-key-property
        [JsonIgnore]
        public NexusUser Owner { get; set; } = default!;

#pragma warning restore CS1591
    }

    /// <summary>
    /// A refresh token request.
    /// </summary>
    /// <param name="RefreshToken">The refresh token.</param>
    public record RefreshTokenRequest(
        [Required] string RefreshToken);

    /// <summary>
    /// A revoke token request.
    /// </summary>
    /// <param name="RefreshToken">The refresh token.</param>
    public record RevokeTokenRequest(
        [Required] string RefreshToken);

    /// <summary>
    /// A token pair.
    /// </summary>
    /// <param name="AccessToken">The JWT token.</param>
    /// <param name="RefreshToken">The refresh token.</param>
    public record TokenPair(
        string AccessToken,
        string RefreshToken);

    /// <summary>
    /// Describes an OpenID connect provider.
    /// </summary>
    /// <param name="Scheme">The scheme.</param>
    /// <param name="DisplayName">The display name.</param>
    public record AuthenticationSchemeDescription(
        string Scheme,
        string DisplayName);

    /// <summary>
    /// A package reference.
    /// </summary>
    /// <param name="Provider">The provider which loads the package.</param>
    /// <param name="Configuration">The configuration of the package reference.</param>
    public record PackageReference(
        string Provider,
        Dictionary<string, string> Configuration);

    /* Required to workaround JsonIgnore problems with local serialization and OpenAPI. */
    internal record InternalPackageReference(
        Guid Id,
        string Provider,
        Dictionary<string, string> Configuration);

    /// <summary>
    /// A structure for export parameters.
    /// </summary>
    /// <param name="Begin">The start date/time.</param>
    /// <param name="End">The end date/time.</param>
    /// <param name="FilePeriod">The file period.</param>
    /// <param name="Type">The writer type. If null, data will be read (and possibly cached) but not returned. This is useful for data pre-aggregation.</param>
    /// <param name="ResourcePaths">The resource paths to export.</param>
    /// <param name="Configuration">The configuration.</param>
    public record ExportParameters(
        DateTime Begin,
        DateTime End,
        TimeSpan FilePeriod,
        string? Type,
        string[] ResourcePaths,
        IReadOnlyDictionary<string, JsonElement>? Configuration);

    /// <summary>
    /// An extension description.
    /// </summary>
    /// <param name="Type">The extension type.</param>
    /// <param name="Version">The extension version.</param>
    /// <param name="Description">A nullable description.</param>
    /// <param name="ProjectUrl">A nullable project website URL.</param>
    /// <param name="RepositoryUrl">A nullable source repository URL.</param>
    /// <param name="AdditionalInformation">Additional information about the extension.</param>
    public record ExtensionDescription(
        string Type,
        string Version,
        string? Description,
        string? ProjectUrl,
        string? RepositoryUrl,
        IReadOnlyDictionary<string, JsonElement>? AdditionalInformation);

    /// <summary>
    /// A structure for catalog information.
    /// </summary>
    /// <param name="Id">The identifier.</param>
    /// <param name="Title">A nullable title.</param>
    /// <param name="Contact">A nullable contact.</param>
    /// <param name="Readme">A nullable readme.</param>
    /// <param name="License">A nullable license.</param>
    /// <param name="IsReadable">A boolean which indicates if the catalog is accessible.</param>
    /// <param name="IsWritable">A boolean which indicates if the catalog is editable.</param>
    /// <param name="IsReleased">A boolean which indicates if the catalog is released.</param>
    /// <param name="IsVisible">A boolean which indicates if the catalog is visible.</param>
    /// <param name="IsOwner">A boolean which indicates if the catalog is owned by the current user.</param>
    /// <param name="DataSourceInfoUrl">A nullable info URL of the data source.</param>
    /// <param name="DataSourceType">The data source type.</param>
    /// <param name="DataSourceRegistrationId">The data source registration identifier.</param>
    /// <param name="PackageReferenceId">The package reference identifier.</param>
    public record CatalogInfo(
        string Id,
        string? Title,
        string? Contact,
        string? Readme,
        string? License,
        bool IsReadable,
        bool IsWritable,
        bool IsReleased,
        bool IsVisible,
        bool IsOwner,
        string? DataSourceInfoUrl,
        string DataSourceType,
        Guid DataSourceRegistrationId,
        Guid PackageReferenceId);

    /// <summary>
    /// A structure for catalog metadata.
    /// </summary>
    /// <param name="Contact">The contact.</param>
    /// <param name="GroupMemberships">A list of groups the catalog is part of.</param>
    /// <param name="Overrides">Overrides for the catalog.</param>
    public record CatalogMetadata(
        string? Contact,
        string[]? GroupMemberships,
        ResourceCatalog? Overrides);

    /// <summary>
    /// A catalog time range.
    /// </summary>
    /// <param name="Begin">The date/time of the first data in the catalog.</param>
    /// <param name="End">The date/time of the last data in the catalog.</param>
    public record CatalogTimeRange(
        DateTime Begin,
        DateTime End);

    /// <summary>
    /// The catalog availability.
    /// </summary>
    /// <param name="Data">The actual availability data.</param>
    public record CatalogAvailability(
        double[] Data);

    /// <summary>
    /// A data source registration.
    /// </summary>
    /// <param name="Type">The type of the data source.</param>
    /// <param name="ResourceLocator">An optional URL which points to the data.</param>
    /// <param name="Configuration">Configuration parameters for the instantiated source.</param>
    /// <param name="InfoUrl">An optional info URL.</param>
    /// <param name="ReleasePattern">An optional regular expressions pattern to select the catalogs to be released. By default, all catalogs will be released.</param>
    /// <param name="VisibilityPattern">An optional regular expressions pattern to select the catalogs to be visible. By default, all catalogs will be visible.</param>
    public record DataSourceRegistration(
        string Type,
        Uri? ResourceLocator,
        IReadOnlyDictionary<string, JsonElement>? Configuration,
        string? InfoUrl = default,
        string? ReleasePattern = default,
        string? VisibilityPattern = default);

    /* Required to workaround JsonIgnore problems with local serialization and OpenAPI. */
    internal record InternalDataSourceRegistration(
        [property: JsonIgnore] Guid Id,
        string Type,
        Uri? ResourceLocator,
        IReadOnlyDictionary<string, JsonElement>? Configuration,
        string? InfoUrl = default,
        string? ReleasePattern = default,
        string? VisibilityPattern = default);

    /// <summary>
    /// Description of a job.
    /// </summary>
    /// <param name="Id">The global unique identifier.</param>
    /// <param name="Owner">The owner of the job.</param>
    /// <param name="Type">The job type.</param>
    /// <param name="Parameters">The job parameters.</param>
    public record Job(
        Guid Id,
        string Type,
        string Owner,
        object? Parameters);

    /// <summary>
    /// Describes the status of the job.
    /// </summary>
    /// <param name="Start">The start date/time.</param>
    /// <param name="Status">The status.</param>
    /// <param name="Progress">The progress from 0 to 1.</param>
    /// <param name="ExceptionMessage">The nullable exception message.</param>
    /// <param name="Result">The nullable result.</param>
    public record JobStatus(
        DateTime Start,
        TaskStatus Status,
        double Progress,
        string? ExceptionMessage,
        object? Result);

    /// <summary>
    /// A me response.
    /// </summary>
    /// <param name="UserId">The user id.</param>
    /// <param name="User">The user.</param>
    /// <param name="IsAdmin">A boolean which indicates if the user is an administrator.</param>
    /// <param name="RefreshTokens">A list of currently present refresh tokens.</param>
    public record MeResponse(
        string UserId,
        NexusUser User,
        bool IsAdmin,
        IReadOnlyDictionary<Guid, RefreshToken> RefreshTokens);
}
