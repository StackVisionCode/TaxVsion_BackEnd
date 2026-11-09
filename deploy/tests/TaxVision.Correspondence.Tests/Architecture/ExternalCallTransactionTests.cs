using System.Reflection;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Application.Messages;
using Wolverine.Attributes;

namespace TaxVision.Correspondence.Tests.Architecture;

/// <summary>COR-08: estos handlers llaman por HTTP; una transacción abierta perdería el rastro al revertirse.</summary>
public sealed class ExternalCallTransactionTests
{
    [Theory]
    [InlineData(typeof(SendDraftHandler))]
    [InlineData(typeof(DownloadAttachmentHandler))]
    public void HandlerWithHttpCall_IsNonTransactional(Type handler)
    {
        var handle = handler.GetMethod("Handle", BindingFlags.Public | BindingFlags.Static)!;

        Assert.NotNull(handle.GetCustomAttribute<NonTransactionalAttribute>());
    }
}
