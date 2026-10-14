using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SkyBellSharp.Json;

namespace SkyBellSharp.Models;

/// <summary>A SkyBell device (<c>GET /api/v5/devices</c>, <c>GET /api/v5/devices/{id}</c>).</summary>
public sealed class SkyBellDevice
{
    [JsonPropertyName("device_id")] public string? DeviceId { get; set; }

    /// <summary>Some responses use <c>id</c> instead of <c>device_id</c>.</summary>
    [JsonPropertyName("id")] public string? IdAlias { get; set; }

    /// <summary>Device id, whichever key the API used.</summary>
    [JsonIgnore] public string Id => DeviceId ?? IdAlias ?? "";

    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("account_id")] public string? AccountId { get; set; }
    [JsonPropertyName("client_id")] public string? ClientId { get; set; }
    [JsonPropertyName("serial")] public string? Serial { get; set; }

    /// <summary>"revision:model", e.g. "1:SKYBELL_MICRO_1000020".</summary>
    [JsonPropertyName("hardware")] public string? Hardware { get; set; }

    [JsonPropertyName("firmware")] public string? Firmware { get; set; }

    [JsonPropertyName("last_event")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public DateTimeOffset? LastEvent { get; set; }

    [JsonPropertyName("last_connected")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public DateTimeOffset? LastConnected { get; set; }

    [JsonPropertyName("last_disconnected")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public DateTimeOffset? LastDisconnected { get; set; }

    /// <summary>
    /// Best-effort online state: connected more recently than disconnected.
    /// Not authoritative (the cloud only knows the last MQTT connection events).
    /// </summary>
    [JsonIgnore]
    public bool? IsOnline => LastConnected is null ? null : LastDisconnected is null || LastConnected > LastDisconnected;

    /// <summary>Sent as a string by the API.</summary>
    [JsonPropertyName("lat")] public double? Latitude { get; set; }
    [JsonPropertyName("lon")] public double? Longitude { get; set; }

    /// <summary>Set when the device is shared with (not owned by) the current user.</summary>
    [JsonPropertyName("shared")] public JsonNode? Shared { get; set; }

    [JsonIgnore] public bool IsShared => Shared is JsonValue v ? !(v.TryGetValue<bool>(out var b) && !b) : Shared is not null;

    /// <summary>True when the device runs the on-device rule engine instead of <see cref="BasicMotion"/>.</summary>
    [JsonPropertyName("using_rules")] public bool? UsingRules { get; set; }

    [JsonPropertyName("basic_motion")] public BasicMotionSettings? BasicMotion { get; set; }

    [JsonPropertyName("location_id")] public string? LocationId { get; set; }

    /// <summary>Configured tones (raw).</summary>
    [JsonPropertyName("tones")] public JsonObject? Tones { get; set; }

    [JsonPropertyName("settings")] public DeviceSettings? Settings { get; set; }
    [JsonPropertyName("device_settings")] public DeviceHardwareInfo? HardwareInfo { get; set; }
    [JsonPropertyName("telemetry")] public DeviceTelemetry? Telemetry { get; set; }

    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Id : $"{Name} ({Id})";
}

/// <summary>User settings of a device (<c>settings</c>). See <see cref="DeviceSettingsUpdate"/> for the meaning of each value.</summary>
public sealed class DeviceSettings
{
    [JsonPropertyName("device_name")] public string? DeviceName { get; set; }
    [JsonPropertyName("time_zone")] public string? TimeZone { get; set; }
    [JsonPropertyName("led_color")] public string? LedColor { get; set; }
    [JsonPropertyName("brightness")] public double? Brightness { get; set; }
    [JsonPropertyName("indoor_chime")] public bool? IndoorChime { get; set; }
    [JsonPropertyName("digital_chime")] public bool? DigitalChime { get; set; }
    [JsonPropertyName("outdoor_chime")] public bool? OutdoorChime { get; set; }
    [JsonPropertyName("outdoor_chime_volume")] public double? OutdoorChimeVolume { get; set; }
    [JsonPropertyName("skybell_chime")] public bool? SkyBellChime { get; set; }
    [JsonPropertyName("button_pressed")] public bool? ButtonPressed { get; set; }
    [JsonPropertyName("motion_detection")] public bool? MotionDetection { get; set; }
    [JsonPropertyName("debug_motion_detect")] public bool? DebugMotionDetect { get; set; }
    [JsonPropertyName("motion_sensitivity")] public double? MotionSensitivity { get; set; }
    [JsonPropertyName("hmbd_sensitivity")] public double? HumanBodySensitivity { get; set; }
    [JsonPropertyName("fd_sensitivity")] public double? FaceDetectionSensitivity { get; set; }
    [JsonPropertyName("fr_sensitivity")] public double? FaceRecognitionSensitivity { get; set; }
    [JsonPropertyName("image_quality")] public int? ImageQuality { get; set; }
    [JsonPropertyName("video_rotation")] public int? VideoRotation { get; set; }
    [JsonPropertyName("video_datetime")] public bool? VideoDateTime { get; set; }
    [JsonPropertyName("speaker_volume")] public int? SpeakerVolume { get; set; }
    [JsonPropertyName("chime_file")] public string? ChimeFile { get; set; }
    [JsonPropertyName("event_duration")] public int? EventDuration { get; set; }
    [JsonPropertyName("audio_cmd_enabled")] public bool? AudioCommandEnabled { get; set; }
    [JsonPropertyName("audio_alarm_enabled")] public bool? AudioAlarmEnabled { get; set; }
    [JsonPropertyName("force_turn")] public bool? ForceTurn { get; set; }
    [JsonPropertyName("motion_zone_config")] public MotionZoneConfig? MotionZoneConfig { get; set; }

    /// <summary>Passive infrared sensor sensitivity (0..1000).</summary>
    [JsonPropertyName("pir_sensitivity")] public double? PirSensitivity { get; set; }

    [JsonPropertyName("basic_motion")] public BasicMotionSettings? BasicMotion { get; set; }
    [JsonPropertyName("using_rules")] public bool? UsingRules { get; set; }
    [JsonPropertyName("motion_chime")] public bool? MotionChime { get; set; }
    [JsonPropertyName("motion_chime_volume")] public int? MotionChimeVolume { get; set; }
    [JsonPropertyName("motion_chime_color")] public string? MotionChimeColor { get; set; }
    [JsonPropertyName("chime_file_motion")] public string? ChimeFileMotion { get; set; }
    [JsonPropertyName("outdoor_chime_color")] public string? OutdoorChimeColor { get; set; }

    /// <summary>Telemetry check-in period, minutes.</summary>
    [JsonPropertyName("telemetry_freq")] public int? TelemetryFrequency { get; set; }

    [JsonPropertyName("aws_region")] public string? AwsRegion { get; set; }
    [JsonPropertyName("device_lat")] public double? Latitude { get; set; }
    [JsonPropertyName("device_lon")] public double? Longitude { get; set; }
    [JsonPropertyName("time_zone_info")] public JsonObject? TimeZoneInfo { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>What happens on each kind of motion when the rule engine is not used.</summary>
public sealed class BasicMotionSettings
{
    [JsonPropertyName("motion_notify")] public bool? MotionNotify { get; set; }
    [JsonPropertyName("motion_record")] public bool? MotionRecord { get; set; }

    /// <summary>Human body detection.</summary>
    [JsonPropertyName("hbd_notify")] public bool? HumanBodyNotify { get; set; }
    [JsonPropertyName("hbd_record")] public bool? HumanBodyRecord { get; set; }

    /// <summary>Face detection.</summary>
    [JsonPropertyName("fd_notify")] public bool? FaceNotify { get; set; }
    [JsonPropertyName("fd_record")] public bool? FaceRecord { get; set; }
}

/// <summary>Hardware / firmware information (<c>device_settings</c>).</summary>
public sealed class DeviceHardwareInfo
{
    [JsonPropertyName("ESSID")] public string? WifiSsid { get; set; }
    [JsonPropertyName("MAC_address")] public string? MacAddress { get; set; }
    [JsonPropertyName("model_rev")] public string? ModelRevision { get; set; }
    [JsonPropertyName("serial_number")] public string? SerialNumber { get; set; }
    [JsonPropertyName("firmware_version")] public string? FirmwareVersion { get; set; }
    [JsonPropertyName("firmware_patch")] public int? FirmwarePatch { get; set; }
    [JsonPropertyName("firmware_major_release")] public int? FirmwareMajorRelease { get; set; }
    [JsonPropertyName("firmware_minor_release")] public int? FirmwareMinorRelease { get; set; }
    [JsonPropertyName("hardware_version")] public string? HardwareVersion { get; set; }
    [JsonPropertyName("OTA_type")] public string? OtaType { get; set; }
    [JsonPropertyName("OTA_version")] public string? OtaVersion { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>Hourly check-in data (<c>telemetry</c>).</summary>
public sealed class DeviceTelemetry
{
    [JsonPropertyName("uptime")] public long? Uptime { get; set; }

    [JsonPropertyName("boot_time")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public DateTimeOffset? BootTime { get; set; }

    [JsonPropertyName("timestamp")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>e.g. "89/100".</summary>
    [JsonPropertyName("link_quality")] public string? LinkQuality { get; set; }

    /// <summary>dBm, e.g. "-38".</summary>
    [JsonPropertyName("signal_level")] public string? SignalLevel { get; set; }

    [JsonPropertyName("wifi_noise")] public string? WifiNoise { get; set; }
    [JsonPropertyName("wifi_bit_rate")] public string? WifiBitRate { get; set; }

    /// <summary>GHz, e.g. "2.437".</summary>
    [JsonPropertyName("network_frequency")] public string? NetworkFrequency { get; set; }

    /// <summary>LAN address of the device (also the source of its UDP broadcasts).</summary>
    [JsonPropertyName("ip_address")] public string? IpAddress { get; set; }
    [JsonPropertyName("ip_subnet")] public string? IpSubnet { get; set; }
    [JsonPropertyName("gateway")] public string? Gateway { get; set; }
    [JsonPropertyName("ip_address_public")] public string? PublicIpAddress { get; set; }

    /// <summary>Millivolts (e.g. 4419 = 4.419 V).</summary>
    [JsonPropertyName("battery_voltage")] public int? BatteryVoltage { get; set; }

    [JsonPropertyName("free_memory")] public long? FreeMemory { get; set; }
    [JsonPropertyName("available_memory")] public long? AvailableMemory { get; set; }

    /// <summary>Upload speed test summary, as reported by the device.</summary>
    [JsonPropertyName("upload_stats")] public string? UploadStats { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>Motion zone grid (recommended 8 x 5).</summary>
public sealed class MotionZoneConfig
{
    [JsonPropertyName("x")] public int X { get; set; } = 8;
    [JsonPropertyName("y")] public int Y { get; set; } = 5;

    /// <summary>Rows (<see cref="Y"/>) of cells (<see cref="X"/>).</summary>
    [JsonPropertyName("zones")] public List<List<MotionZone>> Zones { get; set; } = new();

    [JsonPropertyName("threshold")] public int Threshold { get; set; } = 1;

    /// <summary>Creates a grid where every cell is active at the given sensitivity.</summary>
    public static MotionZoneConfig CreateGrid(int x = 8, int y = 5, int sensitivity = 100) => new()
    {
        X = x,
        Y = y,
        Zones = Enumerable.Range(0, y)
            .Select(_ => Enumerable.Range(0, x).Select(_ => new MotionZone { Sensitivity = sensitivity }).ToList())
            .ToList()
    };
}

public sealed class MotionZone
{
    /// <summary>True to ignore motion in this cell.</summary>
    [JsonPropertyName("ignore")] public bool Ignore { get; set; }
    [JsonPropertyName("sensitivity")] public int Sensitivity { get; set; } = 100;
}

/// <summary>
/// Partial settings update for <c>POST /api/v5/devices/{id}/settings</c>: only non-null
/// properties are sent. Use <see cref="Extra"/> for settings not modelled here.
/// </summary>
public sealed class DeviceSettingsUpdate
{
    [JsonPropertyName("device_name")] public string? DeviceName { get; set; }

    /// <summary>POSIX TZ string, e.g. "CET-1CEST,M3.5.0,M10.5.0/3".</summary>
    [JsonPropertyName("time_zone")] public string? TimeZone { get; set; }

    /// <summary>Hex RGB, e.g. "#00FF00".</summary>
    [JsonPropertyName("led_color")] public string? LedColor { get; set; }

    /// <summary>0 = low, 1 = medium, 2 = high, or 3..999 = 0.3 %..100 %.</summary>
    [JsonPropertyName("brightness")] public int? Brightness { get; set; }

    /// <summary>Mechanical indoor chime.</summary>
    [JsonPropertyName("indoor_chime")] public bool? IndoorChime { get; set; }

    /// <summary>Digital indoor chime.</summary>
    [JsonPropertyName("digital_chime")] public bool? DigitalChime { get; set; }

    /// <summary>Device speaker on button press.</summary>
    [JsonPropertyName("outdoor_chime")] public bool? OutdoorChime { get; set; }

    /// <summary>0 = low, 1 = medium, 2 = high (Chime devices also accept 4..99 %).</summary>
    [JsonPropertyName("outdoor_chime_volume")] public int? OutdoorChimeVolume { get; set; }

    /// <summary>Record an event when motion is detected.</summary>
    [JsonPropertyName("motion_detection")] public bool? MotionDetection { get; set; }

    /// <summary>Draw boxes around detected motion (live and recordings).</summary>
    [JsonPropertyName("debug_motion_detect")] public bool? DebugMotionDetect { get; set; }

    /// <summary>0/1/2 legacy levels or 3..999 (0.3 %..100 %). Higher = more events.</summary>
    [JsonPropertyName("motion_sensitivity")] public int? MotionSensitivity { get; set; }

    /// <summary>Human body detection sensitivity (4294967295 = use <see cref="MotionSensitivity"/>).</summary>
    [JsonPropertyName("hmbd_sensitivity")] public long? HumanBodySensitivity { get; set; }

    /// <summary>Face detection sensitivity (4294967295 = use <see cref="MotionSensitivity"/>).</summary>
    [JsonPropertyName("fd_sensitivity")] public long? FaceDetectionSensitivity { get; set; }

    /// <summary>Face recognition sensitivity, 0..1000.</summary>
    [JsonPropertyName("fr_sensitivity")] public int? FaceRecognitionSensitivity { get; set; }

    /// <summary>0 = 480p, 1 = 720p low, 2 = 720p high, 3 = 1080p.</summary>
    [JsonPropertyName("image_quality")] public int? ImageQuality { get; set; }

    [JsonPropertyName("video_rotation")] public int? VideoRotation { get; set; }

    /// <summary>Live-stream speaker volume: 0 = low, 1 = medium, 2 = high.</summary>
    [JsonPropertyName("speaker_volume")] public int? SpeakerVolume { get; set; }

    /// <summary>Button tone, see <see cref="DeviceTones"/> (use "user.wav" for an uploaded tone).</summary>
    [JsonPropertyName("chime_file")] public string? ChimeFile { get; set; }

    [JsonPropertyName("motion_zone_config")] public MotionZoneConfig? MotionZoneConfig { get; set; }

    /// <summary>Passive infrared sensor sensitivity (0..1000).</summary>
    [JsonPropertyName("pir_sensitivity")] public int? PirSensitivity { get; set; }

    /// <summary>Notify / record per motion kind (used when the rule engine is off).</summary>
    [JsonPropertyName("basic_motion")] public BasicMotionSettings? BasicMotion { get; set; }

    /// <summary>Play a tone on motion.</summary>
    [JsonPropertyName("motion_chime")] public bool? MotionChime { get; set; }
    [JsonPropertyName("motion_chime_volume")] public int? MotionChimeVolume { get; set; }
    [JsonPropertyName("chime_file_motion")] public string? ChimeFileMotion { get; set; }

    /// <summary>Hex RGB of the chime light on button press (Chime devices), e.g. "#00FF00".</summary>
    [JsonPropertyName("outdoor_chime_color")] public string? OutdoorChimeColor { get; set; }

    /// <summary>Hex RGB of the chime light on motion (Chime devices).</summary>
    [JsonPropertyName("motion_chime_color")] public string? MotionChimeColor { get; set; }

    /// <summary>Burn the date and time into recordings.</summary>
    [JsonPropertyName("video_datetime")] public bool? VideoDateTime { get; set; }

    /// <summary>Recording length of an event, seconds.</summary>
    [JsonPropertyName("event_duration")] public int? EventDuration { get; set; }

    [JsonPropertyName("audio_cmd_enabled")] public bool? AudioCommandEnabled { get; set; }
    [JsonPropertyName("audio_alarm_enabled")] public bool? AudioAlarmEnabled { get; set; }

    /// <summary>Telemetry check-in period, minutes.</summary>
    [JsonPropertyName("telemetry_freq")] public int? TelemetryFrequency { get; set; }

    /// <summary>Additional raw settings merged into the request body.</summary>
    [JsonIgnore] public JsonObject? Extra { get; set; }

    /// <summary>Builds the JSON body (non-null properties + <see cref="Extra"/>).</summary>
    public JsonObject ToJson()
    {
        var obj = JsonSerializer.SerializeToNode(this, SkyBellJson.Options) as JsonObject ?? new JsonObject();
        if (Extra is not null)
            foreach (var (k, v) in Extra) obj[k] = v?.DeepClone();
        return obj;
    }
}

/// <summary>Built-in tone files accepted by <c>chime_file</c> and <c>testtone</c>.</summary>
public static class DeviceTones
{
    public const string User = "user.wav";
    public static IReadOnlyList<string> BuiltIn { get; } = new[]
    {
        "user.wav", "1.wav", "2.wav", "3.wav", "4.wav", "5.wav", "8.wav", "9.wav",
        "cartoon-melodic-bell.wav", "home.wav", "modern.wav", "onemomentplease.wav",
        "single-press.wav", "wifi_ring.wav"
    };
}
