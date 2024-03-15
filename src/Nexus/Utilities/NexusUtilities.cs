using Nexus.Core;
using Nexus.DataModel;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;

namespace Nexus.Utilities;

internal static partial class NexusUtilities
{
    private static string? _defaultBaseUrl;

    public static string DefaultBaseUrl
    {
        get
        {
            if (_defaultBaseUrl is null)
            {
                var port = 5000;
                var aspnetcoreEnvVar = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");

                if (aspnetcoreEnvVar is not null)
                {
                    var match = AspNetCoreEnvVarRegex().Match(aspnetcoreEnvVar);

                    if (match.Success && int.TryParse(match.Groups[1].Value, out var parsedPort))
                        port = parsedPort;
                }

                _defaultBaseUrl = $"http://localhost:{port}";
            }

            return _defaultBaseUrl;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Scale(TimeSpan value, TimeSpan samplePeriod) => (int)(value.Ticks / samplePeriod.Ticks);

    public static List<T> GetEnumValues<T>() where T : Enum
    {
        return Enum.GetValues(typeof(T)).Cast<T>().ToList();
    }

    public static async Task FileLoopAsync(
        DateTime begin,
        DateTime end,
        TimeSpan filePeriod,
        Func<DateTime, TimeSpan, TimeSpan, Task> func)
    {
        var lastFileBegin = default(DateTime);
        var currentBegin = begin;
        var totalPeriod = end - begin;
        var remainingPeriod = totalPeriod;

        while (remainingPeriod > TimeSpan.Zero)
        {
            DateTime fileBegin;

            if (filePeriod == totalPeriod)
                fileBegin = lastFileBegin != DateTime.MinValue ? lastFileBegin : begin;

            else
                fileBegin = currentBegin.RoundDown(filePeriod);

            lastFileBegin = fileBegin;

            var fileOffset = currentBegin - fileBegin;
            var remainingFilePeriod = filePeriod - fileOffset;
            var duration = TimeSpan.FromTicks(Math.Min(remainingFilePeriod.Ticks, remainingPeriod.Ticks));

            await func.Invoke(fileBegin, fileOffset, duration);

            // update loop state
            currentBegin += duration;
            remainingPeriod -= duration;
        }
    }

#pragma warning disable VSTHRD200 // Verwenden Sie das Suffix "Async" für asynchrone Methoden
    public static async ValueTask<T[]> WhenAll<T>(params ValueTask<T>[] tasks)
#pragma warning restore VSTHRD200 // Verwenden Sie das Suffix "Async" für asynchrone Methoden
    {
        List<Exception>? exceptions = default;

        var results = new T[tasks.Length];

        for (int i = 0; i < tasks.Length; i++)
        {
            try
            {
                results[i] = await tasks[i];
            }
            catch (Exception ex)
            {
                exceptions ??= new List<Exception>(tasks.Length);
                exceptions.Add(ex);
            }
        }

        return exceptions is null
            ? results
            : throw new AggregateException(exceptions);
    }

    public static async Task WhenAllFailFastAsync(List<Task> tasks, CancellationToken cancellationToken)
    {
        while (tasks.Count != 0)
        {
            var task = await Task
                .WhenAny(tasks)
                .WaitAsync(cancellationToken);

            cancellationToken
                .ThrowIfCancellationRequested();

            if (task.Exception is not null)
                ExceptionDispatchInfo.Capture(task.Exception.InnerException ?? task.Exception).Throw();

            tasks.Remove(task);
        }
    }

    public static Type GetTypeFromNexusDataType(NexusDataType dataType)
    {
        return dataType switch
        {
            NexusDataType.UINT8 => typeof(byte),
            NexusDataType.INT8 => typeof(sbyte),
            NexusDataType.UINT16 => typeof(ushort),
            NexusDataType.INT16 => typeof(short),
            NexusDataType.UINT32 => typeof(uint),
            NexusDataType.INT32 => typeof(int),
            NexusDataType.UINT64 => typeof(ulong),
            NexusDataType.INT64 => typeof(long),
            NexusDataType.FLOAT32 => typeof(float),
            NexusDataType.FLOAT64 => typeof(double),
            _ => throw new NotSupportedException($"The specified data type {dataType} is not supported.")
        };
    }

    public static int SizeOf(NexusDataType dataType)
    {
        return ((ushort)dataType & 0x00FF) / 8;
    }

    public static IEnumerable<T> GetCustomAttributes<T>(this Type type) where T : Attribute
    {
        return type.GetCustomAttributes(false).OfType<T>();
    }

    [GeneratedRegex(":([0-9]+)")]
    private static partial Regex AspNetCoreEnvVarRegex();
}