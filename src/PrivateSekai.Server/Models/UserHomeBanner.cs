using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserHomeBanner
{
    [Key("homeBannerId")] public int homeBannerId;
    [Key("seq")] public int seq;
    [Key("name")] public string? name;
    [Key("assetbundleName")] public string? assetbundleName;
    [Key("homeBannerType")] public string? homeBannerType;
    [Key("transitionDestinationType")] public string? transitionDestinationType;
    [Key("transitionDestinationId")] public int transitionDestinationId;
    [Key("startAt")] public long startAt;
    [Key("endAt")] public long endAt;
    [Key("fromUserRank")] public int fromUserRank;
    [Key("toUserRank")] public int toUserRank;
    [Key("url")] public string? url;
}
