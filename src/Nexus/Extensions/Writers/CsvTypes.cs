using System.Text.Json;

namespace Nexus.Writers
{
    internal record struct Layout(
        int[] HeaderRows);

    internal record struct Constraints(
        bool Required);

    internal record struct Field(
        string Name,
        string Type,
        Constraints Constraints,
        IReadOnlyDictionary<string, JsonElement>? Properties);

    internal record struct Schema(
        string PrimaryKey,
        Field[] Fields,
        IReadOnlyDictionary<string, JsonElement>? Properties);

    internal record struct CsvResource(
        string Encoding,
        string Format,
        string Hashing,
        string Name,
        string Profile,
        string Scheme,
        List<string> Path,
        Layout Layout,
        Schema Schema);
}
