using Nexus.Core;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Timer = System.Timers.Timer;

namespace Nexus.Services;

internal interface IJobService
{
    JobControl AddJob(
        Job job,
        Progress<double> progress,
        Func<JobControl, CancellationTokenSource, Task<object?>> createTask);

    List<JobControl> GetJobs();

    bool TryGetJob(
        Guid key,
        [NotNullWhen(true)] out JobControl? jobControl);
}

internal class JobService : IJobService
{
    #region Fields

    private readonly Timer _timer;

    private readonly ConcurrentDictionary<Guid, JobControl> _jobs = new();

    #endregion

    #region Constructors

    public JobService()
    {
        _timer = new Timer()
        {
            AutoReset = true,
            Enabled = true,
            Interval = TimeSpan.FromDays(1).TotalMilliseconds
        };

        _timer.Elapsed += (sender, e) =>
        {
            var now = DateTime.UtcNow;
            var maxRuntime = TimeSpan.FromDays(3);

            foreach (var jobControl in GetJobs())
            {
                if (jobControl.Task.IsCompleted)
                {
                    var runtime = now - jobControl.Start;

                    if (runtime > maxRuntime)
                        _jobs.TryRemove(jobControl.Job.Id, out _);
                }
            }
        };
    }

    #endregion

    #region Methods

    public JobControl AddJob(
        Job job,
        Progress<double> progress,
        Func<JobControl, CancellationTokenSource, Task<object?>> createTask)
    {
        var cancellationTokenSource = new CancellationTokenSource();

        var jobControl = new JobControl(
            Start: DateTime.UtcNow,
            Job: job,
            CancellationTokenSource: cancellationTokenSource);

        void progressHandler(object? sender, double e)
        {
            jobControl.OnProgressUpdated(e);
        }

        progress.ProgressChanged += progressHandler;
        jobControl.Task = createTask(jobControl, cancellationTokenSource);

        _ = Task.Run(async () =>
        {
            try
            {
                await jobControl.Task;
            }
            finally
            {
                jobControl.OnCompleted();
                jobControl.ProgressUpdated -= progressHandler;
            }
        });

        TryAddJob(jobControl);
        return jobControl;
    }

    private bool TryAddJob(JobControl jobControl)
    {
        var result = _jobs.TryAdd(jobControl.Job.Id, jobControl);
        return result;
    }

    public bool TryGetJob(Guid key, [NotNullWhen(true)] out JobControl? jobControl)
    {
        return _jobs.TryGetValue(key, out jobControl);
    }

    public List<JobControl> GetJobs()
    {
        // http://blog.i3arnon.com/2018/01/16/concurrent-dictionary-tolist/
        // https://stackoverflow.com/questions/41038514/calling-tolist-on-concurrentdictionarytkey-tvalue-while-adding-items
        return _jobs
            .ToArray()
            .Select(entry => entry.Value)
            .ToList();
    }

    #endregion
}
