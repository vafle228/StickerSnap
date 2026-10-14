using System.Text;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Core.Packs;

/// <summary>
/// The link names of packs (t.me/addstickers/&lt;name&gt;) by Telegram's rules: 1 to 64 Latin letters,
/// digits and underscores, starting with a letter, no two underscores in a row, ending in
/// _by_&lt;bot&gt;. The part before the tail is the user's; the tail is the product bot's.
/// </summary>
public static class PackNames
{
    /// <summary>What every name of our packs ends in: 21 characters.</summary>
    public const string Suffix = "_by_" + TelegramLimits.BotUsername;

    /// <summary>Telegram's longest name, tail included.</summary>
    public const int MaxLength = 64;

    /// <summary>The longest part before the tail: 64 less the 21 of <see cref="Suffix"/>.</summary>
    public const int MaxOwnLength = 43;

    private const string LinkPath = "addstickers/";
    private const string AppLink = "tg://addstickers?set=";

    private static readonly Dictionary<char, string> Latin = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e",
        ['ж'] = "zh", ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m",
        ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
        ['ф'] = "f", ['х'] = "h", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = "",
        ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
    };

    /// <summary>
    /// «Кот и все-все-все» → kot_i_vse_vse_vse: Cyrillic in Latin letters, anything else between
    /// words one underscore, no digits or underscores in front, cut to <see cref="MaxOwnLength"/>.
    /// </summary>
    public static string FromTitle(string title)
    {
        var latin = new StringBuilder();
        foreach (var ch in title.ToLowerInvariant())
        {
            if (Latin.TryGetValue(ch, out var letters)) latin.Append(letters);
            else latin.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '_');
        }

        var name = string.Join('_', latin.ToString().Split('_', StringSplitOptions.RemoveEmptyEntries));
        return Cut(name.TrimStart('_', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9'), MaxOwnLength);
    }

    /// <summary>What is wrong with <paramref name="own"/>, the part before the tail; empty when Telegram would take it.</summary>
    public static IReadOnlyList<PackNameProblem> Check(string own)
    {
        if (own.Length == 0) return [PackNameProblem.Empty];

        var problems = new List<PackNameProblem>();
        if (own.Length > MaxOwnLength) problems.Add(PackNameProblem.TooLong);
        // A trailing underscore meets the tail's: two in a row
        if (!own.All(IsNameChar) || own.Contains("__", StringComparison.Ordinal) || own.EndsWith('_'))
            problems.Add(PackNameProblem.BadChars);
        if (!char.IsAsciiLetter(own[0])) problems.Add(PackNameProblem.BadStart);
        return problems;
    }

    /// <summary>The whole name: kot_divan_by_StickerSnapAppBot.</summary>
    public static string Full(string own) => own + Suffix;

    /// <summary>Whether the product bot made a pack of this name: it ends in the tail and keeps the rules.</summary>
    public static bool IsOurs(string name) =>
        name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase) && Check(name[..^Suffix.Length]).Count == 0;

    /// <summary>
    /// The name in what «Добавить по ссылке» was given: https://t.me/addstickers/&lt;name&gt;, the same
    /// without https:// or on telegram.me, tg://addstickers?set=&lt;name&gt;, or the name alone;
    /// null when it holds no name.
    /// </summary>
    public static string? FromLink(string link)
    {
        var text = link.Trim();
        if (text.StartsWith(AppLink, StringComparison.OrdinalIgnoreCase))
            text = text[AppLink.Length..];
        else if (text.IndexOf(LinkPath, StringComparison.OrdinalIgnoreCase) is var at and >= 0)
            text = text[(at + LinkPath.Length)..];

        var name = text.Split('?', '#', '/', '&')[0];
        return name.Length is > 0 and <= MaxLength && name.All(IsNameChar) ? name : null;
    }

    /// <summary>The <paramref name="number"/>th variant of a taken name, kot_divan_2, cut so it still fits.</summary>
    public static string Variant(string own, int number)
    {
        var ending = $"_{number}";
        return Cut(own, MaxOwnLength - ending.Length) + ending;
    }

    private static bool IsNameChar(char ch) => char.IsAsciiLetterOrDigit(ch) || ch == '_';

    private static string Cut(string name, int length) => (name.Length > length ? name[..length] : name).TrimEnd('_');
}

/// <summary>Why a pack name is refused (contract, PackNameCheck.problems).</summary>
public enum PackNameProblem
{
    Empty,
    TooLong,
    BadChars,
    BadStart,
    Taken,
}
