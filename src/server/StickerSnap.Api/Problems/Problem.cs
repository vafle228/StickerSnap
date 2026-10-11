using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using StickerSnap.Core.Errors;

namespace StickerSnap.Api.Problems;

/// <summary>
/// RFC 9457 Problem Details with the contract's extensions (schema Problem): `code`, and when
/// there are any `params`, `fields`, `log`; `traceId` on every one.
/// </summary>
public static class Problem
{
    public static ProblemDetails Of(ErrorCode code, string? detail = null)
    {
        var name = JsonNamingPolicy.SnakeCaseLower.ConvertName(code.ToString());
        return new ProblemDetails
        {
            Type = $"https://stickersnap.app/problems/{name}",
            Status = ErrorStatus.Of(code),
            Title = name.Replace('_', ' '),
            Detail = detail,
            Extensions = { ["code"] = code },
        };
    }

    public static ProblemDetails Of(AppException exception)
    {
        var problem = Of(exception.Code, exception.Message);
        if (exception.Params.Count > 0) problem.Extensions["params"] = exception.Params;
        if (exception.Fields.Count > 0) problem.Extensions["fields"] = exception.Fields;
        if (exception.Log is not null) problem.Extensions["log"] = exception.Log;
        return problem;
    }

    /// <summary>
    /// Fills in what a problem written by the framework lacks: a 400 of a request it could not
    /// read is `bad_request`, anything else without a code is `internal`. Every problem gets the
    /// trace id, which «Подробности» show and the log carries.
    /// </summary>
    public static void Complete(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        if (!problem.Extensions.ContainsKey("code"))
        {
            var code = problem.Status == StatusCodes.Status400BadRequest ? ErrorCode.BadRequest : ErrorCode.Internal;
            var complete = Of(code, problem.Detail);
            problem.Type = complete.Type;
            problem.Title = complete.Title;
            problem.Status = complete.Status;
            problem.Extensions["code"] = code;
            context.HttpContext.Response.StatusCode = complete.Status!.Value;
        }
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    }
}
