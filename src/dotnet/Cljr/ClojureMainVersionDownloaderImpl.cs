using System.Diagnostics;

namespace Cljr;

internal class ClojureMainVersionDownloaderImpl : ICLojureMainVersionDownloader
{
    public DownloaderResult DownloadClojureMainVersion(
        string manifestPath,
        string requestedVersion)
    {
        return DownloadClojureMainVersionAsync(manifestPath, requestedVersion).GetAwaiter().GetResult();
    }

    private async Task<DownloaderResult> DownloadClojureMainVersionAsync(
        string manifestPath,
        string requestedVersion)
    {
        Process process = new()
        {
            StartInfo =
                {
                    FileName = "dotnet",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true, // doesn't really matter, it will not expect any input
                    WorkingDirectory = Environment.CurrentDirectory // the project directory
                }
        };

        var argList = process.StartInfo.ArgumentList;
        argList.Add("tool");
        argList.Add("restore");
        argList.Add("--tool-manifest");
        argList.Add(manifestPath);


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
                return new DownloaderResult(false, process.ExitCode, stderr);
            }

            return new DownloaderResult(true, 0, null);

        }
        catch (Exception ex)
        {
            return new DownloaderResult(false, -1, ex.Message);
        }


    }
}
