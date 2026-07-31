using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserActionSet
{
    public const string ACTION_SET_STATUS_UNREAD = "unread";
    public const string ACTION_SET_STATUS_ALREADY_READ = "already_read";

    [Key("id")] public int id;
    [Key("status")] public string? status;
}
