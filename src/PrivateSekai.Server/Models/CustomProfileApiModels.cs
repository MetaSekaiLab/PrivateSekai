using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserSaveCustomProfileRequest
{
    [Key("name")] public string? name;
    [Key("customProfileCardOrders")] public List<UserCustomProfileCardOrder>? customProfileCardOrders;
}

[MessagePackObject]
public class UserCustomProfileCardOrder
{
    [Key("customProfileId")] public int customProfileId;
    [Key("customProfileCardId")] public int customProfileCardId;
    [Key("seq")] public int seq;
}

[MessagePackObject]
public class UserSaveCustomProfileCardRequest
{
    [Key("thumbnail")] public string? thumbnail;
    [Key("customProfileCard")] public ProfileCardData? customProfileCard;
}

[MessagePackObject]
public class PostCustomProfileCommunityReportRequest
{
    [Key("userReportReason")] public UserReportReason? userReportReason;
}

[MessagePackObject]
public class UserReportReason
{
    [Key("userReportReasonTypes")] public string[]? userReportReasonTypes;
    [Key("userReportLocation")] public string? userReportLocation;
}

[MessagePackObject]
public class EmptyResponse
{
}
