using System.Text.RegularExpressions;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests.Architecture;

// El archivo de textos y el catálogo de kinds dicen lo mismo; la salida de cada falla es la lista de trabajo.
public sealed class NotificationCopyArchitectureTests
{
    private static readonly string[] Languages = ["en", "es"];

    [Fact]
    public void Every_in_app_kind_has_english_and_spanish_copy()
    {
        var missing = NotificationCatalog
            .All.Where(entry => entry.AllowedChannels.Contains(NotificationChannel.InApp))
            .Where(entry => !NotificationCopyCatalog.IsServerText(entry.Kind))
            .SelectMany(entry =>
                Languages
                    .Where(language => !NotificationCopyCatalog.TryGet(entry.Kind, language, out _))
                    .Select(language => $"{entry.Kind} ({language})")
            )
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"In-app kinds without copy ({missing.Length}):\n" + string.Join("\n", missing)
        );
    }

    [Fact]
    public void English_and_spanish_use_the_same_parameters()
    {
        var problems = new List<string>();
        foreach (var kind in NotificationCopyCatalog.Kinds)
        {
            if (
                !NotificationCopyCatalog.TryGet(kind, "en", out var english)
                || !NotificationCopyCatalog.TryGet(kind, "es", out var spanish)
            )
                continue;

            var en = Parameters(english);
            var es = Parameters(spanish);
            if (!en.SetEquals(es))
                problems.Add($"{kind}: en {{{string.Join(", ", en)}}} vs es {{{string.Join(", ", es)}}}");
            if (english.TitleMany is null != spanish.TitleMany is null)
                problems.Add($"{kind}: titleMany in one language only");
            if (english.BodyMany is null != spanish.BodyMany is null)
                problems.Add($"{kind}: bodyMany in one language only");
        }

        Assert.True(problems.Count == 0, "Copy with different parameters:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Copy_has_no_kind_outside_the_catalog()
    {
        var unknown = NotificationCopyCatalog
            .Kinds.Where(kind => !NotificationCatalog.TryGet(kind, out _))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(unknown.Length == 0, "Copy for kinds outside the catalog:\n" + string.Join("\n", unknown));
    }

    [Fact]
    public void Aliases_point_to_existing_kinds()
    {
        var kinds = NotificationCopyCatalog.Kinds.ToHashSet(StringComparer.Ordinal);
        var broken = NotificationCopyCatalog
            .Aliases.Where(alias =>
                kinds.Contains(alias.Key)
                || !kinds.Contains(alias.Value)
                || !NotificationCatalog.TryGet(alias.Value, out _)
            )
            .Select(alias => $"{alias.Key} -> {alias.Value}")
            .ToArray();

        Assert.NotEmpty(NotificationCopyCatalog.Aliases);
        Assert.True(broken.Length == 0, "Broken aliases:\n" + string.Join("\n", broken));
    }

    // Emisores de push con texto fijo (sin plantilla de Scribe), incluida la variante de video de la llamada.
    private static readonly string[] PushKinds =
    [
        NotificationKinds.TasksTaskOverdue,
        NotificationKinds.TasksAttachmentRejected,
        NotificationKinds.TasksClientRequestFulfilled,
        NotificationKinds.CloudStorageFileInfected,
        NotificationKinds.CloudStorageStorageLimitExceeded,
        NotificationKinds.RemindersDue,
        NotificationKinds.CalendarAppointmentScheduled,
        NotificationKinds.CalendarAppointmentBookedOnline,
        NotificationKinds.CalendarAttendeeAdded,
        NotificationKinds.CalendarBookingRequested,
        NotificationKinds.CalendarAppointmentUpdated,
        NotificationKinds.CalendarAppointmentRescheduled,
        NotificationKinds.CalendarAppointmentCancelled,
        NotificationKinds.CalendarAppointmentStartingSoon,
        NotificationKinds.CommunicationSupportOpened,
        NotificationKinds.CommunicationSupportMessageReceived,
        NotificationKinds.CommunicationChatMessage,
        NotificationKinds.CommunicationCallIncoming,
        NotificationKinds.CommunicationCallIncoming + ".video",
    ];

    [Fact]
    public void Every_push_kind_has_push_copy_in_both_languages()
    {
        var missing = PushKinds
            .SelectMany(kind =>
                Languages
                    .Where(language => !NotificationCopyCatalog.TryGetPush(kind, language, out _))
                    .Select(language => $"{kind} ({language})")
            )
            .ToArray();
        var withData = NotificationCopyCatalog
            .PushKeys.Where(key =>
                Languages.Any(language =>
                    NotificationCopyCatalog.TryGetPush(key, language, out var text)
                    && NotificationCopyCatalog.ParametersOf(text.Title + text.Body).Count > 0
                )
            )
            .ToArray();
        var unknown = NotificationCopyCatalog
            .PushKeys.Where(key =>
                !NotificationCatalog.TryGet(key, out _)
                && !NotificationCatalog.All.Any(entry => key.StartsWith(entry.Kind + ".", StringComparison.Ordinal))
            )
            .ToArray();

        Assert.True(missing.Length == 0, "Push kinds without copy:\n" + string.Join("\n", missing));
        Assert.True(withData.Length == 0, "Push copy must not carry data:\n" + string.Join("\n", withData));
        Assert.True(unknown.Length == 0, "Push copy outside the catalog:\n" + string.Join("\n", unknown));
    }

    [Fact]
    public void Every_sms_has_both_languages_with_the_same_parameters()
    {
        var problems = new List<string>();
        foreach (var key in NotificationCopyCatalog.SmsKeys)
        {
            if (
                !NotificationCopyCatalog.TryGetSms(key, "en", out var english)
                || !NotificationCopyCatalog.TryGetSms(key, "es", out var spanish)
            )
                problems.Add($"{key}: missing a language");
            else if (
                !NotificationCopyCatalog.ParametersOf(english).SetEquals(NotificationCopyCatalog.ParametersOf(spanish))
            )
                problems.Add($"{key}: different parameters");
        }

        Assert.NotEmpty(NotificationCopyCatalog.SmsKeys);
        Assert.True(problems.Count == 0, "SMS copy problems:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Every_email_text_has_both_languages_with_the_same_parameters()
    {
        var problems = new List<string>();
        foreach (var key in NotificationCopyCatalog.EmailKeys)
        {
            if (
                !NotificationCopyCatalog.TryGetEmail(key, "en", out var english)
                || !NotificationCopyCatalog.TryGetEmail(key, "es", out var spanish)
            )
                problems.Add($"{key}: missing a language");
            else if (
                !NotificationCopyCatalog.ParametersOf(english).SetEquals(NotificationCopyCatalog.ParametersOf(spanish))
            )
                problems.Add($"{key}: different parameters");
        }

        Assert.NotEmpty(NotificationCopyCatalog.EmailKeys);
        Assert.True(problems.Count == 0, "Email copy problems:\n" + string.Join("\n", problems));
    }

    // Una clave escrita a mano que no está en el archivo fallaría recién al mandar el correo.
    [Fact]
    public void Every_email_text_used_in_code_exists()
    {
        var root = Path.Combine(
            RepositoryRoot(),
            "src",
            "Services",
            "Notification",
            "TaxVision.Notification.Application"
        );
        var missing = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), "EmailText\\.For\\(\\s*\"([^\"]+)\""))
            .Select(match => match.Groups[1].Value)
            .Where(key => !NotificationCopyCatalog.EmailKeys.Contains(key))
            .Distinct()
            .ToArray();

        Assert.True(missing.Length == 0, "Email keys without copy:\n" + string.Join("\n", missing));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static HashSet<string> Parameters(NotificationCopyText text) =>
        new[] { text.Title, text.Body, text.TitleMany, text.BodyMany }
            .Where(template => template is not null)
            .SelectMany(template => NotificationCopyCatalog.ParametersOf(template!))
            .ToHashSet(StringComparer.Ordinal);
}
