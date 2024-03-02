# Data Model

The data model of Nexus is derived from the one defined by the The International Data Spaces Association (IDSA) in their [Reference Architecture Model](https://www.researchgate.net/profile/Boris-Otto/publication/325176822_IDS_Reference_Architecture_Model_Version_20/links/5afc2e8d458515c00b6f07af/IDS-Reference-Architecture-Model-Version-20.pdf).

Hierarchy: [`ResourceCatalog` **1..n** `Resource` **1..n** `Representation` **1..n** `Artifact`]

Whenever Nexus works with these elements, i.e. to read data, it creates a `CatalogItem`, which is a tuple containing instances of a `ResourceCatalog`, `Resource` and `Representation`. The identifiers of each element can be combined into a path. This path is called `ResourcePath`.

## ResourceCatalog

A resource catalog is a collection of properties (= metadata) and resources. In the context of time-series databases, a resource catalog can be imagined as the collection of channels of a measurement system or a combined output of multiple measurement systems (measurement project). 

The `Id` represents the hierarchical location of the catalog (example: `/path/to/catalog`) and is similar to a Unix path. It must be rooted and is not allowed to end with a slash. Its segments must be valid variable names, i.e. start with an ASCII upper case or lower case letter and continue with more letters, numbers or underscores. 

```cs
namespace Nexus.DataModel
{
    public record ResourceCatalog(
        string Id, 
        IReadOnlyDictionary<string, string> Properties, 
        IReadOnlyList<Resource> Resources)
}
```

## Resource

This is the base element in the data model. Examples for resources are a channels or a variables of a measurement system. Thus it may be described by and id (= name), unit, group and other metadata. Additionally, a resource maintains a list of representations. 

A resource `Id` must be valid variable name, i.e. start with an ASCII upper case or lower case letter and continue with more letters, numbers or underscores. The `Id` must be unique within the containing catalog. The identifier it also the name of the resource (example: `Resource_1`). 

```cs
namespace Nexus.DataModel
{
    public record Resource(
        string Id, 
        IReadOnlyDictionary<string, string> Properties, 
        IReadOnlyList<Representation> Representations)
}
```

## Representation

A resource is made up of one or more representations, which is characterized by its datatype and the sample period. A representation represents the raw data itself or some postprocessed data, respectively. In case of aggregated data the optional `Detail` property could be set to something like `mean`, `min`, `max`, `std` or other aggregation methods. A resource has a readonly `Id` property which is generated using the sample period and detail. Examples: `500ms` or `100us_mean`.

```cs
namespace Nexus.DataModel
{
    public record Resource(
        NexusDataType DataType, 
        TimeSpan SamplePeriod, 
        public string? Detail
}
```

## Artifact
Not yet supported but may be used to provide different versions of representations. This may be useful in combination with filter channels. Once the code has been changed, there should be a new artifact, so everyone can continue to work with the old code version.

# Working with the Data Model Types

- All elements of the data model are immutable and of type `record` to make use of the implicit `GetHashCode` and `Equals` implementations. Additionally, the `with` keyword is useful for making copies (otherwise a simple struct would be sufficient, too). This could be changed to `struct record` in future, once this type is available. The benefits would be much faster [array allocation speed](https://stackoverflow.com/a/29669763) and reduced garbage collection pressure.

- Since all types are immutable there are fluent API builder types to make it easier to create them. Example:

```cs
var resource = new ResourceBuilder(id: "resource1")
    .WithUnit("°C")
    .WithGroups("group1")
    .WithProperty("MyProperty", "MyValue")
    .AddRepresentations(representations1)
    .Build();
```

# Properties

The `ResourceCatalog` and `Resource` contain a `JsonElement Properties` property. It acts as a container for e.g. metadata. The content must be a JSON object as shown in the following example:

```json
{
    "Type": "Falcon 9",
    "RelevantCoordinates": [
        {
            "Longitude": "28.4858401",
            "Latitude": "-80.5424245"
        },
        {
            "Longitude": "28.4716636",
            "Latitude": "-80.5377597"
        }
    ]
}
```

# Special Properties

The Nexus UI uses some predefined properties and diplays them directly. The property keys are:

**ResourceCatalog**

- `Description`: Long description of the catalog. Will be rendered as Markdown.

**Resource**

- `Unit`: Unit of the resource.
- `Description`: Single-line description of the resource.
- `Groups`: An array of groups the resource is part of.
