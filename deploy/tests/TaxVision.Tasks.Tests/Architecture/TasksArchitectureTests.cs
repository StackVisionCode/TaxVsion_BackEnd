using System.Reflection;
using System.Text.RegularExpressions;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NetArchTest.Rules;
using TaxVision.Tasks.Application;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Tests.Architecture;

/// <summary>
/// Fitness functions del servicio. Las dos últimas —un handler por archivo y el largo del
/// <c>Handle</c>— son propias de Tasks; el resto es el patrón compartido del monorepo.
///
/// <para>
/// Mientras Application y Api estén casi vacíos varias reglas pasan de vacío. Están escritas ahora a
/// propósito: el guardrail tiene que existir antes del primer handler, no después.
/// </para>
/// </summary>
public sealed class TasksArchitectureTests
{
    private const int MaxHandleBodyLines = 30;

    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(AssemblyMarker).Assembly;
    private static readonly Assembly DomainAssembly = typeof(TaskItem).Assembly;

    [Fact]
    public void Controller_actions_should_declare_AllowActorTypes()
    {
        var violations = FindActionsMissingAllowActorTypes(ApiAssembly);
        Assert.True(
            violations.Count == 0,
            "Actions missing [AllowActorTypes] (method or controller level): " + string.Join(", ", violations)
        );
    }

    [Fact]
    public void Controller_actions_should_declare_RateLimit_or_RateLimitExempt()
    {
        var violations = FindActionsMissingRateLimit(ApiAssembly);
        Assert.True(
            violations.Count == 0,
            "Actions missing [RateLimit] or [RateLimitExempt] (method or controller level): "
                + string.Join(", ", violations)
        );
    }

    [Fact]
    public void Controllers_should_not_depend_on_Infrastructure()
    {
        var result = Types
            .InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("TaxVision.Tasks.Api.Controllers")
            .ShouldNot()
            .HaveDependencyOn("TaxVision.Tasks.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, "Controllers depending on Infrastructure: " + Describe(result));
    }

    /// <summary>
    /// Si Domain referenciara EF, las decisiones del agregado quedarían imposibles de probar sin base
    /// de datos. <c>Ical.Net</c> sí está permitido: es una librería de dominio puro, sin IO.
    /// </summary>
    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("TaxVision.Tasks.Infrastructure")]
    [InlineData("TaxVision.Tasks.Application")]
    public void Domain_should_not_depend_on_infrastructure_concerns(string forbiddenNamespace)
    {
        var result = Types.InAssembly(DomainAssembly).ShouldNot().HaveDependencyOn(forbiddenNamespace).GetResult();

        Assert.True(result.IsSuccessful, $"Domain types depending on {forbiddenNamespace}: " + Describe(result));
    }

    [Fact]
    public void Application_should_not_depend_on_infrastructure()
    {
        var result = Types
            .InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn("TaxVision.Tasks.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, "Application types depending on Infrastructure: " + Describe(result));
    }

    /// <summary>
    /// Un handler por archivo, con el archivo nombrado como él. El <c>record</c> del command puede
    /// acompañarlo; dos handlers no.
    /// </summary>
    [Fact]
    public void No_source_file_should_declare_more_than_one_handler()
    {
        var violations = new List<string>();

        foreach (var file in ApplicationSourceFiles())
        {
            var declared = HandlerDeclarationPattern
                .Matches(File.ReadAllText(file))
                .Select(match => match.Groups["name"].Value)
                .ToList();

            if (declared.Count > 1)
                violations.Add($"{Path.GetFileName(file)} declara {declared.Count}: {string.Join(", ", declared)}");
            else if (declared.Count == 1 && Path.GetFileNameWithoutExtension(file) != declared[0])
                violations.Add($"{Path.GetFileName(file)} declara {declared[0]} — el archivo debe llamarse igual");
        }

        Assert.True(violations.Count == 0, "Un handler por archivo: " + string.Join(" | ", violations));
    }

    /// <summary>Ningún <c>Handle</c> pasa de 30 líneas de cuerpo. El número medido va en el mensaje.</summary>
    [Fact]
    public void No_Handle_method_should_exceed_the_body_line_budget()
    {
        var violations = new List<string>();

        foreach (var file in ApplicationSourceFiles())
        foreach (var (methodName, bodyLines) in MeasureHandleBodies(File.ReadAllLines(file)))
        {
            if (bodyLines > MaxHandleBodyLines)
                violations.Add($"{Path.GetFileName(file)}.{methodName} = {bodyLines} líneas");
        }

        Assert.True(
            violations.Count == 0,
            $"Handlers de más de {MaxHandleBodyLines} líneas — extraer métodos privados: "
                + string.Join(" | ", violations)
        );
    }

    private static readonly Regex HandlerDeclarationPattern = new(
        @"^\s*(?:public|internal)\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+)*class\s+(?<name>\w+(?:Handler|Consumer))\b",
        RegexOptions.Multiline | RegexOptions.Compiled
    );

    private static readonly Regex HandleSignaturePattern = new(
        @"^(?<indent>\s*)(?:public|internal|private)\s+(?:static\s+)?[\w<>,\.\[\]\?\s]+\s(?<name>Handle|HandleAsync|Consume|ConsumeAsync)\s*\(",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Líneas entre la llave que abre el cuerpo y la que lo cierra a la misma indentación. Se apoya en
    /// que CSharpier alinea las llaves con la firma; un método de cuerpo de expresión se salta.
    /// </summary>
    private static IEnumerable<(string MethodName, int BodyLines)> MeasureHandleBodies(string[] lines)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            var signature = HandleSignaturePattern.Match(lines[index]);
            if (!signature.Success)
                continue;

            var closingBrace = signature.Groups["indent"].Value + "}";
            var open = FindBodyOpeningBrace(lines, index);
            if (open < 0)
                continue;

            var close = Array.FindIndex(lines, open + 1, line => line.TrimEnd() == closingBrace);
            if (close < 0)
                continue;

            yield return (signature.Groups["name"].Value, close - open - 1);
            index = close;
        }
    }

