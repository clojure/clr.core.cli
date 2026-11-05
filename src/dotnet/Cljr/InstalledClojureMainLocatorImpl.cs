using System.ComponentModel;
using System.Diagnostics;

namespace Cljr;

internal class InstalledClojureMainLocatorImpl : IInstalledClojureMainLocator
{



    public static readonly List<string> StandardArgs =
        [
            "-e",
            "(print System.AppContext/BaseDirectory)",
        ];


    public InstalledClojureMainData FindInstalledClojureMainDll()
    {
        return FindInstalledClojureMainDllAsync().GetAwaiter().GetResult();
    }


    private static async Task<InstalledClojureMainData> FindInstalledClojureMainDllAsync()
    {
        Process process = new()
        {
            StartInfo =
                {
                    FileName = Platform.IsWindows ? $"{Resolver.ClojureMainFilename}.exe" : Resolver.ClojureMainFilename,  //  Windows requires the .exe suffix, while Linux does not.
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true, // doesn't really matter, it will not expect any input
                    WorkingDirectory = Path.GetTempPath() // don't use the current directory, so clojure.main won't find a user.clj
                }
        };

        foreach (var arg in StandardArgs)
            process.StartInfo.ArgumentList.Add(arg);

        process.StartInfo.EnvironmentVariables["CLOJURE_LOAD_PATH"] = string.Empty; // let's not find things we don't want to find.


        try
        {
            process.Start();

            // Start reading immediately after Start()
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            string stdout = await stdoutTask;
            string stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                return new InstalledClojureMainData
                {
                    Exception = new Exception($"Failed to start {Resolver.ClojureMainFilename}. Ensure that ClojureCLR is installed and that {Resolver.ClojureMainFilename} is in your PATH. Exit code: {process.ExitCode}. Error output: {stderr}")
                };
            }

            string baseDir = stdout.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? string.Empty;

            string clojureMainPath = Path.Combine(baseDir, $"{Resolver.ClojureMainFilename}.dll");
            FileInfo fi = new(clojureMainPath);
            if (!fi.Exists)
            {
                return new InstalledClojureMainData
                {
                    Exception = new FileNotFoundException($"The Clojure.Main executable was not found at the expected location: {clojureMainPath}.\nEnsure that ClojureCLR is installed and that {Resolver.ClojureMainFilename} is in your PATH.\nInstall via 'dotnet tool install -g clojure.main'.")
                };
            }

            return new InstalledClojureMainData
            {
                ClojureMainDirectory = baseDir,
                Timestamp = fi.LastWriteTimeUtc
            };

        }
        catch (Win32Exception ex)
        {
            return new InstalledClojureMainData
            {
                Exception = new Exception($"Failed to start {Resolver.ClojureMainFilename}.\nEnsure that ClojureCLR is installed and that {Resolver.ClojureMainFilename} is in your PATH.\nInstall via 'dotnet tool install -g clojure.main'.", ex)
            };
        }

    }

}
