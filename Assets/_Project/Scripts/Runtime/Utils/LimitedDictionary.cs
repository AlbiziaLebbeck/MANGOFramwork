using System.Collections.Generic;

public class LimitedDictionary<TKey, Tvalue>
{
    private Dictionary<TKey, Tvalue> dict = new Dictionary<TKey, Tvalue>();
    private Queue<TKey> order = new Queue<TKey>();
    private int maxSize;

    public LimitedDictionary(int maxSize)
    {
        this.maxSize = maxSize;
    }

    public void Add(TKey key, Tvalue value)
    {
        if(dict.ContainsKey(key))
        {
            dict[key] = value;
            return;
        }

        if(dict.Count >= maxSize)
        {
            TKey oldestKey = order.Dequeue();
            dict.Remove(oldestKey);
        }

        dict.Add(key, value);
        order.Enqueue(key);
    }

    public bool TryGetValue(TKey key, out Tvalue value)
    {
        return dict.TryGetValue(key, out value);
    }

    public bool ContainsKey(TKey key)
    {
        return dict.ContainsKey(key);
    }

    public int Count => dict.Count;

    public void Clear()
    {
        dict.Clear();
        order.Clear();
    }
}
