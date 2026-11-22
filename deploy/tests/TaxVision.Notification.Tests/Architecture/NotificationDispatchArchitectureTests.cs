namespace TaxVision.Notification.Tests.Architecture;

public sealed class NotificationDispatchArchitectureTests
{
    // Consumers ya pasados al punto único; cada grupo de la migración se añade aquí.
    private static readonly string[] MigratedConsumers =
    [
        // Tareas y pedidos
        "TaskAssignedConsumer.cs",
        "TaskOverdueConsumer.cs",
        "TaskAttachmentRejectedConsumer.cs",
        "TaskWaitingOnClientConsumer.cs",
        "ClientRequestConsumers.cs",
        // Recordatorios, import, financiero y referidos
        "ReminderDueConsumer.cs",
        "CustomersBulkImportedConsumer.cs",
        "CustomerImportFailedConsumer.cs",
        "FinancialImportBatchCompletedConsumer.cs",
        "ReferralAttributedNotificationConsumer.cs",
        "ReferralRewardNotificationConsumers.cs",
        // Citas (las reservas públicas de demo no tienen kind en el catálogo)
        "AppointmentAttendeeAddedConsumer.cs",
        "AppointmentAttendeeRespondedConsumer.cs",
        "AppointmentBookingDeclinedConsumer.cs",
        "AppointmentBookingRequestedConsumer.cs",
        "AppointmentCancelledConsumer.cs",
        "AppointmentDetailsChangedConsumer.cs",
        "AppointmentRescheduledConsumer.cs",
        "AppointmentScheduledConsumer.cs",
        "AppointmentStartingSoonConsumer.cs",
        "CalendarClientNotices.cs",
        "CalendarRecipients.cs",
        // Reuniones, llamadas, grabaciones y soporte
        "CallMissedConsumer.cs",
        "CallRecordingFailedConsumer.cs",
        "CallRecordingReadyConsumer.cs",
        "ChatMessagesMissedConsumer.cs",
        "MeetingCancelledConsumer.cs",
        "MeetingInvitationCreatedConsumer.cs",
        "MeetingInvitationRevokedConsumer.cs",
        "MeetingNotifications.cs",
        "MeetingRecordingFailedConsumer.cs",
        "MeetingRecordingReadyConsumer.cs",
        "MeetingReminderConsumer.cs",
        "MeetingRescheduledConsumer.cs",
        "MobileCommunicationPushConsumer.cs",
        "SupportMessageAddedConsumer.cs",
        "SupportOpenedConsumer.cs",
        "SupportTicketStatusConsumer.cs",
        // Almacenamiento y legal (la invitación por enlace a externos no tiene kind en el catálogo)
        "CloudStorageEventConsumers.cs",
        "FileBlockedByDmcaTakedownConsumer.cs",
        "DmcaCounterNoticeSubmittedConsumer.cs",
        "FileBlockedByPolicyConsumer.cs",
        "FilePendingReviewConsumer.cs",
        "FileReinstatedFromTakedownConsumer.cs",
        "LegalHoldPlacedConsumer.cs",
        "LegalHoldLiftedConsumer.cs",
        "ShareLinkFolderItemAddedConsumer.cs",
        "CustomerDocumentUploadedConsumer.cs",
        // Cobros, suscripción y wallet
        "BillingNotificationPublisher.cs",
        "PayoutFailedNotificationConsumer.cs",
        "TenantConnectAccountNotificationPublisher.cs",
        "TenantConnectAccountRestrictedNotificationConsumer.cs",
        "TenantConnectAccountOnboardingRequiredNotificationConsumer.cs",
        "TenantPaymentSucceededNotificationConsumer.cs",
        "SaaSPaymentMethodExpiringSoonConsumer.cs",
        "WalletAlertSender.cs",
        "WalletBalanceAlertNotificationConsumers.cs",
        "InvoicePaidStaffConsumers.cs",
        "InvoiceSentConsumer.cs",
        "SubscriptionLifecycleEmailConsumers.cs",
        "SubscriptionLifecycleInAppConsumer.cs",
        "SubscriptionDiscountEndingNotificationConsumer.cs",
        // Cuenta y seguridad (los correos con código o enlace de Auth y Onboarding no tienen kind en el catálogo)
        "SecurityAlertConsumer.cs",
        "SeatSlotEmailConsumers.cs",
        "OfficeWispReviewDueConsumer.cs",
        "OffboardingNoticeSender.cs",
        "CampaignSchedulesPausedOnOffboardingConsumer.cs",
        "CloudConnectionReviewRequiredConsumer.cs",
        "ConnectorsOfficeMailboxReviewRequiredConsumer.cs",
        "OfficeSecurityCoordinatorVacatedConsumer.cs",
        "SignaturePreparerAttentionRequiredConsumer.cs",
        "SignaturePreparerChangedConsumer.cs",
        "UserOffboardingIncompleteConsumer.cs",
        // Firma (los avisos al firmante de recordatorio, entrega, vencimiento, rechazo y reto no tienen kind en el catálogo)
        "SignerInvitedConsumer.cs",
    ];

