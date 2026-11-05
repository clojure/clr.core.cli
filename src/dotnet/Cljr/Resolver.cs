using System.Globalization;

namespace Cljr;

internal class Resolver
{
    // The environment variable that can be set to override the default location of the Clojure.Main DLL.
    public const string CljrClojureMainDllEnvVar = "CLJR_CLOJURE_MAIN_DLL";

    // The location where the default Clojure.Main location is cached for future use.  This is a relative path from the user's home directory.

    public static readonly string ClojureMainDLLCacheDirectory = Path.Join(Platform.HomeDir, ".clojure");
    public static readonly string ClojureMainDLLCacheFilename = ".cljr-clojure-main";
    public static readonly string ClojureMainDLLCachePath = Path.Join(ClojureMainDLLCacheDirectory, ClojureMainDLLCacheFilename);

    // The name of the CLojure.Main executable, without extension.  (We'll need it with both .dll and .exe.)
    // Case-sensiitive.
    public static readonly string ClojureMainFilename = "Clojure.Main";

    // Finds the default location of the Clojure.Main DLL, either from the environment variable, the cache, or by locating it in the installed ClojureCLR.
    // If we find it by the third method, we cache the result for future use.
    // The three parameters are passed in to allow for mocking in unit tests.
    public string FindDefaultClojureMainDll(string? envVarLocn, string cacheDirectory, IInstalledClojureMainLocator installedClojureMainLocator)
    {
        string dllFilename = $"{ClojureMainFilename}.dll";
        string cachePath = Path.Join(cacheDirectory, ClojureMainDLLCacheFilename);

        // Check if the environment variable is set and points to a valid file.
        // This is useful for testing or if the user has a custom location for the main.dll.
        if (!string.IsNullOrEmpty(envVarLocn))
        {
            if (File.Exists(Path.Join(envVarLocn, dllFilename)))
                return envVarLocn;
            else
                throw new FileNotFoundException($"The directory specified in the environment variable '{CljrClojureMainDllEnvVar}' does not exist or does not contain {dllFilename}: Directory =  {envVarLocn}");
        }

        // Look for the cached answer.
        // Check if it is current via the recorded timestamp.

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
        var installedClojureMainData = installedClojureMainLocator.FindInstalledClojureMainDll();
        if (installedClojureMainData.Exception != null)
        {
            throw installedClojureMainData.Exception;
        }

        CacheDefaultClojureMainLocation(ClojureMainDLLCachePath, installedClojureMainData);

        return installedClojureMainData.ClojureMainDirectory;
    }

    private static void CacheDefaultClojureMainLocation(string cachePath, InstalledClojureMainData installedClojureMainData)
    {
        // Cache the result for future use.
        string cacheContent = $"{installedClojureMainData.ClojureMainDirectory}\n{installedClojureMainData.Timestamp.ToString("o", CultureInfo.InvariantCulture)}";

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


}
