using StickerSnap.Infrastructure.Ffmpeg;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>The version from the first line of `ffmpeg -version`, as the builds people have print it.</summary>
public sealed class FfmpegVersionTests
{
    [Theory]
    [InlineData("ffmpeg version 9.0 Copyright (c) 2000-2026 the FFmpeg developers", "9.0", 9)]
    [InlineData("ffmpeg version 9.0.2 Copyright (c) 2000-2026 the FFmpeg developers", "9.0.2", 9)]
    [InlineData("ffmpeg version n9.0.1 Copyright (c) 2000-2026 the FFmpeg developers", "9.0.1", 9)]
    [InlineData("ffmpeg version n9.0.2-17-g2a571b6068 Copyright (c) 2000-2026 the FFmpeg developers", "9.0.2-17-g2a571b6068", 9)]
    [InlineData("ffmpeg version 7.1.1-full_build-www.gyan.dev Copyright (c) 2000-2025 the FFmpeg developers", "7.1.1-full_build-www.gyan.dev", 7)]
    [InlineData("ffmpeg version 4.4.2-0ubuntu0.22.04.1 Copyright (c) 2000-2021 the FFmpeg developers", "4.4.2-0ubuntu0.22.04.1", 4)]
    public void Reads_the_release_from_the_first_line(string line, string text, int major)
    {
        var version = FfmpegVersion.Parse($"{line}\nbuilt with gcc 15.2.0\nconfiguration: --enable-libvpx\n");

        version.Should().Be(new FfmpegVersion(text, major));
    }

    [Fact]
    public void Reads_a_build_from_master_as_a_version_without_a_number()
    {
        var version = FfmpegVersion.Parse("ffmpeg version N-121234-g5f8a1c2b3d-20260930 Copyright (c) 2000-2026");

        version.Should().Be(new FfmpegVersion("N-121234-g5f8a1c2b3d-20260930", null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ffprobe version 9.0 Copyright (c) 2007-2026 the FFmpeg developers")]
    [InlineData("Usage: python -m imageio_ffmpeg")]
    public void Is_null_for_output_that_is_not_ffmpeg_s(string output)
    {
        FfmpegVersion.Parse(output).Should().BeNull();
    }

    [Theory]
    [InlineData(9, true)]
    [InlineData(10, true)]
    [InlineData(null, true)]
    [InlineData(8, false)]
    [InlineData(7, false)]
    public void Supports_9_0_and_newer_and_builds_from_master(int? major, bool supported)
    {
        new FfmpegVersion("any", major).IsSupported.Should().Be(supported);
    }
}
