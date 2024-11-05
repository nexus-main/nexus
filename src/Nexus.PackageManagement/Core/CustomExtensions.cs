// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Security.Cryptography;
using System.Text;

namespace Nexus.PackageManagement.Core;

internal static class CustomExtensions
{
    public static byte[] Hash(this string value)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(value));
        return hash;
    }
}
