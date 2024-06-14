using System.Reflection;
using System.Runtime.InteropServices;

namespace Cljr;

public static class Platform
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static string HomeDir => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
}