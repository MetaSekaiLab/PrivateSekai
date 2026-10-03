extern alias game;

using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using MessagePack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Mono.Cecil;
using PrivateSekai.Config;
using PrivateSekai.Transport;
using PrivateSekai.Protocol;
using PrivateSekai.Modules.Accounts;
using PrivateSekai.Modules.Home;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;
using Sekai = game::Sekai;

var root = Path.GetFullPath(args.FirstOrDefault() ?? ".");
var dataRoot = args.Length > 2 ? Path.GetFullPath(args[2], root) : Path.Combine(root, "data");
var dumpDirectory = args.Length > 1 ? Path.GetFullPath(args[1], root) : Path.Combine(root, ".re", "DummyDll");
var dump = Path.Combine(dumpDirectory, "Assembly-CSharp.dll");
var manifestPath = Path.Combine(root, "src", "PrivateSekai.Protocol", "obj", "GameDump", "models.json");
using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dump)));
Check(hash == manifest.RootElement.GetProperty("sourceSha256").GetString(), "构建输入指纹");
Check(typeof(Sekai.SuiteUser).Assembly.GetName().Name == "Assembly-CSharp", "使用原始程序集类型");

using var original = AssemblyDefinition.ReadAssembly(dump);
using var compiled = AssemblyDefinition.ReadAssembly(typeof(Sekai.SuiteUser).Assembly.Location);
var compiledTypes = compiled.MainModule.GetTypes().ToDictionary(t => t.FullName);
var modelCount = 0;
var memberCount = 0;
foreach (var name in manifest.RootElement.GetProperty("models").EnumerateArray())
{
    var type = typeof(Sekai.SuiteUser).Assembly.GetType(name.GetString()!, throwOnError: true)!;
    var source = original.MainModule.GetType(type.FullName!.Replace('+', '/'));
    var expectedSignature = Signature(source);
    if (type == typeof(Sekai.UserChallengeLivePlayStatus))
    {
        Check(expectedSignature.Contains("musicVoiceId:System.Int32:musicVoiceId") &&
            !expectedSignature.Any(s => s.StartsWith("isAuto:", StringComparison.Ordinal)), "挑战状态原始契约前置");
        expectedSignature = expectedSignature.Select(s => s == "musicVoiceId:System.Int32:musicVoiceId"
                ? "musicVoiceId:System.Int32:musicVocalId" : s)
            .Append("isAuto:System.Boolean:isAuto").Order(StringComparer.Ordinal).ToArray();
    }
    if (type == typeof(Sekai.UserPresentData))
    {
        Check(!expectedSignature.Any(s => s.StartsWith("grantedAt:", StringComparison.Ordinal)), "礼物原始契约前置");
        expectedSignature = expectedSignature.Append("grantedAt:System.Int64:grantedAt").Order(StringComparer.Ordinal).ToArray();
    }
    Check(expectedSignature.SequenceEqual(Signature(compiledTypes[source.FullName])), $"元数据及已核验修正 {type.FullName}");
    foreach (var union in DumpContract.For(type).Unions)
    {
        var value = Sample(union.Value, 0)!;
        var unionBytes = MessagePackSerializer.Serialize(type, value, DumpSerializer.Options);
        var unionReader = new MessagePackReader(unionBytes);
        Check(unionReader.ReadArrayHeader() == 2 && unionReader.ReadInt32() == union.Key, $"Union 标签 {type.FullName}");
        var unionValue = MessagePackSerializer.Deserialize(type, unionBytes, DumpSerializer.Options)!;
        Check(unionValue.GetType() == union.Value && DumpSerializer.SerializeObject(value).SequenceEqual(DumpSerializer.SerializeObject(unionValue)), $"Union 往返 {type.FullName}");
    }
    if (type.IsAbstract) continue;
    var sample = Sample(type, 0)!;
    var bytes = DumpSerializer.SerializeObject(sample);
    var restored = MessagePackSerializer.Deserialize(type, bytes, DumpSerializer.Options)!;
    Check(bytes.SequenceEqual(DumpSerializer.SerializeObject(restored)), $"往返 {type.FullName}");
    memberCount += DumpContract.For(type).Members.Count;
    modelCount++;
}
Console.WriteLine($"模型：{modelCount} 个具体类型、{memberCount} 个成员的非默认值往返、原始元数据及显式协议修正检查通过。");

