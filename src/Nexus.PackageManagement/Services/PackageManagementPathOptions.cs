/// <summary>
/// The path options for the package management.
/// </summary>
public interface IPackageManagementPathsOptions
{
    /// <summary>
    /// The path to restore config files to.
    /// </summary>
    string Config { get; set; }

    /// <summary>
    /// The path to restore packages to.
    /// </summary>
    string Packages { get; set; }
}

internal record PackageManagementPathsOptions() : IPackageManagementPathsOptions
{
    private const string ExceptionMessage = "The IPackageManagementPathsOptions must be implemented and added to the service collection by the consuming library.";

    public string Config
    {
        get
        {
            throw new Exception(ExceptionMessage);
        }
        set
        {
            throw new Exception(ExceptionMessage);
        }
    }

    public string Packages
    {
        get
        {
            throw new Exception(ExceptionMessage);
        }
        set
        {
            throw new Exception(ExceptionMessage);
        }
    }
}