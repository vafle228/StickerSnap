using System.Text.Json.Serialization;
using StickerSnap.Application.Feedback;

namespace StickerSnap.Api.Contracts;

/// <summary>«Ошибка», «Идея», «Другое» (schema FeedbackType).</summary>
public enum FeedbackType
{
    Bug,
    Idea,
    Other,
}

/// <summary>POST /api/v1/feedback (schema FeedbackRequest): the window «Написать отзыв», artboard 19·4.</summary>
/// <param name="Text">Up to 2000 characters; empty only for a bug with an error</param>
/// <param name="CanReply">«Можно ответить мне в Telegram» or «Ответить мне на»</param>
/// <param name="Contact">E-mail or @username; sent only while the computer is not linked</param>
/// <param name="Error">The error the window was opened from; only for a bug</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FeedbackRequest(
    FeedbackType Type,
    string Text,
    bool CanReply,
    string? Contact,
    FeedbackErrorBody? Error)
{
    public FeedbackDraft ToDraft() => new(
        Type switch
        {
            FeedbackType.Bug => FeedbackKind.Bug,
            FeedbackType.Idea => FeedbackKind.Idea,
            _ => FeedbackKind.Other,
        },
        Text,
        CanReply,
        Contact,
        Error is { } error ? new FeedbackError(error.Code, error.TraceId) : null);
}

/// <summary>The error of a bug report (schema FeedbackError).</summary>
/// <param name="Code">render_failed: a code of ErrorCode</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FeedbackErrorBody(string Code, string? TraceId);

/// <summary>The feedback's number in the service (schema FeedbackCreated).</summary>
public sealed record FeedbackCreated(long Id);
