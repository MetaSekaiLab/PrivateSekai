extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Models;
using PrivateSekai.Protocol;

namespace PrivateSekai.Shared.Users;

/// <summary>一次用户操作的隔离状态和变化集合，随请求作用域创建。</summary>
public sealed class UserSession
{
    private static readonly Dictionary<string, DumpMember> Fields =
        DumpContract.For(typeof(SuiteUser)).Members.ToDictionary(m => (string)m.Key, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Keys =
        DumpContract.For(typeof(SuiteUser)).Members.ToDictionary(m => m.Member.Name, m => (string)m.Key, StringComparer.Ordinal);
    private static readonly string[] BaseFields =
    [
        "now", "refreshableTypes", "userPresents", "unreadUserTopics",
        "userHomeBanners", "userMaterialExchanges", "userGachaCeilExchanges",
        "userRankMatchResult", "userViewableAppeal", "userBillingRefunds",
        "userUnprocessedOrders", "userInformations"
    ];

    private UserState? _state;
    private readonly HashSet<string> _changed = new(StringComparer.Ordinal);

    internal bool IsActive => _state != null;
    internal UserState State => _state ?? throw new InvalidOperationException("No active user operation.");
    public SuiteUser Data => State.Data;
    public NotSuiteData Private => State.Private;
    public long UserId => Data.userRegistration?.userId ?? 0;
    public long Now { get; private set; }

    internal void Begin(UserState state, long now)
    {
        if (IsActive)
            throw new InvalidOperationException("Nested user operations are not supported.");
        _state = state;
        Now = now;
        _changed.Clear();
        _changed.UnionWith(Data.refreshableTypes ?? []);
        Data.refreshableTypes = [];
    }

    internal void End()
    {
        _state = null;
        _changed.Clear();
        Now = 0;
    }

    public void MarkChanged(string fieldName)
    {
        _ = State;
        if (!Keys.TryGetValue(fieldName, out var key))
            throw new ArgumentException($"Unknown SuiteUser field: {fieldName}", nameof(fieldName));
        _changed.Add(key);
    }

    public void MarkChanged(IEnumerable<string> fieldNames)
    {
        foreach (var name in fieldNames)
            MarkChanged(name);
    }

    // 仅在最外层映射响应时读取；读取不会消耗其他模块的变化。
    public SuiteUser BuildRefresh(IEnumerable<string>? excludedFields = null)
    {
        NormalizeEventBreakTime();
        var fields = new HashSet<string>(BaseFields, StringComparer.Ordinal);
        fields.UnionWith(_changed);
        if (excludedFields != null)
            fields.ExceptWith(excludedFields);
        return SelectFields(fields);
    }

    public SuiteUser BuildParts(IEnumerable<string>? names)
    {
        if (names == null)
            return BuildRefresh();
        var fields = new List<string>();
        foreach (var name in names)
        {
            switch (name)
            {
                case "user_event_break_time": fields.Add("userEventBreakTime"); break;
                case "user_friend": fields.Add("userFriends"); break;
                default: return BuildRefresh();
            }
        }
        NormalizeEventBreakTime();
        return SelectFields(fields);
    }

    public SuiteUser BuildSuite()
    {
        NormalizeEventBreakTime();
        Data.now = Now;
        return Data;
    }

    public void NormalizeEventBreakTime()
    {
        var value = Data.userEventBreakTime;
        if (value != null && (value.lastDecreaseAt <= 0 || value.lastDecreaseAt == 1188486000000L))
        {
            value.lastDecreaseAt = Now;
            value.playTimeUsedMillis = 0;
        }
    }

    private SuiteUser SelectFields(IEnumerable<string> names)
    {
        var result = new SuiteUser { now = Now, refreshableTypes = [] };
        foreach (var name in names)
        {
            if (Fields.TryGetValue(name, out var field) && field.Get(Data) is { } value)
                field.Set(result, value);
        }
        result.now = Now;
        return result;
    }
}
