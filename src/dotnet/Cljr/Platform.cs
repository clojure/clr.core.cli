using System.Reflection;
using System.Runtime.InteropServices;

namespace Cljr;

public static class Platform
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static string HomeDir => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static readonly string Version = StripVersionMetadata(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion) ?? "unknown";

    private static string? StripVersionMetadata(string? version)
    {
        if (version == null) return null;
        int plusIndex = version.IndexOf('+');
        if (plusIndex >= 0)
            return version[..plusIndex];
        return version;
    }
}