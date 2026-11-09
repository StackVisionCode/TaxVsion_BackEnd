using System.Reflection;
using NetArchTest.Rules;

namespace TaxVision.Auth.Tests.Architecture;

/// <summary>
/// AUTH-5: Onboarding vive en sus propios ensamblados, en la misma solución, base y host. La dependencia
/// va en un solo sentido: Onboarding usa el núcleo de Auth y el núcleo no conoce Onboarding.
/// Dos puntos de unión, a propósito:
/// <list type="bullet">
/// <item>El núcleo pregunta por una reserva de onboarding con <c>ISubdomainHoldReader</c>, que implementa Onboarding.</item>
/// <item><c>AuthDbContext</c> y sus migraciones cubren las tablas de Onboarding: es una sola base.</item>
/// </list>
/// Los términos (<c>TermsVersion</c>) son del núcleo: Onboarding los lee, nunca al revés.
/// </summary>
public sealed class OnboardingModuleArchitectureTests
{
    private const string OnboardingRoot = "TaxVision.Auth.Onboarding";

    private static readonly Assembly CoreDomain = typeof(TaxVision.Auth.Domain.Users.User).Assembly;
    private static readonly Assembly CoreApplication =
        typeof(TaxVision.Auth.Application.Invitations.Commands.CreateInvitationHandler).Assembly;
    private static readonly Assembly CoreInfrastructure =
        typeof(TaxVision.Auth.Infrastructure.Persistence.AuthDbContext).Assembly;

    private static Assembly OnboardingDomain => Assembly.Load($"{OnboardingRoot}.Domain");

    private static Assembly OnboardingApplication => Assembly.Load($"{OnboardingRoot}.Application");

    private static Assembly OnboardingInfrastructure => Assembly.Load($"{OnboardingRoot}.Infrastructure");

    // Módulos hermanos que el dominio de Onboarding no toca. De TenantDomains solo usa el VO SubdomainSlug.
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
        "TaxVision.Auth.Domain.TenantDomains.TenantDomain",
        "TaxVision.Auth.Domain.TenantDomains.TenantSubdomainReservation",
        "TaxVision.Auth.Domain.TenantDomains.Events",
    ];

    // Onboarding solo entra al núcleo por Abstractions, Common, Terms y TenantDomains.
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
    ];

    [Fact]
    public void AuthCore_DoesNotReferenceOnboardingAssemblies()
    {
        Assert.NotNull(OnboardingDomain);
        Assert.NotNull(OnboardingApplication);
        Assert.NotNull(OnboardingInfrastructure);

        foreach (var core in new[] { CoreDomain, CoreApplication })
        {
            var references = core.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty);
            Assert.DoesNotContain(references, name => name.StartsWith(OnboardingRoot, StringComparison.Ordinal));

            var result = Types.InAssembly(core).ShouldNot().HaveDependencyOn(OnboardingRoot).GetResult();
            Assert.True(result.IsSuccessful, Fail(result));
        }
    }

    [Fact]
    public void CoreInfrastructure_ReachesOnboardingOnlyFromPersistence()
    {
        var result = Types
            .InAssembly(CoreInfrastructure)
            .That()
            .DoNotResideInNamespace("TaxVision.Auth.Infrastructure.Persistence")
            .ShouldNot()
            .HaveDependencyOn(OnboardingRoot)
            .GetResult();

        Assert.True(result.IsSuccessful, Fail(result));
        Assert.DoesNotContain(
            CoreInfrastructure.GetReferencedAssemblies(),
            reference => reference.Name == $"{OnboardingRoot}.Infrastructure"
        );
    }

    [Fact]
    public void Onboarding_Domain_DoesNotDependOnOtherAuthModules()
    {
        var result = Types
            .InAssembly(OnboardingDomain)
            .Should()
            .NotHaveDependencyOnAny(SiblingDomainModules)
            .GetResult();

        Assert.True(result.IsSuccessful, Fail(result));
    }

    [Fact]
    public void Onboarding_Application_DoesNotReferenceOtherAuthApplicationModules()
    {
        var result = Types
            .InAssembly(OnboardingApplication)
            .Should()
            .NotHaveDependencyOnAny(SiblingApplicationModules)
            .GetResult();

        Assert.True(result.IsSuccessful, Fail(result));
    }

    private static string Fail(TestResult result) =>
        "Offending types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
