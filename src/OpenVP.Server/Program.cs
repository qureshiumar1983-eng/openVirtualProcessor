using OpenVP.Server;

if (args.Contains("--healthcheck")) return HealthCheck.Run(args);
OpenVpServer.Create(args).Run();
return 0;

public partial class Program { }
