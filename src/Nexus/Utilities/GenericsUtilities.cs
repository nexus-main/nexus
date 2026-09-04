// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Linq.Expressions;
using System.Reflection.Emit;

namespace Nexus.Utilities;

internal static class GenericToFloat32<T>
{
    private static readonly Func<T, float> _to_float_function = GenericToFloat32<T>.EmitToFloatConverter();

    private static Func<T, float> EmitToFloatConverter()
    {
        var method = new DynamicMethod(string.Empty, typeof(float), [typeof(T)]);
        var ilGenerator = method.GetILGenerator();

        ilGenerator.Emit(OpCodes.Ldarg_0);

        if (typeof(T) != typeof(float))
            ilGenerator.Emit(OpCodes.Conv_R4);

        ilGenerator.Emit(OpCodes.Ret);

        return (Func<T, float>)method.CreateDelegate(typeof(Func<T, float>));
    }

    public static float ToFloat32(T value)
    {
        return _to_float_function(value);
    }
}

internal static class GenericToFloat64<T>
{
    private static readonly Func<T, double> _to_double_function = GenericToFloat64<T>.EmitToDoubleConverter();

    private static Func<T, double> EmitToDoubleConverter()
    {
        var method = new DynamicMethod(string.Empty, typeof(double), [typeof(T)]);
        var ilGenerator = method.GetILGenerator();

        ilGenerator.Emit(OpCodes.Ldarg_0);

        if (typeof(T) != typeof(double))
            ilGenerator.Emit(OpCodes.Conv_R8);

        ilGenerator.Emit(OpCodes.Ret);

        return (Func<T, double>)method.CreateDelegate(typeof(Func<T, double>));
    }

    public static double ToFloat64(T value)
    {
        return _to_double_function(value);
    }
}

internal static class GenericBitOr<T>
{
    private static readonly Func<T, T, T> _bit_or_function = GenericBitOr<T>.EmitBitOrFunction();

    private static Func<T, T, T> EmitBitOrFunction()
    {
        var _parameterA = Expression.Parameter(typeof(T), "a");
        var _parameterB = Expression.Parameter(typeof(T), "b");

        var _body = Expression.Or(_parameterA, _parameterB);

        return Expression.Lambda<Func<T, T, T>>(_body, _parameterA, _parameterB).Compile();
    }

    public static T BitOr(T a, T b)
    {
        return _bit_or_function(a, b);
    }
}

internal static class GenericBitAnd<T>
{
    private static readonly Func<T, T, T> _bit_and_function = GenericBitAnd<T>.EmitBitAndFunction();

    private static Func<T, T, T> EmitBitAndFunction()
    {
        var _parameterA = Expression.Parameter(typeof(T), "a");
        var _parameterB = Expression.Parameter(typeof(T), "b");

        var _body = Expression.And(_parameterA, _parameterB);

        return Expression.Lambda<Func<T, T, T>>(_body, _parameterA, _parameterB).Compile();
    }

    public static T BitAnd(T a, T b)
    {
        return _bit_and_function(a, b);
    }
}
