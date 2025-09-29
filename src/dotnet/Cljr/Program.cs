using Cljr;
using System.Diagnostics;
using System.Security.Cryptography;

var cliArgs = CommandLineParser.Parse(args);

if (cliArgs.IsError)
{
    Warn(cliArgs.ErrorMessage!);
    return 1;
}

if (cliArgs.Mode is EMode.Help)
{
    PrintHelp();
    return 0;
}

if (cliArgs.Mode is EMode.Version)
{
    PrintVersion();
    return 0;
}

if (cliArgs.HasFlag("pom"))
    Warn("We are CLR!  We don't do -Spom");

if (cliArgs.JvmOpts.Count > 0)
    Warn("We are CLR!  -Jjvm_opts aren't going to do you much good.");

var installDir = AppContext.BaseDirectory;
//var toolsCp = Path.Combine(InstallDir, $"clojure-tools-{Version}.jar");  // TODO -- what do we do instead?

// Determine user config directory; if it does not exist, create it
var configEnv = Environment.GetEnvironmentVariable("CLJ_CONFIG");
var xdgEnv = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
var homeConfig = Platform.HomeDir;

var configDir = !string.IsNullOrEmpty(configEnv)
    ? configEnv
    : !string.IsNullOrEmpty(xdgEnv)
        ? Path.Join(xdgEnv, "clojure")
        : Path.Join(homeConfig, ".clojure");

if (!Directory.Exists(configDir))
    Directory.CreateDirectory(configDir);

const string defaultConfigFile = "deps-clr.edn";
string[] depsFiles = ["deps-clr.edn", "deps.edn"];  // order of preference

// Copy in example deps.edn if no deps.edn in the configDir
var candidate = depsFiles
    .Select(f => Path.Join(configDir, f))
    .FirstOrDefault(File.Exists);

var configUser =
    candidate is null
        ? Path.Join(configDir, defaultConfigFile)
        : candidate;

if (candidate is null)
    File.Copy(Path.Join(installDir, "example-deps.edn"), configUser);



var configToolsDir = Path.Join(configDir, "tools");
// Make sure the configDir tools directory exists.
{
    if (!Directory.Exists(configToolsDir))
        Directory.CreateDirectory(configToolsDir);
}

// Make sure the tools.edn file is up-to-date.
{
    var installToolsEdn = Path.Join(installDir, "tools.edn");
    var configToolsEdn = Path.Join(configToolsDir, "tools.edn");
    if (IsNewerFile(installToolsEdn, configToolsEdn))
        File.Copy(installToolsEdn, configToolsEdn, true);
}

// Determine the user cache directory
var userCacheDir = Environment.GetEnvironmentVariable("CLJ_CACHE") ?? Path.Join(configDir, ".cpcache");

// Chain deps.edn in config paths. repro=skip config dir
List<string> configPaths =
[
    Path.Join(installDir, defaultConfigFile),
    defaultConfigFile
];

// I think in this case it does not matter whether we have deps.edn or deps-clr.edn.
// This is used only to determine wither the user config file is being used or not.
// Which actual project file is used is not really relevant.
if (!cliArgs.HasFlag("repro"))
    configPaths.Insert(1, configUser);

// Determine whether to use user or project cache
var configProject = depsFiles.FirstOrDefault(File.Exists);
var cacheDir = configProject is not null ? ".cpcache" : userCacheDir;
configProject ??= defaultConfigFile;

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
    ..configPaths
]);

var cpFile = Path.Join(cacheDir, $"{cacheKeyHash}.cp");
var jvmFile = Path.Join(cacheDir, $"{cacheKeyHash}.jvm");
var mainFile = Path.Join(cacheDir, $"{cacheKeyHash}.main");
var basisFile = Path.Join(cacheDir, $"{cacheKeyHash}.basis");
var manifestFile = Path.Join(cacheDir, $"{cacheKeyHash}.manifest");

