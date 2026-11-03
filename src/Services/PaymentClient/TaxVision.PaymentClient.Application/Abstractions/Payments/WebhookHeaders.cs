namespace TaxVision.PaymentClient.Application.Abstractions.Payments;

/// <summary>Lectura de encabezados de webhook sin distinguir mayúsculas (HTTP no las distingue).</summary>
public static class WebhookHeaders
{
    public static string? Find(this IReadOnlyDictionary<string, string> headers, string name)
    {
        if (headers.TryGetValue(name, out var value))
            return value;

        foreach (var (key, candidate) in headers)
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return candidate;

        return null;
    }
}
