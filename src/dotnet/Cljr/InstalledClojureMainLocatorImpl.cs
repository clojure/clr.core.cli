using System.ComponentModel;
using System.Diagnostics;

namespace Cljr;

internal class InstalledClojureMainLocatorImpl : IInstalledClojureMainLocator
{
    // The entry point, from IInstalledClojureMainLocator.
    // Find the location of the required Clojure.Main DLL, by starting the Clojure.Main executable and asking it for its base directory.
    // The required Clojure.Main is computed by Resolver.
    public InstalledClojureMainData FindInstalledClojureMainDll(ELocatorMode mode, string workingDirectory)
    {
        return FindInstalledClojureMainDllAsync(mode, workingDirectory).GetAwaiter().GetResult();
    }


    // The arguments to pass to the Clojure.Main executable to get it to print its base directory.
    private static readonly List<string> CommonArgs =
        [
            "-e",
            "(print System.AppContext/BaseDirectory)",
        ];

    private static readonly List<string> VersionModeArgs =
        [
            "tool",
            "run",
            Resolver.ClojureMainFilename,
        ];

    private static async Task<InstalledClojureMainData> FindInstalledClojureMainDllAsync(ELocatorMode mode, string workingDirectory)
    {
        string filename;
        List<string> args = [];

        switch (mode)
        {
            case ELocatorMode.Global:
                filename = Platform.IsWindows ? $"{Resolver.ClojureMainFilename}.exe" : Resolver.ClojureMainFilename;  //  Windows requires the .exe suffix, while Linux does not.
                break;
            case ELocatorMode.Versioned:
                filename = "dotnet";
                args.AddRange(VersionModeArgs);
                break;
            default:
                throw new ArgumentException($"Unknown locator mode: {mode}");
        }

        args.AddRange(CommonArgs);

        Process process = new()
        {
            StartInfo =
                {
                    FileName = filename,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true, // doesn't really matter, it will not expect any input
                    WorkingDirectory = workingDirectory
                }
        };

        foreach (var arg in args)
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
                string message = mode switch
                {
                    ELocatorMode.Versioned => $"Failed to start the requested version of {Resolver.ClojureMainFilename}. Error output: {stderr}",
                    _ => $"Failed to start {Resolver.ClojureMainFilename}. Ensure that ClojureCLR is installed and that {Resolver.ClojureMainFilename} is in your PATH. Exit code: {process.ExitCode}. Error output: {stderr}"
                };

                return new InstalledClojureMainData
                {
                    Exception = new InvalidOperationException(message)
                };
            }

            string baseDir = stdout.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? string.Empty;

            string clojureMainPath = Path.Combine(baseDir, $"{Resolver.ClojureMainFilename}.dll");
            FileInfo fi = new(clojureMainPath);
            if (!fi.Exists)
            {
                string message = mode switch
                {
                    ELocatorMode.Versioned => $"The downloaded version of Clojure.Main executable was not found at the expected location: {clojureMainPath}.",
                    _ => $"The Clojure.Main executable was not found at the expected location: {clojureMainPath}. Ensure that ClojureCLR is installed and that {Resolver.ClojureMainFilename} is in your PATH. Install via 'dotnet tool install -g clojure.main'."
                };
                return new InstalledClojureMainData
                {
                    Exception = new FileNotFoundException(message)
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
            string message = mode switch
            {
                ELocatorMode.Versioned => $"Failed to start 'dotnet tool run'.",
                _ => $"Failed to start {Resolver.ClojureMainFilename}. Ensure that ClojureCLR is installed and that {Resolver.ClojureMainFilename} is in your PATH. Install via 'dotnet tool install -g clojure.main'."
            };
            return new InstalledClojureMainData
            {
                Exception = new Exception(message, ex)
            };
        }

    }

}
