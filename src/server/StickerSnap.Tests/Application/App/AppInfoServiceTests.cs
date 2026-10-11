using StickerSnap.Application.App;
using StickerSnap.Application.Ffmpeg;
using StickerSnap.Core.Ffmpeg;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Tests.Application.App;

public sealed class AppInfoServiceTests
{
    private readonly IHostInfo _host = Substitute.For<IHostInfo>();
    private readonly IFfmpegLocator _ffmpeg = Substitute.For<IFfmpegLocator>();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reports_the_build_the_launch_and_the_found_ffmpeg()
    {
        var found = FfmpegLookup.Found(new FfmpegVersion("9.0.2", 9), "/opt/ffmpeg");
        _host.AppVersion.Returns("0.1.0");
        _host.Mode.Returns(AppMode.Desktop);
        _ffmpeg.FindAsync(Arg.Any<CancellationToken>()).Returns(found);

        var app = await new AppInfoService(_host, _ffmpeg).GetAsync(Cancel);

        app.Should().Be(new AppInfo(
            AppVersion: "0.1.0",
            Ffmpeg: found,
            ApiVersion: "1.0.0",
            Mode: AppMode.Desktop,
            BotUsername: "StickerSnapAppBot",
            Limits: TelegramLimits.Current));
    }

    [Fact]
    public void Gives_the_emoji_the_sticker_s_256_kb()
    {
        var limits = TelegramLimits.Current;

        limits.Sticker.Should().Be(new PresetLimits(MaxBytes: 262_144, Side: 512, MaxDurationSec: 3, MaxFps: 30));
        limits.Emoji.Should().Be(new PresetLimits(MaxBytes: 262_144, Side: 100, MaxDurationSec: 3, MaxFps: 30));
        (limits.RegularPackCapacity, limits.EmojiPackCapacity).Should().Be((120, 200));
    }
}
