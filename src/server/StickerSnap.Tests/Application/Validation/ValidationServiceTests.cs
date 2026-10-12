using StickerSnap.Application.App;
using StickerSnap.Application.Media;
using StickerSnap.Application.Sources;
using StickerSnap.Application.Validation;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Validation;
using Samples = StickerSnap.Tests.Core.Render.Media;

namespace StickerSnap.Tests.Application.Validation;

public sealed class ValidationServiceTests
{
    private readonly IMediaProbe _probe = Substitute.For<IMediaProbe>();
    private readonly IHostInfo _host = Substitute.For<IHostInfo>();
    private readonly IUploadStore _uploads = Substitute.For<IUploadStore>();

    public ValidationServiceTests()
    {
        _host.Mode.Returns(AppMode.Desktop);
        _uploads.Find(Arg.Any<string>()).Returns((string?)null);
    }

    private ValidationService Service => new(new SourceFiles(_host, _uploads), _probe);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Checks_a_webm_on_disk_under_its_name_and_size()
    {
        var path = Path.Combine("stickers", "cat.webm");
        _probe.ProbeAsync(path, Arg.Any<CancellationToken>()).Returns(Samples.Sticker with { SizeBytes = 277_504 });

        var result = await Service.ValidateAsync(new SourceRef.OnDisk(path), Cancel);

        result.FileName.Should().Be("cat.webm");
        result.Bytes.Should().Be(277_504);
        result.Validation.Verdict.Should().Be(ValidationVerdict.Fail);
        result.Validation.Checks.Single(c => c.Status == CheckStatus.Fail).Rule.Should().Be(ValidationRule.Size);
    }

    [Theory]
    [InlineData("clip.mp4", "mp4")]
    [InlineData("cat.GIF", "gif")]
    [InlineData("noext", "")]
    public async Task Refuses_a_file_that_is_not_webm_without_reading_it(string name, string extension)
    {
        var validate = () => Service.ValidateAsync(new SourceRef.OnDisk(name), Cancel);

        (await validate.Should().ThrowAsync<AppException>())
            .Which.Should().Match<AppException>(e => e.Code == ErrorCode.FormatUnsupported && (string)e.Params["ext"] == extension);
        await _probe.DidNotReceiveWithAnyArgs().ProbeAsync(default!, Cancel);
    }

    [Fact]
    public async Task Takes_the_extension_in_any_case()
    {
        _probe.ProbeAsync("CAT.WEBM", Arg.Any<CancellationToken>()).Returns(Samples.Sticker);

        (await Service.ValidateAsync(new SourceRef.OnDisk("CAT.WEBM"), Cancel)).Validation.Verdict.Should().Be(ValidationVerdict.Pass);
    }

    [Fact]
    public async Task Passes_on_what_the_probe_could_not_read()
    {
        _probe.ProbeAsync("broken.webm", Arg.Any<CancellationToken>())
            .Returns<MediaInfo>(_ => throw new AppException(ErrorCode.SourceUnreadable, "EBML header parsing failed"));

        var validate = () => Service.ValidateAsync(new SourceRef.OnDisk("broken.webm"), Cancel);

        (await validate.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.SourceUnreadable);
    }

    [Fact]
    public async Task Refuses_a_path_from_a_browser()
    {
        _host.Mode.Returns(AppMode.Web);

        var validate = () => Service.ValidateAsync(new SourceRef.OnDisk("C:/Users/cat.webm"), Cancel);

        (await validate.Should().ThrowAsync<AppException>())
            .Which.Should().Match<AppException>(e => e.Code == ErrorCode.BadRequest && e.Fields.ContainsKey("source.kind"));
    }

    [Fact]
    public async Task Checks_an_upload_at_the_file_the_store_keeps()
    {
        var path = Path.Combine("uploads", "0f", "cat.webm");
        _uploads.Find("u-1").Returns(path);
        _probe.ProbeAsync(path, Arg.Any<CancellationToken>()).Returns(Samples.Sticker);

        var result = await Service.ValidateAsync(new SourceRef.Upload("u-1"), Cancel);

        result.FileName.Should().Be("cat.webm");
        result.Validation.Verdict.Should().Be(ValidationVerdict.Pass);
    }

    [Fact]
    public async Task Finds_no_upload_the_store_does_not_keep()
    {
        var validate = () => Service.ValidateAsync(new SourceRef.Upload("u-1"), Cancel);

        (await validate.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.SourceNotFound);
    }
}
