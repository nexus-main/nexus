// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Core;
using System.IO.Pipelines;

namespace Nexus.Extensibility;

internal record CatalogItemRequestPipeReader(
    CatalogItemRequest Request,
    PipeReader DataReader);
