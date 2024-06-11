// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Mvc.Formatters;

namespace Nexus.Core;

internal class StreamInputFormatter : IInputFormatter
{
    public bool CanRead(InputFormatterContext context)
    {
        return context.HttpContext.Request.ContentType == "application/octet-stream";
    }

    public async Task<InputFormatterResult> ReadAsync(InputFormatterContext context)
    {
        return await InputFormatterResult.SuccessAsync(context.HttpContext.Request.Body);
    }
}
