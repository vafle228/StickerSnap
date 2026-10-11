using Microsoft.AspNetCore.Http.HttpResults;
using StickerSnap.Application.App;
using AppInfo = StickerSnap.Api.Contracts.AppInfo;

namespace StickerSnap.Api.Endpoints;

/// <summary>Group app of the contract.</summary>
public static class AppEndpoints
{
    public static RouteGroupBuilder MapApp(this RouteGroupBuilder api)
    {
        api.MapGet("/app", GetApp).WithName("getApp").WithTags("app");
        return api;
    }

    private static async Task<Ok<AppInfo>> GetApp(AppInfoService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(AppInfo.From(await service.GetAsync(cancellationToken)));
}
