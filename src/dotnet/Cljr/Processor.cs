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

                var platformDependentToolArgs = Platform.IsWindows
                    ? // incredible hack to get around dealing with what the powershell parser does to args with a : in them
                      toolsArgs.Select(arg => $"\"{arg}\"")
                    : toolsArgs;

                using var process = CreateClojureProcess(
                    mgr.InstallDir,
                    args:
                    [
                        "-m", "clojure.tools.deps.script.make-classpath2",
                "--install-dir", mgr.InstallDir.Replace(@"\", @"\\"),
                "--config-user", mgr.ConfigUser,
                "--config-project", mgr.ConfigProject,
                "--basis-file", mgr.BasisFile,
                "--cp-file", mgr.CpFile,
                "--jvm-file", mgr.JvmFile,
                "--main-file", mgr.MainFile,
                "--manifest-file", mgr.ManifestFile,
                ..platformDependentToolArgs
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

    public static ProcessResult RunExecOrToolProcess(EnvironmentManager mgr)
    {
        // & $JavaCmd -XX:-OmitStackTraceInFastThrow @JavaOpts @JvmCacheOpts @JvmOpts "-Dclojure.basis=$BasisFile" -classpath "$CP;$InstallDir/exec.jar" clojure.main -m clojure.run.exec @ClojureArgs

        Util.Print("Starting exec/tool");

        using var process = CreateClojureProcess(
            mgr.InstallDir,
            args:
            [
                "-m", "clojure.run.exec",
                ..mgr.CliArgs.CommandArgs,
            ],
            env: new()
            {
                ["CLOJURE_LOAD_PATH"] =
                    $"{mgr.GetClasspath()}{Path.PathSeparator}{mgr.InstallDir}", // TODO -- what is this? need to get the equivalent of exec.jar on the load path
                ["clojure.basis"] = mgr.BasisFile,
                ["clojure.cli.install_dir"] = mgr.InstallDir,
            }
        );

        process.Start();
        process.WaitForExit();

        return new ProcessResult { ExitCode = process.ExitCode };
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

        Util.Print("Starting main");

        using var process = CreateClojureProcess(
            mgr.InstallDir,
            args:
            [
                ..mainCacheOpts.Select(arg => arg.Replace(@"""", @"\""")),
                ..mgr.CliArgs.CommandArgs,
            ],
            env: new()
            {
                ["CLOJURE_LOAD_PATH"] = mgr.GetClasspath(), // TODO -- what is this?
                ["clojure.basis"] = mgr.BasisFile, // will this do -Dclojure.basis=$BasisFile  ?
            }
        );

        process.StartInfo.WorkingDirectory = Environment.CurrentDirectory;
        process.Start();
        process.WaitForExit();
        return new ProcessResult { ExitCode = process.ExitCode };
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

}