if (cliArgs.HasFlag("verbose"))
    Print($"""
           version      = {Platform.Version}
           install_dir  = {installDir}
           config_dir   = {configDir}
           config_paths = {string.Join(' ', configPaths)}
           cache_dir    = {cacheDir}
           cp_file      = {cpFile}
           """);

// check for stale classpath
var stale = false;

if (cliArgs.HasFlag("force") || cliArgs.HasFlag("trace") || cliArgs.HasFlag("tree") ||
    cliArgs.HasFlag("prep") || !File.Exists(cpFile))
    stale = true;
else if (cliArgs.ToolName is not null && IsNewerFile(Path.Join(configToolsDir, $"{cliArgs.ToolName}.edn"), cpFile))
    stale = true;
else if (configPaths.ToList().Exists(p => IsNewerFile(p, cpFile)))
    stale = true;

//  test for manifest?
//if (Test - Path $ManifestFile) {
//  $Manifests = @(Get - Content $ManifestFile)
//  if ($Manifests | Where - Object { !(Test - Path $_) -or(Test - NewerFile $_ $CpFile) }) {
//    $Stale = $TRUE
//  }

// Make tools args if needed
List<string> toolsArgs = [];

if (stale || cliArgs.HasFlag("pom"))
{
    if (cliArgs.Deps is not null)
        toolsArgs.AddRange(["--config-data", cliArgs.Deps]);

    if (cliArgs.TryGetCommandAlias(EMode.Main, out var alias))
        toolsArgs.Add($"-M{alias}");

    if (cliArgs.TryGetCommandAlias(EMode.Repl, out alias))
        toolsArgs.Add($"-A{alias}");

    if (cliArgs.TryGetCommandAlias(EMode.Exec, out alias))
        toolsArgs.Add($"-X{alias}");

    if (cliArgs.Mode == EMode.Tool)
        toolsArgs.Add("--tool-mode");

    if (cliArgs.ToolName is not null)
        toolsArgs.AddRange(["--tool-name", cliArgs.ToolName]);

    if (cliArgs.TryGetCommandAlias(EMode.Tool, out alias))
        toolsArgs.Add($"-T{alias}");

    if (cliArgs.ForceClasspath is not null)
        toolsArgs.Add("--skip-cp");

    if (cliArgs.Threads != 0)
        toolsArgs.AddRange(["--threads", cliArgs.Threads.ToString()]);

    if (cliArgs.HasFlag("trace"))
        toolsArgs.Add("--trace");

    if (cliArgs.HasFlag("tree"))
        toolsArgs.Add("--tree");
}


