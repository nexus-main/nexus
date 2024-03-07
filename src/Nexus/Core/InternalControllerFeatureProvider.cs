using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using System.Reflection;

namespace Nexus.Core;

internal class InternalControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    private const string ControllerTypeNameSuffix = "Controller";

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        foreach (var part in parts.OfType<IApplicationPartTypeProvider>())
        {
            foreach (var type in part.Types)
            {
                if (IsController(type) && !feature.Controllers.Contains(type))
                {
                    feature.Controllers.Add(type);
                }
            }
        }
    }

    protected virtual bool IsController(TypeInfo typeInfo)
    {
        if (!typeInfo.IsClass)
            return false;

        if (typeInfo.IsAbstract)
            return false;

        if (typeInfo.ContainsGenericParameters)
            return false;

        if (typeInfo.IsDefined(typeof(NonControllerAttribute)))
            return false;

        if (!typeInfo.Name.EndsWith(ControllerTypeNameSuffix, StringComparison.OrdinalIgnoreCase) &&
            !typeInfo.IsDefined(typeof(ControllerAttribute)))
            return false;

        return true;
    }
}
