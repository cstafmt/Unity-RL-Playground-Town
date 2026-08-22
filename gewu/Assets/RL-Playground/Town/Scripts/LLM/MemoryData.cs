using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// 可序列化的记忆条目（纯数据）
/// </summary>
[Serializable]
public class Memory
{
    public string content;
    public string timestamp;
    public string location;

    public Memory(string content, string location)
    {
        this.content = content;
        this.location = location;
        this.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    public override string ToString()
    {
        return $"[{timestamp} @ {location}] {content}";
    }
}

/// <summary>
/// MonoBehaviour 封装，文件名 MemoryData.cs 必须与类名 MemoryData 一致，方便在 Inspector 中挂载与查看
/// </summary>
public class MemoryData : MonoBehaviour
{
    [Header("记忆流设置")]
    [Tooltip("短期记忆上限（超过则会删除最早的记忆）")]
    public int maxShortTermMemories = 50;

    [Header("记忆列表（运行时追加）")]
    public List<Memory> memories = new List<Memory>();

    /// <summary>
    /// 添加一条记忆（自动裁剪至 maxShortTermMemories）
    /// </summary>
    public void AddMemory(string content, string location)
    {
        var mem = new Memory(content, location);
        memories.Add(mem);

        // 裁剪过长的短期记忆流
        if (maxShortTermMemories > 0 && memories.Count > maxShortTermMemories)
        {
            int removeCount = memories.Count - maxShortTermMemories;
            memories.RemoveRange(0, removeCount);
        }

        Debug.Log($"[MemoryData] 新记忆: {mem}");
    }

    /// <summary>
    /// 获取最近 n 条记忆的字符串（按时间顺序）
    /// </summary>
    public string GetRecentAsString(int n)
    {
        if (memories.Count == 0) return "";
        n = Mathf.Clamp(n, 1, memories.Count);
        int start = memories.Count - n;
        var slice = memories.GetRange(start, n);
        return string.Join("\n", slice.ConvertAll(m => m.ToString()).ToArray());
    }

    /// <summary>
    /// 获取最近 n 条记忆的列表（按时间顺序）
    /// </summary>
    public List<Memory> GetRecentList(int n)
    {
        if (memories.Count == 0) return new List<Memory>();
        n = Mathf.Clamp(n, 1, memories.Count);
        int start = memories.Count - n;
        return memories.GetRange(start, n);
    }

    /// <summary>
    /// 清空记忆（编辑器或运行时调用）
    /// </summary>
    public void ClearAll()
    {
        memories.Clear();
    }
}
