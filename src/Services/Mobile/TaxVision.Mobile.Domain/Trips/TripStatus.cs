using System.Text.Json.Serialization;

namespace TaxVision.Mobile.Domain.Trips;

[JsonConverter(typeof(JsonStringEnumConverter<TripStatus>))]
public enum TripStatus
{
    InProgress,
    Paused,
    Completed,
    Cancelled,
}
