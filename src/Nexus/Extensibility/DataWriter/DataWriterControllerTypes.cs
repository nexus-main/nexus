using Nexus.Core;
using System.IO.Pipelines;

namespace Nexus.Extensibility
{
    internal record CatalogItemRequestPipeReader(
        CatalogItemRequest Request,
        PipeReader DataReader);
}
