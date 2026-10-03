using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2)
    throw new ArgumentException("需要输入 DummyDll 目录和构建输出目录。");

var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("构建输出不能覆盖原始 dump。");
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(input);
using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(input, "Assembly-CSharp.dll"),
    new ReaderParameters { AssemblyResolver = resolver });
var module = assembly.MainModule;
var models = module.GetTypes().Where(t => Has(t, "MessagePack.MessagePackObjectAttribute")).ToArray();
if (!models.Any(t => t.FullName == "Sekai.SuiteUser") || !models.Any(t => t.FullName == "Sekai.SuiteMaster"))
    throw new InvalidDataException("dump 缺少 SuiteUser 或 SuiteMaster。");

foreach (var type in models)
{
    var implicitKeys = type.CustomAttributes.Single(a => a.AttributeType.FullName == "MessagePack.MessagePackObjectAttribute")
        .ConstructorArguments.FirstOrDefault().Value is true;
    var properties = type.Properties.Where(p => Has(p, "MessagePack.KeyAttribute")
        || (implicitKeys && p.GetMethod is { IsPublic: true, IsStatic: false } && !Has(p, "MessagePack.IgnoreMemberAttribute"))).ToArray();
    foreach (var field in type.Fields.ToArray())
    {
        if (!field.IsLiteral && !Has(field, "MessagePack.KeyAttribute")
            && !(implicitKeys && field.IsPublic && !field.IsStatic && !Has(field, "MessagePack.IgnoreMemberAttribute")))
            type.Fields.Remove(field);
        else
        {
            if (!field.IsLiteral) field.IsInitOnly = false;
            CleanAttributes(field);
        }
    }
    CleanAttributes(type);
    // DummyDll 的显式接口实现缺少 CLR 所需的绑定。模型只承载数据，不执行客户端回调。
    type.Interfaces.Clear();
    type.Methods.Clear();
    type.Properties.Clear();
    foreach (var property in properties)
    {
        CleanAttributes(property);
        if (property.Parameters.Count != 0) throw new InvalidDataException($"不支持带 Key 的索引器：{type.FullName}.{property.Name}");
        var backingName = "<" + property.Name + ">k__BackingField";
        var backing = type.Fields.FirstOrDefault(f => f.Name == backingName);
        if (backing == null)
        {
            backing = new FieldDefinition(backingName, FieldAttributes.Private, property.PropertyType);
            type.Fields.Add(backing);
        }
        var getter = new MethodDefinition("get_" + property.Name,
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, property.PropertyType);
        var il = getter.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, backing);
        il.Emit(OpCodes.Ret);
        var setter = new MethodDefinition("set_" + property.Name,
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, module.TypeSystem.Void);
        setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, property.PropertyType));
        il = setter.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, backing);
        il.Emit(OpCodes.Ret);
        property.GetMethod = getter;
        property.SetMethod = setter;
        property.OtherMethods.Clear();
        type.Methods.Add(getter);
        type.Methods.Add(setter);
        type.Properties.Add(property);
    }
    if (!type.IsValueType)
    {
        var constructor = new MethodDefinition(".ctor",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        var baseConstructor = new MethodReference(".ctor", module.TypeSystem.Void, type.BaseType) { HasThis = true };
        var il = constructor.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, baseConstructor);
        il.Emit(OpCodes.Ret);
        type.Methods.Add(constructor);
    }
}

// 官方挑战参与状态使用 musicVocalId，并包含 isAuto；仅修正构建副本。
var challengeStatus = module.GetType("Sekai.UserChallengeLivePlayStatus");
var vocal = challengeStatus.Fields.Single(f => f.Name == "musicVoiceId");
var vocalKey = vocal.CustomAttributes.Single(a => a.AttributeType.FullName == "MessagePack.KeyAttribute");
if (vocal.FieldType.FullName != "System.Int32" ||
    vocalKey.ConstructorArguments.Single().Value is not "musicVoiceId" ||
    challengeStatus.Fields.Any(f => f.Name == "isAuto"))
    throw new InvalidDataException("挑战参与状态 dump 已变化，请重新核验协议修正。");
vocalKey.ConstructorArguments[0] = new CustomAttributeArgument(module.TypeSystem.String, "musicVocalId");
var isAuto = new FieldDefinition("isAuto", FieldAttributes.Public, module.TypeSystem.Boolean);
var autoKey = new CustomAttribute(vocalKey.Constructor);
autoKey.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, "isAuto"));
isAuto.CustomAttributes.Add(autoKey);
challengeStatus.Fields.Add(isAuto);

// 官方礼物包含发放时间；仅补充构建副本，保留原始 dump。
var presentData = module.GetType("Sekai.UserPresentData");
if (presentData.Fields.Any(f => f.Name == "grantedAt"))
    throw new InvalidDataException("礼物 dump 已变化，请重新核验发放时间字段。");
var grantedAt = new FieldDefinition("grantedAt", FieldAttributes.Public, module.TypeSystem.Int64);
var grantedAtKey = new CustomAttribute(vocalKey.Constructor);
grantedAtKey.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, "grantedAt"));
grantedAt.CustomAttributes.Add(grantedAtKey);
presentData.Fields.Add(grantedAt);

Directory.CreateDirectory(output);
var dll = Path.Combine(output, "Assembly-CSharp.dll");
assembly.Write(dll + ".tmp");
File.Move(dll + ".tmp", dll, overwrite: true);
File.WriteAllText(Path.Combine(output, "models.json"), JsonSerializer.Serialize(new
{
    sourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(input, "Assembly-CSharp.dll")))),
    protocolAdjustments = new[]
    {
        "Sekai.UserChallengeLivePlayStatus: musicVoiceId key -> musicVocalId; add isAuto:Boolean",
        "Sekai.UserPresentData: add grantedAt:Int64"
    },
    models = models.Select(t => t.FullName.Replace('/', '+')).Order(StringComparer.Ordinal)
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"已为 {models.Length} 个 dump 模型修复 CLR 接口和属性存储；原始 DLL 未修改。");

static bool Has(ICustomAttributeProvider member, string name) => member.CustomAttributes.Any(a => a.AttributeType.FullName == name);

static void CleanAttributes(ICustomAttributeProvider member)
{
    foreach (var attribute in member.CustomAttributes.ToArray())
        if (attribute.AttributeType.FullName is not ("MessagePack.MessagePackObjectAttribute" or "MessagePack.KeyAttribute"
            or "MessagePack.IgnoreMemberAttribute" or "MessagePack.UnionAttribute" or "MessagePack.MessagePackFormatterAttribute"))
            member.CustomAttributes.Remove(attribute);
}
