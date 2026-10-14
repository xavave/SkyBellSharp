using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SkyBellSharp.Models;

/// <summary>
/// A rule of the on-device rule engine: when <see cref="Triggers"/> happen within
/// <see cref="Window"/> seconds, run <see cref="Actions"/>.
/// </summary>
public sealed class SkyBellRule
{
    [JsonPropertyName("rule_num")] public int? RuleNumber { get; set; }
    [JsonPropertyName("internal")] public bool? Internal { get; set; }
    [JsonPropertyName("motion_rule")] public bool? MotionRule { get; set; }
    [JsonPropertyName("triggers")] public List<RuleTrigger> Triggers { get; set; } = new();

    /// <summary>Seconds within which all triggers must occur (0 = single trigger).</summary>
    [JsonPropertyName("window")] public int? Window { get; set; }

    /// <summary>
    /// Actions (notify, record, record_duration, pre_roll, pre_roll_duration, snapshot, chime,
    /// sound, light_on…). Null values mean "not set".
    /// </summary>
    [JsonPropertyName("actions")] public JsonObject? Actions { get; set; }

    /// <summary>How many times the rule matched.</summary>
    [JsonPropertyName("match_count")] public long? MatchCount { get; set; }

    [JsonPropertyName("light_levels")] public JsonNode? LightLevels { get; set; }
    [JsonPropertyName("temperature_levels")] public JsonNode? TemperatureLevels { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public override string ToString() =>
        $"#{RuleNumber}: {string.Join(" + ", Triggers.Select(t => t.ToString()))} within {Window ?? 0}s";
}

public sealed class RuleTrigger
{
    /// <summary>"PIRMotion" (infrared), "HMBMotion" (human body), "FDMotion" (face)…</summary>
    [JsonPropertyName("event")] public string? Event { get; set; }

    [JsonPropertyName("count")] public int? Count { get; set; }

    public override string ToString() => Count is > 1 ? $"{Event} x{Count}" : Event ?? "?";
}
