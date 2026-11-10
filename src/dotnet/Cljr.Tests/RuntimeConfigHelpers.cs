using System.Text.Json.Nodes;

namespace Cljr.Tests;

public static class RuntimeConfigHelpers
{
    public static void ExtractNamesAndVersions(JsonNode node, out List<string> names, out List<string> versions)
    {
        var options = (node?["runtimeOptions"]) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json, missing runtimeOptions");
        var frameworks = options["frameworks"] as JsonArray;
        var frameworksArray = frameworks?.ToArray() ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json, missing frameworks array");
        names = [.. frameworksArray.Select(fw => fw?["name"]?.GetValue<string>() ?? "unknown")];
        versions = [.. frameworksArray.Select(fw => fw?["version"]?.GetValue<string>() ?? "unknown")];
    }

    public static string ExtractTfm(JsonNode node)
    {
        var options = (node?["runtimeOptions"]) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json, missing runtimeOptions");
        return options["tfm"]?.GetValue<string>() ?? "unknown";
    }

    public static Dictionary<string, string> ExtractConfigProperties(JsonNode node)
    {
        var options = (node?["runtimeOptions"]) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json, missing runtimeOptions");
        var configProperties = options["configProperties"] as JsonObject;
        return configProperties?.ToDictionary(kv => kv.Key, kv => kv.Value?.GetValue<object>().ToString() ?? "unknown") ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json, missing configProperties");
    }

    public static void AssertNoFrameworkElement(JsonNode node)
    {
        var options = (node?["runtimeOptions"]) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json, missing runtimeOptions");
        Assert.Null(options["framework"]);
    }

}
