using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace StickerSnap.Application.ProjectMedia;

/// <summary>Keys of <see cref="IMediaCache"/>: a hash of what is made and of what from.</summary>
public static class MediaKeys
{
    /// <summary>Changes when the media engine is asked for other files, so files made before are not served.</summary>
    internal const string Version = "v1";

    /// <summary>32 lower-case hex digits for <paramref name="parts"/>, such as the kind, the source's fingerprint and the sizes.</summary>
    public static string Of(params object[] parts)
    {
        var text = string.Join('|', parts.Prepend(Version).Select(part => Convert.ToString(part, CultureInfo.InvariantCulture)));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
    }
}
