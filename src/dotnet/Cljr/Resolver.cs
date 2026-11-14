using System.Globalization;

namespace Cljr;

internal class Resolver
{
    // The environment variable that can be set to override the default location of the Clojure.Main DLL.
    public const string CljrClojureMainDllEnvVar = "CLJR_CLOJURE_MAIN_DLL";

    // The location where the default Clojure.Main location is cached for future use.  This is a relative path from the user's home directory.

    public static readonly string ClojureMainDLLCacheFilename = ".cljr-clojure-main";

    public static string MakeCachePath(string cacheDirectory) => Path.Join(cacheDirectory, ClojureMainDLLCacheFilename);

    // The name of the CLojure.Main executable, without extension.  (We'll need it with both .dll and .exe.)
    // Case-sensiitive.
    public static readonly string ClojureMainFilename = "Clojure.Main";


    // Returns true if we must use the provided environment variable location.
    // Will throw an exception if the environment variable is set but does not point to a valid file.
    private bool UseEnvVarLocation(string? envVarLocn)
    {
        if (string.IsNullOrEmpty(envVarLocn))
        {
            return false;
        }
        else
        {
            string dllFilename = $"{ClojureMainFilename}.dll";
            if (File.Exists(Path.Join(envVarLocn, dllFilename)))
                return true;
            else
                throw new FileNotFoundException($"The directory specified in the environment variable '{CljrClojureMainDllEnvVar}' does not exist or does not contain {dllFilename}: Directory =  {envVarLocn}");
        }
    }

    // Finds the default location of the Clojure.Main DLL, either from the environment variable, the cache, or by locating it in the installed ClojureCLR.
    // If we find it by the third method, we cache the result for future use.
    // The three parameters are passed in to allow for mocking in unit tests.
    public string FindDefaultClojureMainDll(string? envVarLocn, string configDir, IInstalledClojureMainLocator installedClojureMainLocator)
    {
        string dllFilename = $"{ClojureMainFilename}.dll";

        if (UseEnvVarLocation(envVarLocn))
        {
            return envVarLocn!;
        }

        // Look for the cached answer.
        // Check if it is current via the recorded timestamp.

        string cachePath = Path.Join(configDir, ClojureMainDLLCacheFilename);

        if (File.Exists(cachePath))
        {
            string content = File.ReadAllText(cachePath).Trim();
            // Format is <path>\n<timestamp>
            string[] lines = content.Split(['\n'], StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 2)
            {
                string clojureMainPath = lines[0].Trim();
                string timestampStr = lines[1].Trim();
                DateTime timestamp = DateTime.TryParseExact(timestampStr, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedTimestamp) ? parsedTimestamp : DateTime.MinValue;
                string dllPath = Path.Join(clojureMainPath, dllFilename);
                if (File.Exists(dllPath) && File.GetLastWriteTimeUtc(dllPath) == timestamp)
                {
                    return clojureMainPath;
                }
            }
        }

        // No env var override, no valid cache, so look for the default location of the installed Clojure main DLL.
        var installedClojureMainData = installedClojureMainLocator.FindInstalledClojureMainDll(ELocatorMode.Global, Path.GetTempPath());
        if (installedClojureMainData.Exception != null)
        {
            throw installedClojureMainData.Exception;
        }

        CacheDefaultClojureMainLocation(MakeCachePath(configDir), installedClojureMainData);

        return installedClojureMainData.ClojureMainDirectory;
    }

    private static void CacheDefaultClojureMainLocation(string cachePath, InstalledClojureMainData installedClojureMainData)
    {
        // Cache the result for future use.
        string cacheContent = $"{installedClojureMainData.ClojureMainDirectory}\n{installedClojureMainData.Timestamp.ToString("o", CultureInfo.InvariantCulture)}";

        WriteFileViaTemp(cachePath, cacheContent);
    }

