using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EDF6ModLoaderWpf.Automation;

internal sealed class McpServer
{
    private const string ProtocolVersion = "2024-11-05";

    private readonly WpfAutomationClient _automationClient;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public McpServer(WpfAutomationClient automationClient)
    {
        _automationClient = automationClient;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();

        while (!cancellationToken.IsCancellationRequested)
        {
            using var message = await ReadMessageAsync(input, cancellationToken);
            if (message is null)
                return;

            var root = message.RootElement;
            if (!root.TryGetProperty("method", out var methodElement))
                continue;

            var method = methodElement.GetString();
            if (string.IsNullOrWhiteSpace(method))
                continue;

            var hasId = root.TryGetProperty("id", out var idElement);
            JsonNode? response = null;

            switch (method)
            {
                case "initialize" when hasId:
                    response = CreateResultResponse(idElement, CreateInitializeResult(root));
                    break;

                case "notifications/initialized":
                    break;

                case "tools/list" when hasId:
                    response = CreateResultResponse(idElement, CreateToolsListResult());
                    break;

                case "tools/call" when hasId:
                    response = CreateToolCallResponse(idElement, root);
                    break;

                case "shutdown" when hasId:
                    response = CreateResultResponse(idElement, new JsonObject());
                    break;

                case "exit":
                    return;

                default:
                    if (hasId)
                        response = CreateErrorResponse(idElement, -32601, $"Unsupported method '{method}'.");
                    break;
            }

            if (response is not null)
                await WriteMessageAsync(output, response, cancellationToken);
        }
    }

    private JsonNode CreateToolCallResponse(JsonElement idElement, JsonElement root)
    {
        try
        {
            var paramsElement = root.GetProperty("params");
            var toolName = paramsElement.GetProperty("name").GetString()
                ?? throw new InvalidOperationException("Tool name is required.");
            var arguments = paramsElement.TryGetProperty("arguments", out var argumentsElement)
                ? argumentsElement
                : default;

            var request = CreateAutomationRequest(arguments);
            var resultText = toolName switch
            {
                "inspect_ui" => _automationClient.DumpTree(request, GetInt(arguments, "maxDepth", 4)),
                "list_windows" => _automationClient.ListWindows(request),
                "invoke_element" => _automationClient.InvokeElement(
                    request,
                    GetRequiredString(arguments, "automationId"),
                    GetString(arguments, "waitForWindowTitleContains")),
                "set_text" => _automationClient.SetText(
                    request,
                    GetRequiredString(arguments, "automationId"),
                    GetRequiredString(arguments, "text")),
                "capture_screenshot" => _automationClient.CaptureScreenshot(
                    request,
                    GetString(arguments, "outputPath"),
                    GetString(arguments, "automationId"),
                    GetString(arguments, "openAutomationId"),
                    GetString(arguments, "targetWindowTitleContains")),
                "capture_dialog" => _automationClient.CaptureDialogScreenshot(
                    request,
                    GetRequiredString(arguments, "openAutomationId"),
                    GetRequiredString(arguments, "targetWindowTitleContains"),
                    GetString(arguments, "outputPath")),
                _ => throw new InvalidOperationException($"Unknown tool '{toolName}'.")
            };

            return CreateResultResponse(idElement, CreateToolResult(resultText));
        }
        catch (Exception ex)
        {
            return CreateResultResponse(idElement, CreateToolResult(ex.Message, isError: true));
        }
    }

    private static object CreateInitializeResult(JsonElement root)
    {
        var requestedProtocolVersion = root.TryGetProperty("params", out var paramsElement) &&
                                       paramsElement.TryGetProperty("protocolVersion", out var protocolVersionElement)
            ? protocolVersionElement.GetString()
            : null;

        return new
        {
            protocolVersion = string.IsNullOrWhiteSpace(requestedProtocolVersion)
                ? ProtocolVersion
                : requestedProtocolVersion,
            capabilities = new
            {
                tools = new { }
            },
            serverInfo = new
            {
                name = "edf6-wpf-automation",
                version = "0.1.0"
            }
        };
    }

