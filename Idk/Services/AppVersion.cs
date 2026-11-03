using System.Reflection;

namespace Idk.Services;

public static class AppVersion
{
    public static string Current
    {
        get
        {
            var version = typeof(AppVersion).Assembly.GetName().Version;
            if (version == null) return "0.1.0";
            return $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        }
    }
}
