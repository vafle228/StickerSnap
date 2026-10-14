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

    [Fact]
    public void Reads_the_libavcodec_the_build_carries()
    {
        // The pinned build's output, cut down to the lines that matter
        var version = FfmpegVersion.Parse("""
            ffmpeg version n9.0.2-17-g2a571b6068-20260930 Copyright (c) 2000-2026 the FFmpeg developers
            built with gcc 16.2.0 (crosstool-NG 1.29.0.7_b1a94f6)
            configuration: --enable-libvpx --enable-libwebp
            libavutil      61.  1.102 / 61.  1.102
            libavcodec     63.  1.102 / 63.  1.102
            libavformat    63.  1.102 / 63.  1.102
            """);

        version.Should().Be(new FfmpegVersion("9.0.2-17-g2a571b6068-20260930", 9, 63));
    }

    [Fact]
    public void Does_not_take_a_build_from_master_of_2013_for_a_new_one()
    {
        // The ffmpeg.exe a Python package put on PATH: the cause of B20's "FFmpeg is installed, the app does not work"
        var version = FfmpegVersion.Parse("""
            ffmpeg version N-55702-g920046a
            built on Aug 21 2013 18:10:00 with gcc 4.7.3 (GCC)
            configuration: --enable-gpl --enable-libvpx
            libavutil      52. 42.100 / 52. 42.100
            libavcodec     55. 29.100 / 55. 29.100
            """);

        version.Should().Be(new FfmpegVersion("N-55702-g920046a", null, 55));
        version!.IsSupported.Should().BeFalse();
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
    [InlineData(9, null, true)]
    [InlineData(10, null, true)]
    [InlineData(8, null, false)]
    [InlineData(7, 63, false)]
    [InlineData(null, 63, true)]
    [InlineData(null, 64, true)]
    [InlineData(null, 62, false)]
    [InlineData(null, null, false)]
    public void Supports_9_0_and_newer_and_builds_from_master_with_its_libavcodec(int? major, int? libavcodec, bool supported)
    {
        new FfmpegVersion("any", major, libavcodec).IsSupported.Should().Be(supported);
    }
}
