using System.Text.Json;

namespace PrivateSekai.Services.Master;

internal static class MasterJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static T[] LoadTable<T>(string path) where T : class
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T[]>(json, Options)
               ?? throw new InvalidDataException($"Master table is empty or invalid: {path}");
    }
}
