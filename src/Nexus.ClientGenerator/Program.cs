// MIT License
// Copyright (c) [2024] [nexus-main]

using Apollo3zehn.OpenApiClientGenerator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Readers;
using Nexus.Controllers.V1;

namespace Nexus.ClientGenerator;

public static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length > 2)
            throw new ArgumentException("Expected at most a solution root and an OpenAPI file name.");

        var solutionRoot = args.Length >= 1
            ? args[0]
            : "../../../../../";

        var openApiFileName = args.Length >= 2
            ? args[1]
            : "openapi.json";

        var openApiV2FileName = Path.GetFileNameWithoutExtension(openApiFileName) == "openapi"
            ? "openapi.v2.json"
            : $"{Path.GetFileNameWithoutExtension(openApiFileName)}.v2{Path.GetExtension(openApiFileName)}";

        //
        var builder = WebApplication.CreateBuilder([]);

        builder.Services
            .AddMvcCore().AddApplicationPart(typeof(ArtifactsController).Assembly);

        builder.Services
            .AddRouting(options => options.LowercaseUrls = true);

        builder.Services
            .AddNexusOpenApi();

        var app = builder.Build();
        var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();

        app.UseNexusOpenApi(provider, addExplorer: false);

        await app.StartAsync();

        try
        {
            // read open API documents
            using var client = new HttpClient();
            using var v1Response = await client.GetAsync("http://localhost:5000/openapi/v1.json");
            using var v2Response = await client.GetAsync("http://localhost:5000/openapi/v2.json");

            v1Response.EnsureSuccessStatusCode();
            v2Response.EnsureSuccessStatusCode();

            var openApiV1JsonString = await v1Response.Content.ReadAsStringAsync();
            var openApiV2JsonString = await v2Response.Content.ReadAsStringAsync();

            var v1Document = new OpenApiStringReader()
                .Read(openApiV1JsonString, out _);

            var v2Document = new OpenApiStringReader()
                .Read(openApiV2JsonString, out _);

            // generate clients
            var settings = new GeneratorSettings(
                Namespace: "Nexus.Api",
                ClientName: "Nexus",
                ExceptionType: "NexusException",
                ExceptionCodePrefix: "N",
                GetOperationName: (path, type, operation) => operation.OperationId.Split(['_'], 2)[1],
                Special_ConfigurationHeaderKey: "Nexus-Configuration",
                Special_WebAssemblySupport: true,
                Special_AccessTokenSupport: true,
                Special_NexusFeatures: true
            );

            // generate C# client
            var csharpGenerator = new CSharpGenerator(settings);
            var csharpOutputFolderPath = Path.Combine(solutionRoot, "src", "clients", "dotnet");

            csharpGenerator.Generate(csharpOutputFolderPath, v1Document, v2Document);

            // generate Python client
            var pythonOutputFolderPath = Path.Combine(solutionRoot, "src", "clients", "python", "nexus_api");
            var pythonGenerator = new PythonGenerator(settings);

            pythonGenerator.Generate(pythonOutputFolderPath, v1Document, v2Document);

            // save open API documents
            var openApiDocumentOutputPath = Path.Combine(solutionRoot, openApiFileName);
            var openApiV2DocumentOutputPath = Path.Combine(solutionRoot, openApiV2FileName);

            await File.WriteAllTextAsync(openApiDocumentOutputPath, openApiV1JsonString);
            await File.WriteAllTextAsync(openApiV2DocumentOutputPath, openApiV2JsonString);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
