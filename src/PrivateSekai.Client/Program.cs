using System.Text.Json.Nodes;

namespace PrivateSekai.Client;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["realtime-transfer", var transferOwner, var transferGuest, var transferTarget, var transferMaster, var transferLobby,
                var transferMusic, var transferDifficulty, var transferVocal, var transferPerformance, "--official-write",
                "auth,reserve-room,private-room-number,private-room-update,create-room,join-room,start-live,clear-live,leave-room"])
                return await Realtime.RealtimeProbe.Run(transferOwner, transferTarget, Path.Combine(transferMaster, "clientConfigs.json"),
                    int.Parse(transferLobby, System.Globalization.CultureInfo.InvariantCulture), transferMaster, transferGuest,
                    Realtime.MultiLiveSong.Read(transferMaster, int.Parse(transferMusic, System.Globalization.CultureInfo.InvariantCulture),
                        transferDifficulty, int.Parse(transferVocal, System.Globalization.CultureInfo.InvariantCulture)),
                    Realtime.MultiLivePerformance.Read(transferPerformance), transferOwnership: true) ? 0 : 2;
            if (args is ["realtime-replay", var observerOwner, var observerGuest, var observerTarget, var observerMaster, var observerLobby,
                var observerMusic, var observerDifficulty, var observerVocal, var observerPerformance, "--observer", var observerConfig, "--official-write",
                "auth,reserve-room,private-room-number,private-room-update,create-room,join-room,start-live,clear-live,recreate-room,leave-room"])
                return await Realtime.RealtimeProbe.Run(observerOwner, observerTarget, Path.Combine(observerMaster, "clientConfigs.json"),
                    int.Parse(observerLobby, System.Globalization.CultureInfo.InvariantCulture), observerMaster, observerGuest,
                    Realtime.MultiLiveSong.Read(observerMaster, int.Parse(observerMusic, System.Globalization.CultureInfo.InvariantCulture),
                        observerDifficulty, int.Parse(observerVocal, System.Globalization.CultureInfo.InvariantCulture)),
                    Realtime.MultiLivePerformance.Read(observerPerformance), true, observerConfig) ? 0 : 2;
            if (args is ["realtime-replay", var replayOwner, var replayGuest, var replayTarget, var replayMaster, var replayLobby,
                var replayMusic, var replayDifficulty, var replayVocal, var replayPerformance, "--official-write",
                "auth,reserve-room,private-room-number,private-room-update,create-room,join-room,start-live,clear-live,recreate-room,leave-room"])
                return await Realtime.RealtimeProbe.Run(replayOwner, replayTarget, Path.Combine(replayMaster, "clientConfigs.json"),
                    int.Parse(replayLobby, System.Globalization.CultureInfo.InvariantCulture), replayMaster, replayGuest,
                    Realtime.MultiLiveSong.Read(replayMaster, int.Parse(replayMusic, System.Globalization.CultureInfo.InvariantCulture),
                        replayDifficulty, int.Parse(replayVocal, System.Globalization.CultureInfo.InvariantCulture)),
                    Realtime.MultiLivePerformance.Read(replayPerformance), true) ? 0 : 2;
            if (args is ["realtime-play", var playOwner, var playGuest, var playTarget, var playMaster, var playLobby,
                var playMusic, var playDifficulty, var playVocal, var performancePath, "--official-write",
                "auth,reserve-room,private-room-number,private-room-update,create-room,join-room,start-live,clear-live,leave-room"])
                return await Realtime.RealtimeProbe.Run(playOwner, playTarget, Path.Combine(playMaster, "clientConfigs.json"),
                    int.Parse(playLobby, System.Globalization.CultureInfo.InvariantCulture), playMaster, playGuest,
                    Realtime.MultiLiveSong.Read(playMaster, int.Parse(playMusic, System.Globalization.CultureInfo.InvariantCulture),
                        playDifficulty, int.Parse(playVocal, System.Globalization.CultureInfo.InvariantCulture)),
                    Realtime.MultiLivePerformance.Read(performancePath)) ? 0 : 2;
            if (args is ["realtime-start", var liveOwner, var liveGuest, var liveTarget, var liveMaster, var liveLobby,
                var music, var difficulty, var vocal, "--official-write",
                "auth,reserve-room,private-room-number,private-room-update,create-room,join-room,start-live,leave-room"])
                return await Realtime.RealtimeProbe.Run(liveOwner, liveTarget, Path.Combine(liveMaster, "clientConfigs.json"),
                    int.Parse(liveLobby, System.Globalization.CultureInfo.InvariantCulture), liveMaster, liveGuest,
                    Realtime.MultiLiveSong.Read(liveMaster, int.Parse(music, System.Globalization.CultureInfo.InvariantCulture),
                        difficulty, int.Parse(vocal, System.Globalization.CultureInfo.InvariantCulture))) ? 0 : 2;
            if (args is ["realtime-party", var ownerConfig, var guestConfig, var partyTarget, var partyMaster, var partyLobby,
                "--official-write", "auth,reserve-room,private-room-number,private-room-update,create-room,join-room,leave-room"])
                return await Realtime.RealtimeProbe.Run(ownerConfig, partyTarget, Path.Combine(partyMaster, "clientConfigs.json"),
                    int.Parse(partyLobby, System.Globalization.CultureInfo.InvariantCulture), partyMaster, guestConfig) ? 0 : 2;
            if (args is ["realtime-create", var createConfig, var createTarget, var createMaster, var createLobby,
                "--official-write", "auth,reserve-room,private-room-number,private-room-update,create-room,leave-room"])
                return await Realtime.RealtimeProbe.Run(createConfig, createTarget, Path.Combine(createMaster, "clientConfigs.json"),
                    int.Parse(createLobby, System.Globalization.CultureInfo.InvariantCulture), createMaster) ? 0 : 2;
            if (args is ["realtime-check", var realtimeConfig, var realtimeTarget, "--official-write", "auth"])
                return await Realtime.RealtimeProbe.Run(realtimeConfig, realtimeTarget) ? 0 : 2;
            if (args is ["realtime-reserve", var reserveConfig, var reserveTarget, var clientConfigs, "--official-write", "auth,reserve-room"])
                return await Realtime.RealtimeProbe.Run(reserveConfig, reserveTarget, clientConfigs) ? 0 : 2;
            if (args is ["realtime-room", var roomConfig, var roomTarget, var roomConfigs, var lobby, "--official-write", "auth,reserve-room,private-room-number,private-room-update"])
                return await Realtime.RealtimeProbe.Run(roomConfig, roomTarget, roomConfigs,
                    int.Parse(lobby, System.Globalization.CultureInfo.InvariantCulture)) ? 0 : 2;
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
            if (args is ["audit-live-rewards", var capturePath, var boxesPath, var reportPath])
            {
                var report = LiveRewardAudit.Analyze(JsonNode.Parse(File.ReadAllText(capturePath))!.AsObject(),
                    JsonNode.Parse(File.ReadAllText(boxesPath))!.AsArray());
                JsonFiles.Write(reportPath, report);
                Console.WriteLine("奖励资源箱候选与资源增量报告已写入；候选不能证明抽取规则或概率。");
                return report["balancesVerified"]!.GetValue<bool>() ? 0 : 2;
            }
            if (args.Length < 4 || args[0] is not ("plan" or "run"))
            {
                Console.WriteLine("realtime-transfer <ownerConfig> <guestConfig> <target> <master> <lobbyId> <musicId> <difficulty> <vocalId> <performance.json> --official-write auth,reserve-room,private-room-number,private-room-update,create-room,join-room,start-live,clear-live,leave-room");
                Console.WriteLine("realtime-replay <ownerConfig> <guestConfig> <target> <master> <lobbyId> <musicId> <difficulty> <vocalId> <performance.json> [--observer <config>] --official-write auth,reserve-room,private-room-number,private-room-update,create-room,join-room,start-live,clear-live,recreate-room,leave-room");
                Console.WriteLine("realtime-play <ownerConfig> <guestConfig> <target> <master> <lobbyId> <musicId> <difficulty> <vocalId> <performance.json> --official-write auth,reserve-room,private-room-number,private-room-update,create-room,join-room,start-live,clear-live,leave-room");
                Console.WriteLine("realtime-start <ownerConfig> <guestConfig> <target> <master> <lobbyId> <musicId> <difficulty> <vocalId> --official-write auth,reserve-room,private-room-number,private-room-update,create-room,join-room,start-live,leave-room");
                Console.WriteLine("realtime-party <ownerConfig> <guestConfig> <target> <master> <lobbyId> --official-write auth,reserve-room,private-room-number,private-room-update,create-room,join-room,leave-room");
                Console.WriteLine("realtime-create <config> <target> <master> <lobbyId> --official-write auth,reserve-room,private-room-number,private-room-update,create-room,leave-room");
                Console.WriteLine("realtime-room <config> <target> <clientConfigs.json> <lobbyId> --official-write auth,reserve-room,private-room-number,private-room-update");
                Console.WriteLine("list\nplan|run <config> <scenario> <target[,target]> [--official-write <operation[,operation]>]\nrealtime-check <config> <target> --official-write auth\nrealtime-reserve <config> <target> <clientConfigs.json> --official-write auth,reserve-room\ncompare <left.json> <right.json> <report.json>\naudit-live-rewards <capture.json> <resourceBoxes.json> <report.json>");
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
