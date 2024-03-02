using System.Text.Json;

namespace Nexus.DataModel
{
    /// <summary>
    /// A resource catalog builder simplifies building a resource catalog.
    /// </summary>
    public record ResourceCatalogBuilder
    {
        #region Fields

        private readonly string _id;
        private Dictionary<string, JsonElement>? _properties;
        private List<Resource>? _resources;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceCatalogBuilder"/>.
        /// </summary>
        /// <param name="id">The identifier of the resource catalog to be built.</param>
        public ResourceCatalogBuilder(string id)
        {
            _id = id;
        }

        #endregion

        #region "Methods"

        /// <summary>
        /// Adds a property.
        /// </summary>
        /// <param name="key">The key of the property.</param>
        /// <param name="value">The value of the property.</param>
        /// <returns>The resource catalog builder.</returns>
        public ResourceCatalogBuilder WithProperty(string key, JsonElement value)
        {
            _properties ??= new();

            _properties[key] = value;

            return this;
        }

        /// <summary>
        /// Adds a property.
        /// </summary>
        /// <param name="key">The key of the property.</param>
        /// <param name="value">The value of the property.</param>
        /// <returns>The resource catalog builder.</returns>
        public ResourceCatalogBuilder WithProperty(string key, object value)
        {
            _properties ??= new();

            _properties[key] = JsonSerializer.SerializeToElement(value);

            return this;
        }

        /// <summary>
        /// Adds a <see cref="Resource"/>.
        /// </summary>
        /// <param name="resource">The <see cref="Resource"/>.</param>
        /// <returns>The resource catalog builder.</returns>
        public ResourceCatalogBuilder AddResource(Resource resource)
        {
            _resources ??= new List<Resource>();

            _resources.Add(resource);

            return this;
        }

        /// <summary>
        /// Adds a list of <see cref="Resource"/>.
        /// </summary>
        /// <param name="resources">The list of <see cref="Resource"/>.</param>
        /// <returns>The resource catalog builder.</returns>
        public ResourceCatalogBuilder AddResources(params Resource[] resources)
        {
            return AddResources((IEnumerable<Resource>)resources);
        }

        /// <summary>
        /// Adds a list of <see cref="Resource"/>.
        /// </summary>
        /// <param name="resources">The list of <see cref="Resource"/>.</param>
        /// <returns>The resource catalog builder.</returns>
        public ResourceCatalogBuilder AddResources(IEnumerable<Resource> resources)
        {
            _resources ??= new List<Resource>();

            _resources.AddRange(resources);

            return this;
        }

        /// <summary>
        /// Builds the <see cref="ResourceCatalog"/>.
        /// </summary>
        /// <returns>The <see cref="ResourceCatalog"/>.</returns>
        public ResourceCatalog Build()
        {
            return new ResourceCatalog(_id, _properties, _resources);
        }

        #endregion
    }
}
