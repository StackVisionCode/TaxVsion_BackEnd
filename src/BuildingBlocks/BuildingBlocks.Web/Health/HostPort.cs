namespace BuildingBlocks.Health;

public sealed record HostPort(string Host, int Port)
{
    public static HostPort Parse(string value, int defaultPort)
    {
        var firstEndpoint = value.Split(',', StringSplitOptions.TrimEntries)[0];
        if (Uri.TryCreate($"tcp://{firstEndpoint}", UriKind.Absolute, out var uri))
            return new HostPort(uri.Host, uri.IsDefaultPort ? defaultPort : uri.Port);

        return new HostPort(firstEndpoint, defaultPort);
    }
}
