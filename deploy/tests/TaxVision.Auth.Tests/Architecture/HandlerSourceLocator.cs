namespace TaxVision.Auth.Tests.Architecture;

/// <summary>
/// PayFlow (auditoría F20) — helper de texto plano para las fitness functions de tamaño de
/// handler. Duplicado deliberadamente en TaxVision.PaymentApp.Tests: los dos handlers que motivan
/// F20 viven en assemblies/microservicios distintos (Auth vs PaymentApp), así que no hay un
/// proyecto de test compartido natural donde poner esto sin crear una dependencia cruzada nueva
/// entre servicios solo para un helper de 20 líneas.
/// </summary>
internal static class HandlerSourceLocator
{
    public static string FindRepoFile(string repoRelativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException(
                "Could not locate the repo root (TaxVision.slnx) from the test output directory."
            );

        var fullPath = Path.Combine(dir.FullName, repoRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Expected source file not found: {fullPath}");

        return fullPath;
    }

    /// <summary>
    /// Cuenta las líneas físicas entre el <c>{</c> de apertura y el <c>}</c> de cierre del método
    /// <c>Handle</c> (primera ocurrencia de " Handle(" en el archivo), usando conteo de llaves para
    /// encontrar el cierre correcto. No parsea C# de verdad — asume el estilo de este repo (un solo
    /// método público llamado exactamente <c>Handle</c> por archivo de handler).
    /// </summary>
    public static int CountHandleMethodBodyLines(string filePath)
    {
        var text = File.ReadAllText(filePath);

        var handleIndex = text.IndexOf(" Handle(", StringComparison.Ordinal);
        if (handleIndex < 0)
            throw new InvalidOperationException($"Could not find a 'Handle(' method in {filePath}.");

        var bodyOpenIndex = text.IndexOf('{', handleIndex);
        if (bodyOpenIndex < 0)
            throw new InvalidOperationException($"Could not find the body of 'Handle' in {filePath}.");

        var depth = 0;
        var bodyCloseIndex = -1;
        for (var i = bodyOpenIndex; i < text.Length; i++)
        {
            if (text[i] == '{')
                depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    bodyCloseIndex = i;
                    break;
                }
            }
        }

        if (bodyCloseIndex < 0)
            throw new InvalidOperationException($"Could not find the closing brace of 'Handle' in {filePath}.");

        var body = text.Substring(bodyOpenIndex + 1, bodyCloseIndex - bodyOpenIndex - 1);
        return body.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line));
    }
}
