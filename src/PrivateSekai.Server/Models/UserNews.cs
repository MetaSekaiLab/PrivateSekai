using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserNews
{
    public const string TAG_INFORMATION = "information";
    public const string TAG_EVENT = "event";
    public const string TAG_GACHA = "gacha";
    public const string TAG_MUSIC = "music";
    public const string TAG_CAMPAIGN = "campaign";
    public const string TAG_BUG = "bug";
    public const string TAG_UPDATE = "update";

    public const string TYPE_NEWS = "normal";
    public const string TYPE_BUG = "bug";
    public const string TYPE_CONTENT = "content";

    public const string BROWSE_INTERNAL = "internal";
    public const string BROWSE_EXTERNAL = "external";

    public const string IOS = "iOS";
    public const string ANDROID = "Android";
    public const string ALL = "all";

    [Key("id")] public int id;
    [Key("seq")] public int seq;
    [Key("displayOrder")] public int displayOrder;
    [Key("informationType")] public string? informationType;
    [Key("informationTag")] public string? informationTag;
    [Key("browseType")] public string? browseType;
    [Key("title")] public string? title;
    [Key("path")] public string? path;
    [Key("startAt")] public long startAt;
    [Key("endAt")] public long? endAt;
    [Key("bannerAssetbundleName")] public string? bannerAssetbundleName;
    [Key("platform")] public string? platform;
}
