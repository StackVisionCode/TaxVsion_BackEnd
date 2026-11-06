extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;

namespace TaxVision.CloudStorage.Tests.Architecture;

// CST-17: StuckScanRescueService usa InvokeAsync; los demás jobs no publican.
public sealed class CloudStorageJobPublishingTests : JobPublishingRulesBase
{
    protected override string SourceDirectory => "src/Services/CloudStorage";
}
