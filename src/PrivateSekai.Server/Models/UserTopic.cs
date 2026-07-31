using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserTopic
{
    public const string TOPIC_STATUS_CAN_NOT_READ = "can_not_read";
    public const string TOPIC_STATUS_UNREAD = "unread";
    public const string TOPIC_STATUS_ALREADY_READY = "already_read";

    [Key("userId")] public long userId;
    [Key("topicId")] public int topicId;
    [Key("topicStatus")] public string? topicStatus;
}
