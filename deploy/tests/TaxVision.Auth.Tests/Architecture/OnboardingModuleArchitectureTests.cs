using System.Reflection;
using NetArchTest.Rules;

namespace TaxVision.Auth.Tests.Architecture;

/// <summary>
/// PayFlow Fase 3 — fitness functions que protegen las fronteras del módulo Onboarding
/// (bounded context modular dentro de Auth, ver PayFlow_Implementation_Plan.md §3.1). El módulo
/// puede depender de <c>TenantDomains.SubdomainSlug</c> (VO compartido) pero de ningún otro tipo
/// de los módulos hermanos de Auth. Nota: a diferencia de lo asumido en §3.1 del plan, Auth NO
/// tiene un VO <c>Email</c> dedicado — <c>User.Email</c> es un <c>string</c> plano — así que esa
/// excepción no aplica hoy; solo <c>SubdomainSlug</c> es un VO real y compartible.
///
/// PayFlow Fase 6 (retrofit) — segunda excepción documentada: <c>Terms</c> (self-service ToS/AUP,
/// preexistente) ahora resuelve su "version vigente" contra Onboarding.TermsVersions.TermsVersion
/// (Opcion C del plan) en vez de TermsOptions.CurrentVersion — ver AcceptTermsHandler. Es una
/// integración deliberada, unidireccional (Terms → Onboarding, nunca al revés), así que
/// <c>NonOnboarding_Files_DoNotReferenceOnboardingInternals</c> excluye explícitamente
/// <c>Terms</c> de sus dos comprobaciones (Domain no lo necesita hoy, pero se documenta ahí por
/// simetría) — el resto de módulos hermanos de Auth siguen prohibidos de tocar Onboarding.
/// </summary>
public sealed class OnboardingModuleArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(TaxVision.Auth.Domain.Users.User).Assembly;
    private static readonly Assembly ApplicationAssembly =
        typeof(TaxVision.Auth.Application.Invitations.Commands.CreateInvitationHandler).Assembly;

    private const string OnboardingDomainNamespace = "TaxVision.Auth.Domain.Onboarding";
    private const string OnboardingApplicationNamespace = "TaxVision.Auth.Application.Onboarding";

    // PayFlow Fase 6 — excepción documentada (ver doc-comment de la clase): Terms es el único
    // módulo hermano autorizado a depender de Onboarding, y solo en un sentido.
    private const string TermsApplicationNamespace = "TaxVision.Auth.Application.Terms";

    private static readonly string[] SiblingDomainModules =
    [
        "TaxVision.Auth.Domain.Users",
        "TaxVision.Auth.Domain.Sessions",
        "TaxVision.Auth.Domain.Mfa",
        "TaxVision.Auth.Domain.Credentials",
        "TaxVision.Auth.Domain.Roles",
        "TaxVision.Auth.Domain.Invitations",
        "TaxVision.Auth.Domain.Tenants",
        "TaxVision.Auth.Domain.Audit",
        "TaxVision.Auth.Domain.RefreshTokens",
        "TaxVision.Auth.Domain.Terms",
        // TenantDomains sí puede tener afinidad (SubdomainSlug es VO compartido), pero los tipos
        // de negocio del módulo (el aggregate y sus eventos) siguen prohibidos:
        "TaxVision.Auth.Domain.TenantDomains.TenantDomain",
        "TaxVision.Auth.Domain.TenantDomains.TenantSubdomainReservation",
        "TaxVision.Auth.Domain.TenantDomains.Events",
    ];

    private static readonly string[] SiblingApplicationModules =
    [
        "TaxVision.Auth.Application.Users",
        "TaxVision.Auth.Application.Sessions",
        "TaxVision.Auth.Application.Mfa",
        "TaxVision.Auth.Application.Credentials",
        "TaxVision.Auth.Application.Roles",
        "TaxVision.Auth.Application.Invitations",
        "TaxVision.Auth.Application.Tenants",
        "TaxVision.Auth.Application.Audit",
        "TaxVision.Auth.Application.RefreshTokens",
        "TaxVision.Auth.Application.Terms",
        "TaxVision.Auth.Application.TenantDomains",
    ];

    [Fact]
    public void Onboarding_Domain_DoesNotDependOnOtherAuthModules()
    {
        var result = Types
            .InAssembly(DomainAssembly)
            .That()
            .ResideInNamespace(OnboardingDomainNamespace)
            .Should()
            .NotHaveDependencyOnAny(SiblingDomainModules)
            .GetResult();

        Assert.True(result.IsSuccessful, Fail(result));
    }

    [Fact]
    public void Onboarding_Application_DoesNotReferenceOtherAuthApplicationModules()
    {
        var result = Types
            .InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespace(OnboardingApplicationNamespace)
            .Should()
            .NotHaveDependencyOnAny(SiblingApplicationModules)
            .GetResult();

        Assert.True(result.IsSuccessful, Fail(result));
    }

    [Fact]
    public void NonOnboarding_Files_DoNotReferenceOnboardingInternals()
    {
        var domainResult = Types
            .InAssembly(DomainAssembly)
            .That()
            .DoNotResideInNamespace(OnboardingDomainNamespace)
            .Should()
            .NotHaveDependencyOnAny(OnboardingDomainNamespace)
            .GetResult();

        var applicationResult = Types
            .InAssembly(ApplicationAssembly)
            .That()
            .DoNotResideInNamespace(OnboardingApplicationNamespace)
            .And()
            .DoNotResideInNamespace(TermsApplicationNamespace)
            .Should()
            .NotHaveDependencyOnAny(OnboardingApplicationNamespace, OnboardingDomainNamespace)
            .GetResult();

        Assert.True(domainResult.IsSuccessful, Fail(domainResult));
        Assert.True(applicationResult.IsSuccessful, Fail(applicationResult));
    }

    private static string Fail(TestResult result) =>
        "Offending types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
