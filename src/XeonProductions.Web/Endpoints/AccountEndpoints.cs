using Microsoft.AspNetCore.Identity;
using XeonProductions.Domain.Entities;

namespace XeonProductions.Web.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin");

        // Sign-out has to be a POST so a stray link or prefetch cannot end the session.
        group.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect("/admin/login");
        }).RequireAuthorization();

        return app;
    }
}
