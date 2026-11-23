namespace OpenVP.Server;

/// <summary>
/// <c>openvp --healthcheck</c>: asks the OpenVP already running on this computer's console port whether it is up (<c>GET /healthz</c>) and exits 0 or 1.
/// Docker and Kubernetes use it where there is no <c>curl</c>. The address comes from the same settings the server uses (<c>--urls</c>, <c>ASPNETCORE_URLS</c>, <c>Urls</c>, <c>appsettings.json</c>).
/// </summary>
public static class HealthCheck
{
    public static int Run(string[] args)
    {
        try
        {
            var cfg = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .AddCommandLine(args.Where(a => a != "--healthcheck").ToArray())
                .Build();
            var urls = cfg["urls"] ?? cfg["ASPNETCORE_URLS"] ?? "http://localhost:8080";
            var first = urls.Split(';', StringSplitOptions.RemoveEmptyEntries).First().Trim()
                .Replace("://*", "://127.0.0.1").Replace("://+", "://127.0.0.1").Replace("://0.0.0.0", "://127.0.0.1").Replace("://[::]", "://127.0.0.1");
            if (!Uri.TryCreate(first, UriKind.Absolute, out var u)) { Console.Error.WriteLine("Can't read the server address: " + first); return 1; }
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            var r = http.GetAsync(new Uri(u, "/healthz")).GetAwaiter().GetResult();
            if (r.IsSuccessStatusCode) return 0;
            Console.Error.WriteLine($"OpenVP answered {(int)r.StatusCode}.");
            return 1;
        }
        catch (Exception e) { Console.Error.WriteLine("OpenVP isn't answering: " + e.Message); return 1; }
    }
}
