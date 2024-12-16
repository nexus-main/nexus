// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Api;
using Nexus.Api.V1;
using Nexus.UI.Core;

namespace Nexus.UI.ViewModels;

public class CatalogItemViewModel
{
    public const string README_KEY = "readme";

    public const string LICENSE_KEY = "license";

    public const string DESCRIPTION_KEY = "description";

    public const string WARNING_KEY = "warning";

    public const string UNIT_KEY = "unit";

    private string? _description;

    private string? _warning;

    private string? _unit;

    public CatalogItemViewModel(ResourceCatalog resourceCatalog, Resource resource, Representation representation)
    {
        Catalog = resourceCatalog;
        Resource = resource;
        Representation = representation;

        if (resource.Properties is not null)
        {
            _description = Resource.Properties.GetStringValue(DESCRIPTION_KEY);
            _warning = Resource.Properties.GetStringValue(WARNING_KEY);
            _unit = Resource.Properties.GetStringValue(UNIT_KEY);
        }
    }

    public ResourceCatalog Catalog { get; }

    public Resource Resource { get; }

    public Representation Representation { get; }

    public string? Description
    {
        get
        {
            return _description;
        }
        set
        {
            if (value != _description)
            {
                _description = value;
                DescriptionHasChanged = true;
            }
        }
    }

    public string? Warning
    {
        get
        {
            return _warning;
        }
        set
        {
            if (value != _warning)
            {
                _warning = value;
                WarningHasChanged = true;
            }
        }
    }

    public string? Unit
    {
        get
        {
            return _unit;
        }
        set
        {
            if (value != _unit)
            {
                _unit = value;
                UnitHasChanged = true;
            }
        }
    }

    public bool DescriptionHasChanged { get; private set; }

    public bool WarningHasChanged { get; private set; }

    public bool UnitHasChanged { get; private set; }

    public void ResetHasChangedState()
    {
        DescriptionHasChanged = false;
        WarningHasChanged = false;
        UnitHasChanged = false;
    }
}