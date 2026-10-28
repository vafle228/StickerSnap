using StickerSnap.Core.Errors;
using StickerSnap.Core.Links;

namespace StickerSnap.Tests.Core.Links;

/// <summary>What a pasted link is, by its look alone: the site, the video and its one link.</summary>
public sealed class VideoLinkTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("youtube.com/watch?v=dQw4w9WgXcQ&t=42s")]
    [InlineData("  https://m.youtube.com/watch?list=PLx&v=dQw4w9WgXcQ&index=3  ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc&t=10")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ?feature=share")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("http://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    public void Takes_a_youtube_video_without_its_time_and_playlist(string text)
    {
        var link = VideoLink.Parse(text);

        link.Should().Be(new VideoLink(LinkSite.Youtube, "dQw4w9WgXcQ", "https://www.youtube.com/watch?v=dQw4w9WgXcQ"));
    }

    [Theory]
    [InlineData("https://giphy.com/gifs/cat-typing-JIX9t2j0ZTN9S", false)]
    [InlineData("giphy.com/gifs/JIX9t2j0ZTN9S", false)]
    [InlineData("https://giphy.com/clips/cat-typing-JIX9t2j0ZTN9S", false)]
    [InlineData("https://media2.giphy.com/media/JIX9t2j0ZTN9S/giphy.gif", false)]
    [InlineData("https://media1.giphy.com/media/v1.Y2lkPTc5MGI3NjEx/JIX9t2j0ZTN9S/giphy.mp4", false)]
    [InlineData("https://i.giphy.com/JIX9t2j0ZTN9S.webp", false)]
    [InlineData("https://giphy.com/stickers/cat-dance-JIX9t2j0ZTN9S", true)]
    public void Takes_a_giphy_gif_or_sticker(string text, bool sticker)
    {
        var link = VideoLink.Parse(text);

        link.Site.Should().Be(LinkSite.Giphy);
        link.VideoId.Should().Be("JIX9t2j0ZTN9S");
        link.IsSticker.Should().Be(sticker);
        link.Url.Should().Be(sticker ? "https://giphy.com/stickers/JIX9t2j0ZTN9S" : "https://giphy.com/gifs/JIX9t2j0ZTN9S");
    }

    [Theory]
    [InlineData("")]
    [InlineData("котик")]
    [InlineData("two words.com")]
    [InlineData("ftp://youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData(null)]
    public void Says_text_that_is_no_link_is_not_one(string? text)
    {
        var parse = () => VideoLink.Parse(text);

        parse.Should().Throw<AppException>()
            .Which.Should().Match<AppException>(e => e.Code == ErrorCode.LinkUnsupported && (string)e.Params["reason"] == "not_link");
    }

    [Theory]
    [InlineData("https://vimeo.com/123456")]
    [InlineData("https://www.youtube.com/@channel")]
    [InlineData("https://www.youtube.com/watch?v=short")]
    [InlineData("https://giphy.com/explore/cats")]
    [InlineData("https://notgiphy.com/gifs/cat-JIX9t2j0ZTN9S")]
    public void Says_a_page_that_is_no_video_of_our_sites_is_another_site(string text)
    {
        var parse = () => VideoLink.Parse(text);

        parse.Should().Throw<AppException>()
            .Which.Should().Match<AppException>(e => e.Code == ErrorCode.LinkUnsupported && (string)e.Params["reason"] == "site");
    }

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PLx")]
    [InlineData("https://www.youtube.com/watch?list=PLx")]
    public void Says_a_playlist_without_a_video_has_none_to_take(string text)
    {
        var parse = () => VideoLink.Parse(text);

        parse.Should().Throw<AppException>()
            .Which.Should().Match<AppException>(e => e.Code == ErrorCode.VideoUnavailable && (string)e.Params["reason"] == "playlist");
    }
}
