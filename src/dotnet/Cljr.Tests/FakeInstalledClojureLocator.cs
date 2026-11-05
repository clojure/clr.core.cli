namespace Cljr.Tests;

internal class FakeInstalledClojureLocator : IInstalledClojureMainLocator
{
    private readonly InstalledClojureMainData _data;
    public FakeInstalledClojureLocator(InstalledClojureMainData data)
    {
        _data = data;
    }
    public InstalledClojureMainData FindInstalledClojureMainDll()
    {
        return _data;
    }


    public static IInstalledClojureMainLocator CreateFakeInstalledClojureMainLocater(string clojureMainPath, DateTime timestamp, Exception? exception = null)
    {
        return new FakeInstalledClojureLocator(new InstalledClojureMainData
        {
            ClojureMainDirectory = clojureMainPath,
            Timestamp = timestamp,
            Exception = exception
        });
    }
}
