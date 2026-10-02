using System.Text.Json.Nodes;

namespace PrivateSekai.Client;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "list")
            {
                foreach (var pair in Operations.All) Console.WriteLine($"{pair.Key,-24} {pair.Value.Method,-6} {pair.Value.Path}");
                return 0;
            }
            if (args.Length == 4 && args[0] == "compare")
            {
                var report = ScenarioRunner.Compare(JsonNode.Parse(File.ReadAllText(args[1]))!.AsObject(), JsonNode.Parse(File.ReadAllText(args[2]))!.AsObject());
                JsonFiles.Write(args[3], report);
                Console.WriteLine("对比报告已写入；差异不能自动判定哪一端正确。");
                return report["complete"]!.GetValue<bool>() ? 0 : 2;
            }
            if (args.Length < 4 || args[0] is not ("plan" or "run"))
            {
                Console.WriteLine("list\nplan|run <config> <scenario> <target[,target]> [--official-write <operation[,operation]>]\ncompare <left.json> <right.json> <report.json>");
                return args.Length == 0 ? 0 : 2;
            }
            var configuration = JsonFiles.Read<ClientConfiguration>(args[1]);
            var scenario = JsonFiles.Read<Scenario>(args[2]);
            var names = args[3].Split(',');
            if (names.Distinct().Count() != names.Length || names.Any(n => !configuration.Targets.ContainsKey(n)))
                throw new InvalidOperationException("目标不存在或重复。");
            var allowed = new HashSet<string>();
            if (args.Length == 6 && args[4] == "--official-write") allowed.UnionWith(args[5].Split(','));
            else if (args.Length != 4) throw new InvalidOperationException("命令参数无效。");
            var targets = names.Select(n => configuration.Targets[n]).ToArray();
            ScenarioRunner.Validate(scenario, targets, allowed);
            for (var i = 0; i < targets.Length; i++) Console.WriteLine($"目标 {i + 1}: {targets[i].Kind}，{scenario.Steps.Count} 个步骤。");
            if (args[0] == "plan") return 0;
            var output = Path.GetFullPath(Path.Combine("tools/captures/mockclient", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]));
            Directory.CreateDirectory(output);
            // 两端串行完成，任一端失败即停止；不会在失败后向另一端继续写入。
            for (var i = 0; i < targets.Length; i++)
            {
                using var client = new ProtocolClient(targets[i], Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
                await ScenarioRunner.Run(client, scenario, Path.Combine(output, $"target-{i + 1}"));
            }
            if (targets.Length == 2)
                for (var i = 1; i <= scenario.Steps.Count; i++)
                    JsonFiles.Write(Path.Combine(output, $"compare-{i:D3}.json"), ScenarioRunner.Compare(
                        JsonNode.Parse(File.ReadAllText(Path.Combine(output, "target-1", $"{i:D3}.json")))!.AsObject(),
                        JsonNode.Parse(File.ReadAllText(Path.Combine(output, "target-2", $"{i:D3}.json")))!.AsObject()));
            Console.WriteLine($"完成。记录目录：{Path.GetRelativePath(Directory.GetCurrentDirectory(), output)}");
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (ex is ClientFailure) Console.Error.WriteLine(ex.Message);
            if (FailureDiagnostics.Transport(ex) is { } transportError)
                Console.Error.WriteLine($"网络错误分类：{transportError.ToJsonString()}");
            Console.Error.WriteLine($"已停止（{ex.GetType().Name}）。未自动重试；检查场景、配置及本地步骤记录。异常正文不输出，以免泄露凭证。");
            return 1;
        }
    }
}
