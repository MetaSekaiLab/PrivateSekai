using PrivateSekai.Config;
using PrivateSekai.Client;
using PrivateSekai.Storage;
using PrivateSekai.Transport;

internal static class InheritHttpChecks
{
    public static async Task Run(ProtocolClient original, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        const string passwordVariable = "PRIVATESEKAI_TEST_INHERIT_PASSWORD";
        const string keyVariable = "PRIVATESEKAI_TEST_INHERIT_KEY";
        var oldPassword = Environment.GetEnvironmentVariable(passwordVariable);
        var oldKey = Environment.GetEnvironmentVariable(keyVariable);
        var archive = Path.Combine(directory, "inherit-account.local.json");
        JsonFiles.Write(archive, new TestAccount { Origin = new Uri(config.BaseUrl).GetLeftPart(UriPartial.Authority),
            UserId = 1, Credential = "fixture-credential", CreatedByMockClient = true });
        try
        {
            Environment.SetEnvironmentVariable(passwordVariable, "fixture-inherit-password");
            Environment.SetEnvironmentVariable(keyVariable, ServerConfig.JwtKey);
            using var client = new ProtocolClient(new()
            {
                BaseUrl = config.BaseUrl, AccountFile = archive, RequireRotatingToken = true,
                InheritPasswordEnv = passwordVariable, InheritSigningKeyEnv = keyVariable
            }, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            var scenario = new Scenario { Steps =
            [
                new() { Operation = "system" }, new() { Operation = "inherit-set" },
                new() { Operation = "inherit-preview" }, new() { Operation = "inherit-execute" }
            ] };
            ScenarioRunner.Validate(scenario, [new() { BaseUrl = config.BaseUrl }], new HashSet<string>());
            await ScenarioRunner.Run(client, scenario, Path.Combine(directory, "inherit"));
            var saved = JsonFiles.Read<TestAccount>(archive);
            var server = store.Read(1)!;
            check(saved.InheritId == server.Private.InheritId && saved.InheritPassword == server.Private.InheritPassword,
                "引继设置成功后把 ID 和密码保存在当前账号私有档案");
            check(saved.Credential != "fixture-credential" && JwtSignature.VerifyCredential(saved.Credential, 1),
                "执行引继返回有效的新账号凭证并更新档案");
            var records = string.Concat(Directory.GetFiles(Path.Combine(directory, "inherit"), "*.json").Select(File.ReadAllText));
            check(!records.Contains(saved.InheritId!) && !records.Contains(saved.InheritPassword!) && !records.Contains(saved.Credential),
                "引继 ID、密码、验证 token 和新凭证不进入步骤记录");
            var before = File.ReadAllText(archive);
            Environment.SetEnvironmentVariable(keyVariable, new string('x', 32));
            var rejected = false;
            try { await client.Send(new() { Operation = "inherit-preview" }); }
            catch (ClientFailure) { rejected = true; }
            check(rejected && client.LastHttpStatus == 401 && File.ReadAllText(archive) == before,
                "错误签名被真实服务器拒绝，旧档案保持完整");
            check(!File.Exists(archive + ".pending"), "档案更新完成后不残留临时文件");
        }
        finally
        {
            Environment.SetEnvironmentVariable(passwordVariable, oldPassword);
            Environment.SetEnvironmentVariable(keyVariable, oldKey);
        }
        await original.Send(new() { Operation = "system" });
    }
}
