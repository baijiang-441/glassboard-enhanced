using System;
using System.Collections.Generic;

namespace GlassBoard;

public sealed class BoundedCache<TKey, TValue>
{
	private sealed class Item
	{
		public TKey Key;

		public TValue Value;
	}

	private readonly Dictionary<TKey, LinkedListNode<Item>> lookup;

	private readonly LinkedList<Item> recent = new LinkedList<Item>();

	private readonly int capacity;

	private readonly object sync = new object();
    public int Count { get { lock(sync) return lookup.Count; } }

	public BoundedCache(int limit, IEqualityComparer<TKey> comparer = null)
	{
		capacity = Math.Max(1, limit);
		lookup = new Dictionary<TKey, LinkedListNode<Item>>(comparer ?? EqualityComparer<TKey>.Default);
	}

	public bool TryGet(TKey key, out TValue value)
	{
        lock (sync)
        {
		if (!lookup.TryGetValue(key, out var value2))
		{
			value = default(TValue);
			return false;
		}
		recent.Remove(value2);
		recent.AddFirst(value2);
		value = value2.Value.Value;
		return true;

        }
    }

	public void Put(TKey key, TValue value)
	{
        lock (sync)
        {
		if (lookup.TryGetValue(key, out var value2))
		{
			value2.Value.Value = value;
			recent.Remove(value2);
			recent.AddFirst(value2);
			return;
		}
		value2 = recent.AddFirst(new Item
		{
			Key = key,
			Value = value
		});
		lookup.Add(key, value2);
		if (lookup.Count > capacity)
		{
			LinkedListNode<Item> last = recent.Last;
			recent.RemoveLast();
			lookup.Remove(last.Value.Key);
		}

        }
    }

	public void Clear()
	{
        lock (sync)
        {
		lookup.Clear();
		recent.Clear();

        }
    }
}
