using System.Security.Cryptography;

namespace Cljr;

// Processes the command line arguments and environment variables, and sets up the environment for the Clojure CLR runtime.
internal class EnvironmentManager
{
    const string CljConfigEnvVar = "CLJ_CONFIG";
    const string XdgConfigEnvVar = "XDG_CONFIG_HOME";
    const string CljCacheEnvVar = "CLJ_CACHE";
    const string DefaultConfigFilename = "deps-clr.edn";
    const string StandardConfigFilename = "deps.edn";

    public ParseItems CliArgs { get; init; }

    public string InstallDir { get; } = AppContext.BaseDirectory;
    public string ConfigDir { get; private set; } = string.Empty;
    public string ConfigToolsDir { get; private set; } = string.Empty;
    public string CacheDir { get; private set; } = string.Empty;

    public string ConfigUser { get; private set; } = string.Empty;

    public string ConfigProject { get; private set; } = string.Empty;

    public List<string> ConfigPaths { get; } = [];

    public bool IsStaleClasspath { get; private set; } = false;


    public string CpFile { get; private set; } = string.Empty;
    public string JvmFile { get; private set; } = string.Empty;
    public string MainFile { get; private set; } = string.Empty;
    public string FrameworksFile { get; private set; } = string.Empty;
    public string RuntimeConfigFile { get; private set; } = string.Empty;
    public string RuntimeConfigSourceFile { get; private set; } = string.Empty;
    public string BasisFile { get; private set; } = string.Empty;
    public string ManifestFile { get; private set; } = string.Empty;

    public Lazy<string> DefaultClojureMainPath { get; }

    public EnvironmentManager(ParseItems cliArgs)
    {
        CliArgs = cliArgs;
        ParseAndConfigureEnvironment(cliArgs);

        DefaultClojureMainPath = new Lazy<string>(InitializeDefaultClojureMainPath);
    }

    private string InitializeDefaultClojureMainPath()
    {
        var resolver = new Resolver();
        var envVarLocn = Environment.GetEnvironmentVariable(Resolver.CljrClojureMainDllEnvVar);
        var installedClojureMainLocator = new InstalledClojureMainLocatorImpl();

        return resolver.FindDefaultClojureMainDll(
            envVarLocn,
            ConfigDir,
            installedClojureMainLocator);
    }

    public void ParseAndConfigureEnvironment(ParseItems cliArgs)
    {
        //var toolsCp = Path.Combine(InstallDir, $"clojure-tools-{Version}.jar");  // TODO -- what do we do instead?

        // Determine user config directory; if it does not exist, create it

        ConfigDir = ComputeConfigDir();

        if (!Directory.Exists(ConfigDir))
            Directory.CreateDirectory(ConfigDir);

        // Copy in example deps.edn if no deps.edn in the configDir

        string[] depsFiles = [DefaultConfigFilename, StandardConfigFilename];  // order of preference

        var candidate = depsFiles
            .Select(f => Path.Join(ConfigDir, f))
            .FirstOrDefault(File.Exists);

        ConfigUser = candidate ?? Path.Join(ConfigDir, DefaultConfigFilename);

        if (candidate is null)
            File.Copy(Path.Join(InstallDir, "example-deps.edn"), ConfigUser);


        // Determine the tools directory
        ConfigToolsDir = Path.Join(ConfigDir, "tools");
        // Make sure the ConfigDir tools directory exists.
        {
            if (!Directory.Exists(ConfigToolsDir))
                Directory.CreateDirectory(ConfigToolsDir);
        }

        // Make sure the tools.edn file is up-to-date.
        {
            var installToolsEdn = Path.Join(InstallDir, "tools.edn");
            var configToolsEdn = Path.Join(ConfigToolsDir, "tools.edn");
            if (IsNewerFile(installToolsEdn, configToolsEdn))
                File.Copy(installToolsEdn, configToolsEdn, true);
        }


        // Chain deps.edn in config paths. repro=skip config dir

        ConfigPaths.Add(Path.Join(InstallDir, DefaultConfigFilename));

        if (!cliArgs.HasFlag("repro"))
            ConfigPaths.Add(ConfigUser);

        // Determine the user cache directory
        var userCacheDir = Environment.GetEnvironmentVariable(CljCacheEnvVar) ?? Path.Join(ConfigDir, ".cpcache");

        // Determine whether to use user or project cache

        var depFile = depsFiles.FirstOrDefault(File.Exists);
        CacheDir = depFile is not null ? ".cpcache" : userCacheDir;
        ConfigProject = depFile ?? DefaultConfigFilename;

        ConfigPaths.Add(ConfigProject);

        // Construct location of cached classpath file
        const string cacheVersion = "CLR4";

        var cacheKeyHash = GetStringHash([
            cacheVersion,
            cliArgs.GetCommandAlias(EMode.Repl),
            cliArgs.GetCommandAlias(EMode.Exec),
            cliArgs.GetCommandAlias(EMode.Main),
            cliArgs.GetCommandAlias(EMode.Tool),
            cliArgs.Deps ?? string.Empty,
            cliArgs.ToolName ?? string.Empty,
            ..ConfigPaths
        ]);

        CpFile = Path.Join(CacheDir, $"{cacheKeyHash}.cp");
        JvmFile = Path.Join(CacheDir, $"{cacheKeyHash}.jvm");
        MainFile = Path.Join(CacheDir, $"{cacheKeyHash}.main");
        BasisFile = Path.Join(CacheDir, $"{cacheKeyHash}.basis");
        ManifestFile = Path.Join(CacheDir, $"{cacheKeyHash}.manifest");
        FrameworksFile = Path.Join(CacheDir, $"{cacheKeyHash}.frameworks");
        RuntimeConfigFile = Path.Join(CacheDir, $"{cacheKeyHash}.runtimeconfig.json");
        RuntimeConfigSourceFile = Path.Join(CacheDir, $"{cacheKeyHash}.runtimeconfig.source");

        // Compute if classpath is stale.

        IsStaleClasspath = false;

        if (cliArgs.HasFlag("force")
            || cliArgs.HasFlag("trace")
            || cliArgs.HasFlag("tree")
            || cliArgs.HasFlag("prep")
            || !File.Exists(CpFile))
            IsStaleClasspath = true;
        else if (cliArgs.ToolName is not null
                   && IsNewerFile(Path.Join(ConfigToolsDir, $"{cliArgs.ToolName}.edn"), CpFile))
            IsStaleClasspath = true;
        else if (ConfigPaths.ToList().Exists(p => IsNewerFile(p, CpFile)))
            IsStaleClasspath = true;

    }

