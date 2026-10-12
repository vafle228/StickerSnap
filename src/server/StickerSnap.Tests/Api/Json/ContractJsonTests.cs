using System.Text.Json;
using StickerSnap.Api.Contracts;
using StickerSnap.Api.Json;
using StickerSnap.Application.App;
using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;

namespace StickerSnap.Tests.Api.Json;

/// <summary>The contract's «Формат»: camelCase, enums as strings, no nulls.</summary>
public sealed class ContractJsonTests
{
    private static readonly JsonSerializerOptions Options = ContractJson.Apply(new JsonSerializerOptions());

    [Theory]
    [InlineData(MediaEngineStatus.TooOld, "\"too_old\"")]
    [InlineData(AppMode.Desktop, "\"desktop\"")]
    [InlineData(ErrorCode.FfmpegMissing, "\"ffmpeg_missing\"")]
    [InlineData(ErrorCode.NotOurPack, "\"not_our_pack\"")]
    public void Writes_enums_as_snake_case_strings(object value, string json)
    {
        JsonSerializer.Serialize(value, value.GetType(), Options).Should().Be(json);
    }

    [Fact]
    public void Does_not_read_an_enum_from_a_number()
    {
        var read = () => JsonSerializer.Deserialize<AppMode>("1", Options);

        read.Should().Throw<JsonException>();
    }

    [Fact]
    public void Leaves_out_an_absent_field_and_names_fields_in_camel_case()
    {
        var json = JsonSerializer.Serialize(new FfmpegInfo(MediaEngineStatus.Missing, Version: null), Options);

        json.Should().Be("""{"status":"missing"}""");
    }

    [Fact]
    public void Keeps_custom_emoji_as_the_sticker_type_is_named()
    {
        var json = JsonSerializer.Serialize(new PackCapacity(Regular: 120, CustomEmoji: 200), Options);

        json.Should().Be("""{"regular":120,"custom_emoji":200}""");
    }
}
