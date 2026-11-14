namespace Cljr.Tests;

internal class FakeClojureDownloader : ICLojureMainVersionDownloader
{
    public int CallCount => manifestPaths.Count;

    public List<string> ManifestPaths => manifestPaths;
    public List<string> RequestedVersions => requestedVersions;

    private readonly List<string> manifestPaths = [];
    private readonly List<string> requestedVersions = [];

    private DownloaderResult _result;

    public FakeClojureDownloader(DownloaderResult data)
    {
        _result = data;
    }

    public DownloaderResult DownloadClojureMainVersion(string manifestPath, string requestedVersion)
    {
        manifestPaths.Add(manifestPath);
        requestedVersions.Add(requestedVersion);
        return _result;
    }

}
