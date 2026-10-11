using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StickerSnap.Api.Shell;

namespace StickerSnap.Tests;

/// <summary>The server as the desktop shell starts it: StickerSnap__Token, __Mode and __ParentPid.</summary>
public sealed class ShellTests
{
    private const string Token = "launch-token";

    /// <summary>The app with its own wwwroot and the settings the shell would pass.</summary>
    private sealed class Factory(params (string Key, string Value)[] settings) : WebApplicationFactory<Program>
    {
        private readonly string _webRoot = Directory.CreateTempSubdirectory("stickersnap-wwwroot-").FullName;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html>");
            builder.UseWebRoot(_webRoot);
            foreach (var (key, value) in settings) builder.UseSetting($"{ShellOptions.Section}:{key}", value);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            // The factory disposes twice when it is disposed by `using`
            if (Directory.Exists(_webRoot)) Directory.Delete(_webRoot, recursive: true);
        }
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/v1/app")]
    [InlineData("/api/v1/projects/6f1d2c3a")]
    [InlineData("/hubs/events/negotiate")]
    public async Task Answers_401_to_a_request_without_the_launch_token(string path)
    {
        using var factory = new Factory(("Token", Token));
        var client = factory.CreateClient();

        var response = await client.GetAsync(path, Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Answers_401_to_a_wrong_token()
    {
        using var factory = new Factory(("Token", Token));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "guess");

        var byHeader = await client.GetAsync("/api/v1/app", Cancel);
        var byQuery = await client.GetAsync("/api/v1/app?access_token=guess", Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, byHeader.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, byQuery.StatusCode);
    }

    [Fact]
    public async Task Lets_the_token_in_as_a_bearer_header()
    {
        using var factory = new Factory(("Token", Token));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        var response = await client.GetAsync("/api/v1/nothing-here", Cancel);

        // Past the gate: no endpoint yet, so the API's own 404
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/projects/6f1d2c3a/media")]
    [InlineData("/hubs/events/negotiate")]
    public async Task Lets_the_token_in_as_access_token_where_the_browser_loads_by_itself(string path)
    {
        using var factory = new Factory(("Token", Token));
        var client = factory.CreateClient();

        var response = await client.GetAsync($"{path}?access_token={Token}", Cancel);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Serves_the_spa_page_without_the_token()
    {
        using var factory = new Factory(("Token", Token));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/", Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Keeps_the_api_open_when_no_shell_passed_a_token()
    {
        using var factory = new Factory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/nothing-here", Cancel);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Stops_when_the_shell_is_gone()
    {
        using var shell = Process.Start(new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true })
            ?? throw new InvalidOperationException("dotnet did not start");
        var pid = shell.Id;
        await shell.WaitForExitAsync(Cancel);

        using var factory = new Factory(("ParentPid", pid.ToString()));
        factory.CreateClient();
        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();

        var stopping = Task.Delay(Timeout.Infinite, lifetime.ApplicationStopping);
        var finished = await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(10), Cancel));

        Assert.Same(stopping, finished);
    }

    [Fact]
    public async Task Keeps_running_while_the_shell_is_alive()
    {
        using var factory = new Factory(("ParentPid", Environment.ProcessId.ToString()));
        factory.CreateClient();
        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();

        await Task.Delay(ParentWatch.Interval * 2.5, Cancel);

        Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);
    }
}
