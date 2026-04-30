using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Application = FlaUI.Core.Application;

namespace EDF6ModLoaderWpf.Automation;

internal sealed record AutomationRequest(string? ExecutablePath, string WindowTitleContains, int TimeoutMs);

internal sealed record NativeWindowInfo(IntPtr Handle, string Title);

internal sealed class WpfAutomationClient
{
    private const string DefaultWindowTitle = "EDF Mod Manager";
    private const string MainProjectFile = "EDF6ModLoaderWpf.csproj";
    private static readonly string DefaultExecutableRelativePath =
        Path.Combine("bin", "Debug", "net10.0-windows", "EDF6ModManager.exe");

    public string DumpTree(AutomationRequest request, int maxDepth)
    {
        using var session = OpenSession(request);

        var builder = new StringBuilder();
        AppendElement(builder, session.MainWindow, 0, Math.Max(0, maxDepth));
        return builder.ToString();
    }

    public string ListWindows(AutomationRequest request)
    {
        using var session = OpenSession(request);

        var windows = GetProcessWindows(session.Process.Id)
            .OrderBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (windows.Count == 0)
            return "No visible top-level windows were found for the running EDF app.";

        var builder = new StringBuilder();
        foreach (var window in windows)
            builder.AppendLine($"- 0x{window.Handle.ToInt64():X} \"{window.Title}\"");

        return builder.ToString();
    }

    public string InvokeElement(AutomationRequest request, string automationId, string? waitWindowTitleContains = null)
    {
        using var session = OpenSession(request);
        var element = FindRequiredElement(session.MainWindow, automationId);

        if (TryInvoke(element))
        {
            if (!string.IsNullOrWhiteSpace(waitWindowTitleContains))
            {
                var window = WaitForNativeWindow(session.Process.Id, waitWindowTitleContains, request.TimeoutMs);
                return $"Invoked {DescribeElement(element)} and found window 0x{window.Handle.ToInt64():X} \"{window.Title}\".";
            }

            return $"Invoked {DescribeElement(element)}.";
        }

        throw new InvalidOperationException(
            $"Element '{automationId}' was found, but it does not support invoke, select, or toggle patterns.");
    }

    public string SetText(AutomationRequest request, string automationId, string text)
    {
        using var session = OpenSession(request);
        var element = FindRequiredElement(session.MainWindow, automationId);
        var valuePattern = element.Patterns.Value.PatternOrDefault;

        if (valuePattern is null)
            throw new InvalidOperationException($"Element '{automationId}' does not support text input.");

        valuePattern.SetValue(text);
        return $"Set text on {DescribeElement(element)} to '{text}'.";
    }

    public string CaptureScreenshot(
        AutomationRequest request,
        string? outputPath,
        string? automationId,
        string? openAutomationId = null,
        string? targetWindowTitleContains = null)
    {
        using var session = OpenSession(request);

        if (!string.IsNullOrWhiteSpace(openAutomationId))
        {
            var element = FindRequiredElement(session.MainWindow, openAutomationId);
            if (!TryInvoke(element))
            {
                throw new InvalidOperationException(
                    $"Element '{openAutomationId}' was found, but it does not support invoke, select, or toggle patterns.");
            }
        }

        var captureWindowTitle = string.IsNullOrWhiteSpace(targetWindowTitleContains)
            ? request.WindowTitleContains
            : targetWindowTitleContains;
        var captureBounds = ResolveCaptureBounds(session, captureWindowTitle, automationId, request.TimeoutMs);

        if (captureBounds.Width <= 0 || captureBounds.Height <= 0)
            throw new InvalidOperationException("The target element is not visible on screen.");

        var resolvedOutputPath = ResolveScreenshotPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);

        CaptureWindowWithPrintWindow(captureBounds, resolvedOutputPath);