    private static readonly string[] DirectDispatchCalls =
    [
        ".RecordInAppAsync(",
        ".SendPushAsync(",
        ".DispatchPushAsync(",
        ".QueueEmailAsync(",
        ".SendSmsAsync(",
    ];

    [Fact]
    public void Migrated_consumers_only_use_DispatchAsync()
    {
        var offenders = MigratedConsumers
            .SelectMany(name =>
            {
                var text = File.ReadAllText(SourceFile(name));
                return DirectDispatchCalls
                    .Where(call => text.Contains(call, StringComparison.Ordinal))
                    .Select(call => $"{name}: {call}");
            })
            .ToArray();

        Assert.True(offenders.Length == 0, "Envío directo en un consumer migrado:\n" + string.Join("\n", offenders));
    }

    // Correos a usuarios de la oficina que aún no pasan por el gate (sin RecipientUserId ni Category).
    // Cada grupo de la migración al punto único los va quitando.
    private static readonly Dictionary<string, int> StaffEmailsWithoutGate = new(StringComparer.Ordinal)
    {
        ["AuthEventConsumers.cs"] = 7,
        ["OnboardingEventConsumers.cs"] = 4,
    };

    // Correos a personas sin cuenta del staff (cliente, firmante, invitado, visitante): no hay preferencia.
    private static readonly Dictionary<string, int> ExternalEmails = new(StringComparer.Ordinal)
    {
        ["ClientFormNotificationSender.cs"] = 1,
        ["PublicBookingEmails.cs"] = 1,
        ["ShareLinkExternalRecipientInvitedConsumer.cs"] = 1,
        ["GiftcardShareConsumer.cs"] = 1,
        ["SignatureRequestExpiredConsumer.cs"] = 1,
        ["SignatureRequestReminderDueConsumer.cs"] = 1,
        ["SignedDocumentsDeliveryReadyConsumer.cs"] = 1,
        ["SignerRejectedConsumer.cs"] = 1,
        ["SignerVerificationChallengeIssuedConsumer.cs"] = 1,
    };

    [Fact]
    public void Emails_to_users_carry_user_and_category()
    {
        var expected = StaffEmailsWithoutGate.Concat(ExternalEmails).ToDictionary(StringComparer.Ordinal);
        var actual = ApplicationFiles()
            .Select(file =>
                (
                    Name: Path.GetFileName(file),
                    Count: Blocks(File.ReadAllText(file), ".QueueEmailAsync(")
                        .Count(block => !block.Contains("RecipientUserId") || !block.Contains("Category"))
                )
            )
            .Where(file => file.Count > 0)
            .ToDictionary(file => file.Name, file => file.Count, StringComparer.Ordinal);

        var differences = expected
            .Keys.Union(actual.Keys)
            .Where(name => expected.GetValueOrDefault(name) != actual.GetValueOrDefault(name))
            .Select(name =>
                $"{name}: lista {expected.GetValueOrDefault(name)}, código {actual.GetValueOrDefault(name)}"
            )
            .ToArray();

        Assert.True(
            differences.Length == 0,
            "Correo sin usuario ni categoría fuera de la lista (o la lista quedó vieja):\n"
                + string.Join("\n", differences)
        );
    }

    internal static IEnumerable<string> Blocks(string text, string start)
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

    // El reto de verificación ya escribe su fila a mano.
    private static readonly string[] DirectSmsAllowed = ["SignerVerificationChallengeIssuedConsumer.cs"];

    [Fact]
    public void No_consumer_calls_ISmsSender_directly()
    {
        var offenders = ApplicationFiles()
            .Where(file => file.Contains($"{Path.DirectorySeparatorChar}Consumers{Path.DirectorySeparatorChar}"))
            .Where(file => File.ReadAllText(file).Contains("ISmsSender", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Where(name => !DirectSmsAllowed.Contains(name))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "SMS sin fila de auditoría ni gate (usar NotificationDispatcher):\n" + string.Join("\n", offenders)
        );
    }

    private static string SourceFile(string fileName) =>
        Directory
            .GetFiles(ApplicationRoot(), fileName, SearchOption.AllDirectories)
            .Single(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    internal static IReadOnlyList<string> ApplicationFiles() =>
        Directory
            .GetFiles(ApplicationRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
            )
            .ToArray();

    private static string ApplicationRoot() =>
        Path.Combine(FindRepositoryRoot(), "src", "Services", "Notification", "TaxVision.Notification.Application");

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("No se encontro la raiz del repositorio.");
    }
}
