using System.IO;

namespace ScratchShell.Services;

internal static class CommonService
{
    internal static T GetEnumValue<T>(string value) where T : struct, Enum
    {
        return Enum.TryParse<T>(value, out var type)
                        ? type
                        : default(T);
    }

    internal static string GetUserPath()
    {
        var userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string path = Path.Combine(userPath, ".ScratchShell");
        if (Path.Exists(path))
        {
            return path;
        }
        else
        {
            Directory.CreateDirectory(path);
            return path;
        }
    }
}