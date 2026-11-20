using MetroRail.Tools;

// dotnet run --project tools/MetroRail.Tools -- <command> [options]
var cmd = args.FirstOrDefault() ?? "help";
var rest = args.Skip(1).ToArray();
return cmd switch
{
    "dev" => await DevRunner.RunAsync(rest),
    "sim" or "simulate" => await Simulator.RunAsync(rest),
    "smoke" => await Smoke.RunAsync(rest),
    "qr-interop" => await QrInterop.RunAsync(rest),
    "openapi" => await OpenApiExport.RunAsync(rest),
    _ => Help(),
};

static int Help()
{
    Console.WriteLine("""
        MetroRail tools
          dev                      run all services without Docker (gateway on :8080; needs nats-server or EVENT_BUS=memory)
          sim [--mode moving|held|stopped] [--train 3412] [--speed 1] [--api http://localhost:8080/api]
          smoke [--api ...]        end-to-end commuter journey against a running stack
          qr-interop               QR built by the customer app (TypeScript) must validate in ticketing (C#)
          openapi                  regenerate contracts/openapi/*.json from the services
        """);
    return 0;
}

namespace MetroRail.Tools
{
    internal static class Args
    {
        public static string Get(string[] args, string name, string fallback)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        public static string RepoRoot()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d is not null && !File.Exists(Path.Combine(d.FullName, "MetroRail.sln"))) d = d.Parent;
            return d?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
