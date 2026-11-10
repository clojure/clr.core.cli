using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cljr;

internal static class RuntimeConfigGenerator
{
    const string NetCoreApp = "Microsoft.NETCore.App";

    public static string Generate(string sourceJson, IEnumerable<string> frameworks)
    {
        var node = JsonNode.Parse(sourceJson) ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json");
        var optionsNode = node["runtimeOptions"] as JsonObject ?? throw new InvalidOperationException("Failed to parse runtimeconfig.json, missing runtimeOptions");
        var frameworkNode = optionsNode["framework"];
        var frameworksNode = optionsNode["frameworks"] as JsonArray;

        if (frameworkNode is null && frameworksNode is null)
        {
            throw new InvalidOperationException("Failed to parse runtimeconfig.json, missing both 'framework' and 'frameworks' properties.");
        }

        List<JsonNode> frameworkList = [];
        HashSet<string> frameworkSet = new(StringComparer.OrdinalIgnoreCase);
        string frameworkVersion = string.Empty;

        if (frameworkNode is not null)
        {
            string frameworkName = frameworkNode["name"]?.GetValue<string>() ?? string.Empty;
            frameworkVersion = frameworkNode["version"]?.GetValue<string>() ?? string.Empty;

            // We make a copy so we don't have to worry about "node already has a parent" exception when we add it to the new array.
            var fwNode = new JsonObject
            {
                ["name"] = frameworkName,
                ["version"] = frameworkVersion
            };

            frameworkList.Add(fwNode);
            frameworkSet.Add(frameworkName);
        }
        else
        {
            // from an earlier condition, we know frameworksNode is not null here.
            foreach (var fwNode in frameworksNode!)
            {
                var fwName = fwNode?["name"]?.GetValue<string>() ?? string.Empty;
                if (String.Equals(fwName, NetCoreApp, StringComparison.OrdinalIgnoreCase))
                    frameworkVersion = fwNode?["version"]?.GetValue<string>() ?? string.Empty;
                frameworkSet.Add(fwName);
            }
        }

        if (string.IsNullOrEmpty(frameworkVersion))
        {
            throw new InvalidOperationException("Failed to determine the version of Microsoft.NETCore.App from runtimeconfig.json.");
        }

        foreach (var fw in frameworks)
        {
            if (frameworkSet.Contains(fw) || String.Equals(NetCoreApp, fw, StringComparison.OrdinalIgnoreCase))
                continue;

            var fwNode = new JsonObject
            {
                ["name"] = fw,
                ["version"] = frameworkVersion
            };
            frameworkList.Add(fwNode);
            frameworkSet.Add(fw);
        }

        optionsNode.Remove("framework");

        if (frameworkList.Count > 0)
        {
            var existingFrameworksNode = optionsNode["frameworks"];
            if (existingFrameworksNode is JsonArray existingArray)
            {
                foreach (var fwNode in frameworkList)
                {
                    existingArray.Add(fwNode);
                }
            }
            else
            {
                var arrayNode = new JsonArray([.. frameworkList]);
                optionsNode["frameworks"] = arrayNode;
            }
        }

        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
