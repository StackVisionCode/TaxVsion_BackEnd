using System.Reflection;
using System.Text.RegularExpressions;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;

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
