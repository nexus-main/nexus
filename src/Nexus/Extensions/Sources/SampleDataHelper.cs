// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.DataModel;
using Nexus.Extensibility;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Nexus.Sources;

internal static class SampleDataHelper
{
    public static readonly float[] Data =
    [
        6.5f,
        6.7f,
        7.9f,
        8.1f,
        7.5f,
        7.6f,
        7.0f,
        6.5f,
        6.0f,
        5.9f,
        5.8f,
        5.2f,
        4.6f,
        5.0f,
        5.1f,
        4.9f,
        5.3f,
        5.8f,
        5.9f,
        6.1f,
        5.9f,
        6.3f,
        6.5f,
        6.9f,
        7.1f,
        6.9f,
        7.1f,
        7.2f,
        7.6f,
        7.9f,
        8.2f,
        8.1f,
        8.2f,
        8.0f,
        7.5f,
        7.7f,
        7.6f,
        8.0f,
        7.5f,
        7.2f,
        6.8f,
        6.5f,
        6.6f,
        6.6f,
        6.7f,
        6.2f,
        5.9f,
        5.7f,
        5.9f,
        6.3f,
        6.6f,
        6.7f,
        6.9f,
        6.5f,
        6.0f,
        5.8f,
        5.3f,
        5.8f,
        6.1f,
        6.8f
    ];

    internal static ResourceCatalog BuildDefaultCatalog(
        string catalogId)
    {
        var resourceA = new ResourceBuilder(id: "T1")
            .WithUnit("°C")
            .WithDescription("Test Resource B")
            .WithGroups("Group 1")
            .AddRepresentation(new Representation(dataType: NexusDataType.Float32, samplePeriod: TimeSpan.FromSeconds(1)))
            .Build();

        var resourceB = new ResourceBuilder(id: "V1")
            .WithUnit("m/s")
            .WithDescription("Test Resource C")
            .WithGroups("Group 1")
            .AddRepresentation(new Representation(dataType: NexusDataType.Float32, samplePeriod: TimeSpan.FromSeconds(1)))
            .Build();

        var resourceC = new ResourceBuilder(id: "unix_time1")
            .WithDescription("Test Resource D")
            .WithGroups("Group 2")
            .AddRepresentation(new Representation(dataType: NexusDataType.Float64, samplePeriod: TimeSpan.FromMilliseconds(40)))
            .Build();

        var resourceD = new ResourceBuilder(id: "unix_time2")
            .WithDescription("Test Resource E")
            .WithGroups("Group 2")
            .AddRepresentation(new Representation(dataType: NexusDataType.Float64, samplePeriod: TimeSpan.FromSeconds(1)))
            .Build();

        var p1Parameters = new Dictionary<string, JsonElement>()
        {
            ["height"] = JsonSerializer.SerializeToElement(new
            {
                type = "input-integer",
                label = "Height",
                @default = 10,
                minimum = 1,
                maximum = 100
            }),
            ["mode"] = JsonSerializer.SerializeToElement(new
            {
                type = "select",
                label = "Mode",
                @default = "mean",
                items = new Dictionary<string, string>()
                {
                    ["mean"] = "Mean",
                    ["max"] = "Maximum"
                }
            })
        };

        var resourceE = new ResourceBuilder(id: "P1")
            .WithUnit("bar")
            .WithDescription("Test Resource A")
            .WithGroups("Group 1")
            .AddRepresentation(new Representation(
                dataType: NexusDataType.Float32,
                samplePeriod: TimeSpan.FromSeconds(1),
                parameters: p1Parameters))
            .AddRepresentation(new Representation(
                dataType: NexusDataType.Float32,
                samplePeriod: TimeSpan.FromMilliseconds(100),
                parameters: p1Parameters))
            .Build();

        var catalogBuilder = new ResourceCatalogBuilder(catalogId);

        catalogBuilder.AddResources(new List<Resource>()
        {
            resourceA,
            resourceB,
            resourceC,
            resourceD,
            resourceE
        });

        catalogBuilder.WithProperty("resources", new
        {
            availability = new[]
            {
                new
                {
                    pattern = $"^{catalogId}/P1/1_s#base=1_s$",
                    begin = (string?)"2020-01-01T00:00:00Z",
                    end = (string?)null
                },
                new
                {
                    pattern = $"^{catalogId}/P1/100_ms#base=100_ms$",
                    begin = (string?)null,
                    end = (string?)"2020-01-01T00:00:00Z"
                }
            }
        });

        return catalogBuilder.Build();
    }

