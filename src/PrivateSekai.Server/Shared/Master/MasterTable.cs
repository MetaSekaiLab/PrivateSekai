using System;
using System.Collections.Generic;
namespace PrivateSekai.Shared.Master;

public sealed class MasterTable<T> where T : class
{
    public MasterTable(string name, T[] rows, long sourceFileBytes, Func<T, int>? idSelector = null)
    {
        Name = name;
        Rows = Array.AsReadOnly(rows);
        EstimatedBytes = Math.Max(sourceFileBytes * 2, rows.LongLength * 128);
        if (idSelector != null)
        {
            var map = new Dictionary<int, T>(rows.Length);
            foreach (var row in rows)
                map[idSelector(row)] = row;
            ById = map;
        }
    }

    public string Name { get; }
    public IReadOnlyList<T> Rows { get; }
    public long EstimatedBytes { get; }
    public IReadOnlyDictionary<int, T>? ById { get; }

    public T? FindById(int id) =>
        ById != null && ById.TryGetValue(id, out var row) ? row : null;
}
