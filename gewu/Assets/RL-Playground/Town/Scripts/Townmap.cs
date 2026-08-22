using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class TownMap : MonoBehaviour
{
    [System.Serializable]
    public class Location
    {
        public string locationName;       // 地点名称，如 "Bakery"
        [TextArea]
        public string description;        // 地点描述，传给LLM用
        public Transform waypoint;        // 场景中的坐标点
    }

    public List<Location> locations;

    // 生成给 LLM 看的地图清单
    public string GetLocationListPrompt()
    {
        string prompt = "Town Locations:\n";
        foreach (var loc in locations)
        {
            prompt += $"- {loc.locationName}: {loc.description}\n";
        }
        return prompt;
    }

    // 根据名字获取坐标
    public Transform GetWaypointByName(string name)
    {
        var loc = locations.FirstOrDefault(l => l.locationName.Trim().ToLower() == name.Trim().ToLower());
        return loc != null ? loc.waypoint : null;
    }
}
