using StickerSnap.Core.Packs;

namespace StickerSnap.Tests.Core.Packs;

/// <summary>Telegram's rules for pack names, the link from a title and «Добавить по ссылке».</summary>
public sealed class PackNamesTests
{
    [Fact]
    public void Leaves_the_user_43_characters_before_the_bots_tail()
    {
        PackNames.Suffix.Should().Be("_by_StickerSnapAppBot").And.HaveLength(21);
        PackNames.MaxOwnLength.Should().Be(PackNames.MaxLength - PackNames.Suffix.Length);
    }

    [Theory]
    [InlineData("Кот и диван", "kot_i_divan")]
    [InlineData("Кот и все-все-все", "kot_i_vse_vse_vse")]
    [InlineData("Реакции с созвона", "reaktsii_s_sozvona")]
    [InlineData("Щука, ёж и Объявление", "schuka_ezh_i_obyavlenie")]
    [InlineData("Summer 2026!", "summer_2026")]
    [InlineData("2026 — лето", "leto")]
    [InlineData("  __Mixed__ Кейс  ", "mixed_keys")]
    [InlineData("🐱🐱", "")]
    [InlineData("", "")]
    public void Makes_the_link_from_the_title(string title, string link)
    {
        PackNames.FromTitle(title).Should().Be(link);
    }

    [Fact]
    public void Cuts_a_long_title_to_what_fits_before_the_tail_without_a_trailing_underscore()
    {
        var link = PackNames.FromTitle("Очень длинное название пака про кота и диван и всё остальное");

        link.Length.Should().BeLessThanOrEqualTo(PackNames.MaxOwnLength);
        link.Should().StartWith("ochen_dlinnoe_nazvanie").And.NotEndWith("_");
        PackNames.Check(link).Should().BeEmpty();
    }

    public static TheoryData<string, PackNameProblem[]> Names => new()
    {
        { "kot_divan", [] },
        { "Kot2", [] },
        { "", [PackNameProblem.Empty] },
        { new string('a', 44), [PackNameProblem.TooLong] },
        { new string('a', 43), [] },
        { "kot-divan", [PackNameProblem.BadChars] },
        { "кот", [PackNameProblem.BadChars, PackNameProblem.BadStart] },
        { "kot__divan", [PackNameProblem.BadChars] },
        { "kot_", [PackNameProblem.BadChars] },
        { "2kota", [PackNameProblem.BadStart] },
        { "_kot", [PackNameProblem.BadStart] },
        { "9" + new string('x', 43) + "!", [PackNameProblem.TooLong, PackNameProblem.BadChars, PackNameProblem.BadStart] },
    };

    [Theory]
    [MemberData(nameof(Names))]
    public void Finds_what_telegram_would_refuse(string own, PackNameProblem[] problems)
    {
        PackNames.Check(own).Should().Equal(problems);
    }

    [Theory]
    [InlineData("https://t.me/addstickers/kot_divan_by_StickerSnapAppBot", "kot_divan_by_StickerSnapAppBot")]
    [InlineData("t.me/addstickers/kot_divan_by_StickerSnapAppBot", "kot_divan_by_StickerSnapAppBot")]
    [InlineData(" https://telegram.me/addstickers/kot_divan_by_StickerSnapAppBot/ ", "kot_divan_by_StickerSnapAppBot")]
    [InlineData("https://t.me/addstickers/Kot_by_StickerSnapAppBot?startapp=1", "Kot_by_StickerSnapAppBot")]
    [InlineData("tg://addstickers?set=kot_by_StickerSnapAppBot", "kot_by_StickerSnapAppBot")]
    [InlineData("kot_by_StickerSnapAppBot", "kot_by_StickerSnapAppBot")]
    [InlineData("https://t.me/addstickers/", null)]
    [InlineData("https://t.me/addstickers/кот", null)]
    [InlineData("котики", null)]
    [InlineData("", null)]
    public void Finds_the_pack_name_in_a_link(string link, string? name)
    {
        PackNames.FromLink(link).Should().Be(name);
    }

    [Theory]
    [InlineData("kot_divan_by_StickerSnapAppBot", true)]
    [InlineData("KOT_by_stickersnapappbot", true)]
    [InlineData("kot_divan_by_OtherBot", false)]
    [InlineData("_by_StickerSnapAppBot", false)]
    [InlineData("kot__by_StickerSnapAppBot", false)]
    [InlineData("kot_divan", false)]
    public void Knows_the_packs_of_the_product_bot(string name, bool ours)
    {
        PackNames.IsOurs(name).Should().Be(ours);
    }

    [Fact]
    public void Numbers_a_taken_name_and_keeps_it_within_the_limit()
    {
        PackNames.Variant("kot_divan", 2).Should().Be("kot_divan_2");
        PackNames.Full("kot_divan_2").Should().Be("kot_divan_2_by_StickerSnapAppBot");

        var longest = PackNames.Variant(new string('a', 40) + "_bc", 3);

        longest.Should().Be(new string('a', 40) + "_3");
        PackNames.Check(longest).Should().BeEmpty();
    }
}
