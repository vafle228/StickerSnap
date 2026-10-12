namespace StickerSnap.Core.Validation;

/// <summary>
/// One line of the checklist. <see cref="Expected"/> and <see cref="Actual"/> are values, not
/// words: `≤ 262144`, `640×360`, `2.97`, `none`; the SPA puts them into Russian.
/// </summary>
public sealed record RuleCheck(ValidationRule Rule, CheckStatus Status, string Expected, string Actual);
