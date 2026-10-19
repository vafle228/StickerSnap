using System.Text.Json.Serialization;
using StickerSnap.Application.Diagnostics;
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
/// <param name="Actions">The interface's last steps, up to 30; only for a bug</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FeedbackRequest(
    FeedbackType Type,
    string Text,
    bool CanReply,
    string? Contact,
    FeedbackErrorBody? Error,
    IReadOnlyList<FeedbackActionBody>? Actions)
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
        Error?.ToError(),
        Actions?.Select(action => new InterfaceAction(action.At, action.What)).ToList());
}

/// <summary>The error of a bug report (schema FeedbackError).</summary>
/// <param name="Code">render_failed: a code of ErrorCode</param>
/// <param name="TraceId">The trace id of a server's error; the interface's own number for the others</param>
/// <param name="Source">What failed; the server when absent</param>
/// <param name="Message">The exception's text, or why the last launch stopped</param>
/// <param name="Stack">The stack and the component stack, or the tail of the server's output</param>
/// <param name="At">When it first happened</param>
/// <param name="Count">How many times it happened in 10 minutes; 1 when absent</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FeedbackErrorBody(
    string Code,
    string? TraceId,
    ErrorSource? Source,
    string? Message,
    string? Stack,
    DateTimeOffset? At,
    int? Count)
{
    public FeedbackError ToError() =>
        new(Code, TraceId, Source ?? ErrorSource.Server, Message, Stack, At, Count ?? 1);
}

/// <summary>One step of the interface before the error (schema FeedbackAction).</summary>
/// <param name="What">«Кнопка: Экспорт»; never what a field holds</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FeedbackActionBody(DateTimeOffset At, string What);

/// <summary>The feedback's number in the service (schema FeedbackCreated).</summary>
public sealed record FeedbackCreated(long Id);
