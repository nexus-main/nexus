using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Nexus.DataModel
{
    /// <summary>
    /// A resource is part of a resource catalog and holds a list of representations.
    /// </summary>
    [DebuggerDisplay("{Id,nq}")]
    public record Resource
    {
        #region Fields

        private string _id = default!;
        private IReadOnlyList<Representation>? _representations;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="Resource"/>.
        /// </summary>
        /// <param name="id">The resource identifier.</param>
        /// <param name="properties">The properties.</param>
        /// <param name="representations">The list of representations.</param>
        /// <exception cref="ArgumentException">Thrown when the resource identifier is not valid.</exception>
        public Resource(
            string id,
            IReadOnlyDictionary<string, JsonElement>? properties = default,
            IReadOnlyList<Representation>? representations = default)
        {
            Id = id;
            Properties = properties;
            Representations = representations;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a regular expression to validate a resource identifier.
        /// </summary>
        [JsonIgnore]
        #warning Remove this when https://github.com/RicoSuter/NSwag/issues/4681 is solved
        public static Regex ValidIdExpression { get; } = new Regex(@"^[a-zA-Z_][a-zA-Z_0-9]*$");

        /// <summary>
        /// Gets a regular expression to find invalid characters in a resource identifier.
        /// </summary>
        [JsonIgnore]
        #warning Remove this when https://github.com/RicoSuter/NSwag/issues/4681 is solved
        public static Regex InvalidIdCharsExpression { get; } = new Regex(@"[^a-zA-Z_0-9]", RegexOptions.Compiled);

        /// <summary>
        /// Gets a regular expression to find invalid start characters in a resource identifier.
        /// </summary>
        [JsonIgnore]
        #warning Remove this when https://github.com/RicoSuter/NSwag/issues/4681 is solved
        public static Regex InvalidIdStartCharsExpression { get; } = new Regex(@"^[^a-zA-Z_]+", RegexOptions.Compiled);

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
                    throw new ArgumentException($"The resource identifier {value} is not valid.");

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
        public IReadOnlyList<Representation>? Representations
        {
            get
            {
                return _representations;
            }

            init
            {
                if (value is not null)
                    ValidateRepresentations(value);

                _representations = value;
            }
        }

        #endregion

        #region "Methods"

        internal Resource Merge(Resource resource)
        {
            if (Id != resource.Id)
                throw new ArgumentException("The resources to be merged have different identifiers.");

            var mergedProperties = DataModelUtilities.MergeProperties(Properties, resource.Properties);
            var mergedRepresentations = DataModelUtilities.MergeRepresentations(Representations, resource.Representations);

            var merged = resource with
            {
                Properties = mergedProperties,
                Representations = mergedRepresentations
            };

            return merged;
        }

        internal Resource DeepCopy()
        {
            return new Resource(
                id: Id,
                representations: Representations?.Select(representation => representation.DeepCopy()).ToList(),
                properties: Properties?.ToDictionary(entry => entry.Key, entry => entry.Value.Clone()));
        }

        private static void ValidateRepresentations(IReadOnlyList<Representation> representations)
        {
            var uniqueIds = representations
                .Select(current => current.Id)
                .Distinct();

            if (uniqueIds.Count() != representations.Count)
                throw new ArgumentException("There are multiple representations with the same identifier.");
        }

        #endregion
    }
}