foreach (var auto in new[] { false, true })
{
    var status = new Sekai.UserChallengeLivePlayStatus { musicVoiceId = 7, isAuto = auto };
    var statusBytes = DumpSerializer.Serialize(status);
    using var statusJson = JsonDocument.Parse(MessagePackSerializer.ConvertToJson(statusBytes));
    Check(statusJson.RootElement.GetProperty("musicVocalId").GetInt32() == 7 &&
        !statusJson.RootElement.TryGetProperty("musicVoiceId", out _) &&
        statusJson.RootElement.GetProperty("isAuto").GetBoolean() == auto, "挑战参与状态使用官方字段名并保留布尔值");
    var imported = JsonSerializer.Deserialize<Sekai.UserChallengeLivePlayStatus>(statusJson.RootElement.GetRawText(), DumpJson.Options)!;
    Check(imported.musicVoiceId == 7 && imported.isAuto == auto &&
        DumpSerializer.Deserialize<Sekai.UserChallengeLivePlayStatus>(statusBytes).isAuto == auto,
        "挑战参与状态 JSON 导入与 MessagePack 往返一致");
}
var legacyStatus = JsonSerializer.Deserialize<Sekai.UserChallengeLivePlayStatus>("{\"musicVoiceId\":7}", DumpJson.Options)!;
Check(legacyStatus.musicVoiceId == 7, "本地旧 JSON 的 CLR 字段别名仍可读取");

var presentWithTime = JsonSerializer.Deserialize<Sekai.UserPresentData>(
    "{\"presentId\":\"fixture\",\"grantedAt\":1790999824218}", DumpJson.Options)!;
var restoredPresent = DumpSerializer.Deserialize<Sekai.UserPresentData>(DumpSerializer.Serialize(presentWithTime));
Check(restoredPresent.grantedAt == 1790999824218L, "礼物发放时间从 JSON 导入后保留 64 位毫秒值");
Check(JsonSerializer.Deserialize<Sekai.UserPresentData>("{\"presentId\":\"legacy\"}", DumpJson.Options)!.grantedAt == 0,
    "旧礼物缺少发放时间时不推测时间");

