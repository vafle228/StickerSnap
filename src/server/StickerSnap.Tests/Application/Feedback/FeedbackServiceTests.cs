using StickerSnap.Application.Diagnostics;
using StickerSnap.Application.Feedback;
using StickerSnap.Core.Errors;

namespace StickerSnap.Tests.Application.Feedback;

/// <summary>The window «Написать отзыв»: what is checked, what a bug carries and what reaches the inbox.</summary>
public sealed class FeedbackServiceTests
{
    private static readonly FeedbackError RenderFailed = new("render_failed", "7f3a-c21e");

    private readonly IFeedbackInbox _inbox = Substitute.For<IFeedbackInbox>();
    private readonly IDiagnosticsService _diagnostics = Substitute.For<IDiagnosticsService>();
    private FeedbackLetter? _sent;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public FeedbackServiceTests()
    {
        _inbox.SendAsync(Arg.Do<FeedbackLetter>(letter => _sent = letter), Arg.Any<CancellationToken>()).Returns(41L);
        _diagnostics.CollectAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new DiagnosticsReport("StickerSnap 0.3.1", 17));
    }

    private FeedbackService Service() => new(_inbox, _diagnostics);

    [Fact]
    public async Task Sends_an_idea_without_a_report()
    {
        var id = await Service().SendAsync(new FeedbackDraft(FeedbackKind.Idea, "  Обрезать на таймлайне  ", CanReply: true), Cancel);

        id.Should().Be(41);
        _sent.Should().Be(new FeedbackLetter(FeedbackKind.Idea, "Обрезать на таймлайне", true));
        await _diagnostics.DidNotReceiveWithAnyArgs().CollectAsync(null, null, Cancel);
    }

    [Fact]
    public async Task Always_attaches_the_report_to_a_bug()
    {
        await Service().SendAsync(new FeedbackDraft(FeedbackKind.Bug, "Экспорт упал", CanReply: false, Error: RenderFailed), Cancel);

        _sent!.Diagnostics.Should().Be("StickerSnap 0.3.1");
        _sent.Error.Should().Be(RenderFailed);
        await _diagnostics.Received(1).CollectAsync("7f3a-c21e", "render_failed", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Takes_an_empty_text_only_from_a_bug_opened_from_an_error()
    {
        await Service().SendAsync(new FeedbackDraft(FeedbackKind.Bug, " ", CanReply: false, Error: RenderFailed), Cancel);

        _sent!.Text.Should().BeEmpty();
        await AssertBadRequest(new FeedbackDraft(FeedbackKind.Bug, " ", CanReply: false), "text", "empty");
        await AssertBadRequest(new FeedbackDraft(FeedbackKind.Other, "", CanReply: false), "text", "empty");
    }

    [Fact]
    public async Task Refuses_a_text_over_2000_characters_and_a_long_contact()
    {
        await AssertBadRequest(new FeedbackDraft(FeedbackKind.Idea, new string('a', 2001), CanReply: false), "text", "too_long");
        await AssertBadRequest(new FeedbackDraft(FeedbackKind.Idea, "Идея", CanReply: true, new string('a', 101)), "contact", "too_long");
    }

    [Fact]
    public async Task Refuses_an_error_on_anything_but_a_bug()
    {
        await AssertBadRequest(new FeedbackDraft(FeedbackKind.Idea, "Идея", CanReply: false, Error: RenderFailed), "error", "bug_only");
        await AssertBadRequest(new FeedbackDraft(FeedbackKind.Bug, "", CanReply: false, Error: new FeedbackError("", null)), "error", "invalid");
    }

    [Fact]
    public async Task Sends_the_contact_only_when_the_user_allowed_an_answer()
    {
        await Service().SendAsync(new FeedbackDraft(FeedbackKind.Idea, "Идея", CanReply: true, " @dima "), Cancel);
        _sent!.Contact.Should().Be("@dima");

        await Service().SendAsync(new FeedbackDraft(FeedbackKind.Idea, "Идея", CanReply: false, "@dima"), Cancel);
        _sent!.Contact.Should().BeNull();
    }

    [Fact]
    public async Task Collects_a_report_without_a_trace_for_a_bug_from_the_menu()
    {
        await Service().SendAsync(new FeedbackDraft(FeedbackKind.Bug, "Упало", CanReply: false), Cancel);

        await _diagnostics.Received(1).CollectAsync(null, null, Arg.Any<CancellationToken>());
    }

    private async Task AssertBadRequest(FeedbackDraft draft, string field, string problem)
    {
        var send = () => Service().SendAsync(draft, Cancel);

        var error = (await send.Should().ThrowAsync<AppException>()).Which;
        error.Code.Should().Be(ErrorCode.BadRequest);
        error.Fields.Should().ContainKey(field).WhoseValue.Should().Equal(problem);
    }
}
