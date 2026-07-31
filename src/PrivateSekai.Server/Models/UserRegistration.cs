using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserRegistration
{
    [Key("userId")] public long userId;
    [Key("signature")] public string? signature;
    [Key("platform")] public string? platform;
    [Key("deviceModel")] public string? deviceModel;
    [Key("operatingSystem")] public string? operatingSystem;
    [Key("registeredAt")] public ulong registeredAt;
    [Key("yearOfBirth")] public int yearOfBirth;
    [Key("monthOfBirth")] public int monthOfBirth;
    [Key("dayOfBirth")] public int dayOfBirth;
    [Key("age")] public int age;
    [Key("billableLimitAgeType")] public string? billableLimitAgeType;
}
