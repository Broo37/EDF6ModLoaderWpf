namespace EDF6ModLoaderWpf.Automation;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Any(IsHelpFlag))
            {
                PrintUsage();
                return 0;
            }

            if (string.Equals(args[0], "--mcp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[0], "mcp", StringComparison.OrdinalIgnoreCase))
            {
                var server = new McpServer(new WpfAutomationClient());
                await server.RunAsync(CancellationToken.None);
                return 0;
            }

            var command = args[0];
            var options = CommandOptions.Parse(args[1..]);
            var client = new WpfAutomationClient();
            var request = new AutomationRequest(
                options.GetValue("exe"),
                options.GetValue("window-title") ?? "EDF Mod Manager",
                options.GetInt("timeout-ms", 15000));

            var output = command.ToLowerInvariant() switch
            {
                "tree" or "list" => client.DumpTree(request, options.GetInt("max-depth", 4)),
                "windows" => client.ListWindows(request),
                "invoke" => client.InvokeElement(
                    request,
                    options.RequireValue("automation-id"),
                    options.GetValue("wait-window-title")),
                "set-text" => client.SetText(
                    request,
                    options.RequireValue("automation-id"),
                    options.RequireValue("text")),
                "screenshot" or "capture" => client.CaptureScreenshot(
                    request,
                    options.GetValue("output"),
                    options.GetValue("automation-id"),
                    options.GetValue("open-automation-id"),
                    options.GetValue("target-window-title")),
                "capture-dialog" => client.CaptureDialogScreenshot(
                    request,
                    options.RequireValue("open-automation-id"),
                    options.RequireValue("target-window-title"),
                    options.GetValue("output")),
                _ => throw new InvalidOperationException($"Unknown command '{command}'.")
            };

            Console.WriteLine(output);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static bool IsHelpFlag(string value) =>
        string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "help", StringComparison.OrdinalIgnoreCase);

    private static void PrintUsage()
    {
        Console.WriteLine("EDF6ModLoaderWpf.Automation");
        Console.WriteLine();
        Console.WriteLine("CLI usage:");
        Console.WriteLine("  tree [--exe <path>] [--window-title <title>] [--max-depth <n>] [--timeout-ms <n>]");
        Console.WriteLine("  windows [--exe <path>] [--timeout-ms <n>]");
        Console.WriteLine("  invoke --automation-id <id> [--wait-window-title <title>] [--exe <path>] [--window-title <title>] [--timeout-ms <n>]");
        Console.WriteLine("  set-text --automation-id <id> --text <value> [--exe <path>] [--window-title <title>] [--timeout-ms <n>]");
        Console.WriteLine("  screenshot [--automation-id <id>] [--open-automation-id <id>] [--target-window-title <title>] [--output <path>] [--exe <path>] [--window-title <title>] [--timeout-ms <n>]");
        Console.WriteLine("  capture-dialog --open-automation-id <id> --target-window-title <title> [--output <path>] [--exe <path>] [--window-title <main-title>] [--timeout-ms <n>]");
        Console.WriteLine();
        Console.WriteLine("MCP mode:");
        Console.WriteLine("  --mcp");
    }

    private sealed class CommandOptions
    {
        private readonly Dictionary<string, string> _values;

        private CommandOptions(Dictionary<string, string> values)
        {
            _values = values;
        }

        public static CommandOptions Parse(IReadOnlyList<string> args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < args.Count; index++)
            {
                var token = args[index];
                if (!token.StartsWith("--", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Unexpected argument '{token}'. Options must start with --.");

                if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Option '{token}' requires a value.");

                values[token[2..]] = args[index + 1];
                index++;
            }

            return new CommandOptions(values);
        }

        public string? GetValue(string key) => _values.GetValueOrDefault(key);

        public int GetInt(string key, int defaultValue)
        {
            var value = GetValue(key);
            return value is not null && int.TryParse(value, out var parsed) ? parsed : defaultValue;
        }

        public string RequireValue(string key) =>
            GetValue(key) ?? throw new InvalidOperationException($"Missing required option '--{key}'.");
    }
}
