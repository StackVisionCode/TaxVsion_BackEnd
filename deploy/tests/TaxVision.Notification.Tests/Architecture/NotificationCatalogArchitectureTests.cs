using System.Reflection;
using System.Text.RegularExpressions;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Tests.Architecture;

public sealed class NotificationCatalogArchitectureTests
{
    private static readonly Regex KindReference = new(
        @"NotificationKinds\.(?<name>[A-Za-z0-9_]+)",
        RegexOptions.Compiled
    );

    [Fact]
    public void Every_declared_kind_exists_in_the_catalog()
    {
        var declaredKinds = typeof(NotificationKinds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(declaredKinds);
        Assert.Equal(declaredKinds.Length, declaredKinds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(
            declaredKinds,
            kind => Assert.True(NotificationCatalog.TryGet(kind, out _), $"Missing catalog entry: {kind}")
        );
    }

    [Fact]
    public void Every_kind_referenced_by_a_consumer_exists_in_the_catalog()
    {
        var fields = typeof(NotificationKinds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);

        var referencedKinds = ConsumerFiles()
            .SelectMany(file =>
                KindReference.Matches(File.ReadAllText(file)).Select(match => match.Groups["name"].Value)
            )
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(referencedKinds);
        Assert.All(
            referencedKinds,
            name =>
            {
                Assert.True(fields.TryGetValue(name, out var kind), $"Unknown NotificationKinds member: {name}");
                Assert.True(NotificationCatalog.TryGet(kind!, out _), $"Missing catalog entry: {kind}");
            }
        );
    }

    [Fact]
    public void Mandatory_entries_declare_at_least_one_delivery_channel()
    {
        // D2: lo obligatorio salta la preferencia del usuario, así que el catálogo debe
        // enumerar explícitamente los canales por los que puede entregarse. Un entry
        // IsMandatory sin AllowedChannels silencia el aviso sin que nadie lo note.
        var mandatoryWithoutChannel = NotificationCatalog
            .All.Where(entry => entry.IsMandatory && entry.AllowedChannels.Count == 0)
            .Select(entry => entry.Kind)
            .ToArray();

        Assert.True(
            mandatoryWithoutChannel.Length == 0,
            $"Mandatory kinds sin AllowedChannels: {string.Join(", ", mandatoryWithoutChannel)}"
        );
    }

    [Fact]
    public void Every_entry_declares_at_least_one_channel()
    {
        var orphans = NotificationCatalog
            .All.Where(entry => entry.AllowedChannels.Count == 0)
            .Select(entry => entry.Kind)
            .ToArray();

        Assert.True(orphans.Length == 0, $"Kinds sin canal de entrega: {string.Join(", ", orphans)}");
    }

    [Fact]
    public void Mandatory_in_app_entries_include_InApp_channel()
    {
        // Si el catálogo marca IsMandatory=true pero no incluye InApp en AllowedChannels,
        // el gate del dispatcher no reconocerá que el canal in-app es obligatorio y la
        // preferencia del usuario podrá apagarlo igual.
        var mandatoryEmailOnly = new HashSet<string>(StringComparer.Ordinal)
        {
            NotificationKinds.TasksFormAccessLink,
            NotificationKinds.TasksFormAccessCode,
            NotificationKinds.TasksFormParticipantReturned,
            NotificationKinds.TasksFormParticipantReminder,
            NotificationKinds.TasksFormParticipantCompleted,
            NotificationKinds.TasksFormParticipantCancelled,
            NotificationKinds.TasksFormParticipantAttachmentRejected,
        };
        var mismatched = NotificationCatalog
            .All.Where(entry =>
                entry.IsMandatory
                && !entry.AllowedChannels.Contains(NotificationChannel.InApp)
                && !mandatoryEmailOnly.Contains(entry.Kind)
            )
            .Select(entry => entry.Kind)
            .ToArray();

        Assert.True(mismatched.Length == 0, $"Mandatory kinds que no habilitan InApp: {string.Join(", ", mismatched)}");

        var invalidEmailOnly = NotificationCatalog
            .All.Where(entry => mandatoryEmailOnly.Contains(entry.Kind))
            .Where(entry => !entry.IsMandatory || !entry.AllowedChannels.SetEquals([NotificationChannel.Email]))
            .Select(entry => entry.Kind)
            .ToArray();
        Assert.True(
            invalidEmailOnly.Length == 0,
            $"Kinds externos con canales invalidos: {string.Join(", ", invalidEmailOnly)}"
        );
    }

    [Fact]
    public void Client_form_kinds_have_the_exact_channel_matrix()
    {
        var inApp = NotificationChannel.InApp;
        var email = NotificationChannel.Email;
        var push = NotificationChannel.Push;
        var expected = new Dictionary<string, (NotificationChannel[] Allowed, NotificationChannel[] Mandatory)>
        {
            [NotificationKinds.TasksFormRequestAssigned] = ([inApp, email, push], [inApp, email]),
            [NotificationKinds.TasksFormRequestSubmitted] = ([inApp, email, push], []),
            [NotificationKinds.TasksFormRequestReturned] = ([inApp, email, push], [inApp, email]),
            [NotificationKinds.TasksFormRequestReminder] = ([inApp, email, push], []),
            [NotificationKinds.TasksFormAttachmentRejected] = ([inApp, email, push], [inApp, email]),
            [NotificationKinds.TasksFormRequestCompleted] = ([inApp, email, push], []),
            [NotificationKinds.TasksFormRequestCancelled] = ([inApp], []),
            [NotificationKinds.TasksFormAccessLink] = ([email], [email]),
            [NotificationKinds.TasksFormAccessCode] = ([email], [email]),
            [NotificationKinds.TasksFormActionFailed] = ([inApp, email, push], []),
            [NotificationKinds.TasksFormAttachmentRejectedStaff] = ([inApp], []),
            [NotificationKinds.TasksFormParticipantReturned] = ([email], [email]),
            [NotificationKinds.TasksFormParticipantReminder] = ([email], [email]),
            [NotificationKinds.TasksFormParticipantCompleted] = ([email], [email]),
            [NotificationKinds.TasksFormParticipantCancelled] = ([email], [email]),
            [NotificationKinds.TasksFormParticipantAttachmentRejected] = ([email], [email]),
        };

        foreach (var (kind, channels) in expected)
        {
            var entry = NotificationCatalog.GetRequired(kind);
            Assert.True(entry.AllowedChannels.SetEquals(channels.Allowed), $"AllowedChannels incorrectos para {kind}");
            var mandatory = Enum.GetValues<NotificationChannel>().Where(entry.IsMandatoryFor).ToHashSet();
            Assert.True(mandatory.SetEquals(channels.Mandatory), $"MandatoryChannels incorrectos para {kind}");
            Assert.DoesNotContain(NotificationChannel.Push, mandatory);
        }
    }

    [Fact]
    public void Push_is_never_mandatory()
    {
        var mandatoryPush = NotificationCatalog
            .All.Where(entry => entry.IsMandatoryFor(NotificationChannel.Push))
            .Select(entry => entry.Kind)
            .ToArray();

        Assert.True(mandatoryPush.Length == 0, $"Push obligatorio: {string.Join(", ", mandatoryPush)}");
    }

    [Fact]
    public void Every_push_kind_exists_in_the_catalog_and_allows_push()
    {
        var fields = typeof(NotificationKinds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var (file, block) in PushBlocks())
        {
            foreach (Match match in KindReference.Matches(block))
            {
                var kind = fields[match.Groups["name"].Value];
                if (
                    !NotificationCatalog.TryGet(kind, out var entry)
                    || !entry!.AllowedChannels.Contains(NotificationChannel.Push)
                )
                    problems.Add($"{Path.GetFileName(file)}: {kind}");
            }
            // Campañas va fuera del catálogo (su push no tiene recurso ni audiencia conocida).
            foreach (Match literal in KindLiteral.Matches(block))
                if (literal.Value != "\"campaign.push\"")
                    problems.Add($"{Path.GetFileName(file)}: literal {literal.Value}");
        }

        Assert.True(
            problems.Count == 0,
            "Kinds de push fuera del catálogo o sin Push:\n" + string.Join("\n", problems)
        );
    }

    [Fact]
    public void No_consumer_uses_the_push_overload_without_data()
    {
        var shortCalls = ApplicationFiles()
            .SelectMany(file => ShortPushOverload.Matches(File.ReadAllText(file)).Select(_ => Path.GetFileName(file)))
            .ToArray();

        Assert.True(shortCalls.Length == 0, $"Push sin datos ni enlace: {string.Join(", ", shortCalls)}");
    }

    [Fact]
    public void Every_consumer_uses_the_preference_category_of_its_kind()
    {
        var fields = typeof(NotificationKinds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);
        var divergences = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (file, block, implied) in CategoryBlocks())
        {
            var categories = CategoryReference
                .Matches(block)
                .Select(match => match.Groups["name"].Value)
                .Concat(implied is null ? [] : [implied])
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            // Categoría variable o ambigua: la prueba no puede saber cuál aplica.
            if (categories.Length != 1)
                continue;
            var expected = Enum.Parse<NotificationCategory>(categories[0]);
            foreach (Match match in KindReference.Matches(block))
            {
                var kind = fields[match.Groups["name"].Value];
                var entry = NotificationCatalog.GetRequired(kind);
                if (entry.PreferenceCategory != expected)
                    divergences.Add(
                        $"{kind}: catálogo {entry.PreferenceCategory}, {Path.GetFileName(file)} pasa {expected}"
                    );
            }
        }

        Assert.True(divergences.Count == 0, "Categoría de preferencia distinta:\n" + string.Join("\n", divergences));
    }

    private static readonly Regex CategoryReference = new(
        @"NotificationCategory\.(?<name>AccountSecurity|DocumentsAndSignatures|StorageAndQuota|Billing|Collaboration|Reminders|ClientRequests|Calendar)\b",
        RegexOptions.Compiled
    );

    // Llamadas que entregan un aviso con kind y categoría.
    private static readonly string[] DispatchStarts =
    [
        "RecordInAppAsync(",
        "SendPushAsync(",
        "DispatchPushAsync(",
        "CanSendPushAsync(",
        "PushPayloads.Request(",
        "new PushDispatchRequest(",
        "QueueEmailAsync(",
        "SendSmsAsync(",
        "new ClientFormNotificationRequest(",
    ];

    // Ayudantes que reciben el kind y fijan la categoría por dentro.
    private static readonly Dictionary<string, string> HelperStarts = new(StringComparer.Ordinal)
    {
        ["BillingNotificationPublisher.RecordAsync("] = nameof(NotificationCategory.Billing),
        ["CalendarRecipients.PushAsync("] = nameof(NotificationCategory.Calendar),
        ["CalendarRecipients.PublishInAppAsync("] = nameof(NotificationCategory.Calendar),
        ["CalendarClientNotices.SendAsync("] = nameof(NotificationCategory.Calendar),
        ["MeetingNotifications.PublishInAppAsync("] = nameof(NotificationCategory.Calendar),
    };

    // Bloques de llamada (paréntesis balanceados) y, si el archivo usa una sola categoría, el archivo entero.
    private static IEnumerable<(string File, string Block, string? Implied)> CategoryBlocks()
    {
        foreach (var file in ApplicationFiles())
        {
            var text = File.ReadAllText(file);
            var fileCategories = CategoryReference
                .Matches(text)
                .Select(match => match.Groups["name"].Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (fileCategories.Length == 1)
                yield return (file, text, null);
            foreach (var start in DispatchStarts)
            foreach (var block in Blocks(text, start))
                yield return (file, block, null);
            foreach (var (start, category) in HelperStarts)
            foreach (var block in Blocks(text, start))
                yield return (file, block, category);
        }
    }

    private static IEnumerable<string> Blocks(string text, string start)
    {
        for (
            var at = text.IndexOf(start, StringComparison.Ordinal);
            at >= 0;
            at = text.IndexOf(start, at + 1, StringComparison.Ordinal)
        )
        {
            var depth = 0;
            var end = at + start.Length - 1;
            for (; end < text.Length; end++)
            {
                depth +=
                    text[end] == '(' ? 1
                    : text[end] == ')' ? -1
                    : 0;
                if (depth == 0)
                    break;
            }
            yield return text[at..Math.Min(end + 1, text.Length)];
        }
    }

    // Tres argumentos o más sin paréntesis de por medio: la sobrecarga corta (sin Data ni Link).
    private static readonly Regex ShortPushOverload = new(
        @"\.SendPushAsync\(\s*[^,()]+,\s*[^,()]+,",
        RegexOptions.Compiled
    );

    private static readonly Regex KindLiteral = new(@"""[a-z]+(\.[a-z_]+)+""", RegexOptions.Compiled);

    private static readonly string[] PushStarts =
    [
        "new PushDispatchRequest(",
        "PushPayloads.Request(",
        "CalendarRecipients.PushAsync(",
    ];

    // Cada llamada que arma un push, con sus argumentos (paréntesis balanceados).
    private static IEnumerable<(string File, string Block)> PushBlocks()
    {
        foreach (var file in ApplicationFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var start in PushStarts)
            {
                for (
                    var at = text.IndexOf(start, StringComparison.Ordinal);
                    at >= 0;
                    at = text.IndexOf(start, at + 1, StringComparison.Ordinal)
                )
                {
                    var depth = 0;
                    var end = at + start.Length - 1;
                    for (; end < text.Length; end++)
                    {
                        depth +=
                            text[end] == '(' ? 1
                            : text[end] == ')' ? -1
                            : 0;
                        if (depth == 0)
                            break;
                    }
                    yield return (file, text[at..Math.Min(end + 1, text.Length)]);
                }
            }
        }
    }

    private static IReadOnlyList<string> ApplicationFiles() =>
        Directory.GetFiles(
            Path.Combine(FindRepositoryRoot(), "src", "Services", "Notification", "TaxVision.Notification.Application"),
            "*.cs",
            SearchOption.AllDirectories
        );

    private static IReadOnlyList<string> ConsumerFiles()
    {
        var root = FindRepositoryRoot();
        var consumers = Path.Combine(
            root,
            "src",
            "Services",
            "Notification",
            "TaxVision.Notification.Application",
            "Consumers"
        );
        return Directory.GetFiles(consumers, "*.cs", SearchOption.AllDirectories);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("No se encontro la raiz del repositorio.");
    }
}
