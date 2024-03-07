using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using System.Diagnostics.CodeAnalysis;

namespace Nexus.Services
{
    internal interface IDatabaseService
    {
        /* /config/catalogs/catalog_id.json */
        bool TryReadCatalogMetadata(string catalogId, [NotNullWhen(true)] out string? catalogMetadata);
        Stream WriteCatalogMetadata(string catalogId);

        /* /config/project.json */
        bool TryReadProject([NotNullWhen(true)] out string? project);
        Stream WriteProject();

        /* /catalogs/catalog_id/... */
        bool AttachmentExists(string catalogId, string attachmentId);
        IEnumerable<string> EnumerateAttachments(string catalogId);
        bool TryReadAttachment(string catalogId, string attachmentId, [NotNullWhen(true)] out Stream? attachment);
        bool TryReadFirstAttachment(string catalogId, string searchPattern, EnumerationOptions enumerationOptions, [NotNullWhen(true)] out Stream? attachment);
        Stream WriteAttachment(string catalogId, string attachmentId);
        void DeleteAttachment(string catalogId, string attachmentId);

        /* /artifacts */
        bool TryReadArtifact(string artifactId, [NotNullWhen(true)] out Stream? artifact);
        Stream WriteArtifact(string fileName);

        /* /cache */
        bool TryReadCacheEntry(CatalogItem catalogItem, DateTime begin, [NotNullWhen(true)] out Stream? cacheEntry);
        bool TryWriteCacheEntry(CatalogItem catalogItem, DateTime begin, [NotNullWhen(true)] out Stream? cacheEntry);
        Task ClearCacheEntriesAsync(string catalogId, DateOnly day, TimeSpan timeout, Predicate<string> predicate);

        /* /users */
        bool TryReadTokenMap(
            string userId, 
            [NotNullWhen(true)] out string? tokenMap);

        Stream WriteTokenMap(
            string userId);
    }

    internal class DatabaseService : IDatabaseService
    {
        // generated, small files:
        //
        // <application data>/config/catalogs/catalog_id.json
        // <application data>/config/project.json
        // <application data>/config/users.db

        // user defined or potentially large files:
        //
        // <application data>/catalogs/catalog_id/...
        // <application data>/users/user_name/...
        // <application data>/cache
        // <application data>/export
        // <user profile>/.nexus/packages

        private readonly PathsOptions _pathsOptions;

        public DatabaseService(IOptions<PathsOptions> pathsOptions)
        {
            _pathsOptions = pathsOptions.Value;
        }

        /* /config/catalogs/catalog_id.json */
        public bool TryReadCatalogMetadata(string catalogId, [NotNullWhen(true)] out string? catalogMetadata)
        {
            var physicalId = catalogId.TrimStart('/').Replace("/", "_");
            var catalogMetadataFileName = $"{physicalId}.json";
            var filePath = SafePathCombine(_pathsOptions.Config, Path.Combine("catalogs", catalogMetadataFileName));

            catalogMetadata = default;

            if (File.Exists(filePath))
            {
                catalogMetadata = File.ReadAllText(filePath);
                return true;
            }

            return false;
        }

        public Stream WriteCatalogMetadata(string catalogId)
        {
            var physicalId = catalogId.TrimStart('/').Replace("/", "_");
            var catalogMetadataFileName = $"{physicalId}.json";
            var folderPath = Path.Combine(_pathsOptions.Config, "catalogs");

            Directory.CreateDirectory(folderPath);

            var filePath = SafePathCombine(folderPath, catalogMetadataFileName);

            return File.Open(filePath, FileMode.Create, FileAccess.Write);
        }

        /* /config/project.json */
        public bool TryReadProject([NotNullWhen(true)] out string? project)
        {
            var filePath = Path.Combine(_pathsOptions.Config, "project.json");
            project = default;

            if (File.Exists(filePath))
            {
                project = File.ReadAllText(filePath);
                return true;
            }

            return false;
        }

        public Stream WriteProject()
        {
            Directory.CreateDirectory(_pathsOptions.Config);

            var filePath = Path.Combine(_pathsOptions.Config, "project.json");
            return File.Open(filePath, FileMode.Create, FileAccess.Write);
        }

