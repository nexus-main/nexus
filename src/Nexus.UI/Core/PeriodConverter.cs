// MIT License
// Copyright (c) [2024] [nexus-main]

using MudBlazor;

namespace Nexus.UI.Core;

public record Period
{
    internal const string SINGLE_FILE_LABEL = "Single File";

    public Period(TimeSpan value)
    {
        Value = value;
    }

    public TimeSpan Value { get; }

    public override string ToString()
    {
        return Value.Equals(default)
            ? SINGLE_FILE_LABEL
            : Utilities.ToUnitString(Value);
    }
}

public static class PeriodHelper
{
    public static readonly Converter<Period> Converter = new()
    {
        SetFunc = value => value.ToString(),
        GetFunc = GetFunc
    };

    private static Period GetFunc(string text)
    {
        try
        {
            return text == Period.SINGLE_FILE_LABEL
                ? new Period(TimeSpan.Zero)
                : new Period(Utilities.ToPeriod(text));
        }

        catch
        {
            return new Period(default);
        }
    }
}