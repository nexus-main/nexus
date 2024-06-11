// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Sources;

namespace DataSource;

public class DataSourceControllerFixture
{
    public DataSourceControllerFixture()
    {
        DataSource = new Sample();

        Registration = new InternalDataSourceRegistration(
            Id: Guid.NewGuid(),
            Type: typeof(Sample).FullName!,
            ResourceLocator: default,
            Configuration: default);
    }

    internal IDataSource DataSource { get; }

    internal InternalDataSourceRegistration Registration { get; }
}