        /* /catalogs/catalog_id/... */

        public bool AttachmentExists(string catalogId, string attachmentId)
        {
            var physicalId = catalogId.TrimStart('/').Replace("/", "_");
            var attachmentFile = SafePathCombine(Path.Combine(_pathsOptions.Catalogs, physicalId), attachmentId);

            return File.Exists(attachmentFile);
        }

        public IEnumerable<string> EnumerateAttachments(string catalogId)
        {
            var physicalId = catalogId.TrimStart('/').Replace("/", "_");
            var attachmentFolder = SafePathCombine(_pathsOptions.Catalogs, physicalId);

            if (Directory.Exists(attachmentFolder))
                return Directory
                    .EnumerateFiles(attachmentFolder, "*", SearchOption.AllDirectories)
                    .Select(attachmentFilePath => attachmentFilePath[(attachmentFolder.Length + 1)..]);

            else
                return Enumerable.Empty<string>();
        }

        public bool TryReadAttachment(string catalogId, string attachmentId, [NotNullWhen(true)] out Stream? attachment)
        {
            attachment = default;

            var physicalId = catalogId.TrimStart('/').Replace("/", "_");
            var attachmentFolder = Path.Combine(_pathsOptions.Catalogs, physicalId);

            if (Directory.Exists(attachmentFolder))
            {
                var attachmentFile = SafePathCombine(attachmentFolder, attachmentId);

                if (File.Exists(attachmentFile))
                {
                    attachment = File.OpenRead(attachmentFile);
                    return true;
                }
            }

            return false;
        }

        public bool TryReadFirstAttachment(string catalogId, string searchPattern, EnumerationOptions enumerationOptions, [NotNullWhen(true)] out Stream? attachment)
        {
            attachment = default;

            var physicalId = catalogId.TrimStart('/').Replace("/", "_");
            var attachmentFolder = SafePathCombine(_pathsOptions.Catalogs, physicalId);

            if (Directory.Exists(attachmentFolder))
            {
                var attachmentFile = Directory
                    .EnumerateFiles(attachmentFolder, searchPattern, enumerationOptions)
                    .FirstOrDefault();

                if (attachmentFile is not null)
                {
                    attachment = File.OpenRead(attachmentFile);
                    return true;
                }
            }

            return false;
        }

        public Stream WriteAttachment(string catalogId, string attachmentId)
        {
            var physicalId = catalogId.TrimStart('/').Replace("/", "_");
            var attachmentFile = SafePathCombine(Path.Combine(_pathsOptions.Catalogs, physicalId), attachmentId);
            var attachmentFolder = Path.GetDirectoryName(attachmentFile)!;

            Directory.CreateDirectory(attachmentFolder);

            return File.Open(attachmentFile, FileMode.Create, FileAccess.Write);
        }

        public void DeleteAttachment(string catalogId, string attachmentId)
        {
            var physicalId = catalogId.TrimStart('/').Replace("/", "_");
            var attachmentFile = SafePathCombine(Path.Combine(_pathsOptions.Catalogs, physicalId), attachmentId);

            File.Delete(attachmentFile);
        }

        /* /artifact */
        public bool TryReadArtifact(string artifactId, [NotNullWhen(true)] out Stream? artifact)
        {
            artifact = default;

            var attachmentFile = SafePathCombine(_pathsOptions.Artifacts, artifactId);

            if (File.Exists(attachmentFile))
            {
                artifact = File.OpenRead(attachmentFile);
                return true;
            }

            return false;
        }

        public Stream WriteArtifact(string fileName)
        {
            Directory.CreateDirectory(_pathsOptions.Artifacts);

            var filePath = Path.Combine(_pathsOptions.Artifacts, fileName);

            return File.Open(filePath, FileMode.Create, FileAccess.Write);
        }

        /* /cache */
        private string GetCacheEntryDirectoryPath(string catalogId, DateOnly day)
            => Path.Combine(_pathsOptions.Cache, $"{catalogId.TrimStart('/').Replace("/", "_")}/{day:yyyy-MM}/{day:dd}");

