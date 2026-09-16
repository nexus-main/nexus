// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using Namotion.Reflection;

namespace Nexus.Extensibility;

internal static class ConfigurationTypeResolver
{
    public static unsafe ContextualType Resolve(Type sourceType)
    {
        // Keep the existing validation and CLR contract selection.
        var configurationType = DataSourceController.GetConfigurationType(sourceType);
        // The attribute overload avoids Namotion's FullName-only cache across extension load contexts.
        return Find(sourceType.ToContextualType([])) ?? configurationType.ToContextualType([]);

        ContextualType? Find(ContextualType source)
        {
            var type = source.OriginalType;

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDataSource<>))
                return type.GenericTypeArguments[0] == configurationType ? source.GenericArguments[0] : null;

            var declaration = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            byte context = 0;

            for (var scope = declaration; scope is not null; scope = scope.DeclaringType)
            {
                var attribute = scope.GetCustomAttributesData().FirstOrDefault(attribute =>
                    attribute.AttributeType.FullName == typeof(NullableContextAttribute).FullName);

                if (attribute is not null)
                {
                    context = (byte)attribute.ConstructorArguments[0].Value!;
                    break;
                }
            }

            // Reflection exposes base-type attributes, but not attributes on InterfaceImpl rows.
            // Raw loaded metadata also works for extensions loaded from streams or bundled assemblies.
            if (declaration.Module == declaration.Assembly.ManifestModule &&
                declaration.Assembly.TryGetRawMetadata(out byte* metadata, out int length))
            {
                var reader = new MetadataReader(metadata, length);
                var definition = reader.GetTypeDefinition((TypeDefinitionHandle)MetadataTokens.Handle(declaration.MetadataToken));

                foreach (var handle in definition.GetInterfaceImplementations())
                {
                    var implementation = reader.GetInterfaceImplementation(handle);
                    var interfaceType = declaration.Module.ResolveType(
                        MetadataTokens.GetToken(implementation.Interface), declaration.GetGenericArguments(), null);

                    if (!interfaceType.GetInterfaces().Any(candidate => candidate.IsGenericType &&
                            candidate.GetGenericTypeDefinition() == typeof(IDataSource<>)) &&
                        !(interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IDataSource<>)))
                    {
                        continue;
                    }

                    byte[] flags = [context];

                    foreach (var attributeHandle in implementation.GetCustomAttributes())
                    {
                        var attribute = reader.GetCustomAttribute(attributeHandle);
                        var constructor = declaration.Module.ResolveMethod(MetadataTokens.GetToken(attribute.Constructor))!;

                        if (constructor.DeclaringType?.FullName != typeof(NullableAttribute).FullName)
                            continue;

                        var blob = reader.GetBlobReader(attribute.Value);
                        blob.ReadUInt16(); // Custom attribute prolog.
                        flags = constructor.GetParameters()[0].ParameterType == typeof(byte)
                            ? [blob.ReadByte()]
                            : blob.ReadBytes(blob.ReadInt32());
                        break;
                    }

                    var result = Find(Substitute(interfaceType.ToContextualType([new NullableAttribute(flags)]), source));

                    if (result is not null)
                    {
                        GC.KeepAlive(declaration.Assembly);
                        return result;
                    }
                }

                GC.KeepAlive(declaration.Assembly);
            }

            if (declaration.BaseType is not null)
            {
                var attribute = declaration.GetCustomAttributesData().FirstOrDefault(attribute =>
                    attribute.AttributeType.FullName == typeof(NullableAttribute).FullName);
                var value = attribute?.ConstructorArguments[0].Value;
                byte[] flags = value is IReadOnlyCollection<CustomAttributeTypedArgument> arguments
                    ? arguments.Select(argument => (byte)argument.Value!).ToArray()
                    : [value is byte flag ? flag : context];

                return Find(Substitute(declaration.BaseType.ToContextualType([new NullableAttribute(flags)]), source));
            }

            return null;
        }
    }

    private static ContextualType Substitute(ContextualType declaration, ContextualType source)
    {
        // Substitute against the open declaration before re-encoding flags: a value-type
        // argument consumes different metadata slots than the type parameter it replaces.
        var type = declaration.OriginalType;

        if (type.IsGenericParameter)
        {
            var argument = source.OriginalGenericArguments[type.GenericParameterPosition];

            if (declaration.Nullability != Nullability.Nullable || argument.IsValueType)
                return argument;

            var nullableFlags = GetFlags(argument).ToArray();
            nullableFlags[0] = 2;
            return argument.OriginalType.ToContextualType([new NullableAttribute(nullableFlags)]);
        }

        var arguments = declaration.OriginalGenericArguments.Select(argument => Substitute(argument, source)).ToArray();
        var element = declaration.ElementType is null ? null : Substitute(declaration.ElementType, source);

        if (type.IsArray)
            type = type.IsSZArray ? element!.OriginalType.MakeArrayType() : element!.OriginalType.MakeArrayType(type.GetArrayRank());
        else if (type.IsGenericType)
            type = type.GetGenericTypeDefinition().MakeGenericType(arguments.Select(argument => argument.OriginalType).ToArray());

        var flags = new List<byte>();

        if (!type.IsValueType || (type.IsGenericType && Nullable.GetUnderlyingType(type) is null))
            flags.Add((byte)declaration.OriginalNullability);

        foreach (var argument in arguments)
            flags.AddRange(GetFlags(argument));

        if (element is not null)
            flags.AddRange(GetFlags(element));

        return type.ToContextualType([new NullableAttribute(flags.Count == 0 ? [0] : flags.ToArray())]);
    }

    private static IEnumerable<byte> GetFlags(ContextualType type)
    {
        if (!type.OriginalType.IsValueType || (type.OriginalType.IsGenericType && !type.IsNullableType))
            yield return (byte)type.OriginalNullability;

        foreach (var argument in type.OriginalGenericArguments)
        {
            foreach (byte flag in GetFlags(argument))
                yield return flag;
        }

        if (type.ElementType is not null)
        {
            foreach (byte flag in GetFlags(type.ElementType))
                yield return flag;
        }
    }
}
