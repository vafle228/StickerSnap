using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using StickerSnap.Application.Diagnostics;
using StickerSnap.Application.Projects;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Application.Diagnostics;

/// <summary>The report a bug goes with: its head, the error with its project, the log around it, 200 KB at most.</summary>
public sealed class DiagnosticsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 13, 30, 0, TimeSpan.Zero);
    private static readonly Guid Cat = SampleProjects.Sample.Id;

    private readonly ISystemSummary _system = Substitute.For<ISystemSummary>();
    private readonly ErrorJournal _journal = new(NullLogger<ErrorJournal>.Instance);
    private readonly ILogArchive _log = Substitute.For<ILogArchive>();
    private readonly IProjectService _projects = Substitute.For<IProjectService>();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public DiagnosticsServiceTests()
    {
        _system.DescribeAsync(Arg.Any<CancellationToken>()).Returns(["StickerSnap 0.3.1 (desktop)", "FFmpeg 9.0.2 (ok)"]);
        _system.UserNames.Returns(["ivan"]);
        _system.SecretsAsync(Arg.Any<CancellationToken>()).Returns(["dk_8f2b5c9e1a7d4f3b"]);
        _log.ReadAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(["2026-10-19T13:20:00.000Z info JobQueue started"]);
        _projects.GetAsync(Cat, Arg.Any<CancellationToken>())
            .Returns(new OpenedProject(SampleProjects.Sample, @"C:\Users\ivan\Коты\cat.ssproj", PresenceStatus.Ok));
    }

    private DiagnosticsService Service() => new(_system, _journal, _log, _projects, new FakeTimeProvider(Now));

    private void Failed(string trace, int minutesAgo, ErrorCode code = ErrorCode.RenderFailed) => _journal.Record(new ErrorRecord(
        trace,
        Now.AddMinutes(-minutesAgo),
        code,
        @"ffmpeg failed pass 2 of C:\Users\ivan\Видео\cat.gif",
        "export job",
        new Dictionary<string, string>
        {
            [ErrorRecord.ProjectKey] = Cat.ToString(),
            [ErrorRecord.SegmentKey] = "seg-2",
            ["command"] = @"ffmpeg -i C:\Users\ivan\Видео\cat.gif -pass 2 out.webm",
        },
        Log: "Conversion failed!"));

    [Fact]
    public async Task Starts_with_the_app_the_time_and_the_error()
    {
        Failed("job-1", minutesAgo: 5);

        var report = (await Service().CollectAsync(new ReportSubject("job-1", "render_failed"), Cancel)).Text;

        report.Should().StartWith("StickerSnap 0.3.1 (desktop)\nFFmpeg 9.0.2 (ok)\nSent at 2026-10-19T13:30:00Z\nError: render_failed, trace job-1\n");
        report.Should().Contain("== render_failed at 2026-10-19T13:25:00Z ==\nOperation: export job\nMessage: ffmpeg failed pass 2 of cat.gif\n");
        report.Should().Contain("Command: ffmpeg -i cat.gif -pass 2 out.webm\n").And.Contain("Segment: seg-2\n").And.Contain("Log:\n  Conversion failed!\n");
    }

    [Fact]
    public async Task Describes_the_project_of_the_error_without_its_video_or_paths()
    {
        Failed("job-1", minutesAgo: 5);

        var report = (await Service().CollectAsync(new ReportSubject("job-1"), Cancel)).Text;

        report.Should().Contain($"== Project {Cat} ==\nRevision 12, saved at 2026-10-12T09:30:00Z\n");
        report.Should().Contain("Segment seg-2: frames 10-20, stretch Squash 270:270, speed 1.5, loop Pingpong, Emoji, long False, autoFit True\n");
        report.Should().NotContain("Видео").And.NotContain("Коты").And.NotContain("ivan");
    }

    [Fact]
    public async Task Takes_the_log_from_15_minutes_before_the_error()
    {
        Failed("job-1", minutesAgo: 5);

        await Service().CollectAsync(new ReportSubject("job-1"), Cancel);

        await _log.Received(1).ReadAsync(Now.AddMinutes(-20), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Takes_the_last_hour_of_the_log_without_an_error_and_says_a_lost_trace_is_not_in_memory()
    {
        var report = (await Service().CollectAsync(new ReportSubject("lost-1"), Cancel)).Text;

        report.Should().Contain("Trace lost-1 is not in memory");
        report.Should().Contain("== Server log since 2026-10-19T12:30:00Z ==\n2026-10-19T13:20:00.000Z info JobQueue started\n");
        await _log.Received(1).ReadAsync(Now.AddHours(-1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lists_the_latest_other_errors_in_a_line_each()
    {
        Failed("job-1", minutesAgo: 9);
        Failed("t-2", minutesAgo: 2, ErrorCode.Internal);

        var report = (await Service().CollectAsync(new ReportSubject(null, "internal"), Cancel)).Text;

        report.Should().Contain("Error: internal\n");
        report.Should().Contain("== Latest errors ==\n2026-10-19T13:28:00Z internal in export job, trace t-2\n2026-10-19T13:21:00Z render_failed in export job, trace job-1\n");
    }

    [Fact]
    public async Task Keeps_the_head_and_the_newest_log_entries_within_200_KB()
    {
        Failed("job-1", minutesAgo: 5);
        var entries = Enumerable.Range(0, 3000).Select(i => $"2026-10-19T13:2{i % 10}:00.000Z info Ж {i} {new string('ж', 40)}").ToList();
        _log.ReadAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(entries);

        var report = await Service().CollectAsync(new ReportSubject("job-1"), Cancel);

        report.Bytes.Should().Be(Encoding.UTF8.GetByteCount(report.Text)).And.BeLessThanOrEqualTo(DiagnosticsService.LimitBytes);
        report.Text.Should().StartWith("StickerSnap 0.3.1").And.EndWith($"Ж 2999 {new string('ж', 40)}\n");
        report.Text.Should().MatchRegex(@"\(\d+ earlier entries left out\)").And.NotContain("Ж 0 ");
    }

    [Fact]
    public async Task Writes_the_device_key_as_secret()
    {
        _log.ReadAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(["linked with dk_8f2b5c9e1a7d4f3b"]);

        var report = (await Service().CollectAsync(ReportSubject.None, Cancel)).Text;

        report.Should().Contain("linked with <secret>").And.NotContain("dk_8f2b5c9e1a7d4f3b");
    }

    [Fact]
    public async Task Refuses_a_trace_id_over_64_characters()
    {
        var collecting = () => Service().CollectAsync(new ReportSubject(new string('a', 65)), Cancel);

        var error = (await collecting.Should().ThrowAsync<AppException>()).Which;
        error.Code.Should().Be(ErrorCode.BadRequest);
        error.Fields.Should().ContainKey("traceId").WhoseValue.Should().Equal("too_long");
    }

    [Fact]
    public async Task Describes_an_error_of_the_interface_without_looking_for_it_on_the_server()
    {
        var subject = new ReportSubject(
            "a1b2-c3d4",
            "internal",
            ErrorSource.Interface,
            "TypeError: segment is undefined",
            "at Inspector (Inspector.tsx:12)\n  in Inspector\n",
            Now.AddMinutes(-3),
            Count: 3);

        var report = (await Service().CollectAsync(subject, Cancel)).Text;

        report.Should().Contain("Error: internal, number a1b2-c3d4 (interface)\nRepeated 3 times in 10 minutes\n");
        report.Should().Contain(
            "== Interface error at 2026-10-19T13:27:00Z ==\nMessage: TypeError: segment is undefined\nStack:\n  at Inspector (Inspector.tsx:12)\n    in Inspector\n");
        report.Should().NotContain("not in memory");
        await _log.Received(1).ReadAsync(Now.AddMinutes(-18), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Describes_the_last_launch_the_shell_found_crashed_without_its_paths()
    {
        var subject = new ReportSubject(
            "e5f6-a7b8",
            "internal",
            ErrorSource.Shell,
            "The server stopped with code 3",
            @"Unhandled exception in C:\Users\ivan\AppData\Local\StickerSnap\server\StickerSnap.Api.dll",
            Now.AddHours(-2));

        var report = (await Service().CollectAsync(subject, Cancel)).Text;

        report.Should().Contain("Error: internal, number e5f6-a7b8 (shell)\n");
        report.Should().Contain("== Last launch at 2026-10-19T11:30:00Z ==\nMessage: The server stopped with code 3\n");
        report.Should().Contain("Unhandled exception in StickerSnap.Api.dll").And.NotContain("ivan");
        report.Should().NotContain("Repeated");
    }

    [Fact]
    public async Task Lists_the_last_actions_of_the_interface_a_line_each()
    {
        var subject = new ReportSubject(Actions:
        [
            new InterfaceAction(Now.AddSeconds(-60), "Раздел: Проекты"),
            new InterfaceAction(Now.AddSeconds(-30), "Кнопка: Экспорт\nвсех"),
        ]);

        var report = (await Service().CollectAsync(subject, Cancel)).Text;

        report.Should().Contain(
            "== Last actions in the interface ==\n2026-10-19T13:29:00Z Раздел: Проекты\n2026-10-19T13:29:30Z Кнопка: Экспорт всех\n");
        report.Should().NotContain("Error:");
    }
}
