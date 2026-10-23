using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace TaxVision.DataCollection.Engine.Serialization;

internal static class StrictJson
{
    internal static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 32,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            AllowDuplicateProperties = false,
            // `"values": null` en una propiedad no anulable es JSON inválido, no un null que revienta después.
            RespectNullableAnnotations = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SkipAbsentJsonElements } },
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    // Una clave ausente (`value`, `props`, `config`) queda como JsonElement por defecto: se omite al
    // escribir, igual que en el JSON original, en vez de lanzar.
    private static void SkipAbsentJsonElements(JsonTypeInfo typeInfo)
    {
        foreach (var property in typeInfo.Properties)
        {
            if (property.PropertyType == typeof(JsonElement))
                property.ShouldSerialize = static (_, value) =>
                    value is JsonElement { ValueKind: not JsonValueKind.Undefined };
        }
    }
}
