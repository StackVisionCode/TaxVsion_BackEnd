using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// Visibilidad por asignación (P2): cada servicio consumidor expone un flag GLOBAL de despliegue
/// (default OFF) para el rollout seguro migración→siembra→verificar→encender. El flag debe estar
/// cableado en los DOS lados del control plane de prod: referenciado en docker-compose.yml como
/// <c>${VAR:-false}</c> y escrito por <c>deploy.yml</c> a partir del secret homónimo. Si falta
/// cualquiera de los dos, encender el flag en P2 no tendría efecto (o el compose fallaría al resolver
/// la var). Mismo contrato que <see cref="ComposeInternalBaseUrlWiringTests"/> y los *DeployConfigWiringTests
/// por servicio.
/// </summary>
public sealed class AssignmentVisibilityDeployWiringTests
{
    // Un flag por consumidor: 7 .NET (Billing, Signature, Sms, Calendar, Notes, Campaigns, Tasks) + Communication (Node).
    private static readonly string[] AssignmentVisibilityEnvVars =
    [
        "BILLING_ASSIGNMENT_VISIBILITY_ENABLED",
        "SIGNATURE_ASSIGNMENT_VISIBILITY_ENABLED",
        "SMS_ASSIGNMENT_VISIBILITY_ENABLED",
        "CALENDAR_ASSIGNMENT_VISIBILITY_ENABLED",
        "NOTES_ASSIGNMENT_VISIBILITY_ENABLED",
        "CAMPAIGNS_ASSIGNMENT_VISIBILITY_ENABLED",
        "TASKS_ASSIGNMENT_VISIBILITY_ENABLED",
        "COMMUNICATION_ASSIGNMENT_VISIBILITY_ENABLED",
    ];

    [Theory]
    [MemberData(nameof(AssignmentVisibilityVars))]
    public void Assignment_visibility_flag_is_mapped_in_compose(string varName)
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        Assert.Matches(new Regex(@"\$\{" + Regex.Escape(varName) + @"[:}]"), compose);
    }

    [Theory]
    [MemberData(nameof(AssignmentVisibilityVars))]
    public void Assignment_visibility_flag_defaults_to_false_in_compose(string varName)
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        // Rollout seguro: el default en el compose es siempre false (se enciende por secret en P2).
        Assert.Contains("${" + varName + ":-false}", compose);
    }

    [Theory]
    [MemberData(nameof(AssignmentVisibilityVars))]
    public void Assignment_visibility_flag_is_passed_by_deploy_workflow(string varName)
    {
        var workflow = ReadRepoFile(".github/workflows/deploy.yml");

        Assert.Contains($"{varName}=${{{{ secrets.{varName} }}}}", workflow);
    }

    public static IEnumerable<object[]> AssignmentVisibilityVars() =>
        AssignmentVisibilityEnvVars.Select(v => new object[] { v });

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate '{relativePath}' walking up from {AppContext.BaseDirectory}."
        );
    }
}
