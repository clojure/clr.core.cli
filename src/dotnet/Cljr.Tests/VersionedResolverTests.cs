using System.Text.Json.Nodes;
using static Cljr.Resolver;

namespace Cljr.Tests;

public class VersionedResolverTests : IDisposable
{
    internal string ConfigDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    internal string InstalledDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    internal DateTime StandardBaseTime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    internal string FakeClojureMainDllPath => Path.Combine(InstalledDirectory, "Clojure.Main.dll");

    internal string VersionDirectory(Resolver resolver, string version) => resolver.VersionDirectory(ConfigDirectory, version);
    internal string DllPathPath(Resolver resolver, string version) => Path.Combine(VersionDirectory(resolver, version), resolver.DllPathFilename);

    internal FakeInstalledClojureLocator CreateFakeLocatorWithGlobalModeData(InstalledClojureMainData globalModeData, InstalledClojureMainData versionModeData)
    {
        return FakeInstalledClojureLocator.CreateFakeInstalledClojureMainLocator(globalModeData, versionModeData);
    }

    internal FakeClojureDownloader CreateFakeDownloaderWithResult(DownloaderResult result)
    {
        return new FakeClojureDownloader(result);
    }


    private string StandardDllPathContent(DateTime timestamp) => $"{InstalledDirectory}\n{timestamp:o}\n{Environment.Version.Major}";

    private void CreateValidVersionDirectory(Resolver resolver, string configDirectory, string version)
    {
        string versionDirectory = VersionDirectory(resolver, version);
        DateTime timestamp = File.GetLastWriteTimeUtc(FakeClojureMainDllPath);

        Directory.CreateDirectory(versionDirectory);
        File.WriteAllText(DllPathPath(resolver, version), StandardDllPathContent(timestamp));

        Directory.CreateDirectory(Path.GetDirectoryName(resolver.ManifestPath(configDirectory, version))!);
        File.WriteAllText(resolver.ManifestPath(configDirectory, version), $$"""
            {
                "version": 1,
                "isRoot": true,
                "tools": {
                    "{{Resolver.ClojureMainFilename.ToLower()}}": {
                        "version": "{{version}}",
                        "commands": [ "{{Resolver.ClojureMainFilename}}" ],
                        "rollForward": false
                        }   
                    }
                }
            """);
    }

    private void CreateStandardFixture(
        out Resolver resolver,
        out InstalledClojureMainData globalModeData,
        out InstalledClojureMainData versionModeData,
        out FakeInstalledClojureLocator fakeLocator,
        out FakeClojureDownloader fakeDownloader,
        bool downloaderSuccess = false,
        string downloadResultMessage = "don't call me")
    {
        resolver = new();
        globalModeData = new()
        {
            ClojureMainDirectory = InstalledDirectory,
            Timestamp = StandardBaseTime
        };
        versionModeData = new()
        {
            ClojureMainDirectory = InstalledDirectory,
            Timestamp = StandardBaseTime - TimeSpan.FromDays(1)
        };
        fakeLocator = CreateFakeLocatorWithGlobalModeData(globalModeData, versionModeData);
        fakeDownloader = CreateFakeDownloaderWithResult(new DownloaderResult(downloaderSuccess, (downloaderSuccess ? 0 : 1), downloadResultMessage));
    }

