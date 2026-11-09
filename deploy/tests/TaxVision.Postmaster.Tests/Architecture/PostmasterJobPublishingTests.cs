extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using BuildingBlocks.Persistence;
using TaxVision.Postmaster.Application.Feedback.Commands.ApplyPolledDeliveryFeedback;
using TaxVision.Postmaster.Application.Feedback.Commands.ReconcileDeliveryFeedback;
using TaxVision.Postmaster.Application.Feedback.Commands.ReconcileEngagementFeedback;
using Wolverine.Attributes;

namespace TaxVision.Postmaster.Tests.Architecture;

// PMS-08: ningún job de Postmaster publica suelto. El de reconciliación entra por InvokeAsync, patrón aceptado por BB-3.
public sealed class PostmasterJobPublishingTests : JobPublishingRulesBase
{
    private const string ReconciliationJob =
        "src/Services/Postmaster/TaxVision.Postmaster.Api/Jobs/DeliveryFeedbackReconciliationJob.cs";

    protected override string SourceDirectory => "src/Services/Postmaster";

    [Fact]
    public void ReconciliationJob_OnlyInvokes() =>
        Assert.Equal(
            JobPublishingKind.InvokesPerEntity,
            JobPublishingRules.Scan(SourceDirectory).Single(finding => finding.Path == ReconciliationJob).Kind
        );

    // La condición del patrón: lo que aplica es transaccional, así que su outbox confirma con lo guardado.
    [Fact]
    public void ApplyingHandler_IsTransactional()
    {
        var handle = typeof(ApplyPolledDeliveryFeedbackHandler).GetMethod(
            "Handle",
            BindingFlags.Public | BindingFlags.Static
        )!;

        Assert.Null(handle.GetCustomAttribute<NonTransactionalAttribute>());
        Assert.Contains(handle.GetParameters(), parameter => parameter.ParameterType == typeof(IUnitOfWork));
    }

    // PMS-06: lo que invoca el job consulta al proveedor sin transacción y delega el aplicar.
    [Theory]
    [InlineData(typeof(ReconcileDeliveryFeedbackHandler))]
    [InlineData(typeof(ReconcileEngagementFeedbackHandler))]
    public void PollingHandlers_AreNotTransactional(Type handler)
    {
        var handle = handler.GetMethod("Handle", BindingFlags.Public | BindingFlags.Static)!;

        Assert.NotNull(handle.GetCustomAttribute<NonTransactionalAttribute>());
        Assert.DoesNotContain(handle.GetParameters(), parameter => parameter.ParameterType == typeof(IUnitOfWork));
    }
}
