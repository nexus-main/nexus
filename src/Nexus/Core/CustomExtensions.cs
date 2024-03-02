using Nexus.Utilities;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Nexus.Core
{
    internal static class CustomExtensions
    {
#pragma warning disable VSTHRD200 // Verwenden Sie das Suffix "Async" f�r asynchrone Methoden
        public static Task<(T[] Results, AggregateException Exception)> WhenAllEx<T>(this IEnumerable<Task<T>> tasks)
#pragma warning restore VSTHRD200 // Verwenden Sie das Suffix "Async" f�r asynchrone Methoden
        {
            tasks = tasks.ToArray();

            return Task.WhenAll(tasks).ContinueWith(t =>
            {
                var results = tasks
                .Where(task => task.Status == TaskStatus.RanToCompletion)
                .Select(task => task.Result)
                .ToArray();

                var aggregateExceptions = tasks
                    .Where(task => task.IsFaulted && task.Exception is not null)
                    .Select(task => task.Exception!)
                    .ToArray();

                var flattenedAggregateException = new AggregateException(aggregateExceptions).Flatten();

                return (results, flattenedAggregateException);
            }, default, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public static byte[] Hash(this string value)
        {
            var md5 = MD5.Create(); // compute hash is not thread safe!
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(value)); // 
            return hash;
        }

        public static Memory<To> Cast<TFrom, To>(this Memory<TFrom> buffer)
            where TFrom : unmanaged
            where To : unmanaged
        {
            return new CastMemoryManager<TFrom, To>(buffer).Memory;
        }

        public static ReadOnlyMemory<To> Cast<TFrom, To>(this ReadOnlyMemory<TFrom> buffer)
            where TFrom : unmanaged
            where To : unmanaged
        {
            return new CastMemoryManager<TFrom, To>(MemoryMarshal.AsMemory(buffer)).Memory;
        }

        public static DateTime RoundDown(this DateTime dateTime, TimeSpan timeSpan)
        {
            return new DateTime(dateTime.Ticks - (dateTime.Ticks % timeSpan.Ticks), dateTime.Kind);
        }

        public static DateTime RoundUp(this DateTime dateTime, TimeSpan timeSpan)
        {
            var remainder = dateTime.Ticks % timeSpan.Ticks;

            return remainder == 0
                ? dateTime
                : dateTime.AddTicks(timeSpan.Ticks - remainder);
        }

        public static TimeSpan RoundDown(this TimeSpan timeSpan1, TimeSpan timeSpan2)
        {
            return new TimeSpan(timeSpan1.Ticks - (timeSpan1.Ticks % timeSpan2.Ticks));
        }
    }
}
