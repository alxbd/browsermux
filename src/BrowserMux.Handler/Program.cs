using System.IO.Pipes;

// The Handler is the exe registered with Windows as the default browser.
// It must start as fast as possible and do ONE thing:
// forward the URL to the main app via named pipe,
// or launch it if not already running.

const string PipeName = BrowserMux.Core.AppInfo.PipeName;
const string AppExeName = BrowserMux.Core.AppInfo.AppExeName;

var url = args.Length > 0 ? args[0] : string.Empty;
if (string.IsNullOrWhiteSpace(url)) return;

// Try to send the URL to an already running instance
if (await TrySendViaPipe(url, PipeName)) return;

// Otherwise launch the main app with the URL as argument
LaunchApp(AppExeName, url);

static async Task<bool> TrySendViaPipe(string url, string pipeName)
{
    try
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
        using var cts = new CancellationTokenSource(millisecondsDelay: 300);
        await client.ConnectAsync(cts.Token);

        using var writer = new StreamWriter(client) { AutoFlush = true };
        await writer.WriteLineAsync(url);
        return true;
    }
    catch
    {
        return false;
    }
}

static void LaunchApp(string exeName, string url)
{
    var appPath = FindApp(exeName);
    if (appPath is null)
    {
        Console.Error.WriteLine($"Could not find {exeName}");
        return;
    }

    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = appPath,
        Arguments = $"\"{url}\"",
        UseShellExecute = false,
    });
}

static string? FindApp(string exeName)
{
    // 1. Same directory as the handler (production: both in {app}/)
    var handlerDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
    var candidate = Path.Combine(handlerDir, exeName);
    if (File.Exists(candidate)) return candidate;

    // 2. out/ directory (dev: build.ps1 deploys both there)
    var dir = new DirectoryInfo(handlerDir);
    while (dir is not null)
    {
        var outPath = Path.Combine(dir.FullName, "out", exeName);
        if (File.Exists(outPath)) return outPath;
        dir = dir.Parent;
    }

    return null;
}