    // Determine user config directory; 
    static string ComputeConfigDir()
    {
        // Priority to CLJ_CONFIG environment variable
        var configEnv = Environment.GetEnvironmentVariable(CljConfigEnvVar);

        if (!string.IsNullOrEmpty(configEnv))
            return configEnv;

        // Next priority to XDG_CONFIG_HOME environment variable
        var xdgEnv = Environment.GetEnvironmentVariable(XdgConfigEnvVar);
        if (!string.IsNullOrEmpty(xdgEnv))
            return Path.Join(xdgEnv, "clojure");

        // Default to home directory + ".clojure"
        var homeConfig = Platform.HomeDir;
        return Path.Join(homeConfig, ".clojure");
    }

    public string? GetRuntimeConfigPath()
    {
        return GetRuntimeConfigPath(FrameworksFile, RuntimeConfigFile, RuntimeConfigSourceFile, DefaultClojureMainPath.Value);
    }

    public static string? GetRuntimeConfigPath(string frameworksFile, string runtimeConfigFile, string runtimeConfigSourceFile, string clojureMainDirectory)
    {
        List<string> frameworks = EnvironmentManager.GetFrameworks(frameworksFile);

        if (frameworks.Count == 0)
        {
            // No frameworks specified (no file, or file with no content), so we don't generate a runtimeconfig.json file.
            return null;
        }

        string sourceConfigPath = Path.Combine(clojureMainDirectory, $"{Resolver.ClojureMainFilename}.runtimeconfig.json");
        if (!File.Exists(sourceConfigPath))
            throw new FileNotFoundException($"The runtimeconfig.json file was not found in the Clojure.Main.dll directory: {sourceConfigPath}");

        string recordedSourcePath = File.Exists(runtimeConfigSourceFile) ? File.ReadAllText(runtimeConfigSourceFile).Trim() : string.Empty;

        // Check for staleness.
        // The three conditions for staleness are:
        //   <hash>.frameworks (= FrameworksFile) is newer than <hash>.runtimeconfig.json (= RuntimeConfigFile)
        //   runtimeconfig.json (= sourceConfigPath) is newer than <hash>.runtimeconfig.json (= RuntimeConfigFile)  (Clojure.Main was upgraded in place)
        //   The recorded source path is different from the current sourceConfigPath  (e.g., the user switched CLJR_CLOJURE_MAIN_DLL environment variable to point to a different Clojure.Main.dll)

        if (!IsNewerFile(frameworksFile, runtimeConfigFile) && !IsNewerFile(sourceConfigPath, runtimeConfigFile) && recordedSourcePath == sourceConfigPath)
        {
            // Not stale, so we don't need to regenerate the runtimeconfig.json file.
            return runtimeConfigFile;
        }

        // the <hash>.runtimeconfig.json file is stale, so we need to regenerate it.

        string newJson = RuntimeConfigGenerator.Generate(File.ReadAllText(sourceConfigPath), frameworks);

        File.WriteAllText(runtimeConfigFile, newJson);
        File.WriteAllText(runtimeConfigSourceFile, sourceConfigPath);

        return runtimeConfigFile;
    }

