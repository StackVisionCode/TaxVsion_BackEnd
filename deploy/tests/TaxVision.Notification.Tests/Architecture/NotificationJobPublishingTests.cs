extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;

namespace TaxVision.Notification.Tests.Architecture;

// NOT-18: los jobs de Notification no publican por un IMessageBus suelto.
public sealed class NotificationJobPublishingTests : JobPublishingRulesBase
{
    protected override string SourceDirectory => "src/Services/Notification";
}
