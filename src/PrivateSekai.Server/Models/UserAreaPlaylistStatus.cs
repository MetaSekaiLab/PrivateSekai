using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserAreaPlaylistStatus
{
    public const string AREA_PLAYLIST_STATUS_UNRELEASED = "unreleased";
    public const string AREA_PLAYLIST_STATUS_RELEASED = "released";

    [Key("areaPlaylistId")] public int areaPlaylistId;
    [Key("status")] public string? status;
}
