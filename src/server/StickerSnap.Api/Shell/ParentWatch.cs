using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace StickerSnap.Api.Shell;

/// <summary>
/// Stops the server when the shell that started it is gone. A normal quit stops the server
/// itself; this covers a crashed or killed shell, and `dotnet run`, whose child the shell cannot
/// reach, so no server is left behind holding the port.
/// </summary>
public sealed class ParentWatch(
    IOptions<ShellOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<ParentWatch> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.ParentPid is not int pid) return;

        using var timer = new PeriodicTimer(Interval);
        do
        {
            if (IsAlive(pid)) continue;
            logger.LogInformation("The shell (pid {Pid}) is gone, stopping", pid);
            lifetime.StopApplication();
            return;
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}
