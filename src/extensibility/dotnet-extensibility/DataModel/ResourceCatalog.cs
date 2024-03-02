using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexus.DataModel
{
    /// <summary>
    /// A catalog is a top level element and holds a list of resources.
    /// </summary>
    [DebuggerDisplay("{Id,nq}")]
    public record ResourceCatalog
    {
        #region Fields

        private string _id = default!;
        private IReadOnlyList<Resource>? _resources;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceCatalog"/>.
        /// </summary>
        /// <param name="id">The catalog identifier.</param>
        /// <param name="properties">The properties.</param>
        /// <param name="resources">The list of resources.</param>
        /// <exception cref="ArgumentException">Thrown when the resource identifier is not valid.</exception>
        public ResourceCatalog(
            string id,
            IReadOnlyDictionary<string, JsonElement>? properties = default,
            IReadOnlyList<Resource>? resources = default)
        {
            Id = id;
            Properties = properties;
            Resources = resources;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a regular expression to validate a resource catalog identifier.
        /// </summary>
        public static Regex ValidIdExpression { get; } = new Regex(@"^(?:\/[a-zA-Z_][a-zA-Z_0-9]*)+$", RegexOptions.Compiled);

        private static Regex _matchSingleParametersExpression { get; } = new Regex(@"\s*(.+?)\s*=\s*([^,\)]+)\s*,?", RegexOptions.Compiled);

        /// <summary>
        /// Gets the identifier.
        /// </summary>
        public string Id
        {
            get
            {
                return _id;
            }

            init
            {
                if (!ValidIdExpression.IsMatch(value))
                    throw new ArgumentException($"The resource catalog identifier {value} is not valid.");

                _id = value;
            }
        }

        /// <summary>
        /// Gets the properties.
        /// </summary>
        public IReadOnlyDictionary<string, JsonElement>? Properties { get; init; }

        /// <summary>
        /// Gets the list of representations.
        /// </summary>
        public IReadOnlyList<Resource>? Resources
        {
            get
            {
                return _resources;
            }

            init
            {
                if (value is not null)
                    ValidateResources(value);

                _resources = value;
            }
        }

        #endregion

        #region "Methods"

        /// <summary>
        /// Merges another catalog with this instance.
        /// </summary>
        /// <param name="catalog">The catalog to merge into this instance.</param>
        /// <returns>The merged catalog.</returns>
        public ResourceCatalog Merge(ResourceCatalog catalog)
        {
            if (Id != catalog.Id)
                throw new ArgumentException("The catalogs to be merged have different identifiers.");

            var mergedProperties = DataModelUtilities.MergeProperties(Properties, catalog.Properties);
            var mergedResources = DataModelUtilities.MergeResources(Resources, catalog.Resources);

            var merged = catalog with
            {
                Properties = mergedProperties,
                Resources = mergedResources
            };

            return merged;
        }

        internal bool TryFind(ResourcePathParseResult parseResult, [NotNullWhen(true)] out CatalogItem? catalogItem)
        {
            catalogItem = default;

            if (parseResult.CatalogId != Id)
                return false;

            var resource = Resources?.FirstOrDefault(resource => resource.Id == parseResult.ResourceId);

            if (resource is null)
                return false;

            Representation? representation;

            if (parseResult.Kind == RepresentationKind.Original)
            {
                var representationId = parseResult.SamplePeriod.ToUnitString();
                representation = resource.Representations?.FirstOrDefault(representation => representation.Id == representationId);
            }
            else
            {
                representation = parseResult.BasePeriod is null
                    ? resource.Representations?.FirstOrDefault()
                    : resource.Representations?.FirstOrDefault(representation => representation.Id == parseResult.BasePeriod.Value.ToUnitString());
            }

            if (representation is null)
                return false;

            IReadOnlyDictionary<string, string>? parameters = default;

            if (parseResult.Parameters is not null)
            {
                var matches = _matchSingleParametersExpression
                    .Matches(parseResult.Parameters);

                if (matches.Any())
                {
                    parameters = new ReadOnlyDictionary<string, string>(matches
                        .Select(match => (match.Groups[1].Value, match.Groups[2].Value))
                        .ToDictionary(tuple => tuple.Item1, tuple => tuple.Item2));
                }
            }

            var parametersAreOK =

                (representation.Parameters is null && parameters is null) ||

                (representation.Parameters is not null && parameters is not null &&
                 representation.Parameters.All(current =>

                    parameters.ContainsKey(current.Key) &&

                    (current.Value.GetStringValue("type") == "input-integer" && long.TryParse(parameters[current.Key], out var _) ||
                     current.Value.GetStringValue("type") == "select" && true /* no validation here */)));

            if (!parametersAreOK)
                return false;

            catalogItem = new CatalogItem(
                this with { Resources = default },
                resource with { Representations = default },
                representation,
                parameters);

            return true;
        }

        internal CatalogItem Find(string resourcePath)
        {
            if (!DataModelUtilities.TryParseResourcePath(resourcePath, out var parseResult))
                throw new Exception($"The resource path {resourcePath} is invalid.");

            return Find(parseResult);
        }

        internal CatalogItem Find(ResourcePathParseResult parseResult)
        {
            if (!TryFind(parseResult, out var catalogItem))
                throw new Exception($"The resource path {parseResult} could not be found.");

            return catalogItem;
        }

        private static void ValidateResources(IReadOnlyList<Resource> resources)
        {
            var uniqueIds = resources
                .Select(current => current.Id)
                .Distinct();

            if (uniqueIds.Count() != resources.Count)
                throw new ArgumentException("There are multiple resources with the same identifier.");
        }

        #endregion
    }
}