        return resolvedOutputPath;
    }

    public string CaptureDialogScreenshot(
        AutomationRequest request,
        string openAutomationId,
        string targetWindowTitleContains,
        string? outputPath)
    {
        return CaptureScreenshot(
            request,
            outputPath,
            automationId: null,
            openAutomationId,
            targetWindowTitleContains);
    }

    private static AutomationSession OpenSession(AutomationRequest request)
    {
        var executablePath = ResolveExecutablePath(request.ExecutablePath);
        var processName = Path.GetFileNameWithoutExtension(executablePath);
        var process = Process.GetProcessesByName(processName)
            .FirstOrDefault(candidate => !candidate.HasExited);
        var launched = false;

        if (process is null)
        {
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException(
                    $"Could not find the WPF executable at '{executablePath}'. Build the app first or pass --exe.",
                    executablePath);
            }

            var startInfo = new ProcessStartInfo(executablePath)
            {
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
                UseShellExecute = true
            };

            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to launch '{executablePath}'.");
            launched = true;

            try
            {
                process.WaitForInputIdle(Math.Min(request.TimeoutMs, 5000));
            }
            catch (InvalidOperationException)
            {
                // Some windows appear before the process reaches an idle state; the polling loop below covers that case.
            }
        }

        var automation = new UIA3Automation();
        var application = Application.Attach(process.Id);
        var mainWindow = WaitForWindow(process, application, automation, request.WindowTitleContains, request.TimeoutMs);
        return new AutomationSession(process, application, automation, mainWindow, launched);
    }

    private static Window WaitForWindow(Process process, Application application, UIA3Automation automation, string windowTitleContains, int timeoutMs)
    {
        var timeout = TimeSpan.FromMilliseconds(Math.Max(1000, timeoutMs));
        var start = Stopwatch.StartNew();
        var effectiveTitle = string.IsNullOrWhiteSpace(windowTitleContains) ? DefaultWindowTitle : windowTitleContains;

        while (start.Elapsed < timeout)
        {
            var windows = application.GetAllTopLevelWindows(automation);
            var match = windows.FirstOrDefault(window =>
                window.Title.Contains(effectiveTitle, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                var nativeMatch = GetProcessWindows(process.Id)
                    .FirstOrDefault(window => window.Title.Contains(effectiveTitle, StringComparison.OrdinalIgnoreCase));

                if (nativeMatch is not null)
                {
                    var nativeElement = automation.FromHandle(nativeMatch.Handle);
                    match = nativeElement.AsWindow();
                }
            }

            if (match is null && string.Equals(effectiveTitle, DefaultWindowTitle, StringComparison.OrdinalIgnoreCase))
            {
                match = windows.FirstOrDefault();

                if (match is null)
                {
                    var nativeFallback = GetProcessWindows(process.Id).FirstOrDefault();
                    if (nativeFallback is not null)
                        match = automation.FromHandle(nativeFallback.Handle).AsWindow();
                }
            }

            if (match is not null)
                return match;

            Thread.Sleep(200);
        }

        throw new TimeoutException($"Timed out waiting for a window containing '{effectiveTitle}'.");
    }

    private static AutomationElement FindRequiredElement(Window mainWindow, string automationId)
    {
        if (string.Equals(mainWindow.AutomationId, automationId, StringComparison.OrdinalIgnoreCase))
            return mainWindow;

        var element = mainWindow.FindFirstDescendant(condition => condition.ByAutomationId(automationId));
        if (element is not null)
            return element;

        throw new InvalidOperationException($"Could not find an element with AutomationId '{automationId}'.");
    }

    private static bool TryInvoke(AutomationElement element)
    {
        var invokePattern = element.Patterns.Invoke.PatternOrDefault;
        if (invokePattern is not null)
        {
            invokePattern.Invoke();
            return true;
        }

        var selectionItemPattern = element.Patterns.SelectionItem.PatternOrDefault;
        if (selectionItemPattern is not null)
        {
            selectionItemPattern.Select();
            return true;
        }

        var togglePattern = element.Patterns.Toggle.PatternOrDefault;
        if (togglePattern is not null)
        {
            togglePattern.Toggle();
            return true;
        }

        return false;
    }

    private static void AppendElement(StringBuilder builder, AutomationElement element, int depth, int maxDepth)
    {
        builder.Append(' ', depth * 2);
        builder.Append("- ");
        builder.Append(element.ControlType);

        if (!string.IsNullOrWhiteSpace(element.AutomationId))
        {
            builder.Append(" #");
            builder.Append(element.AutomationId);
        }

        if (!string.IsNullOrWhiteSpace(element.Name))
        {
            builder.Append(" \"");
            builder.Append(element.Name);
            builder.Append('"');
        }

        if (!string.IsNullOrWhiteSpace(element.ClassName))
        {
            builder.Append(" <");
            builder.Append(element.ClassName);
            builder.Append('>');
        }

        builder.AppendLine();

        if (depth >= maxDepth)
            return;

        foreach (var child in element.FindAllChildren())
            AppendElement(builder, child, depth + 1, maxDepth);
    }

    private static string DescribeElement(AutomationElement element)
    {
        var controlType = element.ControlType.ToString();
        var name = string.IsNullOrWhiteSpace(element.Name) ? "<unnamed>" : element.Name;
        var automationId = string.IsNullOrWhiteSpace(element.AutomationId) ? "<no id>" : element.AutomationId;
        return $"{controlType} '{name}' ({automationId})";
    }

    private static CaptureBounds ResolveCaptureBounds(
        AutomationSession session,
        string windowTitleContains,
        string? automationId,
        int timeoutMs)
    {
        if (!string.IsNullOrWhiteSpace(automationId))
        {
            var element = FindRequiredElement(session.MainWindow, automationId);
            var bounds = element.BoundingRectangle;
            return new CaptureBounds(
                session.Process.MainWindowHandle,
                (int)Math.Floor((double)bounds.Left),
                (int)Math.Floor((double)bounds.Top),
                Math.Max(1, (int)Math.Ceiling((double)bounds.Width)),
                Math.Max(1, (int)Math.Ceiling((double)bounds.Height)));
        }

        var nativeWindow = WaitForNativeWindow(session.Process.Id, windowTitleContains, timeoutMs);
        var rect = GetWindowRect(nativeWindow.Handle);
        return new CaptureBounds(
            nativeWindow.Handle,
            rect.Left,
            rect.Top,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top));
    }

    private static NativeWindowInfo WaitForNativeWindow(int processId, string windowTitleContains, int timeoutMs)
    {
        var timeout = TimeSpan.FromMilliseconds(Math.Max(1000, timeoutMs));
        var start = Stopwatch.StartNew();
        var effectiveTitle = string.IsNullOrWhiteSpace(windowTitleContains) ? DefaultWindowTitle : windowTitleContains;

        while (start.Elapsed < timeout)
        {
            var match = TryResolveNativeWindow(processId, effectiveTitle);
            if (match is not null)
                return match;

            Thread.Sleep(200);
        }

        throw new TimeoutException($"Timed out waiting for a visible top-level window containing '{effectiveTitle}'.");
    }

    private static NativeWindowInfo? TryResolveNativeWindow(int processId, string effectiveTitle)
    {
        var windows = GetProcessWindows(processId);

        var match = windows.FirstOrDefault(window =>
            window.Title.Contains(effectiveTitle, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
            return match;

        if (string.Equals(effectiveTitle, DefaultWindowTitle, StringComparison.OrdinalIgnoreCase))
        {
            var fallback = windows.FirstOrDefault();
            if (fallback is not null)
                return fallback;
        }

        return null;
    }

    private static IReadOnlyList<NativeWindowInfo> GetProcessWindows(int processId)
    {
        var windows = new List<NativeWindowInfo>();

        NativeMethods.EnumWindows((handle, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(handle, out var ownerProcessId);
            if (ownerProcessId != processId || !NativeMethods.IsWindowVisible(handle))
                return true;

            var title = GetWindowTitle(handle);
            if (string.IsNullOrWhiteSpace(title))
                return true;

            windows.Add(new NativeWindowInfo(handle, title));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = NativeMethods.GetWindowTextLength(handle);
        if (length <= 0)
            return string.Empty;

        var builder = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static NativeMethods.RECT GetWindowRect(IntPtr handle)
    {
        if (!NativeMethods.GetWindowRect(handle, out var rect))
            throw new InvalidOperationException("Failed to get the target window bounds.");

        return rect;
    }

    private static void CaptureWindowWithPrintWindow(CaptureBounds captureBounds, string outputPath)
    {
        // Always capture the full window via PrintWindow (works off-screen, behind other windows,
        // and gets the real DWM-composited frame — unlike CopyFromScreen which grabs a stale GDI buffer).
        var windowRect = GetWindowRect(captureBounds.Handle);
        var windowWidth  = Math.Max(1, windowRect.Right  - windowRect.Left);
        var windowHeight = Math.Max(1, windowRect.Bottom - windowRect.Top);

        using var windowBitmap   = new Bitmap(windowWidth, windowHeight);
        using var windowGraphics = Graphics.FromImage(windowBitmap);

        var hdc = windowGraphics.GetHdc();
        try
        {
            NativeMethods.PrintWindow(captureBounds.Handle, hdc, NativeMethods.PwRenderFullContent);
        }
        finally
        {
            windowGraphics.ReleaseHdc(hdc);
        }

        // If capturing a sub-element, crop to its bounds relative to the window origin.
        bool isSubElement =
            captureBounds.Left   != windowRect.Left   ||
            captureBounds.Top    != windowRect.Top    ||
            captureBounds.Width  != windowWidth       ||
            captureBounds.Height != windowHeight;

        if (isSubElement)
        {
            var cropRect = new Rectangle(
                captureBounds.Left - windowRect.Left,
                captureBounds.Top  - windowRect.Top,
                captureBounds.Width,
                captureBounds.Height);

            using var cropped = windowBitmap.Clone(cropRect, windowBitmap.PixelFormat);
            cropped.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
        }
        else
        {
            windowBitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    private static void BringWindowToForeground(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return;

        NativeMethods.ShowWindow(handle, NativeMethods.SwRestore);
        NativeMethods.SetForegroundWindow(handle);
    }

    private static string ResolveExecutablePath(string? executablePath)
    {
        if (!string.IsNullOrWhiteSpace(executablePath))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(executablePath));

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory) ?? FindRepoRoot(Environment.CurrentDirectory);
        if (repoRoot is null)
        {
            throw new DirectoryNotFoundException(
                $"Could not locate '{MainProjectFile}' from the current directory. Pass --exe explicitly.");
        }

        return Path.Combine(repoRoot, DefaultExecutableRelativePath);
    }

    private static string ResolveScreenshotPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory) ?? FindRepoRoot(Environment.CurrentDirectory) ?? Environment.CurrentDirectory;
        return Path.Combine(repoRoot, "artifacts", "ui-captures", $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.png");
    }

    private static string? FindRepoRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        if (!directory.Exists && directory.Parent is not null)
            directory = directory.Parent;

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, MainProjectFile)))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }

    private sealed class AutomationSession : IDisposable
    {
        private readonly bool _launched;

        public AutomationSession(Process process, Application application, UIA3Automation automation, Window mainWindow, bool launched)
        {
            Process = process;
            Application = application;
            Automation = automation;
            MainWindow = mainWindow;
            _launched = launched;
        }

        public Process Process { get; }

        public Application Application { get; }

        public UIA3Automation Automation { get; }

        public Window MainWindow { get; }

        public void Dispose()
        {
            if (_launched)
            {
                try
                {
                    foreach (var window in GetProcessWindows(Process.Id))
                        NativeMethods.PostMessage(window.Handle, NativeMethods.WmClose, IntPtr.Zero, IntPtr.Zero);

                    if (!Process.WaitForExit(1500) && MainWindow.Patterns.Window.PatternOrDefault is { } windowPattern)
                        windowPattern.Close();

                    if (!Process.WaitForExit(1500) && !Process.HasExited)
                        Process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Keep the tool best-effort; the launched app can remain open if Windows refuses the close request.
                }
            }

            Automation.Dispose();
            Application.Dispose();
        }
    }

    private static class NativeMethods
    {
        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        internal const int SwRestore = 9;
        internal const int WmClose = 0x0010;
        internal const uint PwRenderFullContent = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        internal static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }

    private sealed record CaptureBounds(IntPtr Handle, int Left, int Top, int Width, int Height);
}
