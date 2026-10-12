using StickerSnap.Api.Problems;
using StickerSnap.Core.Errors;

namespace StickerSnap.Tests.Api.Problems;

/// <summary>
/// A problem named after its code. That each code has the status of the contract's table
/// («Ошибки») is checked against openapi.yaml itself, in Api/Contract/ContractTests.
/// </summary>
public sealed class ErrorStatusTests
{
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
