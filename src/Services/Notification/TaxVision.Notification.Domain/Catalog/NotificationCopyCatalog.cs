using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TaxVision.Notification.Domain.Catalog;

/// <summary>Texto de un kind en un idioma. <c>TitleMany</c>/<c>BodyMany</c> aplican cuando <c>count</c> &gt; 1.</summary>
public sealed record NotificationCopyText(string Title, string Body, string? TitleMany, string? BodyMany);

/// <summary>Texto ya armado con los parámetros.</summary>
public sealed record NotificationCopyRendering(string Title, string Body);

/// <summary>
/// Fuente única de los textos in-app (recurso <c>NotificationCopy.json</c>); el CRM y el portal llevan una copia
/// literal. <c>{param}</c> = valor tal cual; <c>{param|date}</c> y <c>{param|datetime}</c> los formatea el
/// frontend: aquí salen con el valor crudo. Un parámetro que no llega sale vacío. Las secciones <c>push</c>,
/// <c>sms</c> y <c>email</c> las traduce solo el servidor.
/// </summary>
public static partial class NotificationCopyCatalog
{
    public const string DefaultLanguage = "en";
    private const string ResourceName = "TaxVision.Notification.Domain.Catalog.NotificationCopy.json";

    private static readonly Lazy<CopyFile> Copy = new(Load);

    public static int Version => Copy.Value.Version;

    public static IReadOnlyCollection<string> Kinds => Copy.Value.Kinds.Keys;

    /// <summary>Claves de la sección <c>push</c>: un kind o un kind con variante (<c>kind.variante</c>).</summary>
    public static IReadOnlyCollection<string> PushKeys => Copy.Value.Push.Keys;

    /// <summary>Claves de la sección <c>sms</c>.</summary>
    public static IReadOnlyCollection<string> SmsKeys => Copy.Value.Sms.Keys;

    /// <summary>Claves de la sección <c>email</c>: textos que el sistema mete dentro de un correo.</summary>
    public static IReadOnlyCollection<string> EmailKeys => Copy.Value.Email.Keys;

    /// <summary>Kinds viejos que siguen en filas guardadas → kind vigente.</summary>
    public static IReadOnlyDictionary<string, string> Aliases => Copy.Value.Aliases;

    /// <summary>El frontend muestra el texto del servidor en los idiomas sin copia (caso wallet).</summary>
    public static bool IsServerText(string kind) => Entry(kind) is { ServerText: true };

    /// <summary>Texto exacto del idioma (sin caer a inglés); resuelve alias.</summary>
    public static bool TryGet(string kind, string? language, [NotNullWhen(true)] out NotificationCopyText? text)
    {
        text = null;
        return Entry(kind) is { } entry && entry.Texts.TryGetValue(Normalize(language), out text);
    }

    /// <summary>Texto con parámetros; sin copia en el idioma cae a inglés; null si el kind no tiene copia.</summary>
    public static NotificationCopyRendering? Render(
        string kind,
        string? language,
        IReadOnlyDictionary<string, string>? parameters
    )
    {
        if (!TryGet(kind, language, out var text) && !TryGet(kind, DefaultLanguage, out text))
            return null;

        var many =
            parameters is not null
            && parameters.TryGetValue("count", out var raw)
            && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            && count > 1;
        return new(
            Fill(many ? text.TitleMany ?? text.Title : text.Title, parameters),
            Fill(many ? text.BodyMany ?? text.Body : text.Body, parameters)
        );
    }

    /// <summary>Texto exacto del push en el idioma (sin caer a inglés).</summary>
    public static bool TryGetPush(string key, string? language, [NotNullWhen(true)] out NotificationCopyRendering? text)
    {
        text = null;
        return Copy.Value.Push.TryGetValue(key, out var texts) && texts.TryGetValue(Normalize(language), out text);
    }

    /// <summary>Texto del push; sin copia en el idioma cae a inglés; null si la clave no existe.</summary>
    public static NotificationCopyRendering? RenderPush(string key, string? language) =>
        TryGetPush(key, language, out var text) || TryGetPush(key, DefaultLanguage, out text) ? text : null;

    /// <summary>Texto exacto del SMS en el idioma (sin caer a inglés ni poner parámetros).</summary>
    public static bool TryGetSms(string key, string? language, [NotNullWhen(true)] out string? text)
    {
        text = null;
        return Copy.Value.Sms.TryGetValue(key, out var texts) && texts.TryGetValue(Normalize(language), out text);
    }

    /// <summary>SMS con parámetros; sin copia en el idioma cae a inglés; null si la clave no existe.</summary>
    public static string? RenderSms(string key, string? language, IReadOnlyDictionary<string, string>? parameters) =>
        TryGetSms(key, language, out var text) || TryGetSms(key, DefaultLanguage, out text)
            ? Fill(text, parameters)
            : null;

