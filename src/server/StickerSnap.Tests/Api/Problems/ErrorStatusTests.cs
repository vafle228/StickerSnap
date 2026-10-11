using System.Text.Json;
using StickerSnap.Api.Problems;
using StickerSnap.Core.Errors;

namespace StickerSnap.Tests.Api.Problems;

/// <summary>Every code answers with the status from the contract's table («Ошибки»).</summary>
public sealed class ErrorStatusTests
{
    /// <summary>The table as the contract has it, written in the contract's words.</summary>
    private static readonly Dictionary<string, int> Contract = new()
    {
        ["source_unreadable"] = 422,
        ["format_unsupported"] = 415,
        ["source_not_found"] = 404,
        ["project_not_found"] = 404,
        ["project_invalid"] = 422,
        ["project_too_new"] = 422,
        ["save_failed"] = 507,
        ["revision_conflict"] = 409,
        ["disk_full"] = 507,
        ["render_failed"] = 500,
        ["cannot_fit"] = 422,
        ["ffmpeg_missing"] = 503,
        ["job_not_found"] = 404,
        ["file_not_found"] = 404,
        ["validation_failed"] = 422,
        ["account_not_linked"] = 409,
        ["bot_blocked"] = 403,
        ["network_unavailable"] = 503,
        ["pack_name_invalid"] = 422,
        ["pack_name_taken"] = 409,
        ["pack_full"] = 409,
        ["pack_not_found"] = 404,
        ["not_our_pack"] = 422,
        ["rate_limited"] = 429,
        ["telegram_error"] = 502,
        ["bad_request"] = 400,
        ["internal"] = 500,
    };

    [Fact]
    public void Gives_each_code_the_status_of_the_contract()
    {
        var statuses = Enum.GetValues<ErrorCode>().ToDictionary(
            code => JsonNamingPolicy.SnakeCaseLower.ConvertName(code.ToString()),
            ErrorStatus.Of);

        statuses.Should().Equal(Contract);
    }

    [Fact]
    public void Names_the_problem_type_and_title_after_the_code()
    {
        var problem = Problem.Of(ErrorCode.SourceUnreadable, "moov atom not found");

        problem.Type.Should().Be("https://stickersnap.app/problems/source_unreadable");
        problem.Title.Should().Be("source unreadable");
        problem.Status.Should().Be(422);
        problem.Detail.Should().Be("moov atom not found");
    }

    [Fact]
    public void Carries_the_params_fields_and_log_of_the_exception_only_when_there_are_any()
    {
        var bare = Problem.Of(new AppException(ErrorCode.Internal, "boom"));
        var full = Problem.Of(new AppException(ErrorCode.FormatUnsupported, "avi")
        {
            Params = new Dictionary<string, object> { ["ext"] = "avi" },
            Fields = new Dictionary<string, string[]> { ["source.path"] = ["unsupported"] },
            Log = "Input #0, avi",
        });

        bare.Extensions.Keys.Should().BeEquivalentTo(["code"]);
        full.Extensions.Keys.Should().BeEquivalentTo(["code", "params", "fields", "log"]);
        full.Extensions["log"].Should().Be("Input #0, avi");
    }
}
