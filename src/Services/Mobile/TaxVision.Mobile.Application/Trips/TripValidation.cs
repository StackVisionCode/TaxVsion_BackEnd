using System.Security.Cryptography;
using System.Text.Json;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips;

public static class TripValidation
{
    public static string Hash<T>(string kind, T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Kind = kind, Value = value })));

    public static bool ValidPage(int page, int size) =>
        page > 0 && size is >= 1 and <= 100 && (long)(page - 1) * size <= int.MaxValue;

    public static bool ValidFilter(TripFilter f) =>
        ValidPage(f.Page, f.Size)
        && (f.Purpose is null || TripPurpose.IsValid(f.Purpose))
        && (f.Status is null || Enum.IsDefined(f.Status.Value))
        && !(f.StartDate.HasValue && f.EndDate.HasValue && f.StartDate > f.EndDate)
        && (
            f.MinDistanceMiles is null
            || double.IsFinite(f.MinDistanceMiles.Value) && f.MinDistanceMiles is >= 0 and <= 1e12
        )
        && (
            f.MaxDistanceMiles is null
            || double.IsFinite(f.MaxDistanceMiles.Value) && f.MaxDistanceMiles is >= 0 and <= 1e12
        )
        && !(f.MinDistanceMiles.HasValue && f.MaxDistanceMiles.HasValue && f.MinDistanceMiles > f.MaxDistanceMiles)
        && f.SortBy is "date" or "distance" or "duration"
        && f.SortOrder is "asc" or "desc";
}
