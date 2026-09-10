// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Nexus.Core;
using NJsonSchema.Generation;
using NSwag;
using NSwag.AspNetCore;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.DependencyInjection;

internal static class NexusOpenApiExtensions
{
    public static IServiceCollection AddNexusOpenApi(
        this IServiceCollection services
    )
    {
        // https://github.com/dotnet/aspnet-api-versioning/tree/master/samples/aspnetcore/SwaggerSample
        services
            .AddControllers(options => options.InputFormatters.Add(new StreamInputFormatter()))
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .ConfigureApplicationPartManager(
                manager => manager.FeatureProviders.Add(new InternalControllerFeatureProvider())
            );

        services.AddApiVersioning(
            options =>
            {
                options.ReportApiVersions = true;
            });

        services.AddVersionedApiExplorer(
            options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        /* not optimal */
        var provider = services.BuildServiceProvider().GetRequiredService<IApiVersionDescriptionProvider>();

        foreach (var description in provider.ApiVersionDescriptions)
        {
            services.AddOpenApiDocument(config =>
            {
                config.SchemaSettings.DefaultReferenceTypeNullHandling = ReferenceTypeNullHandling.NotNull;

                config.Title = "Nexus REST API";
                config.Version = description.GroupName;
                config.Description = "Explore resources and get their data."
                    + (description.IsDeprecated ? " This API version is deprecated." : "");

                config.ApiGroupNames = [description.GroupName];
                config.DocumentName = description.GroupName;

                config.PostProcess = document =>
                {
                    if (document.Components.Schemas.TryGetValue("Precision", out var schema))
                    {
                        schema.ExtensionData ??= new Dictionary<string, object?>();
                        schema.ExtensionData["x-enum-values"] = new[] { 4, 8 };
                    }

                    // NSwag ignores custom response content types for FileStreamResult.
                    // https://github.com/RicoSuter/NSwag/issues/3920
                    if (description.GroupName == "v2" &&
                        document.Paths.TryGetValue("/api/v2/data", out var dataPath) &&
                        dataPath.TryGetValue(OpenApiOperationMethod.Post, out var dataOperation) &&
                        dataOperation.Responses.TryGetValue("200", out var response) &&
                        response.Content.TryGetValue("application/octet-stream", out var streamContent))
                    {
                        response.Content.Remove("application/octet-stream");
                        response.Content["application/vnd.apache.arrow.stream"] = streamContent;
                    }
                };
            });
        }

        return services;
    }

    public static WebApplication UseNexusOpenApi(
        this WebApplication app,
        IApiVersionDescriptionProvider provider,
        bool addExplorer
    )
    {
        app.UseOpenApi(settings => settings.Path = "/openapi/{documentName}.json");

        if (addExplorer)
        {
            app.UseSwaggerUi(settings =>
            {
                settings.Path = "/api";

                foreach (var description in provider.ApiVersionDescriptions)
                {
                    settings.SwaggerRoutes.Add(
                        new SwaggerUiRoute(
                            description.GroupName.ToUpperInvariant(),
                            $"/openapi/{description.GroupName}.json"));
                }
            });
        }

        return app;
    }
}
