extern alias game;

using System.Text.Json;
using game::Sekai;
using PrivateSekai.Models;
using PrivateSekai.Protocol;

namespace PrivateSekai.Shared.Users;

public sealed class UserState
{
    public SuiteUser Data { get; init; } = new();
    public NotSuiteData Private { get; init; } = new();

    public UserState DeepClone() => new()
    {
        Data = DumpSerializer.Deserialize<SuiteUser>(DumpSerializer.Serialize(Data)),
        Private = JsonSerializer.Deserialize<NotSuiteData>(
            JsonSerializer.SerializeToUtf8Bytes(Private, DumpJson.Options), DumpJson.Options)!
    };
}
