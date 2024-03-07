using Nexus.Core;
using System.IO.Pipelines;

namespace Nexus.Extensibility;

internal record CatalogItemRequestPipeWriter(
    CatalogItemRequest Request,
    PipeWriter DataWriter);

internal record DataReadingGroup(
    IDataSourceController Controller,
    CatalogItemRequestPipeWriter[] CatalogItemRequestPipeWriters);
