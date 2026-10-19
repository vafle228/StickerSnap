using Microsoft.Extensions.Logging.Abstractions;
using StickerSnap.Application.Diagnostics;
using StickerSnap.Core.Errors;

namespace StickerSnap.Tests.Application.Diagnostics;

/// <summary>The last 50 reportable errors, found by the trace id the SPA was shown.</summary>
public sealed class ErrorJournalTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly ErrorJournal _journal = new(NullLogger<ErrorJournal>.Instance);

    private static ErrorRecord Error(string trace, ErrorCode code = ErrorCode.RenderFailed, int minute = 0) =>
        new(trace, Noon.AddMinutes(minute), code, "ffmpeg failed", "export job", new Dictionary<string, string>());

    [Fact]
    public void Finds_every_error_of_a_trace_oldest_first()
    {
        _journal.Record(Error("job-1", minute: 1));
        _journal.Record(Error("job-2", minute: 2));
        _journal.Record(Error("job-1", ErrorCode.TelegramError, minute: 3));

        _journal.Find("job-1").Select(error => error.Code).Should().Equal(ErrorCode.RenderFailed, ErrorCode.TelegramError);
        _journal.Find("job-9").Should().BeEmpty();
    }

    [Fact]
    public void Keeps_no_expected_error_the_user_mends_alone()
    {
        _journal.Record(Error("t-1", ErrorCode.NetworkUnavailable));
        _journal.Record(Error("t-2", ErrorCode.RateLimited));

        _journal.Latest(10).Should().BeEmpty();
    }

    [Fact]
    public void Keeps_the_last_50_and_lists_the_newest_first()
    {
        for (var i = 0; i < ErrorJournal.Capacity + 5; i++) _journal.Record(Error($"t-{i}", minute: i));

        _journal.Find("t-4").Should().BeEmpty();
        _journal.Find("t-5").Should().ContainSingle();
        _journal.Latest(3).Select(error => error.TraceId).Should().Equal("t-54", "t-53", "t-52");
    }

    [Fact]
    public void Takes_the_code_log_and_context_of_an_app_exception_over_what_the_caller_knows()
    {
        var thrown = new AppException(ErrorCode.RenderFailed, "ffmpeg failed pass 2")
        {
            Log = "Conversion failed!",
            Context = new Dictionary<string, string> { ["command"] = "ffmpeg -i cat.gif" },
        };

        var record = ErrorRecord.Of(thrown, "job-1", Noon, "export job", new Dictionary<string, string> { ["project"] = "p-1" });

        record.Code.Should().Be(ErrorCode.RenderFailed);
        record.Log.Should().Be("Conversion failed!");
        record.Context.Should().BeEquivalentTo(new Dictionary<string, string> { ["project"] = "p-1", ["command"] = "ffmpeg -i cat.gif" });
    }

    [Fact]
    public void Records_any_other_exception_as_internal_with_its_stack()
    {
        var record = ErrorRecord.Of(new InvalidOperationException("boom"), "t-1", Noon, "GET /api/v1/app", new Dictionary<string, string>());

        record.Code.Should().Be(ErrorCode.Internal);
        record.Exception.Should().Contain("InvalidOperationException: boom");
    }
}
