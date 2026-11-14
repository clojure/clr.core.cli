namespace Cljr;

public record DownloaderResult(bool IsSuccess, int ExitCode, string? ErrMsg);


internal interface ICLojureMainVersionDownloader
{
    DownloaderResult DownloadClojureMainVersion(string manifestPath, string requestedVersion);
}