var card = new Sekai.UserCard { cardId = 123, userId = 9007199254740993L, level = 7 };
var suite = new Sekai.SuiteUser { userCards = [card], refreshableTypes = [] };
var restoredSuite = DumpSerializer.Deserialize<Sekai.SuiteUser>(DumpSerializer.Serialize(suite));
Check(restoredSuite.userCards[0].userId == card.userId, "64 位 ID");
using (var json = JsonDocument.Parse(MessagePackSerializer.ConvertToJson(DumpSerializer.Serialize(suite))))
{
    Check(!json.RootElement.TryGetProperty("userDecks", out _), "省略 null");
    Check(json.RootElement.GetProperty("refreshableTypes").GetArrayLength() == 0, "保留空数组");
    Check(json.RootElement.GetProperty("userCards")[0].GetProperty("exp").GetInt32() == 0, "保留零值");
}
var profile = new Sekai.CustomProfile.ObjectData { isLock = false };
using (var json = JsonDocument.Parse(MessagePackSerializer.ConvertToJson(DumpSerializer.Serialize(profile))))
{
    Check(!json.RootElement.GetProperty("lock").GetBoolean(), "Key 与成员名不同、保留 false");
    Check(!json.RootElement.TryGetProperty("isLock", out _), "使用网络 Key");
}
var vector = new game::UnityEngine.Vector3 { x = 1.25f, y = -2.5f, z = 3.75f };
var vectorBytes = DumpSerializer.Serialize(vector);
Check(vectorBytes.Length == 16 && vectorBytes[0] == 0x93 && vectorBytes[1] == 0xca, "Vector3 数组和 Float32");
Check(DumpSerializer.Deserialize<game::UnityEngine.Vector3>(vectorBytes).z == vector.z, "值类型成员赋值");
ServerConfig.Load(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["PrivateSekai:AesKey"] = new('k', 16),
    ["PrivateSekai:AesIv"] = new('i', 16),
    ["PrivateSekai:JwtKey"] = new('j', 32),
    ["PrivateSekai:EmptyRequestCiphertext"] = "00",
    ["PrivateSekai:IgnoreInvalidCredential"] = "false",
    ["PrivateSekai:SkipTutorial"] = "false",
    ["PrivateSekai:Debug"] = "false",
    ["PrivateSekai:Port"] = "0",
    ["PrivateSekai:GameVersionDomain"] = "example.invalid",
    ["PrivateSekai:Paths:Template"] = Path.Combine(dataRoot, "template"),
    ["PrivateSekai:Paths:SuiteMasterFile"] = Path.Combine(dataRoot, "suitemasterfile"),
    ["PrivateSekai:Paths:SekaiMasterDbDiff"] = Path.Combine(dataRoot, "sekai-master-db-diff")
}).Build(), root);
var encrypted = PrskCrypto.PrskEnc(suite);
Check(DumpSerializer.SerializeObject(null).SequenceEqual(new byte[] { 0xc0 }), "空对象");
Check(DumpSerializer.SerializeObject(suite).SequenceEqual(DumpSerializer.Serialize(suite)), "动态与强类型序列化一致");
Check(PrskCrypto.PrskDec<Sekai.SuiteUser>(encrypted).userCards[0].userId == card.userId, "加解密协议往返");

var buffer = new ArrayBufferWriter<byte>();
var writer = new MessagePackWriter(buffer);
writer.WriteMapHeader(2);
writer.Write("cardId"); writer.Write(123);
writer.Write("futureField"); writer.WriteArrayHeader(1); writer.Write("futureValue");
writer.Flush();
Check(DumpSerializer.Deserialize<Sekai.UserCard>(buffer.WrittenMemory).cardId == 123, "跳过未知字段");
Console.WriteLine("协议：Key、null、零值、空数组、64 位整数、Float32、未知字段和加解密检查通过。");

var templates = new (string File, Type Type)[]
{
    ("api_system.json", typeof(Sekai.SystemFullResponse)),
    ("api_user_auth.json", typeof(Sekai.UserAuthResponse)),
    ("user_0.json", typeof(Sekai.SuiteUser))
};
foreach (var (file, type) in templates)
{
    var text = File.ReadAllText(Path.Combine(dataRoot, "template", file));
    var value = JsonSerializer.Deserialize(text, type, DumpJson.Options)!;
    var bytes = DumpSerializer.SerializeObject(value);
    var restored = MessagePackSerializer.Deserialize(type, bytes, DumpSerializer.Options)!;
    Check(bytes.SequenceEqual(DumpSerializer.SerializeObject(restored)), $"模板 {file}");
    using var document = JsonDocument.Parse(text);
    var unknown = new SortedSet<string>(StringComparer.Ordinal);
    FindUnknown(document.RootElement, type, "$", unknown);
    if (unknown.Count > 0) Console.WriteLine($"模板 {file} 存在 {unknown.Count} 种未映射字段路径：{string.Join(", ", unknown.Take(12))}");
}
Console.WriteLine("模板：3 个现有模板读取与 MessagePack 往返通过（不代表模板已迁移至当前客户端版本）。");

