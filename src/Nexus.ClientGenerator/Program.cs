// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Reflection;
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
        var solutionRoot = args.Length >= 1
            ? args[0]
            : "../../../../../";

        var openApiFileName = args.Length == 2
            ? args[1]
            : "openapi.json";

        //
        var builder = WebApplication.CreateBuilder(args);

        builder.Services
            .AddMvcCore().AddApplicationPart(typeof(ArtifactsController).Assembly);

        builder.Services
            .AddRouting(options => options.LowercaseUrls = true);

        builder.Services
            .AddNexusOpenApi();

        var app = builder.Build();
        var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();

        app.UseNexusOpenApi(provider, addExplorer: false);

        _ = app.RunAsync();

        // read open API document
        var client = new HttpClient();
        var response = await client.GetAsync("http://localhost:5000/openapi/v1.json");

        response.EnsureSuccessStatusCode();

        var openApiJsonString = await response.Content.ReadAsStringAsync();

        var document = new OpenApiStringReader()
            .Read(openApiJsonString, out var diagnostic);

        // generate clients
        var basePath = Assembly.GetExecutingAssembly().Location;

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
        var csharpOutputFolderPath = $"{solutionRoot}src/clients/dotnet";

        csharpGenerator.Generate(csharpOutputFolderPath, document);

        // generate Python client
        var pythonOutputFolderPath = $"{solutionRoot}src/clients/python/nexus_api";
        var pythonGenerator = new PythonGenerator(settings);

        pythonGenerator.Generate(pythonOutputFolderPath, document);

        // save open API document
        var openApiDocumentOutputPath = $"{solutionRoot}{openApiFileName}";
        File.WriteAllText(openApiDocumentOutputPath, openApiJsonString);
    }
}