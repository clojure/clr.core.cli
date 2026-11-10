using System.Text.Json.Nodes;

namespace Cljr.Tests;

public class RuntimeConfigGeneratorTests
{
    // Helpers

    static void ExtractNamesAndVersions(JsonNode node, out List<string> names, out List<string> versions)
    {
        var options = (node?["runtimeOptions"]) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json, missing runtimeOptions");
        var frameworks = options["frameworks"] as JsonArray;
        var frameworksArray = frameworks?.ToArray() ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json, missing frameworks array");
        names = frameworksArray.Select(fw => fw?["name"]?.GetValue<string>() ?? "unknown").ToList();
        versions = frameworksArray.Select(fw => fw?["version"]?.GetValue<string>() ?? "unknown").ToList();
    }

    static string ExtractTfm(JsonNode node)
    {
        var options = (node?["runtimeOptions"]) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json, missing runtimeOptions");
        return options["tfm"]?.GetValue<string>() ?? "unknown";
    }

    static Dictionary<string, string> ExtractConfigProperties(JsonNode node)
    {
        var options = (node?["runtimeOptions"]) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json, missing runtimeOptions");
        var configProperties = options["configProperties"] as JsonObject;
        return configProperties?.ToDictionary(kv => kv.Key, kv => kv.Value?.GetValue<object>().ToString() ?? "unknown") ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json, missing configProperties");
    }

    static void AssertNoFrameworkElement(JsonNode node)
    {
        var options = (node?["runtimeOptions"]) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json, missing runtimeOptions");
        Assert.Null(options["framework"]);
    }

    // Tests

    [Fact]
    public void TestGenerateWithFramework()
    {
        const string source = @"{
          ""runtimeOptions"": {
            ""tfm"": ""net11.0"",
            ""framework"": {
              ""name"": ""Microsoft.NETCore.App"",
              ""version"": ""11.0.0-rc.1.26425.128""
            },
            ""configProperties"": {
              ""System.Reflection.Metadata.MetadataUpdater.IsSupported"": false,
              ""System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization"": true
            }
          }
        }";

        const string sourceVersion = "11.0.0-rc.1.26425.128";
        const string sourceTfm = "net11.0";

        Dictionary<string, string> sourceConfigProperties = new()
        {
            { "System.Reflection.Metadata.MetadataUpdater.IsSupported", "False" },
            { "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", "True" }
        };

        List<string> frameworksToAdd = ["Microsoft.AspNetCore.App", "Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App", "Microsoft.AspNetCore.App"];
        List<string> expectedNames = ["Microsoft.NETCore.App", "Microsoft.AspNetCore.App", "Microsoft.WindowsDesktop.App"];

        var result = RuntimeConfigGenerator.Generate(source, frameworksToAdd);
        var node = JsonNode.Parse(result) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json");

        ExtractNamesAndVersions(node, out var frameworkNames, out var frameworkVersions);
        string tfm = ExtractTfm(node);
        var configProperties = ExtractConfigProperties(node);

