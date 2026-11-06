// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Documents.Application.Branding;
using TaxVision.Documents.Domain.Branding;

namespace TaxVision.Documents.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class DocumentsLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(DocumentBranding).Assembly;

    protected override Assembly ApplicationAssembly => typeof(UpsertDocumentBrandingHandler).Assembly;
}
