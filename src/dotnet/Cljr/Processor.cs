using System.Diagnostics;

namespace Cljr;

// Run the various processes:
//   make-classpath
//   Exec or Tool
//   Main
internal static class Processor
{
    // Here's where I'd really like to have discriminated unions.
    public class ProcessResult
    {
        // An exception occurred while trying to run the process.  The exception is stored here.
        public Exception? Exception { get; init; }

        public int ExitCode { get; init; }

        public bool IsImmediateExit => ExitCode != 0;
    }

    public static ProcessResult MaybeMakeClasspath(EnvironmentManager mgr)
    {

        // If stale, run make-classpath to refresh cached classpath
        if (mgr.IsStaleClasspath && !mgr.CliArgs.HasFlag("describe"))
        {
            if (mgr.CliArgs.HasFlag("verbose"))
                Util.Print("Refreshing classpath");

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
                var toolsArgs = mgr.ComputeToolsArgs();


                using var process = CreateClojureProcess(
                    mgr.InstallDir,
                    mgr.DefaultClojureMainPath.Value,
                    args:
                    [
                        "-m", "clojure.tools.deps.script.make-classpath2",
                        "--install-dir", mgr.InstallDir,
                        "--config-user", mgr.ConfigUser,
                        "--config-project", mgr.ConfigProject,
                        "--basis-file", mgr.BasisFile,
                        "--cp-file", mgr.CpFile,
                        "--jvm-file", mgr.JvmFile,
                        "--main-file", mgr.MainFile,
                        "--manifest-file", mgr.ManifestFile,
                        "--frameworks-file", mgr.FrameworksFile,
                        "--clojure-version-file", mgr.ClojureVersionFile,
                        ..toolsArgs
                    ],
                    env: new()
                    {
                        ["CLOJURE_LOAD_PATH"] = mgr.InstallDir,
                        ["clojure.cli.install_dir"] = mgr.InstallDir
                    }
                );

                //Print($"Classpath: toolsArg =  {string.Join(' ', toolsArgs)}");
                //Print($"Classpath: argList = {string.Join(' ', argList)}");
                //Print($"Classpath: installdir = {installDir}");
                process.Start();
                process.WaitForExit();
                if (process.ExitCode is not 0)
                    return new ProcessResult { ExitCode = process.ExitCode };

                if (!File.Exists(mgr.CpFile))
                    throw new InvalidOperationException("Failed to create classpath file");
            }
            catch (Exception ex)
            {
                return new ProcessResult { ExitCode = 1, Exception = ex };
            }
        }
        return new ProcessResult { ExitCode = 0 };
    }

    internal static string ComputeClojureMainDll(EnvironmentManager mgr, bool checkFloor)
    {
        var versionInfo = mgr.GetClojureMainVersionInfo();

        if (versionInfo is not null)
        {
            var requestedVersion = versionInfo.Version;
            var source = versionInfo.Source;

            string envVar = Environment.GetEnvironmentVariable(Resolver.CljrClojureMainDllEnvVar) ?? string.Empty;

            if (checkFloor && String.IsNullOrEmpty(envVar) && ClojureVersion.IsBelowExecFloor(requestedVersion))
            {
                throw new ArgumentException($"-X/-T need ClojureCLR {ClojureVersion.ExecFloor} or later with this cljr; :clr/clojure-version is {requestedVersion} (from {source}). Use -M or a new version.");
            }

            Resolver resolver = new();

            try
            {
                Resolver.VersionedClojureMainData result = resolver.FindVersionedClojureMainDll(
                    envVar,
                    mgr.ConfigDir,
                    requestedVersion,
                    new InstalledClojureMainLocatorImpl(),
                    new ClojureMainVersionDownloaderImpl());

                if (mgr.IsVerbose)
                {
                    if (result.Step == Resolver.EStep.EnvVarOverride)
                        Util.Print($"CLJR_CLOJURE_MAIN_DLL set; :clr/clojure-version = {requestedVersion} ignored.");

                    Util.Print($"clojure_main = {result.ClojureMainDirectory} (step: {result.Step})");
                }

                return result.ClojureMainDirectory;
            }
            catch (Resolver.FailedDownloadException ex)
            {
                throw new Resolver.FailedDownloadException($"Could not get ClojureCLR {requestedVersion} (:clr/clojure-version from {source}):{Environment.NewLine}{ex.ErrorOutput}", ex.ExitCode, ex.ErrorOutput);
            }
        }
        else
        {
            // No version is requested, use the default.
            return mgr.DefaultClojureMainPath.Value;
        }
    }

    public static ProcessResult RunExecOrToolProcess(EnvironmentManager mgr)
    {
        // & $JavaCmd -XX:-OmitStackTraceInFastThrow @JavaOpts @JvmCacheOpts @JvmOpts "-Dclojure.basis=$BasisFile" -classpath "$CP;$InstallDir/exec.jar" clojure.main -m clojure.run.exec @ClojureArgs

        //Util.Print("Starting exec/tool");

        try
        {
            string clojureMainDll = ComputeClojureMainDll(mgr, checkFloor: true);

            var runtimeConfigPath = mgr.GetRuntimeConfigPath(clojureMainDll);

            using var process = CreateClojureProcess(
                mgr.InstallDir,
                clojureMainDll,
                args:
                [
                    "-m", "clojure.run.exec",
                    ..mgr.CliArgs.CommandArgs,
                ],
                env: new()
                {
                    ["CLOJURE_LOAD_PATH"] =
                        $"{mgr.GetClasspath()}{Path.PathSeparator}{mgr.InstallDir}",
                    ["clojure.basis"] = mgr.BasisFile,
                    ["clojure.cli.install_dir"] = mgr.InstallDir,
                },
                runtimeConfigPath);

            process.Start();
            process.WaitForExit();

            return new ProcessResult { ExitCode = process.ExitCode };
        }
        catch (Resolver.FailedDownloadException ex)
        {
            return new ProcessResult { ExitCode = ex.ExitCode, Exception = ex };
        }
        catch (Exception ex)
        {
            return new ProcessResult { ExitCode = 1, Exception = ex };
        }
    }

    public static ProcessResult RunMain(EnvironmentManager mgr)
    {
        //            if (Test - Path $MainFile) {
        //    # TODO this seems dangerous
        //    $MainCacheOpts = @(Get - Content $MainFile) -replace '"', '\"'
        //            }
        var mainCacheOpts = File.Exists(mgr.MainFile) ? File.ReadAllLines(mgr.MainFile).ToList() : [];

        if (mgr.CliArgs.CommandArgs.Count > 0 && mgr.CliArgs.HasFlag("repl"))
            Util.Warn("WARNING: Implicit use of clojure.main with options is deprecated, use -M");

        // & $JavaCmd - XX:-OmitStackTraceInFastThrow @JavaOpts @JvmCacheOpts @JvmOpts -Dclojure.basis=$BasisFile -classpath $CP clojure.main @MainCacheOpts @ClojureArgs

        //Util.Print("Starting main");

        try
        {
            string clojureMainDll = ComputeClojureMainDll(mgr, checkFloor: false);

            var runtimeConfigPath = mgr.GetRuntimeConfigPath(clojureMainDll);

            using var process = CreateClojureProcess(
                mgr.InstallDir,
                clojureMainDll,
                args:
                [
                    ..mainCacheOpts,
                    ..mgr.CliArgs.CommandArgs,
                ],
                env: new()
                {
                    ["CLOJURE_LOAD_PATH"] = mgr.GetClasspath(),
                    ["clojure.basis"] = mgr.BasisFile,
                },
                runtimeConfigPath);

            process.StartInfo.WorkingDirectory = Environment.CurrentDirectory;

            process.Start();
            process.WaitForExit();
            return new ProcessResult { ExitCode = process.ExitCode };
        }
        catch (Resolver.FailedDownloadException ex)
        {
            return new ProcessResult { ExitCode = ex.ExitCode, Exception = ex };
        }
        catch (Exception ex)
        {
            return new ProcessResult { ExitCode = 1, Exception = ex };
        }
    }

    static Process CreateClojureProcess(
        string installDir,
        string clojurMainDir,
        string[] args,
        Dictionary<string, string> env,
        string? runtimeConfigPath = null)
    {
        Process process = new()
        {
            StartInfo =
            {
                UseShellExecute = false,
                CreateNoWindow = false,
                FileName = "dotnet"
            }
        };

        foreach (var (key, value) in env)
            process.StartInfo.EnvironmentVariables[key] = value;


        process.StartInfo.ArgumentList.Add("exec");
        if (runtimeConfigPath is not null)
        {
            process.StartInfo.ArgumentList.Add("--runtimeconfig");
            process.StartInfo.ArgumentList.Add(runtimeConfigPath);
        }
        process.StartInfo.ArgumentList.Add(Path.Join(clojurMainDir, $"{Resolver.ClojureMainFilename}.dll"));

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        return process;
    }

}