    public VersionedResolverTests()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(InstalledDirectory);
        File.WriteAllText(FakeClojureMainDllPath, "This is a fake Clojure.Main.dll file for testing purposes.");
        File.SetLastWriteTimeUtc(FakeClojureMainDllPath, StandardBaseTime - TimeSpan.FromDays(1));

    }

    [Theory]
    [InlineData("1.12.7")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EnvVarHasPrecedence(string? version)
    {
        // Arrange
        CreateStandardFixture(out var resolver, out var globalModeData, out var versionModeData, out var fakeLocator, out var fakeDownloader);
        // Act
        var result = resolver.FindVersionedClojureMainDll(InstalledDirectory, ConfigDirectory, version, fakeLocator, fakeDownloader);
        // Assert
        Assert.Equal(InstalledDirectory, result.ClojureMainDirectory);
        Assert.Equal(Resolver.EStep.EnvVarOverride, result.Step);
        Assert.Equal(0, fakeDownloader.CallCount);
        Assert.Equal(0, fakeLocator.CallCount);
    }

    [Fact]
    public void EnvVarPointingToMissingClojureMainThrows()
    {
        // Arrange
        CreateStandardFixture(out var resolver, out var globalModeData, out var versionModeData, out var fakeLocator, out var fakeDownloader);

        // Act
        // Assert
        Assert.Throws<FileNotFoundException>(() => resolver.FindVersionedClojureMainDll(ConfigDirectory, ConfigDirectory, "1.12.7", fakeLocator, fakeDownloader));

    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NoVersionSuppliedCallsGlobalLocator(string? version)
    {
        // Arrange
        CreateStandardFixture(out var resolver, out var globalModeData, out var versionModeData, out var fakeLocator, out var fakeDownloader);

        // Act
        var result = resolver.FindVersionedClojureMainDll(null, ConfigDirectory, version, fakeLocator, fakeDownloader);
        // Assert
        Assert.Equal(InstalledDirectory, result.ClojureMainDirectory);
        Assert.Equal(Resolver.EStep.Default, result.Step);
        Assert.Equal(0, fakeDownloader.CallCount);
        Assert.Equal(1, fakeLocator.CallCount);
        Assert.Equal(ELocatorMode.Global, fakeLocator.Modes[0]);
    }

    [Fact]
    public void VersionWithValidDllPathReturnsWithNoLocatorOrDownloaderCalls()
    {
        // Arrange
        CreateStandardFixture(out var resolver, out var globalModeData, out var versionModeData, out var fakeLocator, out var fakeDownloader);
        CreateValidVersionDirectory(resolver, ConfigDirectory, "1.12.7");

        // Act
        var result = resolver.FindVersionedClojureMainDll(null, ConfigDirectory, "1.12.7", fakeLocator, fakeDownloader);

        // Assert
        Assert.Equal(InstalledDirectory, result.ClojureMainDirectory);
        Assert.Equal(Resolver.EStep.VersionCache, result.Step);
        Assert.Equal(0, fakeDownloader.CallCount);
        Assert.Equal(0, fakeLocator.CallCount);
    }

    private enum EBadDllPathType
    {
        MissingDllPathFile,
        TwoLines,
        FourLines,
        EmptyFile,
        MajorVersionMismatch,
        BadMajorVersionValue,
        MissingDirectory,
        DirectoryExistsButNoDll,
        BadTimestampFormat,
        BadTimestampValue,
        TimestampMismatch
    }

    [Theory]
    [InlineData(EBadDllPathType.MissingDllPathFile)]
    [InlineData(EBadDllPathType.TwoLines)]
    [InlineData(EBadDllPathType.FourLines)]
    [InlineData(EBadDllPathType.EmptyFile)]
    [InlineData(EBadDllPathType.MajorVersionMismatch)]
    [InlineData(EBadDllPathType.BadMajorVersionValue)]
    [InlineData(EBadDllPathType.MissingDirectory)]
    [InlineData(EBadDllPathType.DirectoryExistsButNoDll)]
    [InlineData(EBadDllPathType.BadTimestampFormat)]
    [InlineData(EBadDllPathType.BadTimestampValue)]
    [InlineData(EBadDllPathType.TimestampMismatch)]
    void VersionWithInvalidDllPathCallsDownloader(EBadDllPathType type)
    {
        string requestedVersion = "1.12.7";
        // Arrange
        CreateStandardFixture(out var resolver, out var globalModeData, out var versionModeData, out var fakeLocator, out var fakeDownloader, true, "");
        CreateValidVersionDirectory(resolver, ConfigDirectory, requestedVersion);

        switch (type)
        {
            case EBadDllPathType.MissingDllPathFile:
                File.Delete(DllPathPath(resolver, requestedVersion));
                break;
            case EBadDllPathType.TwoLines:
                File.WriteAllText(DllPathPath(resolver, requestedVersion), $"{InstalledDirectory}\n{StandardBaseTime:o}");
                break;
            case EBadDllPathType.FourLines:
                File.WriteAllText(DllPathPath(resolver, requestedVersion), $"{InstalledDirectory}\n{StandardBaseTime:o}\n{Environment.Version.Major}\nExtraLine");
                break;
            case EBadDllPathType.EmptyFile:
                File.WriteAllText(DllPathPath(resolver, requestedVersion), "");
                break;
            case EBadDllPathType.MajorVersionMismatch:
                File.WriteAllText(DllPathPath(resolver, requestedVersion), $"{InstalledDirectory}\n{StandardBaseTime:o}\n{Environment.Version.Major + 1}");
                break;
            case EBadDllPathType.BadMajorVersionValue:
                File.WriteAllText(DllPathPath(resolver, requestedVersion), $"{InstalledDirectory}\n{StandardBaseTime:o}\nNotANumber");
                break;
            case EBadDllPathType.MissingDirectory:
                Directory.Delete(InstalledDirectory, true);
                break;
            case EBadDllPathType.DirectoryExistsButNoDll:
                File.Delete(FakeClojureMainDllPath);
                break;
            case EBadDllPathType.BadTimestampFormat:
                File.WriteAllText(DllPathPath(resolver, requestedVersion), $"{InstalledDirectory}\nNotATimestamp\n{Environment.Version.Major}");
                break;
            case EBadDllPathType.BadTimestampValue:
                File.WriteAllText(DllPathPath(resolver, requestedVersion), $"{InstalledDirectory}\n2024-13-01T00:00:00Z\n{Environment.Version.Major}");
                break;
            case EBadDllPathType.TimestampMismatch:
                File.WriteAllText(DllPathPath(resolver, requestedVersion), $"{InstalledDirectory}\n{(StandardBaseTime - TimeSpan.FromDays(2)).ToString("o")}\n{Environment.Version.Major}");
                break;
        }

        // Act
        var result = resolver.FindVersionedClojureMainDll(null, ConfigDirectory, requestedVersion, fakeLocator, fakeDownloader);

        // Assert
        Assert.Equal(InstalledDirectory, result.ClojureMainDirectory);
        Assert.Equal(Resolver.EStep.Restore, result.Step);
        Assert.Equal(1, fakeLocator.CallCount);
        Assert.Equal(1, fakeDownloader.CallCount);
    }


    [Fact]
    public void VersionWithRestoreCreatesProperConfiguration()
    {
        // We do not create a version directory, so the resolver will call the downloader.
        // We want to validate that all the correct files are created.

        string requestedVersion = "1.12.7";

        // Arrange
        CreateStandardFixture(out var resolver, out var globalModeData, out var versionModeData, out var fakeLocator, out var fakeDownloader, true, "");

        // Act
        var result = resolver.FindVersionedClojureMainDll(null, ConfigDirectory, requestedVersion, fakeLocator, fakeDownloader);

        // Assert

        // Correct path to the Clojure.Main.dll should be returned, and the step should indicate that a restore was performed.
        Assert.Equal(InstalledDirectory, result.ClojureMainDirectory);
        Assert.Equal(Resolver.EStep.Restore, result.Step);

        // The manifest file should exist, should parse as JSON and has the correct version, commands, and rollForward.
        Assert.True(File.Exists(resolver.ManifestPath(ConfigDirectory, requestedVersion)));
        string manifestContent = File.ReadAllText(resolver.ManifestPath(ConfigDirectory, requestedVersion));
        var manifestJson = JsonNode.Parse(manifestContent);
        var manifestTools = manifestJson?["tools"];
        var manifestClojureMain = manifestTools?[Resolver.ClojureMainFilename.ToLower()];
        Assert.Equal(requestedVersion, manifestClojureMain?["version"]?.ToString());
        Assert.Equal(1, manifestClojureMain?["commands"]?.AsArray()?.Count);
        Assert.Equal(Resolver.ClojureMainFilename, manifestClojureMain?["commands"]?[0]?.ToString());
        Assert.Equal(false, manifestClojureMain?["rollForward"]?.GetValue<bool>());


        // The locator was called in versioned mode and got the correct working directory (the version directory)
        Assert.Equal(1, fakeLocator.CallCount);
        Assert.Equal(ELocatorMode.Versioned, fakeLocator.Modes[0]);
        Assert.Equal(VersionDirectory(resolver, requestedVersion), fakeLocator.Paths[0]);

        // The downloader was called once with the correct manifest path and requested version.
        Assert.Equal(1, fakeDownloader.CallCount);
        Assert.Equal(resolver.ManifestPath(ConfigDirectory, requestedVersion), fakeDownloader.ManifestPaths[0]);
        Assert.Equal(requestedVersion, fakeDownloader.RequestedVersions[0]);

        // The dll-path file has three lines: the directory, the timestamp, and the major version of the runtime.
        string[] dllPathLines = File.ReadAllLines(DllPathPath(resolver, requestedVersion));
        Assert.Equal(3, dllPathLines.Length);
        Assert.Equal(InstalledDirectory, dllPathLines[0]);
        Assert.Equal((StandardBaseTime - TimeSpan.FromDays(1)).ToString("o"), dllPathLines[1]);
        Assert.Equal(Environment.Version.Major.ToString(), dllPathLines[2]);

        // No leftover files: the only files under the ConfigDirectory should be the manifest file and the dll-path file.
        string[] configDirectoryFiles = Directory.GetFiles(ConfigDirectory, "*", SearchOption.AllDirectories);
        Assert.Equal(2, configDirectoryFiles.Length);
        Assert.Contains(resolver.ManifestPath(ConfigDirectory, requestedVersion), configDirectoryFiles);
        Assert.Contains(DllPathPath(resolver, requestedVersion), configDirectoryFiles);
    }

    [Fact]
    public void VersionRoundTripIsSuccessful()
    {
        // A second call gives VersionCache, and the downloader count stays at 1.
        string requestedVersion = "1.12.7";

        // Arrange
        CreateStandardFixture(out var resolver, out var globalModeData, out var versionModeData, out var fakeLocator, out var fakeDownloader, true, "");

        // Act
        var result = resolver.FindVersionedClojureMainDll(null, ConfigDirectory, requestedVersion, fakeLocator, fakeDownloader);
        result = resolver.FindVersionedClojureMainDll(null, ConfigDirectory, requestedVersion, fakeLocator, fakeDownloader);

        Assert.Equal(InstalledDirectory, result.ClojureMainDirectory);
        Assert.Equal(Resolver.EStep.VersionCache, result.Step);
        Assert.Equal(1, fakeLocator.CallCount);
        Assert.Equal(1, fakeDownloader.CallCount);
    }

    [Fact]
    public void VersionWithFailedDownloadReportsError()
    {
        string requestedVersion = "1.12.7";

        // Arrange
        CreateStandardFixture(out var resolver, out var globalModeData, out var versionModeData, out var fakeLocator, out var fakeDownloader, false, "So bad!");

        // Act/Assert
        FailedDownloadException? ex = Assert.Throws<FailedDownloadException>(() => resolver.FindVersionedClojureMainDll(null, ConfigDirectory, requestedVersion, fakeLocator, fakeDownloader));

        Assert.Equal(1, ex.ExitCode);
        Assert.Equal("So bad!", ex.ErrorOutput);
        Assert.Equal(0, fakeLocator.CallCount);
        Assert.False(File.Exists(DllPathPath(resolver, requestedVersion)));
    }

    [Fact]
    public void VersionWithDownloadButFailedLocatorReportsLocatorError()
    {
        string requestedVersion = "1.12.7";

        // Arrange
        CreateStandardFixture(out var resolver, out var globalModeData, out var versionModeData, out var fakeLocator, out var fakeDownloader, true, "");

        // But we need a locator that fails

        versionModeData = new()
        {
            Exception = new FileNotFoundException("Ouch, that hurts!")

        };
        fakeLocator = CreateFakeLocatorWithGlobalModeData(globalModeData, versionModeData);

        // Act/Assert
        FileNotFoundException? ex = Assert.Throws<FileNotFoundException>(() => resolver.FindVersionedClojureMainDll(null, ConfigDirectory, requestedVersion, fakeLocator, fakeDownloader));

        Assert.Equal("Ouch, that hurts!", ex.Message);
        Assert.False(File.Exists(DllPathPath(resolver, requestedVersion)));
    }


    [Fact]
    public void VersionDoesNotRewriteManifestFileOnRestore()
    {
        string requestedVersion = "1.12.7";
        string sentinelValue = "sentinel";

        // Arrange
        CreateStandardFixture(out var resolver, out var globalModeData, out var versionModeData, out var fakeLocator, out var fakeDownloader, true, "");

        // We want a version directory with no dll-path file and a manifest file with known content. The resolver should not rewrite the manifest file on restore.

        string versionDirectory = VersionDirectory(resolver, requestedVersion);

        Directory.CreateDirectory(Path.GetDirectoryName(resolver.ManifestPath(ConfigDirectory, requestedVersion))!);

        string manifestPath = resolver.ManifestPath(ConfigDirectory, requestedVersion);
        File.WriteAllText(manifestPath, sentinelValue);

        // Act
        var result = resolver.FindVersionedClojureMainDll(null, ConfigDirectory, requestedVersion, fakeLocator, fakeDownloader);

        // Assert
        Assert.Equal(InstalledDirectory, result.ClojureMainDirectory);
        Assert.Equal(Resolver.EStep.Restore, result.Step);
        Assert.Equal(1, fakeLocator.CallCount);
        Assert.Equal(1, fakeDownloader.CallCount);
        Assert.Equal(sentinelValue, File.ReadAllText(manifestPath));
    }

    /////////////////////////////////////////////
    // Implementing IDisposable, standard pattern

    private bool disposedValue;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // Dispose managed state (managed objects)
                if (Directory.Exists(ConfigDirectory))
                {
                    Directory.Delete(ConfigDirectory, true);
                }
                if (Directory.Exists(InstalledDirectory))
                {
                    Directory.Delete(InstalledDirectory, true);
                }

            }

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

