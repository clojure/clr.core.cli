using System.Globalization;

namespace Cljr.Tests;

public class ResolverTests : IDisposable
{
    // Path for temporary directory containing no files. (Will fail on containing Clojure.Main.dll).
    public string EmptyDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    // Path for temporary directory containing a fake Clojure.Main.dll.
    public string OccupiedDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    // Timestamp of the fake Clojure.Main.dll file in the occupied directory.  (So I don't have to keep looking it up.)
    public DateTime OccupiedDllTimeStamp { get; private set; } = DateTime.MinValue;

    // Path for temporary directory that we will not create.
    public string NonexistentDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    // The directory where we will read and write the cache file for the tests.
    public string TestCacheFileDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    // The path to the cache file for the tests. 
    public string TestCacheFilePath => Path.Combine(TestCacheFileDirectory, Resolver.ClojureMainDLLCacheFilename);





    // For IDisposable support
    private bool disposedValue;


    public ResolverTests()
    {
        // Create the empty directory.
        Directory.CreateDirectory(EmptyDirectory);

        // Create the occupied directory and add a fake Clojure.Main.dll file.
        Directory.CreateDirectory(OccupiedDirectory);
        string fakeClojureMainDllPath = Path.Combine(OccupiedDirectory, $"{Resolver.ClojureMainFilename}.dll");
        File.WriteAllText(fakeClojureMainDllPath, "This is a fake Clojure.Main.dll file for testing purposes.");
        OccupiedDllTimeStamp = File.GetLastWriteTimeUtc(fakeClojureMainDllPath);

        // Create the temporary cache file directory.
        Directory.CreateDirectory(TestCacheFileDirectory);
    }


    [Fact]
    public void EnvVarCaseFailsOnNonexistentDirectory()
    {
        // Arrange
        Resolver resolver = new();

        // Act & Assert
        var ex = Assert.Throws<FileNotFoundException>(() =>
                resolver.FindDefaultClojureMainDll(NonexistentDirectory, TestCacheFileDirectory, CreateFakeLocatorWithException()));
    }

    [Fact]
    public void EnvVarCaseFailsOnEmptyDirectory()
    {
        // Arrange
        Resolver resolver = new();

        // Act & Assert
        var ex = Assert.Throws<FileNotFoundException>(() =>
                resolver.FindDefaultClojureMainDll(EmptyDirectory, TestCacheFileDirectory, CreateFakeLocatorWithException()));
    }

    [Fact]
    public void EnvVarCaseSucceedsOnOccupiedDirectory()
    {
        // Arrange
        Resolver resolver = new();
        // Act
        string result = resolver.FindDefaultClojureMainDll(OccupiedDirectory, TestCacheFileDirectory, CreateFakeLocatorWithException());
        // Assert
        Assert.Equal(OccupiedDirectory, result);
    }


    [Fact]
    public void CacheCaseSucceedsWithValidCacheEntry()
    {
        // Arrange
        WriteProperCacheFile();
        Resolver resolver = new();
        // Act
        string result = resolver.FindDefaultClojureMainDll(null, TestCacheFileDirectory, CreateFakeLocatorWithException());
        // Assert
        Assert.Equal(OccupiedDirectory, result);
    }

    [Fact]
    public void CacheCaseFailsWithInvalidTimestamp()
    {
        // Arrange
        WriteCacheFileWithMismatchedTimestamp();
        Resolver resolver = new();
        // Act
        string result = resolver.FindDefaultClojureMainDll(null, TestCacheFileDirectory, CreateFakeLocatorWithInvalidData());
        // Assert
        // Note that we fall through to the locator, which returns the empty directory path, since the cache was invalid.
        // (hence we know the cache was ignored).
        Assert.Equal(EmptyDirectory, result);
    }

    [Fact]
    public void CacheCaseFailsOnEmptyDirectory()
    {
        // Arrange
        WriteCacheFilePointingToEmptyDirectory();
        Resolver resolver = new();
        // Act
        string result = resolver.FindDefaultClojureMainDll(null, TestCacheFileDirectory, CreateFakeLocatorWithValidData());
        // Assert
        // Cache file pointing to empty directory, means the cache case falls through to the locator, which returns the occupied directory path.
        // (hence we know the cache was ignored).
        Assert.Equal(OccupiedDirectory, result);
    }