        Assert.Equal(expectedNames, frameworkNames);
        Assert.All(frameworkVersions, v => Assert.Equal(sourceVersion, v));
        Assert.Equal(sourceTfm, tfm);
        Assert.Equal(sourceConfigProperties, configProperties);
        AssertNoFrameworkElement(node);
    }

    [Fact]
    public void TestGenerateWithFrameworks()
    {
        const string source = @"{
            ""runtimeOptions"": {
            ""tfm"": ""net10.0"",
            ""frameworks"": [
                { ""name"": ""Microsoft.NETCore.App"", ""version"": ""10.0.0"" },
                { ""name"": ""Microsoft.AspNetCore.App"", ""version"": ""10.0.0"" }
            ],
            ""configProperties"": {
                ""System.Reflection.Metadata.MetadataUpdater.IsSupported"": false,
                ""System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization"": true
            }
          }
        }";

        const string sourceVersion = "10.0.0";
        const string sourceTfm = "net10.0";
        Dictionary<string, string> sourceConfigProperties = new()
        {
            { "System.Reflection.Metadata.MetadataUpdater.IsSupported", "False" },
            { "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", "True" }
        };

        List<string> frameworksToAdd = ["Microsoft.AspNetCore.App", "Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App", "Microsoft.AspNetCore.App"];
        List<string> expectedNames = ["Microsoft.NETCore.App", "Microsoft.AspNetCore.App", "Microsoft.WindowsDesktop.App"];

        var result = RuntimeConfigGenerator.Generate(source, frameworksToAdd);
        var node = JsonNode.Parse(result) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json");

        ExtractNamesAndVersions(node, out var frameworkNames, out var frameworkVersions);
        string tfm = ExtractTfm(node);
        var configProperties = ExtractConfigProperties(node);

        Assert.Equal(expectedNames, frameworkNames);
        Assert.All(frameworkVersions, v => Assert.Equal(sourceVersion, v));
        Assert.Equal(sourceTfm, tfm);
        Assert.Equal(sourceConfigProperties, configProperties);
        AssertNoFrameworkElement(node);
    }

    [Fact]
    public void GenerateWithMixedVersionUsesMicrosoftNETCoreAppVersion()
    {
        const string source = @"{
          ""runtimeOptions"": {
            ""tfm"": ""net10.0"",
            ""frameworks"": [
                { ""name"": ""Microsoft.NETCore.App"", ""version"": ""10.0.0"" },
                { ""name"": ""Microsoft.AspNetCore.App"", ""version"": ""11.0.0"" }
            ],
            ""configProperties"": {
              ""System.Reflection.Metadata.MetadataUpdater.IsSupported"": false,
              ""System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization"": true
            }
          }
        }";

        List<string> frameworksToAdd = ["Microsoft.WindowsDesktop.App", "Microsoft.AspNetCore.App"];
        List<string> expectedNames = ["Microsoft.NETCore.App", "Microsoft.AspNetCore.App", "Microsoft.WindowsDesktop.App"];
        List<string> expectedVersions = ["10.0.0", "11.0.0", "10.0.0"];

        var result = RuntimeConfigGenerator.Generate(source, frameworksToAdd);
        var node = JsonNode.Parse(result) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json");
        ExtractNamesAndVersions(node, out var frameworkNames, out var frameworkVersions);

        Assert.Equal(expectedNames, frameworkNames);
        Assert.Equal(expectedVersions, frameworkVersions);
        AssertNoFrameworkElement(node);
    }

    [Fact]
    public void GenerateWithNoFrameworkThrows()
    {
        const string source = @"{
          ""runtimeOptions"": {
            ""tfm"": ""net11.0"",
            ""configProperties"": {
              ""System.Reflection.Metadata.MetadataUpdater.IsSupported"": false,
              ""System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization"": true
            }
          }
        }";

        List<string> frameworksToAdd = ["Microsoft.AspNetCore.App", "Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App", "Microsoft.AspNetCore.App"];
        var ex = Assert.Throws<InvalidOperationException>(() => RuntimeConfigGenerator.Generate(source, frameworksToAdd));
        Assert.Equal("Failed to parse runtimeconfig.json, missing both 'framework' and 'frameworks' properties.", ex.Message);
    }

    [Fact]
    public void GenerateWithOnlyNetCoreAppRequestedHasOneEntry()
    {
        const string source = @"{
          ""runtimeOptions"": {
            ""tfm"": ""net11.0"",
            ""framework"": {
              ""name"": ""Microsoft.NETCore.App"",
              ""version"": ""11.0.0-rc.1.26425.128""
            },
            ""configProperties"": {
              ""System.Reflection.Metadata.MetadataUpdater.IsSupported"": false,
              ""System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization"": true
            }
          }
        }";

        List<string> frameworksToAdd = ["Microsoft.NETCore.App"];
        List<string> expectedNames = ["Microsoft.NETCore.App"];

        var result = RuntimeConfigGenerator.Generate(source, frameworksToAdd);
        var node = JsonNode.Parse(result) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json");

        ExtractNamesAndVersions(node, out var frameworkNames, out var _);

        Assert.Equal(expectedNames, frameworkNames);
        AssertNoFrameworkElement(node);
    }

    [Fact]
    public void GenerateWithNoNetCoreAppThrows()
    {
        const string source = @"{
          ""runtimeOptions"": {
            ""tfm"": ""net10.0"",
            ""frameworks"": [
                { ""name"": ""Microsoft.AspNetCore.App"", ""version"": ""11.0.0"" }
            ],
            ""configProperties"": {
              ""System.Reflection.Metadata.MetadataUpdater.IsSupported"": false,
              ""System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization"": true
            }
          }
        }";

        List<string> frameworksToAdd = ["Microsoft.WindowsDesktop.App", "Microsoft.AspNetCore.App"];

        var ex = Assert.Throws<InvalidOperationException>(() => RuntimeConfigGenerator.Generate(source, frameworksToAdd));
        Assert.Equal("Failed to determine the version of Microsoft.NETCore.App from runtimeconfig.json.", ex.Message);
    }

    [Fact]
    public void GenerateIgnoresCase()
    {
        const string source = @"{
            ""runtimeOptions"": {
            ""tfm"": ""net10.0"",
            ""frameworks"": [
                { ""name"": ""Microsoft.NETCore.App"", ""version"": ""10.0.0"" },
                { ""name"": ""Microsoft.AspNetCore.App"", ""version"": ""10.0.0"" }
            ],
            ""configProperties"": {
                ""System.Reflection.Metadata.MetadataUpdater.IsSupported"": false,
                ""System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization"": true
            }
          }
        }";

        const string sourceVersion = "10.0.0";
        const string sourceTfm = "net10.0";
        Dictionary<string, string> sourceConfigProperties = new()
        {
            { "System.Reflection.Metadata.MetadataUpdater.IsSupported", "False" },
            { "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", "True" }
        };

        List<string> frameworksToAdd = ["microsoft.aspnetcore.app", "microsoft.netcore.app", "Microsoft.WindowsDesktop.App"];
        List<string> expectedNames = ["Microsoft.NETCore.App", "Microsoft.AspNetCore.App", "Microsoft.WindowsDesktop.App"];

        var result = RuntimeConfigGenerator.Generate(source, frameworksToAdd);
        var node = JsonNode.Parse(result) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json");

        ExtractNamesAndVersions(node, out var frameworkNames, out var frameworkVersions);
        string tfm = ExtractTfm(node);
        var configProperties = ExtractConfigProperties(node);

        Assert.Equal(expectedNames, frameworkNames);
        Assert.All(frameworkVersions, v => Assert.Equal(sourceVersion, v));
        Assert.Equal(sourceTfm, tfm);
        Assert.Equal(sourceConfigProperties, configProperties);
        AssertNoFrameworkElement(node);
    }

}
