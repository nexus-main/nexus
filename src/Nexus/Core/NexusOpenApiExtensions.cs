// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Nexus.Core;
using NJsonSchema.Generation;
using NSwag.AspNetCore;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.DependencyInjection;

internal static class NexusOpenApiExtensions
{
    public static IServiceCollection AddNexusOpenApi(
        this IServiceCollection services)
    {
        // https://github.com/dotnet/aspnet-api-versioning/tree/master/samples/aspnetcore/SwaggerSample
        services
            .AddControllers(options => options.InputFormatters.Add(new StreamInputFormatter()))
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .ConfigureApplicationPartManager(
                manager =>
                {
                    manager.FeatureProviders.Add(new InternalControllerFeatureProvider());
                });

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

                config.ApiGroupNames = new[] { description.GroupName };
                config.DocumentName = description.GroupName;
            });
        }

        return services;
    }

    public static IApplicationBuilder UseNexusOpenApi(
        this IApplicationBuilder app,
        IApiVersionDescriptionProvider provider,
        bool addExplorer)
    {
        app.UseOpenApi(settings => settings.Path = "/openapi/{documentName}/openapi.json");

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
                            $"/openapi/{description.GroupName}/openapi.json"));
                }
            });
        }

        return app;
    }
}