    [Fact]
    public void CacheCaseFailsOnMissingCachefile()
    {
        // Arrange
        DeleteCacheFile();
        Resolver resolver = new();
        // Act
        string result = resolver.FindDefaultClojureMainDll(null, TestCacheFileDirectory, CreateFakeLocatorWithValidData());
        // Assert
        // Cache file doesn't exist, means the cache case falls through to the locator, which returns the occupied directory path.
        // (hence we know the cache was ignored).
        Assert.Equal(OccupiedDirectory, result);
    }


    // This is more of an integration test than a unit test, but it is useful to verify that the default locator works as expected in a real scenario.
    [Fact]
    public void DefaultLocatorCaseSucceeds()
    {
        // Arrange
        DeleteCacheFile();  // make sure we fall through to the default locator case.
        Resolver resolver = new();
        var defaultLocator = new InstalledClojureMainLocatorImpl();
        // Act
        string result = resolver.FindDefaultClojureMainDll(null, TestCacheFileDirectory, defaultLocator);
        // Assert
        Assert.False(string.IsNullOrEmpty(result));
        Assert.True(Directory.Exists(result));
        string clojureMainDllPath = Path.Combine(result, $"{Resolver.ClojureMainFilename}.dll");
        Assert.True(File.Exists(clojureMainDllPath));
    }





    private IInstalledClojureMainLocator CreateFakeLocatorWithException()
    {
        return FakeInstalledClojureLocator.CreateFakeInstalledClojureMainLocater(
            clojureMainPath: OccupiedDirectory,
            timestamp: DateTime.UtcNow,
            exception: new Exception("Simulated exception from fake locator."));
    }

    private IInstalledClojureMainLocator CreateFakeLocatorWithValidData()
    {
        return FakeInstalledClojureLocator.CreateFakeInstalledClojureMainLocater(
            clojureMainPath: OccupiedDirectory,
            timestamp: OccupiedDllTimeStamp);
    }

    private IInstalledClojureMainLocator CreateFakeLocatorWithInvalidData()
    {
        return FakeInstalledClojureLocator.CreateFakeInstalledClojureMainLocater(
            clojureMainPath: EmptyDirectory,
            timestamp: DateTime.UtcNow - TimeSpan.FromDays(1000));
    }


    /////////////////////////////////////////////
    // Cache file management


    private void DeleteCacheFile()
    {
        if (File.Exists(TestCacheFilePath))
        {
            File.Delete(TestCacheFilePath);
        }
    }

    private void WriteCacheFile(string clojureMainPath, DateTime timestamp)
    {
        DeleteCacheFile();

        string cacheContent = $"{clojureMainPath}\n{timestamp.ToString("o", CultureInfo.InvariantCulture)}";

        File.WriteAllText(TestCacheFilePath, cacheContent);
    }


    private void WriteCacheFilePointingToBadDirectory()
    {
        WriteCacheFile(NonexistentDirectory, DateTime.UtcNow);
    }

    private void WriteCacheFilePointingToEmptyDirectory()
    {
        WriteCacheFile(EmptyDirectory, DateTime.UtcNow);
    }

    private void WriteCacheFileWithMismatchedTimestamp()
    {
        WriteCacheFile(OccupiedDirectory, DateTime.UtcNow - TimeSpan.FromDays(1000));
    }

    private void WriteProperCacheFile()
    {
        // Get the timestamp of the fake Clojure.Main.dll file in the occupied directory.

        string fakeClojureMainDllPath = Path.Combine(OccupiedDirectory, $"{Resolver.ClojureMainFilename}.dll");
        FileInfo fi = new(fakeClojureMainDllPath);
        WriteCacheFile(OccupiedDirectory, fi.LastWriteTimeUtc);
    }



    /////////////////////////////////////////////
    // Implementing IDisposable, standard pattern

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // Dispose managed state (managed objects)
                if (Directory.Exists(EmptyDirectory))
                {
                    Directory.Delete(EmptyDirectory, true);
                }
                if (Directory.Exists(OccupiedDirectory))
                {
                    Directory.Delete(OccupiedDirectory, true);
                }
                if (Directory.Exists(TestCacheFileDirectory))
                {
                    Directory.Delete(TestCacheFileDirectory, true);
                }
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
