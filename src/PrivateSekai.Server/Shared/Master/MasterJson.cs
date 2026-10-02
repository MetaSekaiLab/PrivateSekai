using System.IO;
using System.Text.Json;
using PrivateSekai.Protocol;

namespace PrivateSekai.Shared.Master;

internal static class MasterJson
{
    public static T[] LoadTable<T>(string path) where T : class
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T[]>(json, DumpJson.Options)
               ?? throw new InvalidDataException($"Master table is empty or invalid: {path}");
    }
}
