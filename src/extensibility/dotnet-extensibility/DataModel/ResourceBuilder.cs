using System.Diagnostics;
using System.Text.Json;

namespace Nexus.DataModel;

/// <summary>
/// A resource builder simplifies building a resource.
/// </summary>
[DebuggerDisplay("{Id,nq}")]
public record ResourceBuilder
{
    private readonly string _id;
    private Dictionary<string, JsonElement>? _properties;
    private List<Representation>? _representations;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceBuilder"/>.
    /// </summary>
    /// <param name="id">The identifier of the resource to be built.</param>
    public ResourceBuilder(string id)
    {
        _id = id;
    }

    /// <summary>
    /// Adds a property.
    /// </summary>
    /// <param name="key">The key of the property.</param>
    /// <param name="value">The value of the property.</param>
    /// <returns>The resource builder.</returns>
    public ResourceBuilder WithProperty(string key, object value)
    {
        _properties ??= new();

        _properties[key] = JsonSerializer.SerializeToElement(value);

        return this;
    }

    /// <summary>
    /// Adds a <see cref="Representation"/>.
    /// </summary>
    /// <param name="representation">The <see cref="Representation"/>.</param>
    /// <returns>The resource builder.</returns>
    public ResourceBuilder AddRepresentation(Representation representation)
    {
        _representations ??= new List<Representation>();

        _representations.Add(representation);

        return this;
    }

    /// <summary>
    /// Adds a list of <see cref="Representation"/>.
    /// </summary>
    /// <param name="representations">The list of <see cref="Representation"/>.</param>
    /// <returns>The resource builder.</returns>
    public ResourceBuilder AddRepresentations(params Representation[] representations)
    {
        return AddRepresentations((IEnumerable<Representation>)representations);
    }

    /// <summary>
    /// Adds a list of <see cref="Representation"/>.
    /// </summary>
    /// <param name="representations">The list of <see cref="Representation"/>.</param>
    /// <returns>The resource builder.</returns>
    public ResourceBuilder AddRepresentations(IEnumerable<Representation> representations)
    {
        _representations ??= new List<Representation>();

        _representations.AddRange(representations);

        return this;
    }

    /// <summary>
    /// Builds the <see cref="Resource"/>.
    /// </summary>
    /// <returns>The <see cref="Resource"/>.</returns>
    public Resource Build()
    {
        return new Resource(_id, _properties, _representations);
    }
}