    /// <summary>Texto exacto de la sección <c>email</c> en el idioma (sin caer a inglés ni poner parámetros).</summary>
    public static bool TryGetEmail(string key, string? language, [NotNullWhen(true)] out string? text)
    {
        text = null;
        return Copy.Value.Email.TryGetValue(key, out var texts) && texts.TryGetValue(Normalize(language), out text);
    }

    /// <summary>Texto de correo con parámetros; sin copia en el idioma cae a inglés; null si la clave no existe.</summary>
    public static string? RenderEmail(string key, string? language, IReadOnlyDictionary<string, string>? parameters) =>
        TryGetEmail(key, language, out var text) || TryGetEmail(key, DefaultLanguage, out text)
            ? Fill(text, parameters)
            : null;

    /// <summary>Nombres de los parámetros de un texto, sin el formato.</summary>
    public static IReadOnlySet<string> ParametersOf(string template) =>
        Placeholder().Matches(template).Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"\{(?<name>[A-Za-z0-9_]+)(?:\|(?<format>date|datetime))?\}")]
    private static partial Regex Placeholder();

    private static string Fill(string template, IReadOnlyDictionary<string, string>? parameters) =>
        Placeholder()
            .Replace(
                template,
                match =>
                    parameters is not null && parameters.TryGetValue(match.Groups["name"].Value, out var value)
                        ? value
                        : string.Empty
            );

    private static string Normalize(string? language) =>
        string.IsNullOrWhiteSpace(language) || language.Trim().Length < 2
            ? DefaultLanguage
            : language.Trim()[..2].ToLowerInvariant();

    private static CopyEntry? Entry(string kind)
    {
        var copy = Copy.Value;
        if (copy.Kinds.TryGetValue(kind, out var entry))
            return entry;
        return copy.Aliases.TryGetValue(kind, out var target) ? copy.Kinds.GetValueOrDefault(target) : null;
    }

    private static CopyFile Load()
    {
        using var stream =
            typeof(NotificationCopyCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        var kinds = new Dictionary<string, CopyEntry>(StringComparer.Ordinal);
        foreach (var kind in root.GetProperty("kinds").EnumerateObject())
        {
            var serverText = false;
            var texts = new Dictionary<string, NotificationCopyText>(StringComparer.Ordinal);
            foreach (var property in kind.Value.EnumerateObject())
            {
                if (property.Name == "serverText")
                    serverText = property.Value.GetBoolean();
                else
                    texts[property.Name] = ReadText(kind.Name, property);
            }
            kinds[kind.Name] = new CopyEntry(serverText, texts.ToFrozenDictionary(StringComparer.Ordinal));
        }

        var aliases = root.TryGetProperty("aliases", out var aliasNode)
            ? aliasNode.EnumerateObject().ToDictionary(alias => alias.Name, alias => alias.Value.GetString()!)
            : new Dictionary<string, string>();
        return new CopyFile(
            root.GetProperty("version").GetInt32(),
            kinds.ToFrozenDictionary(StringComparer.Ordinal),
            aliases.ToFrozenDictionary(StringComparer.Ordinal),
            ReadSection(root, "push", ReadPush),
            ReadSection(root, "sms", ReadPlain),
            ReadSection(root, "email", ReadPlain)
        );
    }

    private static FrozenDictionary<string, FrozenDictionary<string, T>> ReadSection<T>(
        JsonElement root,
        string name,
        Func<string, JsonProperty, T> read
    )
    {
        if (!root.TryGetProperty(name, out var section))
            return FrozenDictionary<string, FrozenDictionary<string, T>>.Empty;
        return section
            .EnumerateObject()
            .ToFrozenDictionary(
                key => key.Name,
                key =>
                    key.Value.EnumerateObject()
                        .ToFrozenDictionary(language => language.Name, language => read(key.Name, language)),
                StringComparer.Ordinal
            );
    }

    private static NotificationCopyRendering ReadPush(string key, JsonProperty language)
    {
        var text = ReadText(key, language);
        return new NotificationCopyRendering(text.Title, text.Body);
    }

    private static string ReadPlain(string key, JsonProperty language)
    {
        var text = language.Value.GetString();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"Copy for {key} ({language.Name}) is empty.");
        return text;
    }

    private static NotificationCopyText ReadText(string kind, JsonProperty language)
    {
        string? Read(string name) => language.Value.TryGetProperty(name, out var value) ? value.GetString() : null;

        var title = Read("title");
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException($"Copy for {kind} ({language.Name}) has no title.");
        return new NotificationCopyText(title, Read("body") ?? string.Empty, Read("titleMany"), Read("bodyMany"));
    }

    private sealed record CopyEntry(bool ServerText, FrozenDictionary<string, NotificationCopyText> Texts);

    private sealed record CopyFile(
        int Version,
        FrozenDictionary<string, CopyEntry> Kinds,
        FrozenDictionary<string, string> Aliases,
        FrozenDictionary<string, FrozenDictionary<string, NotificationCopyRendering>> Push,
        FrozenDictionary<string, FrozenDictionary<string, string>> Sms,
        FrozenDictionary<string, FrozenDictionary<string, string>> Email
    );
}
