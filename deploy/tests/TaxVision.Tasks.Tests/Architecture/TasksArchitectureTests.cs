using System.Reflection;
using System.Text.RegularExpressions;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NetArchTest.Rules;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.Application;
using TaxVision.Tasks.ClientForms.Domain.Templates;
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
    private static readonly Assembly ClientFormsApplicationAssembly =
        typeof(TaxVision.Tasks.ClientForms.Application.Templates.Commands.CreateFormTemplateHandler).Assembly;
    private static readonly Assembly ClientFormsDomainAssembly = typeof(FormTemplate).Assembly;
    private static readonly Assembly EngineAssembly = typeof(IFormEngine).Assembly;

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

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Wolverine")]
    [InlineData("TaxVision.Tasks.Domain")]
    [InlineData("TaxVision.Tasks.Application")]
    [InlineData("TaxVision.Tasks.ClientForms.Application")]
    public void Client_forms_domain_should_stay_isolated(string forbiddenNamespace)
    {
        var result = Types
            .InAssembly(ClientFormsDomainAssembly)
            .ShouldNot()
            .HaveDependencyOn(forbiddenNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, $"Client Forms Domain depends on {forbiddenNamespace}: " + Describe(result));
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("TaxVision.Tasks.Infrastructure")]
    [InlineData("TaxVision.Tasks.Domain")]
    [InlineData("TaxVision.Tasks.Application")]
    public void Client_forms_application_should_stay_isolated(string forbiddenNamespace)
    {
        var result = Types
            .InAssembly(ClientFormsApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn(forbiddenNamespace)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Client Forms Application depends on {forbiddenNamespace}: " + Describe(result)
        );
    }

    [Theory]
    [InlineData("TaxVision.Tasks.ClientForms.Domain")]
    [InlineData("TaxVision.Tasks.ClientForms.Application")]
    public void Existing_tasks_layers_should_not_depend_on_client_forms(string forbiddenNamespace)
    {
        var domain = Types.InAssembly(DomainAssembly).ShouldNot().HaveDependencyOn(forbiddenNamespace).GetResult();
        var application = Types
            .InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn(forbiddenNamespace)
            .GetResult();

        Assert.True(domain.IsSuccessful, $"Tasks Domain depends on {forbiddenNamespace}: " + Describe(domain));
        Assert.True(
            application.IsSuccessful,
            $"Tasks Application depends on {forbiddenNamespace}: " + Describe(application)
        );
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Wolverine")]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("TaxVision.Tasks")]
    public void Engine_should_stay_framework_and_service_agnostic(string forbiddenNamespace)
    {
        var result = Types.InAssembly(EngineAssembly).ShouldNot().HaveDependencyOn(forbiddenNamespace).GetResult();

        Assert.True(result.IsSuccessful, $"Engine depends on {forbiddenNamespace}: " + Describe(result));
    }

    [Fact]
    public void Client_forms_domain_should_not_use_linq_or_status_shortcuts()
    {
        var violations = SourceFiles("TaxVision.Tasks.ClientForms.Domain")
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(file =>
                File.ReadAllLines(file)
                    .Index()
                    .Where(line => !line.Item.TrimStart().StartsWith("//") && DomainLinqPattern.IsMatch(line.Item))
                    .Select(line => $"{Path.GetFileName(file)}:{line.Index + 1} «{line.Item.Trim()}»")
            )
            .ToArray();

        Assert.Empty(violations);
    }

    /// <summary>
    /// El regex no ve un operador invocado sin punto ni uno que llegue por un alias; el IL sí.
    /// </summary>
    [Fact]
    public void Client_forms_domain_should_not_reference_linq()
    {
        var result = Types
            .InAssembly(ClientFormsDomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("System.Linq")
            .GetResult();

        Assert.True(result.IsSuccessful, "Client Forms Domain usa LINQ: " + Describe(result));
    }

    // Operadores de Enumerable con límite de palabra: «.AnyPending(» o «.Counter(» no cuentan. Math.Min/Max y
    // string.Concat no son LINQ, y un Contains con literal es el de string.
    private static readonly Regex DomainLinqPattern = new(
        @"(?<!\b(?:Math|string|String))\.(?:Where|Select|SelectMany|Any|All|First|FirstOrDefault|Single|SingleOrDefault|Last|LastOrDefault|ElementAt|ElementAtOrDefault|OrderBy|OrderByDescending|ThenBy|ThenByDescending|GroupBy|ToLookup|Sum|Min|Max|MinBy|MaxBy|Average|Count|LongCount|Aggregate|ToList|ToArray|ToDictionary|ToHashSet|Distinct|DistinctBy|Skip|SkipWhile|Take|TakeWhile|Cast|OfType|Concat|Except|ExceptBy|Intersect|IntersectBy|Union|UnionBy|Zip|SequenceEqual|DefaultIfEmpty|AsEnumerable)\b\s*(?:<[^<>()]*>)?\s*\("
            + @"|\.Contains\s*\(\s*(?!['""])"
            + @"|^\s*using\s+(?:static\s+)?System\.Linq\b"
            + @"|\bChangeStatus\s*\(",
        RegexOptions.Compiled
    );

    [Fact]
    public void Logs_should_never_name_sensitive_form_values()
    {
        var sensitiveTemplate = new Regex(
            @"Log(?:Trace|Debug|Information|Warning|Error|Critical)\s*\([^\r\n]*(Answers?|TaxIdentifier|Ssn|Plain|RawValue)",
            RegexOptions.IgnoreCase
        );
        var violations = ServiceSourceFiles()
            .SelectMany(file =>
                File.ReadAllLines(file)
                    .Index()
                    .Where(line => sensitiveTemplate.IsMatch(line.Item))
                    .Select(line => $"{Path.GetFileName(file)}:{line.Index + 1}")
            )
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Client_form_integration_events_should_not_expose_sensitive_values()
    {
        var sensitive = new Regex("answers?|ssn|taxidentifier|cipher|plaintext|rawvalue", RegexOptions.IgnoreCase);
        var violations = typeof(IntegrationEvent)
            .Assembly.GetTypes()
            .Where(type =>
                type.Namespace?.StartsWith("BuildingBlocks.Messaging.ClientForms", StringComparison.Ordinal) == true
            )
            .Where(type => typeof(IntegrationEvent).IsAssignableFrom(type))
            .SelectMany(type =>
                type.GetProperties()
                    .Where(property => sensitive.IsMatch(property.Name))
                    .Select(property => $"{type.Name}.{property.Name}")
            )
            .ToArray();

        Assert.Empty(violations);
    }

    /// <summary>
    /// Un listado nunca lleva respuestas ni valores sensibles. Se miran los DTO que devuelve cada
    /// <c>List*Handler</c>, los que se llaman como listado en cualquier namespace de Client Forms y,
    /// de forma transitiva, los DTO que cuelgan de ellos.
    /// </summary>
    [Fact]
    public void Client_form_list_dtos_should_not_expose_answers()
    {
        var sensitive = new Regex("answers?|ssn|taxidentifier|cipher|plaintext|rawvalue", RegexOptions.IgnoreCase);
        var listDtos = ListDtoRoots().SelectMany(ClientFormsDtoClosure).Distinct().ToArray();
        var violations = listDtos
            .SelectMany(type =>
                type.GetProperties()
                    .Where(property => sensitive.IsMatch(property.Name))
                    .Select(property => $"{type.FullName}.{property.Name}")
            )
            .ToArray();

        Assert.True(listDtos.Length > 0, "No se encontró ningún DTO de listado: el barrido dejó de ver los handlers.");
        Assert.Empty(violations);
    }

    private static IEnumerable<Type> ListDtoRoots()
    {
        var types = ClientFormsApplicationAssembly.GetTypes();
        var byHandler = types
            .Where(type =>
                type.Name.StartsWith("List", StringComparison.Ordinal)
                && type.Name.EndsWith("Handler", StringComparison.Ordinal)
            )
            .SelectMany(type =>
                type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(method => method.Name == "Handle")
            )
            .Select(method => method.ReturnType);
        var byName = types.Where(type =>
            type.Namespace?.StartsWith("TaxVision.Tasks.ClientForms.Application", StringComparison.Ordinal) == true
            && (
                type.Name.Contains("Summary", StringComparison.Ordinal)
                || type.Name.Contains("ListItem", StringComparison.Ordinal)
            )
        );
        return byHandler.Concat(byName);
    }

    // Abre Task<>, Result<>, PagedResult<> y colecciones hasta los DTO propios, y sigue por sus propiedades.
    private static IEnumerable<Type> ClientFormsDtoClosure(Type root)
    {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>([root]);
        while (pending.Count > 0)
        {
            var type = pending.Pop();
            if (!seen.Add(type))
                continue;
            if (type.IsArray)
                pending.Push(type.GetElementType()!);
            foreach (var argument in type.IsGenericType ? type.GetGenericArguments() : [])
                pending.Push(argument);
            if (type.Assembly != ClientFormsApplicationAssembly || type.IsEnum || type.IsInterface)
                continue;
            yield return type;
            foreach (var property in type.GetProperties())
                pending.Push(property.PropertyType);
        }
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

    /// <summary>
    /// Ningún <c>Handle</c> pasa de 30 líneas de cuerpo, y en un handler o consumer tampoco ningún otro
    /// método: un <c>Handle</c> de una línea que delega en un privado enorme no cumple la regla.
    /// </summary>
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

        foreach (var file in HandlerAndConsumerSourceFiles())
        foreach (var (methodName, bodyLines) in MeasureMemberBodies(File.ReadAllLines(file)))
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

    /// <summary>
    /// La lectura entre tenants que sirve a los tenants (la biblioteca) filtra por TenantId
    /// (<c>== PlatformTenant.Id</c>). Cualquier <c>IgnoreQueryFilters</c> en Infrastructure/ClientForms
    /// debe ir acompañado, en el mismo archivo, de un filtro explícito de <c>TenantId</c>, salvo los
    /// lectores cross-tenant sancionados (gauges de observabilidad y uso de catálogo de plataforma),
    /// que deben agregarse a <see cref="SanctionedCrossTenantReaders"/> a propósito.
    /// </summary>
    [Fact]
    public void ClientForms_IgnoreQueryFilters_must_filter_by_tenant()
    {
        var violations = new List<string>();

        foreach (var file in ClientFormsInfrastructureSourceFiles())
        {
            var name = Path.GetFileName(file);
            if (SanctionedCrossTenantReaders.Contains(name))
                continue;
            var text = File.ReadAllText(file);
            if (
                text.Contains("IgnoreQueryFilters", StringComparison.Ordinal)
                && !text.Contains("TenantId", StringComparison.Ordinal)
            )
                violations.Add(name);
        }

        Assert.True(
            violations.Count == 0,
            "IgnoreQueryFilters sin filtro de TenantId en ClientForms: " + string.Join(", ", violations)
        );
    }

    // Lectores cross-tenant a propósito: gauges para todos los tenants y uso de catálogo (plataforma).
    private static readonly HashSet<string> SanctionedCrossTenantReaders = new(StringComparer.Ordinal)
    {
        "ClientFormsBacklogReader.cs",
        "FormCatalogUsageReader.cs",
    };

    private static IEnumerable<string> ClientFormsInfrastructureSourceFiles() =>
        Directory
            .EnumerateFiles(
                Path.Combine(ServiceRoot, "TaxVision.Tasks.Infrastructure", "ClientForms"),
                "*.cs",
                SearchOption.AllDirectories
            )
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static readonly Regex HandlerDeclarationPattern = new(
        @"^(?<indent>[ \t]*)(?:public|internal)\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+)*class\s+(?<name>\w+(?:Handler|Consumer))\b",
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

    /// <summary>
    /// Todo cuerpo con llaves de un handler o consumer, sea público o privado, estático o no. No depende de
    /// reconocer la firma: toma cada llave sola a la indentación de miembro y cuenta hasta su cierre.
    /// </summary>
    private static IEnumerable<(string MethodName, int BodyLines)> MeasureMemberBodies(string[] lines)
    {
        foreach (Match declaration in HandlerDeclarationPattern.Matches(string.Join('\n', lines)))
        {
            var memberIndent = declaration.Groups["indent"].Value + "    ";
            for (var index = 0; index < lines.Length; index++)
            {
                if (lines[index].TrimEnd() != memberIndent + "{")
                    continue;

                var close = Array.FindIndex(lines, index + 1, line => line.TrimEnd() == memberIndent + "}");
                if (close < 0)
                    break;

                yield return (
                    $"{declaration.Groups["name"].Value}.{MemberNameBefore(lines, index, memberIndent)}",
                    close - index - 1
                );
                index = close;
            }
        }
    }

    /// <summary>La firma es la última línea a indentación de miembro antes de la llave que no cierra paréntesis.</summary>
    private static string MemberNameBefore(string[] lines, int openBrace, string memberIndent)
    {
        for (var index = openBrace - 1; index >= 0; index--)
        {
            var line = lines[index];
            if (!line.StartsWith(memberIndent, StringComparison.Ordinal) || line.Length == memberIndent.Length)
                continue;
            var first = line[memberIndent.Length];
            if (first == ' ' || first == ')')
                continue;

            var name = MemberNamePattern.Match(line);
            return name.Success ? name.Groups["name"].Value : line.Trim();
        }

        return "?";
    }

    private static readonly Regex MemberNamePattern = new(@"(?<name>\w+)\s*(?:<[^()]*>)?\s*\(", RegexOptions.Compiled);

    private static IEnumerable<string> HandlerAndConsumerSourceFiles() =>
        ApplicationSourceFiles()
            .Concat(SourceFiles("TaxVision.Tasks.Infrastructure"))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Where(file => HandlerDeclarationPattern.IsMatch(File.ReadAllText(file)));

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
    [InlineData(@"\bIFormFile(?:Collection)?\b")]
    public void Task_should_never_touch_file_bytes(string forbidden)
    {
        // Barrido propio: ServiceSourceFiles() salta la capa Api, que es justo por donde entraría un
        // IFormFile si alguien decidiera recibir el archivo acá en vez de en CloudStorage. El límite de
        // palabra deja pasar nombres como IFormFileScanVerdictRepository.
        var pattern = new Regex(forbidden);
        var offenders = AllServiceSourceFiles()
            .Where(file => pattern.IsMatch(File.ReadAllText(file)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"«{forbidden}» no tiene lugar en Task —el byte vive en CloudStorage—: " + string.Join(", ", offenders)
        );
    }

    /// <summary>El mismo guardrail por reflexión: ni un parámetro de acción ni una propiedad suya es un archivo.</summary>
    [Fact]
    public void Api_actions_should_never_receive_file_bytes()
    {
        var offenders = ControllerTypes(ApiAssembly)
            .SelectMany(controller => Actions(controller).Select(action => (controller, action)))
            .SelectMany(entry =>
                entry.action.GetParameters().Select(parameter => (entry.controller, entry.action, parameter))
            )
            .Where(entry =>
                IsFileType(entry.parameter.ParameterType)
                || entry.parameter.ParameterType.GetProperties().Any(property => IsFileType(property.PropertyType))
            )
            .Select(entry => $"{entry.controller.Name}.{entry.action.Name}({entry.parameter.Name})")
            .ToList();

        Assert.True(offenders.Count == 0, "El byte vive en CloudStorage: " + string.Join(", ", offenders));
    }

    private static bool IsFileType(Type type) =>
        typeof(Microsoft.AspNetCore.Http.IFormFile).IsAssignableFrom(type)
        || typeof(Microsoft.AspNetCore.Http.IFormFileCollection).IsAssignableFrom(type)
        || typeof(IEnumerable<Microsoft.AspNetCore.Http.IFormFile>).IsAssignableFrom(type);

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
        new[]
        {
            "TaxVision.Tasks.Domain",
            "TaxVision.Tasks.Application",
            "TaxVision.Tasks.ClientForms.Domain",
            "TaxVision.Tasks.ClientForms.Application",
            "TaxVision.DataCollection.Engine",
            "TaxVision.Tasks.Infrastructure",
        }
            .SelectMany(SourceFiles)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

    private static IEnumerable<string> SourceFiles(string project) =>
        Directory.EnumerateFiles(Path.Combine(ServiceRoot, project), "*.cs", SearchOption.AllDirectories);

    private static IEnumerable<string> AllServiceSourceFiles() =>
        Directory
            .EnumerateFiles(ServiceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

    private static string ControllersDirectory => Path.Combine(ServiceRoot, "TaxVision.Tasks.Api", "Controllers");

    private static string ServiceRoot => Directory.GetParent(ApplicationSourceDirectory)!.FullName;

    private static IEnumerable<string> ApplicationSourceFiles() =>
        new[] { ApplicationSourceDirectory, ClientFormsApplicationSourceDirectory }
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string ClientFormsApplicationSourceDirectory =>
        Path.Combine(ServiceRoot, "TaxVision.Tasks.ClientForms.Application");

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
