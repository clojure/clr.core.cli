namespace Cljr;


internal class InstalledClojureMainData
{
    public string ClojureMainDirectory { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.MinValue;
    public Exception? Exception { get; set; } = null;
}

// Interface for the DefaultClojureMainLocator class, which is responsible for locating the default Clojure main DLL.
// Abstracted the interface for mocking in unit tests.
internal interface IInstalledClojureMainLocator
{
    InstalledClojureMainData FindInstalledClojureMainDll();
}
