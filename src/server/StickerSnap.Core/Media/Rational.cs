namespace StickerSnap.Core.Media;

/// <summary>
/// A fraction such as fps 30000/1001, kept whole so frame numbers and times convert without a
/// rounding error (contract, Rational). ffprobe writes it as `30000/1001`, an aspect ratio as `2:1`.
/// </summary>
public readonly record struct Rational(int Num, int Den)
{
    /// <summary>Positive on both sides; ffprobe writes `0/0` for a rate it does not know.</summary>
    public bool IsValid => Num > 0 && Den > 0;

    public double ToDouble() => (double)Num / Den;

    /// <summary>`30000/1001` or `2:1`; null for anything else.</summary>
    public static Rational? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var parts = text.Split('/', ':');
        return parts.Length == 2 && int.TryParse(parts[0], out var num) && int.TryParse(parts[1], out var den)
            ? new Rational(num, den)
            : null;
    }

    public override string ToString() => $"{Num}/{Den}";
}