var masterDirectory = Path.Combine(dataRoot, "sekai-master-db-diff");
var tables = 0;
var rows = 0;
foreach (var member in DumpContract.For(typeof(Sekai.SuiteMaster)).Members)
{
    var path = Path.Combine(masterDirectory, member.Key + ".json");
    if (!File.Exists(path)) continue;
    // JSON master 使用文件名定位；类型完全来自 SuiteMaster。
    var value = JsonSerializer.Deserialize(File.ReadAllText(path), member.Type, DumpJson.Options)!;
    var bytes = DumpSerializer.SerializeObject(value);
    var restored = MessagePackSerializer.Deserialize(member.Type, bytes, DumpSerializer.Options)!;
    Check(bytes.SequenceEqual(DumpSerializer.SerializeObject(restored)), $"master {member.Key}");
    if (value is ICollection collection) rows += collection.Count;
    tables++;
}
Console.WriteLine($"master：{tables} 张本地表、{rows} 行读取与 MessagePack 往返通过。");
var master = new MasterData(ServerConfig.MasterCache, ServerConfig.SekaiMasterDbDiffPath);
var accounts = new AccountTemplates();
var users = new MemoryUserStore();
users.Save(0, accounts.CreateUser(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
var session = new UserSession();
var operations = new UserOperation(users, new UserLocks(), session, TimeProvider.System);
var userBytes = operations.Create(accounts.CreateUser, () =>
{
    new HomeService(session).EnsureShopAreaActionSets();
    return session.BuildSuite();
});
var newUser = DumpSerializer.Deserialize<Sekai.SuiteUser>(userBytes);
var userId = newUser.userRegistration.userId;
Check(newUser.userRegistration.userId == userId && userId != 0, "新用户 ID");
Check(users.Read(0)!.Data.userRegistration.userId == 0, "模板用户未被修改");
Check(DumpSerializer.Deserialize<Sekai.SuiteUser>(DumpSerializer.Serialize(newUser)).userRegistration.userId == userId, "新用户协议往返");
var restoredUserId = userId + 10;
var authController = new AuthController(accounts, TimeProvider.System, operations, session, new HomeService(session));
var authResult = authController.HandleAuthUser(restoredUserId, new Sekai.UserAuthRequest
{
    credential = JwtSignature.GenUserCredential(restoredUserId)
});
Check(authResult is FileContentResult && users.Read(restoredUserId)?.Data.userRegistration.userId == restoredUserId,
    "已验证凭证在认证入口恢复缺失用户");
var restoredAuth = DumpSerializer.Deserialize<Sekai.UserAuthResponse>(((FileContentResult)authResult).FileContents);
Check(!string.IsNullOrEmpty(restoredAuth.sessionToken), "恢复账号返回独立认证响应");
var firstAuth = accounts.GetAuth("sample-session-one");
var secondAuth = accounts.GetAuth("sample-session-two");
Check(firstAuth.sessionToken == "sample-session-one" && secondAuth.sessionToken == "sample-session-two", "认证模板不共享可变响应");
var invalidId = restoredUserId + 1;
Check(authController.HandleAuthUser(invalidId, new Sekai.UserAuthRequest { credential = "invalid-sample" }) is UnauthorizedObjectResult &&
    users.Read(invalidId) == null, "无效凭证不创建账号");
Console.WriteLine("初始化：master 缓存、模板用户加载及新用户创建通过。");
Check(hash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dump))), "原始 DLL 未修改");

static void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("检查失败：" + label);
}

static void FindUnknown(JsonElement json, Type type, string path, ISet<string> unknown)
{
    type = Nullable.GetUnderlyingType(type) ?? type;
    if (json.ValueKind == JsonValueKind.Array)
    {
        var elementType = type.IsArray ? type.GetElementType() : type.IsGenericType ? type.GenericTypeArguments.FirstOrDefault() : null;
        if (elementType != null)
            foreach (var element in json.EnumerateArray()) FindUnknown(element, elementType, path + "[]", unknown);
        return;
    }
    if (json.ValueKind != JsonValueKind.Object || !DumpContract.Supports(type)) return;
    var members = DumpContract.For(type).Members;
    foreach (var property in json.EnumerateObject())
    {
        var member = members.FirstOrDefault(m => Equals(m.Key, property.Name) || m.Member.Name == property.Name);
        if (member == null) unknown.Add(path + "." + property.Name);
        else FindUnknown(property.Value, member.Type, path + "." + property.Name, unknown);
    }
}

