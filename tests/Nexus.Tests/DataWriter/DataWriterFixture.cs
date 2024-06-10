// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.DataModel;

namespace DataWriter;

public class DataWriterFixture : IDisposable
{
    readonly List<string> _targetFolders = [];

    public DataWriterFixture()
    {
        // catalog 1
        var representations1 = new List<Representation>()
        {
            new(dataType: NexusDataType.FLOAT32, samplePeriod: TimeSpan.FromSeconds(1)),
            new(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromSeconds(10)),
        };

        var resourceBuilder1 = new ResourceBuilder(id: "resource1")
            .WithUnit("°C")
            .WithGroups("group1")
            .AddRepresentations(representations1);

        var catalogBuilder1 = new ResourceCatalogBuilder(id: "/A/B/C")
            .WithProperty("my-custom-parameter1", "my-custom-value1")
            .WithProperty("my-custom-parameter2", "my-custom-value2")
            .AddResource(resourceBuilder1.Build());

        // catalog 2
        var representation2 = new Representation(dataType: NexusDataType.INT64, samplePeriod: TimeSpan.FromSeconds(1));

        var resourceBuilder2 = new ResourceBuilder(id: "resource3")
            .WithUnit("m/s")
            .WithGroups("group2")
            .AddRepresentation(representation2);

        var catalogBuilder2 = new ResourceCatalogBuilder(id: "/D/E/F")
            .WithProperty("my-custom-parameter3", "my-custom-value3")
            .AddResource(resourceBuilder2.Build());

        Catalogs = [catalogBuilder1.Build(), catalogBuilder2.Build()];
    }

    public ResourceCatalog[] Catalogs { get; }

    public string GetTargetFolder()
    {
        var targetFolder = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
        Directory.CreateDirectory(targetFolder);

        _targetFolders.Add(targetFolder);
        return targetFolder;
    }

    public void Dispose()
    {
        foreach (var targetFolder in _targetFolders)
        {
            try
            {
                Directory.Delete(targetFolder, true);
            }
            catch
            {
                //
            }
        }
    }
}
