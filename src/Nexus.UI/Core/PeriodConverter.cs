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
    public static Converter<Period> CreateConverter(Func<Period> getCurrentValue)
    {
        return new()
        {
            SetFunc = value => value != null ? value.ToString() : "",
            GetFunc = text => GetFunc(text, getCurrentValue())
        };
    }

    private static Period GetFunc(string? text, Period currentValue)
    {
        /* Whenever the input is invalid, the current value of the 
         * autocomplete input this converter belongs to is returned. 
         * This ensures that the input's current value does not change
         * which in turn allows the user to continue typing until a
         * valid value is entered. A valid value is a number + unit, 
         * e.g. "10 min".
         */

        if (text is null)
            return currentValue;

        try
        {
            return text == Period.SINGLE_FILE_LABEL
                ? new Period(TimeSpan.Zero)
                : new Period(Utilities.ToPeriod(text));
        }

        catch
        {
            return currentValue;
        }
    }
}