    private static object CreateToolsListResult()
    {
        return new
        {
            tools = new object[]
            {
                new
                {
                    name = "inspect_ui",
                    description = "Launches or attaches to the EDF WPF app and returns a readable automation tree.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            exePath = new { type = "string", description = "Optional path to EDF6ModManager.exe." },
                            windowTitleContains = new { type = "string", description = "Optional window title filter." },
                            maxDepth = new { type = "integer", description = "Tree depth to print. Defaults to 4." },
                            timeoutMs = new { type = "integer", description = "Time to wait for the window. Defaults to 15000." }
                        },
                        required = Array.Empty<string>()
                    }
                },
                new
                {
                    name = "list_windows",
                    description = "Lists the visible top-level windows that belong to the running EDF WPF app.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            exePath = new { type = "string", description = "Optional path to EDF6ModManager.exe." },
                            timeoutMs = new { type = "integer", description = "Time to wait for the app. Defaults to 15000." }
                        },
                        required = Array.Empty<string>()
                    }
                },
                new
                {
                    name = "invoke_element",
                    description = "Invokes a WPF element by AutomationId using invoke, select, or toggle patterns.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            automationId = new { type = "string", description = "Target AutomationId." },
                            waitForWindowTitleContains = new { type = "string", description = "Optional title fragment to wait for after invoking the element." },
                            exePath = new { type = "string", description = "Optional path to EDF6ModManager.exe." },
                            windowTitleContains = new { type = "string", description = "Optional window title filter." },
                            timeoutMs = new { type = "integer", description = "Time to wait for the window. Defaults to 15000." }
                        },
                        required = new[] { "automationId" }
                    }
                },
                new
                {
                    name = "set_text",
                    description = "Sets text on a WPF element that exposes a value pattern.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            automationId = new { type = "string", description = "Target AutomationId." },
                            text = new { type = "string", description = "New text value." },
                            exePath = new { type = "string", description = "Optional path to EDF6ModManager.exe." },
                            windowTitleContains = new { type = "string", description = "Optional window title filter." },
                            timeoutMs = new { type = "integer", description = "Time to wait for the window. Defaults to 15000." }
                        },
                        required = new[] { "automationId", "text" }
                    }
                },
                new
                {
                    name = "capture_screenshot",
                    description = "Captures a PNG screenshot of the current window or a specific element by AutomationId.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            automationId = new { type = "string", description = "Optional AutomationId for a specific element." },
                            openAutomationId = new { type = "string", description = "Optional AutomationId to invoke before capture, useful for opening dialogs." },
                            targetWindowTitleContains = new { type = "string", description = "Optional window title fragment to capture after opening a dialog." },
                            outputPath = new { type = "string", description = "Optional absolute or relative PNG path." },
                            exePath = new { type = "string", description = "Optional path to EDF6ModManager.exe." },
                            windowTitleContains = new { type = "string", description = "Optional window title filter." },
                            timeoutMs = new { type = "integer", description = "Time to wait for the window. Defaults to 15000." }
                        },
                        required = Array.Empty<string>()
                    }
                },
                new
                {
                    name = "capture_dialog",
                    description = "Invokes a WPF element, waits for a dialog window title, and captures that dialog as a PNG.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            openAutomationId = new { type = "string", description = "AutomationId to invoke before capture." },
                            targetWindowTitleContains = new { type = "string", description = "Window title fragment to wait for and capture." },
                            outputPath = new { type = "string", description = "Optional absolute or relative PNG path." },
                            exePath = new { type = "string", description = "Optional path to EDF6ModManager.exe." },
                            windowTitleContains = new { type = "string", description = "Optional main window title filter. Defaults to EDF Mod Manager." },
                            timeoutMs = new { type = "integer", description = "Time to wait for the app and dialog. Defaults to 15000." }
                        },
                        required = new[] { "openAutomationId", "targetWindowTitleContains" }
                    }
                }
            }
        };
    }

    private static AutomationRequest CreateAutomationRequest(JsonElement arguments)
    {
        return new AutomationRequest(
            GetString(arguments, "exePath") ?? GetString(arguments, "exe"),
            GetString(arguments, "windowTitleContains") ?? "EDF Mod Manager",
            GetInt(arguments, "timeoutMs", 15000));
    }

    private static JsonObject CreateToolResult(string text, bool isError = false)
    {
        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            },
            ["isError"] = isError
        };
    }

    private JsonObject CreateResultResponse(JsonElement idElement, object result)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(idElement.GetRawText()),
            ["result"] = result as JsonNode ?? JsonSerializer.SerializeToNode(result, _serializerOptions)
        };
    }

    private static JsonObject CreateErrorResponse(JsonElement idElement, int code, string message)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(idElement.GetRawText()),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
    }

    private async Task<JsonDocument?> ReadMessageAsync(Stream input, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var line = await ReadAsciiLineAsync(input, cancellationToken);
            if (line is null)
                return null;

            if (line.Length == 0)
            {
                if (headers.Count == 0)
                    continue;

                break;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
                continue;

            headers[line[..separatorIndex].Trim()] = line[(separatorIndex + 1)..].Trim();
        }

        if (!headers.TryGetValue("Content-Length", out var contentLengthValue) ||
            !int.TryParse(contentLengthValue, out var contentLength) ||
            contentLength < 0)
        {
            throw new InvalidOperationException("Received an MCP message without a valid Content-Length header.");
        }

        var payload = new byte[contentLength];
        await FillBufferAsync(input, payload, cancellationToken);
        return JsonDocument.Parse(payload);
    }

    private static async Task<string?> ReadAsciiLineAsync(Stream input, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var buffer = new byte[1];

        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            if (read == 0)
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());

            if (buffer[0] == (byte)'\n')
            {
                if (bytes.Count > 0 && bytes[^1] == (byte)'\r')
                    bytes.RemoveAt(bytes.Count - 1);

                return Encoding.ASCII.GetString(bytes.ToArray());
            }

            bytes.Add(buffer[0]);
        }
    }

    private static async Task FillBufferAsync(Stream input, byte[] payload, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < payload.Length)
        {
            var read = await input.ReadAsync(payload.AsMemory(offset), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of stream while reading MCP payload.");

            offset += read;
        }
    }

    private async Task WriteMessageAsync(Stream output, JsonNode message, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(message.ToJsonString(_serializerOptions));
        var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");

        await output.WriteAsync(header.AsMemory(), cancellationToken);
        await output.WriteAsync(payload.AsMemory(), cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.GetRawText();
    }

    private static string GetRequiredString(JsonElement element, string propertyName) =>
        GetString(element, propertyName)
        ?? throw new InvalidOperationException($"Tool argument '{propertyName}' is required.");

    private static int GetInt(JsonElement element, string propertyName, int defaultValue)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
            return defaultValue;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            return number;

        return property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out number)
            ? number
            : defaultValue;
    }
}