    internal static double ToUnixTimeStamp(
        DateTime value)
    {
        return value.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
    }

    internal static async Task ReadAsync(
        DateTime begin,
        ReadRequest[] requests,
        IProgress<double> progress,
        CancellationToken cancellationToken,
        Action<ReadRequest>? validateRequest = null)
    {
        var tasks = requests.Select(request =>
        {
            return Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var catalogItem = request.CatalogItem;
                var data = request.Data;
                var status = request.Status;
                var catalog = catalogItem.Catalog;
                var resource = catalogItem.Resource;
                var representation = catalogItem.Representation;

                // validate request (e.g. check credentials)
                if (validateRequest is not null)
                    validateRequest(request);

                var beginTime = ToUnixTimeStamp(begin);
                var elementCount = data.Length / representation.ElementSize;

                // unix time
                if (resource.Id.Contains("unix_time"))
                {
                    var dt = representation.SamplePeriod.TotalSeconds;
                    var target = MemoryMarshal.Cast<byte, double>(data.Span);

                    for (int i = 0; i < elementCount; i++)
                        target[i] = i * dt + beginTime;
                }

                // parameterized temperature
                else if (resource.Id == "P1")
                {
                    var height = catalogItem.Parameters is not null && catalogItem.Parameters.TryGetValue("height", out var heightValue) && int.TryParse(heightValue, out var parsedHeight)
                        ? parsedHeight
                        : 10;
                    var mode = catalogItem.Parameters is not null && catalogItem.Parameters.TryGetValue("mode", out var modeValue)
                        ? modeValue
                        : "mean";
                    var dataLength = Data.Length;
                    var sourceOffset = (int)(((long)beginTime % dataLength + dataLength) % dataLength);
                    var target = MemoryMarshal.Cast<byte, float>(data.Span);
                    var heightOffset = 0.12f * height;
                    var modeScale = mode == "max" ? 1.25f : 1.0f;
                    var modeOffset = mode == "max" ? 8.0f : 0.0f;

                    for (int i = 0; i < target.Length; i++)
                    {
                        target[i] = (Data[sourceOffset] - heightOffset) * modeScale + modeOffset;
                        sourceOffset = (sourceOffset + 1) % dataLength;
                    }
                }

                // temperature or wind speed
                else
                {
                    var dataLength = Data.Length;
                    var sourceOffset = (int)(((long)beginTime % dataLength + dataLength) % dataLength);
                    var target = MemoryMarshal.Cast<byte, float>(data.Span);

                    while (!target.IsEmpty)
                    {
                        var source = Data.AsSpan(sourceOffset);
                        var count = Math.Min(source.Length, target.Length);
                        source[..count].CopyTo(target);
                        target = target[count..];
                        sourceOffset = 0;
                    }
                }

                status.Span
                    .Fill(1);

                await request.CompleteAsync();
            });
        }).ToList();

        try
        {
            var finishedTasks = 0;

            while (tasks.Count != 0)
            {
                var task = await Task.WhenAny(tasks);
                await task;
                finishedTasks++;
                progress.Report(finishedTasks / (double)requests.Length);
                tasks.Remove(task);
            }
        }
        finally
        {
            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                // Preserve the first failure after every worker has unwound.
            }
        }
    }
}