static string[] Signature(TypeDefinition type) => type.Fields.Select(f => (f.Name, Type: f.FieldType, Attributes: f.CustomAttributes))
    .Concat(type.Properties.Select(p => (p.Name, Type: p.PropertyType, Attributes: p.CustomAttributes)))
    .Where(m => m.Attributes.Any(a => a.AttributeType.FullName == "MessagePack.KeyAttribute"))
    .Select(m => $"{m.Name}:{m.Type.FullName}:{m.Attributes.Single(a => a.AttributeType.FullName == "MessagePack.KeyAttribute").ConstructorArguments[0].Value}")
    .Order(StringComparer.Ordinal).ToArray();

static object? Sample(Type type, int depth)
{
    if (Nullable.GetUnderlyingType(type) is { } underlying) return Sample(underlying, depth);
    if (type == typeof(string) || type == typeof(object)) return "sample-日本語";
    if (type == typeof(bool)) return true;
    if (type.IsEnum) return Enum.ToObject(type, 1);
    if (type == typeof(DateTime)) return new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    if (type == typeof(DateTimeOffset)) return new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    if (type == typeof(TimeSpan)) return TimeSpan.FromSeconds(17);
    if (type == typeof(Guid)) return Guid.Parse("00000000-0000-0000-0000-000000000001");
    if (type == typeof(float)) return 1.25f;
    if (type == typeof(double)) return -2.5d;
    if (type == typeof(char)) return '語';
    if (type.IsPrimitive || type == typeof(decimal)) return Convert.ChangeType(17, type);
    if (depth > 3) return type.IsValueType ? Activator.CreateInstance(type) : null;
    if (type.IsArray)
    {
        var element = type.GetElementType()!;
        var array = Array.CreateInstance(element, 1);
        array.SetValue(Sample(element, depth + 1), 0);
        return array;
    }
    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
    {
        var list = (IList)Activator.CreateInstance(type)!;
        list.Add(Sample(type.GenericTypeArguments[0], depth + 1));
        return list;
    }
    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
    {
        var dictionary = (IDictionary)Activator.CreateInstance(type)!;
        dictionary.Add(Sample(type.GenericTypeArguments[0], depth + 1)!, Sample(type.GenericTypeArguments[1], depth + 1));
        return dictionary;
    }
    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
    {
        var set = Activator.CreateInstance(type)!;
        type.GetMethod("Add")!.Invoke(set, [Sample(type.GenericTypeArguments[0], depth + 1)]);
        return set;
    }
    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        return Activator.CreateInstance(type, type.GenericTypeArguments.Select(t => Sample(t, depth + 1)).ToArray());
    if (!DumpContract.Supports(type)) throw new NotSupportedException($"需要补充样本：{type}");
    var contract = DumpContract.For(type);
    if (type.IsAbstract) return Sample(contract.Unions.Values.First(), depth + 1);
    var instance = RuntimeHelpers.GetUninitializedObject(type);
    foreach (var member in contract.Members)
    {
        var value = Sample(member.Type, depth + 1);
        member.Set(instance, value);
        var actual = member.Get(instance);
        var equal = value != null && member.Type.IsValueType && DumpContract.Supports(member.Type)
            ? DumpSerializer.SerializeObject(actual!).SequenceEqual(DumpSerializer.SerializeObject(value))
            : Equals(actual, value);
        Check(equal, $"字段或属性存储 {type.FullName}.{member.Member.Name}");
    }
    return instance;
}
