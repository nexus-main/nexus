// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Api;
using Nexus.UI.Core;

namespace Nexus.UI.ViewModels;

public class CatalogItemViewModel
{
    public const string README_KEY = "readme";
    public const string LICENSE_KEY = "license";
    public const string DESCRIPTION_KEY = "description";
    public const string WARNING_KEY = "warning";
    public const string UNIT_KEY = "unit";

    public CatalogItemViewModel(ResourceCatalog resourceCatalog, Resource resource, Representation representation)
    {
        Catalog = resourceCatalog;
        Resource = resource;
        Representation = representation;

        if (resource.Properties is not null)
        {
            Description = resource.Properties.GetStringValue(DESCRIPTION_KEY);
            Warning = resource.Properties.GetStringValue(WARNING_KEY);
            Unit = resource.Properties.GetStringValue(UNIT_KEY);
        }
    }

    public ResourceCatalog Catalog { get; }

    public Resource Resource { get; }

    public Representation Representation { get; }

    public string? Description;
    public string? Warning;
    public string? Unit;
}