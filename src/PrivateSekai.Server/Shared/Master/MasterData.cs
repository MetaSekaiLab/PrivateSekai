extern alias game;

using System;
using game::Sekai;
using PrivateSekai.Config;

namespace PrivateSekai.Shared.Master;

public sealed class MasterData
{
    private readonly MasterTableCache _cache;

    public MasterData(MasterCacheConfig config, string rootPath)
    {
        _cache = new MasterTableCache(config, rootPath);
        WarmPinnedTables(config);
    }

    public MasterTable<T> GetTable<T>(string tableName, Func<T, int>? idSelector = null) where T : class =>
        _cache.GetTable(tableName, idSelector);

    public IDisposable BeginRequest() => _cache.BeginRequest();

    public void ClearCache() => _cache.Clear();

    private void WarmPinnedTables(MasterCacheConfig config)
    {
        foreach (var table in config.PinTables)
        {
            switch (table)
            {
                case "cards":
                    _ = _cache.GetTable<MasterCard>("cards", c => c.id).Rows;
                    break;
                case "gachas":
                    _ = _cache.GetTable<MasterGacha>("gachas", g => g.id).Rows;
                    break;
                case "resourceBoxes":
                    _ = _cache.GetTable<MasterResourceBox>("resourceBoxes", b => b.id).Rows;
                    break;
                case "shopItems":
                    _ = _cache.GetTable<MasterShopItem>("shopItems", i => i.id).Rows;
                    break;
            }
        }
    }
}
