using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SkyBellSharp.Models;

/// <summary>A location (<c>GET /api/v5/locations</c>).</summary>
public sealed class SkyBellLocation
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("owner_account_id")] public string? OwnerAccountId { get; set; }
    [JsonPropertyName("detail")] public LocationDetails? Detail { get; set; }
    [JsonPropertyName("device_ids")] public List<string>? DeviceIds { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public override string ToString() => Detail?.BuildingInformation?.Name ?? Id;
}

/// <summary>Location details (body of <c>POST /api/v5/locations</c>).</summary>
public sealed class LocationDetails
{
    [JsonPropertyName("location")] public GeoPosition? Location { get; set; }
    [JsonPropertyName("building_information")] public BuildingInformation? BuildingInformation { get; set; }
    [JsonPropertyName("contact1_full_name")] public string? Contact1FullName { get; set; }
    [JsonPropertyName("contact1_phone_number")] public string? Contact1PhoneNumber { get; set; }
    [JsonPropertyName("contact1_note")] public string? Contact1Note { get; set; }
    [JsonPropertyName("contact2_full_name")] public string? Contact2FullName { get; set; }
    [JsonPropertyName("contact2_phone_number")] public string? Contact2PhoneNumber { get; set; }
    [JsonPropertyName("contact2_note")] public string? Contact2Note { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>Coordinates are strings in the API.</summary>
public sealed class GeoPosition
{
    [JsonPropertyName("latitude")] public string? Latitude { get; set; }
    [JsonPropertyName("longitude")] public string? Longitude { get; set; }
    [JsonPropertyName("uncertainty")] public string? Uncertainty { get; set; } = "-1";
}

public sealed class BuildingInformation
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("street_1")] public string? Street1 { get; set; }
    [JsonPropertyName("street_2")] public string? Street2 { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("zipcode")] public string? ZipCode { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}
