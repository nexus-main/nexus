// MIT License
// Copyright (c) [2024] [nexus-main]

using MudBlazor;

namespace Nexus.UI.Core;

public record Period
{
    internal const string SINGLE_FILE_LABEL = "Single File";

    public Period(TimeSpan value)
    {
        if (value == TimeSpan.MinValue)
        {
            Value = TimeSpan.MinValue;
            IsValid = false;
        }
        else
        {
            Value = value;
        }
    }

    public TimeSpan Value { get; }

    //Exception passes MinValue to create invalid period
    public bool IsValid { get; private set; } = true;

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
        SetFunc = value => value != null ? value.ToString() : "",
        GetFunc = GetFunc
    };

    private static Period? GetFunc(string? text)
    {
        if (text is null)
            return default;

        try
        {
            return text == Period.SINGLE_FILE_LABEL
                ? new Period(TimeSpan.Zero)
                : new Period(Utilities.ToPeriod(text));
        }

        catch
        {
            //Utilities.ToPeriod threw an exception, thus return invalid Period.
            return new Period(TimeSpan.MinValue);
        }
    }
}