        private string GetCacheEntryId(CatalogItem catalogItem, DateTime begin)
        {
            var parametersString = DataModelUtilities.GetRepresentationParameterString(catalogItem.Parameters);
            return $"{GetCacheEntryDirectoryPath(catalogItem.Catalog.Id, DateOnly.FromDateTime(begin))}/{begin:yyyy-MM-ddTHH-mm-ss-fffffff}_{catalogItem.Resource.Id}_{catalogItem.Representation.Id}{parametersString}.cache";
        }

        public bool TryReadCacheEntry(CatalogItem catalogItem, DateTime begin, [NotNullWhen(true)] out Stream? cacheEntry)
        {
            cacheEntry = default;

            var cacheEntryFilePath = GetCacheEntryId(catalogItem, begin);

            try
            {
                cacheEntry = File.Open(cacheEntryFilePath, FileMode.Open, FileAccess.Read, FileShare.None);
                return true;

            }
            catch
            {
                return false;
            }
        }

        public bool TryWriteCacheEntry(CatalogItem catalogItem, DateTime begin, [NotNullWhen(true)] out Stream? cacheEntry)
        {
            cacheEntry = default;

            var cacheEntryFilePath = GetCacheEntryId(catalogItem, begin);
            var cacheEntryDirectoryPath = Path.GetDirectoryName(cacheEntryFilePath);

            if (cacheEntryDirectoryPath is null)
                return false;

            Directory.CreateDirectory(cacheEntryDirectoryPath);

            try
            {
                cacheEntry = File.Open(cacheEntryFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;

            }
            catch
            {
                return false;
            }
        }

        public async Task ClearCacheEntriesAsync(string catalogId, DateOnly day, TimeSpan timeout, Predicate<string> predicate)
        {
            var cacheEntryDirectoryPath = GetCacheEntryDirectoryPath(catalogId, day);

            if (Directory.Exists(cacheEntryDirectoryPath))
            {
                var deleteTasks = new List<Task>();

                foreach (var cacheEntry in Directory.EnumerateFiles(cacheEntryDirectoryPath))
                {
                    /* if file should be deleted */
                    if (predicate(cacheEntry))
                    {
                        /* try direct delete */
                        try
                        {
                            File.Delete(cacheEntry);
                        }

                        /* otherwise try asynchronously for a minute */
                        catch (IOException)
                        {
                            deleteTasks.Add(DeleteCacheEntryAsync(cacheEntry, timeout));
                        }
                    }
                }

                await Task.WhenAll(deleteTasks);
            }
        }

        private static async Task DeleteCacheEntryAsync(string cacheEntry, TimeSpan timeout)
        {
            var end = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < end)
            {
                try
                {
                    File.Delete(cacheEntry);
                    break;
                }
                catch (IOException)
                {
                    // file is still in use
                }

                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            if (File.Exists(cacheEntry))
                throw new Exception($"Cannot delete cache entry {cacheEntry}.");
        }

        /* /users */
        public bool TryReadTokenMap(
            string userId, 
            [NotNullWhen(true)] out string? tokenMap)
        {
            var folderPath = SafePathCombine(_pathsOptions.Users, userId);
            var tokenFilePath = Path.Combine(folderPath, "tokens.json");

            tokenMap = default;

            if (File.Exists(tokenFilePath))
            {
                tokenMap = File.ReadAllText(tokenFilePath);
                return true;
            }

            return false;
        }

        public Stream WriteTokenMap(
            string userId)
        {
            var folderPath = SafePathCombine(_pathsOptions.Users, userId);
            var tokenFilePath = Path.Combine(folderPath, "tokens.json");

            Directory.CreateDirectory(folderPath);

            return File.Open(tokenFilePath, FileMode.Create, FileAccess.Write);
        }

        //
        private static string SafePathCombine(string basePath, string relativePath)
        {
            var filePath = Path.GetFullPath(Path.Combine(basePath, relativePath));

            if (!filePath.StartsWith(basePath))
                throw new Exception("Invalid path.");

            return filePath;
        }
    }
}