    private static void WriteFileViaTemp(string cachePath, string cacheContent)
    {
        string cacheDirectory = Path.GetDirectoryName(cachePath)
            ?? throw new InvalidOperationException("Failed to get directory name for cache path.");

        Directory.CreateDirectory(cacheDirectory);
        string tempCachePath = Path.Combine(
                    cacheDirectory,
                    $".{Path.GetFileName(cachePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempCachePath, cacheContent);
            File.Move(tempCachePath, cachePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempCachePath))
            {
                File.Delete(tempCachePath);
            }
        }
    }


    // The possible sources for the CLojure.Main.dll path
    public enum EStep
    {
        EnvVarOverride,
        VersionCache,
        Restore,
        Default
    }

    public record VersionedClojureMainData(string ClojureMainDirectory, EStep Step);



    // Find Clojure.Main.dll, with an optional requested version.
    // The priority order of resolution is:
    //   1. Environment variable CLJR_CLOJURE_MAIN_DLL
    //   2. Version requested: look up in the version cache, restore if necessary, and return the path
    //   3. No version requested: Default version: look up in the default cache, restore if necessary, and return the path.
    public VersionedClojureMainData FindVersionedClojureMainDll(
        string? envVarLocn,
        string configDir,
        string? requestedVersion,
        IInstalledClojureMainLocator installedClojureMainLocator,
        ICLojureMainVersionDownloader clojureMainVersionDownloader)
    {

        if (UseEnvVarLocation(envVarLocn))
        {
            return new VersionedClojureMainData(envVarLocn!, EStep.EnvVarOverride);
        }

        if (String.IsNullOrWhiteSpace(requestedVersion))
        {
            // No version requested, look for the default location.
            var result = FindDefaultClojureMainDll(envVarLocn, configDir, installedClojureMainLocator);
            return new VersionedClojureMainData(result, EStep.Default);
        }
        else
        {
            return FindRequestedClojureMainDll(configDir, requestedVersion!, installedClojureMainLocator, clojureMainVersionDownloader);
        }
    }

    public readonly string VersionSubdirectory = Path.Join(".cljr", "clojure-main");
    public readonly string DllPathFilename = "dll-path";
    public readonly string ManifestSubpath = Path.Join(".config", "dotnet-tools.json");

    public string VersionDirectory(string configDir, string version) => Path.Join(configDir, VersionSubdirectory, version);

    public string ManifestPath(string configDir, string requestedVersion) => Path.Join(VersionDirectory(configDir, requestedVersion), ManifestSubpath);

    private VersionedClojureMainData FindRequestedClojureMainDll(
        string configDir,
        string requestedVersion,
        IInstalledClojureMainLocator installedClojureMainLocator,
        ICLojureMainVersionDownloader clojureMainVersionDownloader)
    {
        if (TryValidateVersionCache(configDir, requestedVersion, out string dllPath))
        {
            return new VersionedClojureMainData(Path.GetDirectoryName(dllPath)!, EStep.VersionCache);
        }
        else
        {
            dllPath = RestoreRequestedClojureMainDll(configDir, requestedVersion, installedClojureMainLocator, clojureMainVersionDownloader);
            return new VersionedClojureMainData(dllPath, EStep.Restore);
        }
    }

    private bool TryValidateVersionCache(string configDir, string requestedVersion, out string dllPath)
    {
        dllPath = String.Empty;

        string versionDir = VersionDirectory(configDir, requestedVersion);

        string dllPathFile = Path.Join(versionDir, DllPathFilename);

        if (!File.Exists(dllPathFile))
        {
            return false;
        }

        string dllPathFileContent = File.ReadAllText(dllPathFile).Trim();
        string[] lines = dllPathFileContent.Split(['\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length != 3)
        {
            return false;
        }

        string dllDirectory = lines[0].Trim();
        string dllTimestampStr = lines[1].Trim();
        DateTime dllTimestamp = DateTime.TryParseExact(dllTimestampStr, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedTimestamp) ? parsedTimestamp : DateTime.MinValue;
        int runtimeMajorVersion = Int32.TryParse(lines[2].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedRuntimeMajorVersion) ? parsedRuntimeMajorVersion : -1;

        // the entry is stale if
        //   1. the runtime major version is different from the recorded runtime major version
        //   2. the dll is missing
        //   3. the dll timestamp is different from the recorded timestamp

        if (runtimeMajorVersion != Environment.Version.Major)
        {
            return false;
        }

        string dllPathCandidate = Path.Combine(dllDirectory, $"{ClojureMainFilename}.dll");

        if (!File.Exists(dllPathCandidate))
        {
            return false;
        }

        if (File.GetLastWriteTimeUtc(dllPathCandidate) != dllTimestamp)
        {
            return false;
        }

        dllPath = dllPathCandidate;
        return true;
    }

    public class FailedDownloadException(string message, int exitCode, string errorOutput) : Exception(message)
    {
        public int ExitCode { get; } = exitCode;
        public string ErrorOutput { get; } = errorOutput;
    }

    private string RestoreRequestedClojureMainDll(
        string configDir,
        string requestedVersion,
        IInstalledClojureMainLocator installedClojureMainLocator,
        ICLojureMainVersionDownloader clojureMainVersionDownloader)
    {
        WriteClojureMainManifestIfNeeded(configDir, requestedVersion);

        System.Console.Error.WriteLine($"Downloading ClojureCLR {requestedVersion}...");

        DownloaderResult result = clojureMainVersionDownloader.DownloadClojureMainVersion(ManifestPath(configDir, requestedVersion), requestedVersion);
        if (!result.IsSuccess)
        {
            throw new FailedDownloadException($"Failed to download ClojureCLR {requestedVersion}.", result.ExitCode, result.ErrMsg!);
        }

        InstalledClojureMainData result2 = installedClojureMainLocator.FindInstalledClojureMainDll(ELocatorMode.Versioned, VersionDirectory(configDir, requestedVersion));
        if (result2.Exception != null)
        {
            throw result2.Exception;
        }

        // We have been successful in downloading and locating the requested version of ClojureCLR.  Cache the result for future use.

        string versionDir = VersionDirectory(configDir, requestedVersion);

        WriteFileViaTemp(
            Path.Join(versionDir, DllPathFilename),
            $"{result2.ClojureMainDirectory}\n{result2.Timestamp.ToString("o")}\n{Environment.Version.Major}");

        return result2.ClojureMainDirectory;
    }



    private void WriteClojureMainManifestIfNeeded(string configDir, string requestedVersion)
    {
        string manifestPath = ManifestPath(configDir, requestedVersion);

        if (!File.Exists(manifestPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            string manifestContent = $$"""
            {
                "version": 1,
                "isRoot": true,
                "tools": {
                    "{{ClojureMainFilename.ToLower()}}": {
                        "version": "{{requestedVersion}}",
                        "commands": [ "{{ClojureMainFilename}}" ],
                        "rollForward": false
                        }   
                    }
                }
            """;

            WriteFileViaTemp(manifestPath, manifestContent);
        }
    }


}
