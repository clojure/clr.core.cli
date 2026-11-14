namespace Cljr.Tests;

internal class FakeInstalledClojureLocator : IInstalledClojureMainLocator
{
    public int CallCount => _modes.Count;
    private readonly InstalledClojureMainData _globalModeData;
    private readonly InstalledClojureMainData _versionModeData;
    private readonly List<ELocatorMode> _modes = [];
    private readonly List<string> _path = [];

    public IReadOnlyList<ELocatorMode> Modes => _modes.AsReadOnly();
    public IReadOnlyList<string> Paths => _path.AsReadOnly();

    public FakeInstalledClojureLocator(InstalledClojureMainData globalModeData, InstalledClojureMainData versionModeData)
    {
        _globalModeData = globalModeData;
        _versionModeData = versionModeData;
    }

    public InstalledClojureMainData FindInstalledClojureMainDll(ELocatorMode mode, string path)
    {
        _modes.Add(mode);
        _path.Add(path);

        return mode switch
        {
            ELocatorMode.Global => _globalModeData,
            ELocatorMode.Versioned => _versionModeData,
            _ => throw new ArgumentException($"Unknown locator mode: {mode}")
        };
    }

    public static FakeInstalledClojureLocator CreateFakeInstalledClojureMainLocator(InstalledClojureMainData globalModeData, InstalledClojureMainData versionModeData)
    {
        return new FakeInstalledClojureLocator(globalModeData, versionModeData);
    }

    public static FakeInstalledClojureLocator CreateFakeInstalledClojureMainLocator(string clojureMainPath, DateTime timestamp, Exception? exception = null)
    {
        return new FakeInstalledClojureLocator(
            new InstalledClojureMainData
            {
                ClojureMainDirectory = clojureMainPath,
                Timestamp = timestamp,
                Exception = exception
            },
            new InstalledClojureMainData
            {
                ClojureMainDirectory = string.Empty,
                Timestamp = DateTime.MinValue,
                Exception = null
            });
    }
}
