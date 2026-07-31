using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserAreaStatus : IMessagePackSerializationCallbackReceiver
{
    public const string AREA_STATUS_UNRELEASED = "unreleased";
    public const string AREA_STATUS_RELEASED = "released";

    [IgnoreMember]
    public UserAreaStatusType Type =>
        status switch
        {
            AREA_STATUS_UNRELEASED => UserAreaStatusType.unreleased,
            AREA_STATUS_RELEASED => UserAreaStatusType.released,
            _ => UserAreaStatusType.undefined
        };

    [Key("areaId")] public int areaId;
    [Key("status")] public string? status;
    [Key("userAreaPlaylistStatus")] public UserAreaPlaylistStatus? userAreaPlaylistStatus;

    public void OnBeforeSerialize()
    {
    }

    public void OnAfterDeserialize()
    {
    }
}
