using System.Text.Json.Nodes;

namespace Cljr.Tests;

public class EnvironmentManagerTests : IDisposable
{
    public string BaseDirectory { get; init; }

    public string CacheDirectory { get; init; }
    public string FrameworksFile { get; init; }
    public string RuntimeConfigFile { get; init; }
    public string RuntimeConfigSourceFile { get; init; }

    public string ClojureMainDirectory { get; init; }
    public string SourceRuntimeConfigJsonFile { get; init; }

    public DateTime StandardBaseTime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    const string PrimaryRuntimeConfigJson = @"{
          ""runtimeOptions"": {
            ""tfm"": ""net10.0"",
            ""framework"": {
              ""name"": ""Microsoft.NETCore.App"",
              ""version"": ""10.0.0""
            },
            ""configProperties"": {
              ""System.Reflection.Metadata.MetadataUpdater.IsSupported"": false,
              ""System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization"": true
            }
          }
        }";

    //const string PrimarySourceVersion = "10.0.0";
    //const string PrimarySourceTfm = "net10.0";

    const string SecondaryRuntimeConfigJson = @"{
          ""runtimeOptions"": {
            ""tfm"": ""net11.0"",
            ""framework"": {
              ""name"": ""Microsoft.NETCore.App"",
              ""version"": ""11.0.0""
            },
            ""configProperties"": {
              ""System.Reflection.Metadata.MetadataUpdater.IsSupported"": false,
              ""System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization"": true
            }
          }
        }";

    const string SecondarySourceVersion = "11.0.0";
    const string SecondarySourceTfm = "net11.0";

    // For IDisposable support
    private bool disposedValue;

    public EnvironmentManagerTests()
    {
        BaseDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        CacheDirectory = Path.Combine(BaseDirectory, "cache");
        FrameworksFile = Path.Combine(CacheDirectory, "test.frameworks");
        RuntimeConfigFile = Path.Combine(CacheDirectory, "test.runtimeconfig.json");
        RuntimeConfigSourceFile = Path.Combine(CacheDirectory, "test.runtimeconfig.source");

        ClojureMainDirectory = Path.Combine(BaseDirectory, "main");
        SourceRuntimeConfigJsonFile = Path.Combine(ClojureMainDirectory, $"{Resolver.ClojureMainFilename}.runtimeconfig.json");

        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(ClojureMainDirectory);
    }

    private void CreateMinimalSetup()
    {
        // The frameworks that would be coming from :clr/frameworks
        File.WriteAllText(FrameworksFile, "Microsoft.NETCore.App\nMicrosoft.AspNetCore.App");

        // Need a valid runtimeconfig.json file in the ClojureMainDirectory, since GetRuntimeConfigPath will read it.
        File.WriteAllText(SourceRuntimeConfigJsonFile, PrimaryRuntimeConfigJson);
    }

    // Helper method to create a basic setup.
    // Runs GetRuntimeConfigPath once to create reasonable files,
    // then sets the last write times to known values.

    private void CreateBasicSetup()
    {
        CreateMinimalSetup();

        // Make the first run to set up the runtimeconfig.json and runtimeconfig.source files.
        EnvironmentManager.GetRuntimeConfigPath(FrameworksFile, RuntimeConfigFile, RuntimeConfigSourceFile, ClojureMainDirectory);

        // Set the times to make sure things look fresh.
        File.SetLastWriteTimeUtc(SourceRuntimeConfigJsonFile, StandardBaseTime - TimeSpan.FromMinutes(10));
        File.SetLastWriteTimeUtc(FrameworksFile, StandardBaseTime - TimeSpan.FromMinutes(10));
        File.SetLastWriteTimeUtc(RuntimeConfigFile, StandardBaseTime - TimeSpan.FromMinutes(5));
        File.SetLastWriteTimeUtc(RuntimeConfigSourceFile, StandardBaseTime - TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void GetRuntimeConfigPathReturnsNullWhenNoFrameworks()
    {
        // Arrange
        CreateMinimalSetup();
        File.Delete(FrameworksFile); // No frameworks file, so no frameworks to add.

        // Act
        string? runtimeConfigPath = EnvironmentManager.GetRuntimeConfigPath(FrameworksFile, RuntimeConfigFile, RuntimeConfigSourceFile, ClojureMainDirectory);

        // Assert
        Assert.Null(runtimeConfigPath);
        Assert.False(File.Exists(RuntimeConfigFile));
        Assert.False(File.Exists(RuntimeConfigSourceFile));
    }

    [Fact]
    public void GetRuntimeConfigPathReturnsNullWhenFrameworksFileHasOnlyBlankOrWhitespaceLines()
    {
        // Arrange
        CreateMinimalSetup();
        File.WriteAllText(FrameworksFile, "   \n\t  ");

        // Act
        string? runtimeConfigPath = EnvironmentManager.GetRuntimeConfigPath(FrameworksFile, RuntimeConfigFile, RuntimeConfigSourceFile, ClojureMainDirectory);

        // Assert
        Assert.Null(runtimeConfigPath);
        Assert.False(File.Exists(RuntimeConfigFile));
        Assert.False(File.Exists(RuntimeConfigSourceFile));
    }

    [Fact]
    public void GetRuntimeConfigPathFromScratchWritesRuntimeConfigFile()
    {
        // Arrange
        CreateMinimalSetup();

        // Act
        var computedRuntimeConfigPath = EnvironmentManager.GetRuntimeConfigPath(FrameworksFile, RuntimeConfigFile, RuntimeConfigSourceFile, ClojureMainDirectory);

        // Assert

        Assert.True(File.Exists(RuntimeConfigFile));
        Assert.True(File.Exists(RuntimeConfigSourceFile));

        string runtimeConfigJson = File.ReadAllText(RuntimeConfigFile);
        RuntimeConfigHelpers.ExtractNamesAndVersions(JsonNode.Parse(runtimeConfigJson) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json"), out var frameworkNames, out var _);

        List<String> expectedFrameworkNames = ["Microsoft.NETCore.App", "Microsoft.AspNetCore.App"];
        Assert.Equal(expectedFrameworkNames, frameworkNames);

        Assert.Equal(RuntimeConfigFile, computedRuntimeConfigPath);
        Assert.Equal(SourceRuntimeConfigJsonFile, File.ReadAllText(RuntimeConfigSourceFile));
    }

    [Fact]
    public void GetRuntimeConfigPathReusesFreshCache()
    {
        // Arrange

        CreateBasicSetup();

        // Write a sentinel value to the runtimeconfig.json file so we can check if it gets rewritten.
        File.WriteAllText(RuntimeConfigFile, "SENTINEL");
        File.SetLastWriteTimeUtc(RuntimeConfigFile, StandardBaseTime - TimeSpan.FromMinutes(5));

        // Act
        EnvironmentManager.GetRuntimeConfigPath(FrameworksFile, RuntimeConfigFile, RuntimeConfigSourceFile, ClojureMainDirectory);

        // Assert

        // The sentinel value should still be there, meaning the runtimeconfig.json file was not rewritten.
        Assert.Equal(StandardBaseTime - TimeSpan.FromMinutes(5), File.GetLastWriteTimeUtc(RuntimeConfigFile));
        Assert.Equal("SENTINEL", File.ReadAllText(RuntimeConfigFile));
    }

    [Fact]
    public void GetRuntimeConfigPathNewerFrameworkRegenerates()
    {
        // Arrange

        CreateBasicSetup();

        // Make the frameworks file newer than the runtimeconfig.json file.
        File.SetLastWriteTimeUtc(FrameworksFile, StandardBaseTime);

        // Write a sentinel value to the runtimeconfig.json file so we can check if it gets rewritten.
        File.WriteAllText(RuntimeConfigFile, "SENTINEL");
        File.SetLastWriteTimeUtc(RuntimeConfigFile, StandardBaseTime - TimeSpan.FromMinutes(5));

        // Act
        EnvironmentManager.GetRuntimeConfigPath(FrameworksFile, RuntimeConfigFile, RuntimeConfigSourceFile, ClojureMainDirectory);

        // Assert
        Assert.True(StandardBaseTime < File.GetLastWriteTimeUtc(RuntimeConfigFile));

        // And we have valid runtimeconfig.json content.

        string runtimeConfigJson = File.ReadAllText(RuntimeConfigFile);
        RuntimeConfigHelpers.ExtractNamesAndVersions(JsonNode.Parse(runtimeConfigJson) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json"), out var frameworkNames, out var _);

        List<String> expectedFrameworkNames = ["Microsoft.NETCore.App", "Microsoft.AspNetCore.App"];
        Assert.Equal(expectedFrameworkNames, frameworkNames);
    }

    [Fact]
    public void GetRuntimeConfigPathNewerSourceRegenerates()
    {
        // Arrange
        CreateBasicSetup();

        // Make the source file newer than the runtimeconfig.json file.
        File.SetLastWriteTimeUtc(SourceRuntimeConfigJsonFile, StandardBaseTime);

        // Write a sentinel value to the runtimeconfig.json file so we can check if it gets rewritten.
        File.WriteAllText(RuntimeConfigFile, "SENTINEL");
        File.SetLastWriteTimeUtc(RuntimeConfigFile, StandardBaseTime - TimeSpan.FromMinutes(5));

        // Act
        EnvironmentManager.GetRuntimeConfigPath(FrameworksFile, RuntimeConfigFile, RuntimeConfigSourceFile, ClojureMainDirectory);

        // Assert

        Assert.True(StandardBaseTime < File.GetLastWriteTimeUtc(RuntimeConfigFile));

        string runtimeConfigJson = File.ReadAllText(RuntimeConfigFile);
        RuntimeConfigHelpers.ExtractNamesAndVersions(JsonNode.Parse(runtimeConfigJson) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json"), out var frameworkNames, out var _);

        List<String> expectedFrameworkNames = ["Microsoft.NETCore.App", "Microsoft.AspNetCore.App"];
        Assert.Equal(expectedFrameworkNames, frameworkNames);
    }

    [Fact]
    public void GetRuntimeConfigPathSwitchedSourceReadsNewSource()
    {
        // Arrange
        CreateBasicSetup();

        string newMainDirectory = Path.Combine(BaseDirectory, "main2");
        string newSourceRuntimeConfigJsonFile = Path.Combine(newMainDirectory, $"{Resolver.ClojureMainFilename}.runtimeconfig.json");

        // Need a valid runtimeconfig.json file in the new main2 directory, since GetRuntimeConfigPath will read it
        Directory.CreateDirectory(newMainDirectory);
        File.WriteAllText(newSourceRuntimeConfigJsonFile, SecondaryRuntimeConfigJson);
        File.SetLastWriteTimeUtc(newSourceRuntimeConfigJsonFile, StandardBaseTime - TimeSpan.FromMinutes(10));

        // Act
        EnvironmentManager.GetRuntimeConfigPath(FrameworksFile, RuntimeConfigFile, RuntimeConfigSourceFile, newMainDirectory);

        // Assert
        Assert.True(StandardBaseTime < File.GetLastWriteTimeUtc(RuntimeConfigFile));

        // And we have valid runtimeconfig.json content.

        string runtimeConfigJson = File.ReadAllText(RuntimeConfigFile);
        JsonNode node = JsonNode.Parse(runtimeConfigJson) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json");
        RuntimeConfigHelpers.ExtractNamesAndVersions(node, out var frameworkNames, out var frameworkVersion);

        List<String> expectedFrameworkNames = ["Microsoft.NETCore.App", "Microsoft.AspNetCore.App"];
        Assert.Equal(expectedFrameworkNames, frameworkNames);

        Assert.All(frameworkVersion, v => Assert.Equal(SecondarySourceVersion, v));

        string tfm = RuntimeConfigHelpers.ExtractTfm(node);
        Assert.Equal(SecondarySourceTfm, tfm);

        string newSourceFileContent = File.ReadAllText(RuntimeConfigSourceFile);
        Assert.Equal(newSourceRuntimeConfigJsonFile, newSourceFileContent);
    }

    [Fact]
    public void GetRuntimePathThrowsOnMissingSourceFile()
    {
        CreateBasicSetup();

        File.Delete(SourceRuntimeConfigJsonFile);

        var ex = Assert.Throws<FileNotFoundException>(() =>
                            EnvironmentManager.GetRuntimeConfigPath(FrameworksFile, RuntimeConfigFile, RuntimeConfigSourceFile, ClojureMainDirectory));
        Assert.Contains(SourceRuntimeConfigJsonFile, ex.Message);
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
                if (Directory.Exists(BaseDirectory))
                {
                    Directory.Delete(BaseDirectory, true);

                }

                disposedValue = true;
            }
        }
    }
    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
