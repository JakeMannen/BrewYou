using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Data.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BreweryRole
{
    Owner,
    Brewer,
    Viewer
}