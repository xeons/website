using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using XeonProductions.Infrastructure.Data;
using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Web.Endpoints;

public static class MediaEndpoints
{
    /// <summary>Matches the editor's own cap, so a rejection is explained rather than truncated.</summary>
    private const long MaxUploadBytes = 25 * 1024 * 1024;

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/api/media")
            .RequireAuthorization("CanEditContent");

        // The editor posts here when an image is dropped or pasted into a post.
        //
        // The editor cannot attach an antiforgery token to its own upload, so this endpoint
        // opts out of that check. It is not open: the auth cookie is SameSite=Lax, which
        // browsers refuse to send on a cross-site POST, so another origin cannot drive this
        // even with a logged-in admin visiting it.
        group.MapPost("/upload", UploadAsync)
            .DisableAntiforgery()
            .WithName("EditorImageUpload");

        return app;
    }

    private static async Task<IResult> UploadAsync(
        HttpContext http,
        IMediaService media,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("MediaUpload");

        if (!http.Request.HasFormContentType)
        {
            return TypedResults.BadRequest(new { message = "Expected a multipart form upload." });
        }

        var form = await http.Request.ReadFormAsync(ct);

        // TinyMCE names the part "file"; accept whatever arrived rather than insisting.
        var file = form.Files["file"] ?? form.Files.FirstOrDefault();

        if (file is null || file.Length == 0)
        {
            return TypedResults.BadRequest(new { message = "No file was received." });
        }

        if (file.Length > MaxUploadBytes)
        {
            return TypedResults.BadRequest(
                new { message = $"That file is larger than {MaxUploadBytes / 1024 / 1024} MB." });
        }

        if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.BadRequest(new { message = "Only images can be uploaded here." });
        }

        var userId = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        await using var stream = file.OpenReadStream();
        var result = await media.SaveAsync(stream, file.FileName, file.ContentType, userId, null, ct);

        if (!result.Success || result.Item is null)
        {
            logger.LogWarning("Editor upload of {Name} was rejected: {Error}", file.FileName, result.Error);
            return TypedResults.BadRequest(new { message = result.Error ?? "The upload failed." });
        }

        logger.LogInformation("Editor upload stored as {Path}.", result.Item.RelativePath);

        // TinyMCE expects exactly this shape: { "location": "<url>" }.
        return TypedResults.Ok(new { location = media.PublicUrl(result.Item.RelativePath) });
    }
}