    /// <summary>Primera llave de apertura tras la firma; -1 si el método es de cuerpo de expresión.</summary>
    private static int FindBodyOpeningBrace(string[] lines, int signatureIndex)
    {
        for (var index = signatureIndex; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd();
            if (line.EndsWith('{'))
                return index;
            if (line.EndsWith(';'))
                return -1;
        }

        return -1;
    }

    [Fact]
    public void No_comment_should_reference_the_plan()
    {
        var violations = new List<string>();

        foreach (var file in ServiceSourceFiles())
        foreach (var (number, line) in File.ReadAllLines(file).Index())
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("//") && !trimmed.StartsWith("///"))
                continue;

            var hit = PlanReferencePattern.Match(trimmed);
            if (hit.Success)
                violations.Add($"{Path.GetFileName(file)}:{number + 1} «{hit.Value}»");
        }

        Assert.True(
            violations.Count == 0,
            "La trazabilidad al plan vive en los MD, no en el .cs: " + string.Join(" | ", violations)
        );
    }

    /// <summary>
    /// El objeto interno del staff no se le abre al cliente añadiéndole un actor type. Lleva las
    /// notas internas, el asignado y las horas imputadas: lo que el cliente ve es
    /// <c>ClientRequest</c>, que es otro agregado con otro ciclo de vida.
    ///
    /// <para>
    /// Los controllers del namespace <c>Portal</c> sí lo declaran —son la superficie del cliente— y
    /// por eso quedan fuera de la regla; el resto, nunca.
    /// </para>
    /// </summary>
    [Fact]
    public void Only_portal_controllers_may_open_up_to_the_customer()
    {
        var offenders = Directory
            .EnumerateFiles(ControllersDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Portal{Path.DirectorySeparatorChar}"))
            .Where(file => File.ReadAllText(file).Contains("ActorType.CustomerPortal", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "El cliente ve ClientRequest, no la tarea interna: " + string.Join(", ", offenders)
        );
    }

    /// <summary>
    /// El estado de una tarea es un enum, no una palabra. Comparar contra <c>"completed"</c> —o su
    /// traducción— compila, pasa los tests y se rompe el día que alguien renombra el enum o cambia
    /// el idioma de la UI.
    /// </summary>
    [Fact]
    public void No_source_should_compare_task_state_against_a_string_literal()
    {
        // Sólo comparaciones: un literal que viaja dentro de un contrato de integración es texto por
        // diseño, y prohibirlo obligaría a inventar un enum compartido entre servicios.
        var pattern = new Regex(
            @"(==|!=|\.Equals\(|case\s+)\s*""(completed|complete|completado|completada|done|terminado|terminada)""",
            RegexOptions.IgnoreCase
        );

        var violations = ServiceSourceFiles()
            .SelectMany(file =>
                File.ReadAllLines(file)
                    .Index()
                    .Where(entry => !entry.Item.TrimStart().StartsWith("//") && pattern.IsMatch(entry.Item))
                    .Select(entry => $"{Path.GetFileName(file)}:{entry.Index + 1}")
            )
            .ToList();

        Assert.True(
            violations.Count == 0,
            "El estado se compara contra el enum, nunca contra texto: " + string.Join(" | ", violations)
        );
    }

    /// <summary>
    /// El reloj lo arranca la persona, no el sistema. Que <c>Create</c>, <c>Assign</c> o un consumer
    /// lo arranquen solos imputaría horas que nadie trabajó.
    /// </summary>
    [Fact]
    public void StartTimer_should_only_be_called_by_its_own_handler()
    {
        var callers = ServiceSourceFiles()
            .Where(file => File.ReadAllText(file).Contains("StartTimer("))
            .Select(Path.GetFileName)
            .Where(name => name != "TaskItem.cs" && name != "StartTaskTimerHandler.cs")
            .ToList();

        Assert.True(callers.Count == 0, "StartTimer sólo lo dispara su propio handler: " + string.Join(", ", callers));
    }

    /// <summary>
    /// Task guarda ids de archivo, nunca bytes. Una referencia a MinIO, S3 o <c>IFormFile</c>
    /// significa que alguien empezó a mover el archivo por acá en vez de por CloudStorage.
    /// </summary>
    [Theory]
    [InlineData("Minio")]
    [InlineData("AmazonS3")]
    [InlineData("IFormFile")]
    public void Task_should_never_touch_file_bytes(string forbidden)
    {
        // Barrido propio: ServiceSourceFiles() salta la capa Api, que es justo por donde entraría un
        // IFormFile si alguien decidiera recibir el archivo acá en vez de en CloudStorage.
        var offenders = AllServiceSourceFiles()
            .Where(file => File.ReadAllText(file).Contains(forbidden, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"«{forbidden}» no tiene lugar en Task —el byte vive en CloudStorage—: " + string.Join(", ", offenders)
        );
    }

    [Fact]
    public void Raw_sql_should_stay_where_it_was_justified()
    {
        var found = ServiceSourceFiles()
            .SelectMany(file =>
                File.ReadAllLines(file)
                    .Where(line => line.Contains("FromSqlRaw") || line.Contains("ExecuteSqlRaw"))
                    .Select(_ => Path.GetFileName(file))
            )
            .ToHashSet();

        // CTE recursivo, UPDLOCK y MERGE masivo no tienen forma en LINQ; lo demás sí la tiene.
        string[] justified = ["TaskDependencyRepository.cs", "CustomerDirectoryRepository.cs"];

        Assert.True(
            found.SetEquals(justified),
            "SQL crudo fuera de lo acordado — escribirlo en LINQ: " + string.Join(", ", found.Except(justified))
        );
    }

    private static readonly Regex PlanReferencePattern = new(
        @"ADR-[A-Z]|Fase\s+\d|Checkpoint|§|\d{2}_[A-Z]\w*\.md",
        RegexOptions.Compiled
    );

    private static IEnumerable<string> ServiceSourceFiles() =>
        new[] { "TaxVision.Tasks.Domain", "TaxVision.Tasks.Application", "TaxVision.Tasks.Infrastructure" }
            .Select(project => Path.Combine(ServiceRoot, project))
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

    private static IEnumerable<string> AllServiceSourceFiles() =>
        Directory
            .EnumerateFiles(ServiceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

    private static string ControllersDirectory => Path.Combine(ServiceRoot, "TaxVision.Tasks.Api", "Controllers");

    private static string ServiceRoot => Directory.GetParent(ApplicationSourceDirectory)!.FullName;

    private static IEnumerable<string> ApplicationSourceFiles() =>
        Directory
            .EnumerateFiles(ApplicationSourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>
    /// Esas dos reglas miran el texto fuente —nombre de archivo y largo de método no sobreviven a la
    /// compilación— así que hay que localizar el repo desde el binario del test.
    /// </summary>
    private static string ApplicationSourceDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
                directory = directory.Parent;

            if (directory is null)
                throw new InvalidOperationException($"No se encontró TaxVision.slnx desde {AppContext.BaseDirectory}.");

            return Path.Combine(directory.FullName, "src", "Services", "Tasks", "TaxVision.Tasks.Application");
        }
    }

    private static string Describe(TestResult result) =>
        string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>());

    private static List<string> FindActionsMissingAllowActorTypes(Assembly apiAssembly)
    {
        var violations = new List<string>();
        foreach (var controllerType in ControllerTypes(apiAssembly))
        {
            var classIsAnonymous =
                controllerType.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null;
            var classAllowActorTypes = controllerType.GetCustomAttribute<AllowActorTypesAttribute>(inherit: true);

            foreach (var action in Actions(controllerType))
            {
                if (classIsAnonymous || action.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                    continue;

                var allowActorTypes = action.GetCustomAttribute<AllowActorTypesAttribute>() ?? classAllowActorTypes;
                if (allowActorTypes is null)
                    violations.Add($"{controllerType.FullName}.{action.Name}");
            }
        }

        return violations;
    }

    private static List<string> FindActionsMissingRateLimit(Assembly apiAssembly)
    {
        var violations = new List<string>();
        foreach (var controllerType in ControllerTypes(apiAssembly))
        {
            var classRateLimit = controllerType.GetCustomAttribute<RateLimitAttribute>(inherit: true);
            var classRateLimitExempt = controllerType.GetCustomAttribute<RateLimitExemptAttribute>(inherit: true);

            foreach (var action in Actions(controllerType))
            {
                if (!action.GetCustomAttributes().OfType<HttpMethodAttribute>().Any())
                    continue;

                var rateLimit = action.GetCustomAttribute<RateLimitAttribute>() ?? classRateLimit;
                var rateLimitExempt = action.GetCustomAttribute<RateLimitExemptAttribute>() ?? classRateLimitExempt;

                if (rateLimit is null && rateLimitExempt is null)
                    violations.Add($"{controllerType.FullName}.{action.Name}");
            }
        }

        return violations;
    }

    private static IEnumerable<Type> ControllerTypes(Assembly apiAssembly) =>
        Types.InAssembly(apiAssembly).That().Inherit(typeof(ControllerBase)).And().AreClasses().GetTypes();

    private static IEnumerable<MethodInfo> Actions(Type controllerType) =>
        controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NonActionAttribute>() is null);
}
