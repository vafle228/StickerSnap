using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StickerSnap.Api.Shell;

namespace StickerSnap.Tests.Api.Shell;

/// <summary>The server stops once the shell that started it (StickerSnap__ParentPid) is gone.</summary>
public sealed class ParentWatchTests
{
    /// <summary>No process has this id: Process.GetProcessById throws at once.</summary>
    private const int GoneShell = int.MaxValue;

    private readonly FakeTimeProvider _time = new();
    private readonly IHostApplicationLifetime _lifetime = Substitute.For<IHostApplicationLifetime>();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private ParentWatch Watch(int? parentPid) => new(
        Options.Create(new ShellOptions { ParentPid = parentPid }),
        _lifetime,
        _time,
        NullLogger<ParentWatch>.Instance);

    [Fact]
    public async Task Stops_the_server_when_the_shell_is_gone()
    {
        using var watch = Watch(GoneShell);

        await watch.StartAsync(Cancel);
        await watch.ExecuteTask!.WaitAsync(Cancel);

        _lifetime.Received(1).StopApplication();
    }

    [Fact]
    public async Task Keeps_the_server_running_while_the_shell_is_alive()
    {
        using var watch = Watch(Environment.ProcessId);

        await watch.StartAsync(Cancel);
        _time.Advance(ParentWatch.Interval * 3);

        watch.ExecuteTask!.IsCompleted.Should().BeFalse();
        _lifetime.DidNotReceive().StopApplication();
        await watch.StopAsync(Cancel);
    }

    [Fact]
    public async Task Watches_nothing_when_no_shell_started_the_server()
    {
        using var watch = Watch(parentPid: null);

        await watch.StartAsync(Cancel);
        await watch.ExecuteTask!.WaitAsync(Cancel);

        _lifetime.DidNotReceive().StopApplication();
    }

    [Fact]
    public async Task Runs_in_the_server_the_shell_starts()
    {
        using var factory = new ApiFactory(("ParentPid", GoneShell.ToString()));
        factory.CreateClient();
        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();

        var stopping = Task.Delay(Timeout.Infinite, lifetime.ApplicationStopping);
        var finished = await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(10), Cancel));

        finished.Should().BeSameAs(stopping);
    }
}
