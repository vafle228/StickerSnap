using Microsoft.AspNetCore.Http.HttpResults;
using StickerSnap.Api.Contracts;
using StickerSnap.Application.Settings;

namespace StickerSnap.Api.Endpoints;

/// <summary>/api/v1/settings of group app.</summary>
public static class SettingsEndpoints
{
    public static RouteGroupBuilder MapSettings(this RouteGroupBuilder api)
    {
        api.MapGet("/settings", GetSettings).WithName("getSettings").WithTags("app");
        api.MapPatch("/settings", PatchSettings).WithName("patchSettings").WithTags("app");
        return api;
    }

    private static async Task<Ok<Settings>> GetSettings(SettingsService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(Settings.From(await service.GetAsync(cancellationToken)));

    private static async Task<Ok<Settings>> PatchSettings(
        SettingsPatch patch, SettingsService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(Settings.From(await service.UpdateAsync(patch.ToChange(), cancellationToken)));
}
