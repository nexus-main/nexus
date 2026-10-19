// MIT License
// Copyright (c) [2024] [nexus-main]

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace Nexus.Benchmarks;

internal class Program
{
    private static void Main(string[] args)
    {
        var config = DefaultConfig.Instance
            .AddJob(Job.Default
                .WithWarmupCount(1)
                .WithIterationCount(3));

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}
