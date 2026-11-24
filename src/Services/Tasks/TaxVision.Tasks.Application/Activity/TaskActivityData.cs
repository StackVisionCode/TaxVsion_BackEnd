using System.Text.Json;

namespace TaxVision.Tasks.Application.Activity;

/// <summary>Los detalles de una entrada: pares clave y texto, en JSON.</summary>
public static class TaskActivityData
{
    private static readonly IReadOnlyDictionary<string, string?> Empty = new Dictionary<string, string?>();

    public static string? Write(IReadOnlyDictionary<string, string?>? data) =>
        data is null || data.Count == 0 ? null : JsonSerializer.Serialize(data);

    public static IReadOnlyDictionary<string, string?> Read(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? Empty
            : JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new Dictionary<string, string?>();
}
