// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Core.V1;
using Nexus.Extensibility;
using Nexus.Sources;

namespace DataSource;

public class DataSourceControllerFixture
{
    internal IDataSource DataSource1 { get; } = new Sample();

    internal IDataSource DataSource2 { get; } = new TestSource();

    internal DataSourceRegistration Registration1 { get; } = new DataSourceRegistration
    (
        Type: typeof(Sample).FullName!,
        ResourceLocator: default,
        Configuration: default
    );

    internal DataSourceRegistration Registration2 { get; } = new DataSourceRegistration
    (
        Type: typeof(TestSource).FullName!,
        ResourceLocator: default,
        Configuration: default
    );
}
