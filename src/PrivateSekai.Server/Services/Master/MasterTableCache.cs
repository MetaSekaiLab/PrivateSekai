using System.Collections.Concurrent;
using PrivateSekai.Config;

namespace PrivateSekai.Services.Master;

public sealed class MasterTableCache
{
    private readonly MasterCacheConfig _config;
    private readonly string _rootPath;
    private readonly HashSet<string> _pinnedTables;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _loadLocks = new();
    private readonly object _sync = new();
    private readonly LinkedList<string> _lru = new();
    private readonly Dictionary<string, LinkedListNode<string>> _lruNodes = new();
    private readonly Dictionary<string, CachedTable> _tables = new();
    private readonly AsyncLocal<HashSet<string>?> _requestPins = new();

    private long _loadedBytes;

    public MasterTableCache(MasterCacheConfig config, string rootPath)
    {
        _config = config;
        _rootPath = rootPath;
        _pinnedTables = new HashSet<string>(config.PinTables, StringComparer.Ordinal);
    }

    public IDisposable BeginRequest()
    {
        _requestPins.Value = new HashSet<string>(StringComparer.Ordinal);
        return new RequestScope(() => _requestPins.Value = null);
    }

    public MasterTable<T> GetTable<T>(string tableName, Func<T, int>? idSelector = null) where T : class
    {
        _requestPins.Value?.Add(tableName);

        if (TryGetCached(tableName, out var cached))
        {
            Touch(tableName);
            return (MasterTable<T>)cached.Table;
        }

        var gate = _loadLocks.GetOrAdd(tableName, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {
            if (TryGetCached(tableName, out cached))
            {
                Touch(tableName);
                return (MasterTable<T>)cached.Table;
            }

            var path = Path.Combine(_rootPath, tableName + ".json");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Master table not found: {path}", path);

            var sourceBytes = new FileInfo(path).Length;
            EnsureCapacity(tableName, sourceBytes * 2);

            var rows = MasterJson.LoadTable<T>(path);
            var table = new MasterTable<T>(tableName, rows, sourceBytes, idSelector);
            Register(tableName, table, table.EstimatedBytes);
            return table;
        }
        finally
        {
            gate.Release();
        }
    }

    public void WarmTable(string tableName, Func<MasterTableCache, object> loader) =>
        _ = loader(this);

    public void Clear()
    {
        lock (_sync)
        {
            _tables.Clear();
            _lru.Clear();
            _lruNodes.Clear();
            _loadedBytes = 0;
        }
    }

    private bool TryGetCached(string tableName, out CachedTable cached)
    {
        lock (_sync)
            return _tables.TryGetValue(tableName, out cached!);
    }

    private void Register(string tableName, object table, long estimatedBytes)
    {
        lock (_sync)
        {
            _tables[tableName] = new CachedTable(table, estimatedBytes);
            _loadedBytes += estimatedBytes;
            TouchLocked(tableName);
        }
    }

    private void EnsureCapacity(string incomingTable, long incomingBytes)
    {
        lock (_sync)
        {
            if (_tables.ContainsKey(incomingTable))
                return;

            while ((_loadedBytes + incomingBytes > _config.MaxLoadedBytes ||
                    _tables.Count >= _config.MaxLoadedTables) &&
                   TryEvictOneLocked(incomingTable))
            {
            }
        }
    }

    private bool TryEvictOneLocked(string incomingTable)
    {
        for (var node = _lru.First; node != null; node = node.Next)
        {
            var candidate = node.Value;
            if (!CanEvict(candidate, incomingTable))
                continue;

            if (!_tables.Remove(candidate, out var removed))
                continue;

            _loadedBytes = Math.Max(0, _loadedBytes - removed.EstimatedBytes);
            _lru.Remove(node);
            _lruNodes.Remove(candidate);
            return true;
        }

        if (_pinnedTables.Contains(incomingTable) || _requestPins.Value?.Contains(incomingTable) == true)
            return false;

        throw new InvalidOperationException(
            $"Master cache budget exceeded and no evictable table found while loading '{incomingTable}'.");
    }

    private bool CanEvict(string tableName, string incomingTable)
    {
        if (tableName == incomingTable)
            return false;
        if (_pinnedTables.Contains(tableName))
            return false;
        return _requestPins.Value?.Contains(tableName) != true;
    }

    private void Touch(string tableName)
    {
        lock (_sync)
            TouchLocked(tableName);
    }

    private void TouchLocked(string tableName)
    {
        if (_lruNodes.Remove(tableName, out var node))
            _lru.Remove(node);

        _lruNodes[tableName] = _lru.AddLast(tableName);
    }

    private sealed class CachedTable(object table, long estimatedBytes)
    {
        public object Table { get; } = table;
        public long EstimatedBytes { get; } = estimatedBytes;
    }

    private sealed class RequestScope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
