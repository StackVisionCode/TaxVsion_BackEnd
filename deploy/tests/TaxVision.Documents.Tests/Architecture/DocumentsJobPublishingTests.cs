extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;

namespace TaxVision.Documents.Tests.Architecture;

// DOC-8: Documents no tiene jobs hoy (el job opcional de DOC-1 no se hizo); uno nuevo entra por esta regla.
public sealed class DocumentsJobPublishingTests : JobPublishingRulesBase
{
    protected override string SourceDirectory => "src/Services/Documents";
}