// If stale, run make-classpath to refresh cached classpath
if (stale && !cliArgs.HasFlag("describe"))
{
    if (cliArgs.HasFlag("verbose"))
        Print("Refreshing classpath");

    // TODO: MAKE PROCESS CALL CORRESPONDING TO:
    //  & $JavaCmd - XX:-OmitStackTraceInFastThrow @CljJvmOpts -classpath $ToolsCp clojure.main -m clojure.tools.deps.script.make-classpath2
    //         --config-user $ConfigUser
    //         --config-project $ConfigProject
    //         --basis-file $BasisFile
    //         --cp-file $CpFile
    //         --jvm-file $JvmFile
    //         --main-file $MainFile
    //         --manifest-file $ManifestFile @ToolsArgs
    //  if ($LastExitCode - ne 0) {
    //      return

    try
    {

        var platformDependentToolArgs = Platform.IsWindows
            ? // incredible hack to get around dealing with what the powershell parser does to args with a : in them
              toolsArgs.Select(arg => $"\"{arg}\"")
            : toolsArgs;

        using var process = CreateClojureProcess(
            installDir,
            args:
            [
                "-m", "clojure.tools.deps.script.make-classpath2",
                "--install-dir", installDir.Replace(@"\", @"\\"),
                "--config-user", configUser,
                "--config-project", configProject,
                "--basis-file", basisFile,
                "--cp-file", cpFile,
                "--jvm-file", jvmFile,
                "--main-file", mainFile,
                "--manifest-file", manifestFile,
                ..platformDependentToolArgs
            ],
            env: new()
            {
                ["CLOJURE_LOAD_PATH"] = installDir,
            }
        );

        //Print($"Classpath: toolsArg =  {string.Join(' ', toolsArgs)}");
        //Print($"Classpath: argList = {string.Join(' ', argList)}");
        //Print($"Classpath: installdir = {installDir}");
        process.Start();
        process.WaitForExit();
        if (process.ExitCode is not 0)
            Environment.Exit(process.ExitCode);

        if (!File.Exists(cpFile))
            throw new InvalidOperationException();
    }
    catch (Exception ex)
    {
        Warn($"Error creating classpath: {ex.Message}");
        return 1;
    }
}

var classpath =
    cliArgs.HasFlag("describe")
        ? string.Empty
        : cliArgs.ForceClasspath ?? File.ReadAllText(cpFile);

if (cliArgs.HasFlag("prep"))
{
    /* already done */
}
else if (cliArgs.HasFlag("pom"))
{
    // TODO -- are we doing this?
    //   & $JavaCmd -XX:-OmitStackTraceInFastThrow @CljJvmOpts -classpath $ToolsCp clojure.main -m clojure.tools.deps.script.generate-manifest2 --config-user $ConfigUser --config-project $ConfigProject --gen=pom @ToolsArgs
}
else if (cliArgs.HasFlag("path"))
{
    Print(classpath);
}
else if (cliArgs.HasFlag("describe"))
{
    var pathVector = string.Join(' ', configPaths.Select(p => p.Replace(@"\", @"\\")));
    Print(
        $$"""
          {:version {{Platform.Version}}
           :config-files [{{pathVector}}]
           :config-user {{configUser.Replace(@"\", @"\\")}}
           :config-project {{configProject.Replace(@"\", @"\\")}}
           :install-dir {{installDir.Replace(@"\", @"\\")}}
           :config-dir {{configDir.Replace(@"\", @"\\")}}
           :cache-dir {{cacheDir.Replace(@"\", @"\\")}}
           :force {{cliArgs.HasFlag("force")}}
           :repro {{cliArgs.HasFlag("repro")}}
           :main - aliases {{cliArgs.GetCommandAlias(EMode.Main)}}
           :repl - aliases {{cliArgs.GetCommandAlias(EMode.Repl)}}
           :exec - aliases {{cliArgs.GetCommandAlias(EMode.Exec)}}
          }
          """
    );
}
else if (cliArgs.HasFlag("tree"))
{
    /* already done */
}
else if (cliArgs.HasFlag("trace"))
{
    Print("Wrote trace.edn");
}
else
{
    //if (Test - Path $JvmFile) {
    //    $JvmCacheOpts = @(Get - Content $JvmFile)

    if (cliArgs.Mode is EMode.Exec or EMode.Tool)
    {
        // & $JavaCmd -XX:-OmitStackTraceInFastThrow @JavaOpts @JvmCacheOpts @JvmOpts "-Dclojure.basis=$BasisFile" -classpath "$CP;$InstallDir/exec.jar" clojure.main -m clojure.run.exec @ClojureArgs

        Print("Starting exec/tool");

        using var process = CreateClojureProcess(
            installDir,
            args:
            [
                "-m", "clojure.run.exec",
                ..cliArgs.CommandArgs,
            ],
            env: new()
            {
                ["CLOJURE_LOAD_PATH"] =
                    $"{classpath}{Path.PathSeparator}{installDir}", // TODO -- what is this? need to get the equivalent of exec.jar on the load path
                ["clojure.basis"] = basisFile,
                ["clojure.cli.install-dir"] = installDir,
            }
        );

        process.Start();
        process.WaitForExit();
        if (process.ExitCode is not 0)
            Environment.Exit(process.ExitCode);
    }
    else
    {
        //            if (Test - Path $MainFile) {
        //    # TODO this seems dangerous
        //    $MainCacheOpts = @(Get - Content $MainFile) -replace '"', '\"'
        //            }
        var mainCacheOpts = File.Exists(mainFile) ? File.ReadAllLines(mainFile).ToList() : [];

        if (cliArgs.CommandArgs.Count > 0 && cliArgs.HasFlag("repl"))
            Warn("WARNING: Implicit use of clojure.main with options is deprecated, use -M");

        // & $JavaCmd - XX:-OmitStackTraceInFastThrow @JavaOpts @JvmCacheOpts @JvmOpts -Dclojure.basis=$BasisFile -classpath $CP clojure.main @MainCacheOpts @ClojureArgs

        //using Process process = new();
        //process.StartInfo.UseShellExecute = false;
        //process.StartInfo.FileName = "powershell.exe";
        //process.StartInfo.CreateNoWindow = false;   // TODO: When done debugging, set to true
        //process.StartInfo.WorkingDirectory = Environment.CurrentDirectory;
        //var env = process.StartInfo.EnvironmentVariables;
        //env["CLOJURE_LOAD_PATH"] = classpath;  // TODO -- what is this?
        //env["clojure.basis"] = basisFile;   // will this do -Dclojure.basis=$BasisFile  ?
        //var argList = process.StartInfo.ArgumentList;
        //argList.Add(Path.Join(installDir, Path.Join("tools", "run-clojure-main.ps1")));
        //argList.Add("-e");
        //argList.Add("\'(println 12)(load \\\"hello\\\")(println (hello/run))\'");
        //process.Start();
        //process.WaitForExit();

        Print("Starting main");

        using var process = CreateClojureProcess(
            installDir,
            args:
            [
                ..mainCacheOpts.Select(arg => arg.Replace(@"""", @"\""")),
                ..cliArgs.CommandArgs,
            ],
            env: new()
            {
                ["CLOJURE_LOAD_PATH"] = classpath, // TODO -- what is this?
                ["clojure.basis"] = basisFile, // will this do -Dclojure.basis=$BasisFile  ?
            }
        );

        process.StartInfo.WorkingDirectory = Environment.CurrentDirectory;
        process.Start();
        process.WaitForExit();
        if (process.ExitCode is not 0)
            Environment.Exit(process.ExitCode);
    }
}

return 0;

static void PrintHelp() => Print(
    $$$"""
       Version: {{{Platform.Version}}}

       You use the Clojure tools('clj' or 'clojure') to run Clojure programs
       on the JVM, e.g.to start a REPL or invoke a specific function with data.
       The Clojure tools will configure the JVM process by defining a classpath
       (of desired libraries), an execution environment(JVM options) and
       specifying a main class and args.

       Using a deps.edn file (or files), you tell Clojure where your source code
       resides and what libraries you need.Clojure will then calculate the full
       set of required libraries and a classpath, caching expensive parts of this
       process for better performance.

       Note: For projects intended for both Clojure and ClojureCLR, it might be 
       necessary to supply a different deps.edn file for each platform.  The 
       ClojureCLR version of the file should be named deps-clr.edn.  When running 
       ClojureCLR, the deps-clr.edn in preference to deps.edn should both exist.

       The internal steps of the Clojure tools, as well as the Clojure functions
       you intend to run, are parameterized by data structures, often maps.Shell
       command lines are not optimized for passing nested data, so instead you
       will put the data structures in your deps.edn file and refer to them on the
       command line via 'aliases' - keywords that name data structures.

       'clj' and 'clojure' differ in that 'clj' has extra support for use as a REPL
       in a terminal, and should be preferred unless you don't want that support,
       then use 'clojure'.

       Usage:
         Start a REPL clj     clj-opt* [-Aaliases]
         Exec fn(s) clojure [clj-opt*] -X[aliases][a / fn *][kpath v]*
         Run main      clojure[clj - opt *] -M[aliases][init - opt *][main - opt][arg *]
         Run tool clojure [clj-opt*] -T[name | aliases] a/fn[kpath v] kv-map?
         Prepare       clojure[clj - opt *] -P[other exec opts]

       exec-opts:
        -Aaliases Use concatenated aliases to modify classpath
        -X[aliases] Use concatenated aliases to modify classpath or supply exec fn/args
        -M[aliases] Use concatenated aliases to modify classpath or supply main opts
        -P Prepare deps - download libs, cache classpath, but don't exec

       clj-opts:
        -Jopt Pass opt through in java_opts, ex: -J-Xmx512m
        -Sdeps EDN     Deps data to use as the last deps file to be merged
        -Spath Compute classpath and echo to stdout only
        -Stree Print dependency tree
        -Scp CP        Do NOT compute or cache classpath, use this one instead
        -Srepro Ignore the ~/.clojure/deps.edn config file
        -Sforce Force recomputation of the classpath(don't use the cache)
        -Sverbose Print important path info to console
        -Sdescribe Print environment and command parsing info as data
        -Sthreads Set specific number of download threads
        -Strace Write a trace.edn file that traces deps expansion
        --             Stop parsing dep options and pass remaining arguments to clojure.main
        --version Print the version to stdout and exit
        -version Print the version to stdout and exit

       The following non-standard options are available only in deps.clj:
       
        -Sdeps-file Use this file instead of deps.edn
        -Scommand A custom command that will be invoked. Substitutions: { { classpath} }, {{main-opts
       }}.

       init - opt:
        -i, --init path Load a file or resource
        -e, --eval string   Eval exprs in string; print non-nil values
        --report target     Report uncaught exception to ""file"" (default), ""stderr"", or ""none""

       main-opt:
        -m, --main ns - name  Call the -main function from namespace w/args
        -r, --repl Run a repl
        path Run a script from a file or resource
        -                   Run a script from standard input
        -h, -?, --help Print this help message and exit

       Programs provided by :deps alias:
        -X:deps mvn-pom Generate (or update) pom.xml with deps and paths
        -X:deps list              List full transitive deps set and licenses
        -X:deps tree              Print deps tree
        -X:deps find-versions Find available versions of a library
        -X:deps prep              Prepare all unprepped libs in the dep tree
        -X:deps mvn-install Install a maven jar to the local repository cache
        -X:deps git-resolve-tags Resolve git coord tags to shas and update deps.edn

       For more info, see:
        https://clojure.org/guides/deps_and_cli
        https://clojure.org/reference/repl_and_main
       """
);

static void PrintVersion() => Print($"ClojureCLR CLI Version: {Platform.Version}");
static void Warn(string message) => Console.Error.WriteLine(message);
static void Print(string message) => Console.WriteLine(message);

//static void EndExecution(int exitCode, string message)
//{
//    Warn(message);
//    EndExecution(exitCode);
//}

//static void EndExecution(int exitCode) => Environment.Exit(exitCode);

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

static Process CreateClojureProcess(string installDir, string[] args, Dictionary<string, string> env)
{
    Process process = new()
    {
        StartInfo =
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            FileName = Platform.IsWindows ? "powershell.exe" : "bash",
        }
    };

    foreach (var (key, value) in env)
        process.StartInfo.EnvironmentVariables[key] = value;

    List<string> prependArgs = [];

    if (Platform.IsWindows)
        prependArgs.AddRange([
            "-NoProfile",
            "-ExecutionPolicy", "ByPass",
            "-File", Path.Join(installDir, "tools", "run-clojure-main.ps1")
        ]);
    else
        prependArgs.Add(Path.Join(installDir, "tools", "run-clojure-main.sh"));

    if (Platform.IsWindows)
    {
        foreach (var arg in prependArgs.Concat(args))
            process.StartInfo.ArgumentList.Add(arg.Contains(' ') ? $"\"{arg}\"" : arg);
    }
    else
    {
        foreach (var arg in prependArgs.Concat(args))
            process.StartInfo.ArgumentList.Add(arg);
    }

    return process;

}