    public string GetVerboseMessage()
    {
        string clojureMainPath;
        try
        {
            clojureMainPath = Path.Join(DefaultClojureMainPath.Value, $"{Resolver.ClojureMainFilename}.dll");
        }
        catch (Exception ex)
        {
            clojureMainPath = $"Error finding Clojure.Main: {ex.Message}";
        }

        List<string> frameworks = GetFrameworks(FrameworksFile);

        string frameworksText = frameworks.Count > 0
            ? $"""{Environment.NewLine}frameworks   = {string.Join(',', frameworks)}{Environment.NewLine}runtimeconf  = {RuntimeConfigFile}"""
            : string.Empty;

        return $"""
           version      = {Platform.Version}
           install_dir  = {InstallDir}
           config_dir   = {ConfigDir}
           config_paths = {string.Join(' ', ConfigPaths)}
           cache_dir    = {CacheDir}
           cp_file      = {CpFile}
           clojure_main = {clojureMainPath}{frameworksText}
           """;
    }

    private static List<string> GetFrameworks(string frameworksFile)
    {
        return File.Exists(frameworksFile)
            ? [.. File.ReadAllLines(frameworksFile).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim())]
            : [];
    }

    public string GetDescribeMessage()
    {
        var pathVector = string.Join(' ', ConfigPaths.Select(p => p.Replace(@"\", @"\\")));
        List<string> frameworks = GetFrameworks(FrameworksFile);

        return $$"""
          {:version {{Platform.Version}}
           :config-files [{{pathVector}}]
           :config-user {{ConfigUser.Replace(@"\", @"\\")}}
           :config-project {{ConfigProject.Replace(@"\", @"\\")}}
           :install-dir {{InstallDir.Replace(@"\", @"\\")}}
           :config-dir {{ConfigDir.Replace(@"\", @"\\")}}
           :cache-dir {{CacheDir.Replace(@"\", @"\\")}}
           :clr-frameworks [{{string.Join(' ', frameworks.Select(f => f.Replace(@"\", @"\\")))}}]
           :force {{CliArgs.HasFlag("force")}}
           :repro {{CliArgs.HasFlag("repro")}}
           :main - aliases {{CliArgs.GetCommandAlias(EMode.Main)}}
           :repl - aliases {{CliArgs.GetCommandAlias(EMode.Repl)}}
           :exec - aliases {{CliArgs.GetCommandAlias(EMode.Exec)}}
          }
          """;
    }

    public List<string> ComputeToolsArgs()
    {
        List<string> toolsArgs = [];

        if (IsStaleClasspath || CliArgs.HasFlag("pom"))
        {
            if (CliArgs.Deps is not null)
                toolsArgs.AddRange(["--config-data", CliArgs.Deps]);

            if (CliArgs.TryGetCommandAlias(EMode.Main, out var alias))
                toolsArgs.Add($"-M{alias}");

            if (CliArgs.TryGetCommandAlias(EMode.Repl, out alias))
                toolsArgs.Add($"-A{alias}");

            if (CliArgs.TryGetCommandAlias(EMode.Exec, out alias))
                toolsArgs.Add($"-X{alias}");

            if (CliArgs.Mode == EMode.Tool)
                toolsArgs.Add("--tool-mode");

            if (CliArgs.ToolName is not null)
                toolsArgs.AddRange(["--tool-name", CliArgs.ToolName]);

            if (CliArgs.TryGetCommandAlias(EMode.Tool, out alias))
                toolsArgs.Add($"-T{alias}");

            if (CliArgs.ForceClasspath is not null)
                toolsArgs.Add("--skip-cp");

            if (CliArgs.Threads != 0)
                toolsArgs.AddRange(["--threads", CliArgs.Threads.ToString()]);

            if (CliArgs.HasFlag("trace"))
                toolsArgs.Add("--trace");

            if (CliArgs.HasFlag("tree"))
                toolsArgs.Add("--tree");
        }

        return toolsArgs;
    }

    static bool IsNewerFile(string filename1, string filename2)
    {
        if (!File.Exists(filename1)) return false;
        if (!File.Exists(filename2)) return true;
        var mod1 = new FileInfo(filename1).LastWriteTimeUtc;
        var mod2 = new FileInfo(filename2).LastWriteTimeUtc;
        return mod1 > mod2;
    }

    static string GetStringHash(params string[] s)
    {
        var cacheKey = string.Join('|', s);
        var hash = MD5.HashData(System.Text.Encoding.UTF8.GetBytes(cacheKey));
        return BitConverter.ToString(hash).Replace("-", "");
    }

    public string GetClasspath()
    {
        return CliArgs.HasFlag("describe")
        ? string.Empty
        : CliArgs.ForceClasspath ?? File.ReadAllText(CpFile);
    }

}
