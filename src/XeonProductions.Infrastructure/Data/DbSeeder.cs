using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XeonProductions.Domain.Entities;
using XeonProductions.Domain.Enums;
using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Infrastructure.Data;

/// <summary>
/// Brings a fresh database up to a usable state: schema, roles, an owner account, the default
/// menus and the sidebar widgets. Safe to run on every start; every step is idempotent.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<AppDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");
        var config = sp.GetRequiredService<IConfiguration>();

        await db.Database.MigrateAsync(ct);

        await SeedRolesAsync(sp, ct);
        var owner = await SeedOwnerAsync(sp, config, logger, ct);
        await SeedSettingsAsync(sp, ct);
        await SeedTaxonomyAsync(db, ct);
        await SeedMenusAsync(db, ct);
        await SeedWidgetsAsync(db, ct);
        await SeedHomePageAsync(db, owner, ct);

        logger.LogInformation("Database seed complete.");
    }

    private static async Task SeedRolesAsync(IServiceProvider sp, CancellationToken ct)
    {
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    private static async Task<ApplicationUser?> SeedOwnerAsync(
        IServiceProvider sp, IConfiguration config, ILogger logger, CancellationToken ct)
    {
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        var email = config["Seed:AdminEmail"];
        var password = config["Seed:AdminPassword"];

        // Never invent a password: without one configured, no account is created at all.
        if (string.IsNullOrWhiteSpace(email))
        {
            return await userManager.Users.FirstOrDefaultAsync(ct);
        }

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null) return existing;

        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Seed:AdminEmail is set but Seed:AdminPassword is not. No admin account was created.");
            return await userManager.Users.FirstOrDefaultAsync(ct);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = config["Seed:AdminDisplayName"] ?? "Brandon",
            Slug = SlugHelper.Slugify(config["Seed:AdminDisplayName"] ?? "brandon")
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogError("Could not create the admin account: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return null;
        }

        await userManager.AddToRoleAsync(user, Roles.Administrator);
        logger.LogInformation("Created administrator {Email}.", email);
        return user;
    }

    private static async Task SeedSettingsAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        if (await db.SiteSettings.AnyAsync(ct)) return;

        var settingsService = sp.GetRequiredService<ISiteSettingsService>();
        await settingsService.SaveAsync(new SiteSettings
        {
            SiteTitle = "Xeon Productions",
            Tagline = "The future is uncertain, but the end is always near.",
            FooterText = "Built with ASP.NET Core and Blazor.",
            GitHubUsername = "xeonproductions"
        }, ct);
    }

    private static async Task SeedTaxonomyAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Categories.AnyAsync(ct)) return;

        db.Categories.AddRange(
            new Category { Name = "Blog", Slug = "blog", SortOrder = 1 },
            new Category { Name = "Site News", Slug = "site-news", SortOrder = 2 },
            new Category { Name = "Tutorials", Slug = "tutorials", SortOrder = 3 },
            new Category { Name = "Reviews", Slug = "reviews", SortOrder = 4 });

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedMenusAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Menus.AnyAsync(ct)) return;

        var primary = new Menu
        {
            Name = "Primary Navigation",
            Location = MenuLocation.Primary,
            Items =
            [
                new MenuItem { Label = "Home", Url = "/", SortOrder = 1 },
                new MenuItem { Label = "About", Url = "/about", SortOrder = 2 },
                new MenuItem { Label = "Reviews", Url = "/reviews", SortOrder = 3 },
                new MenuItem { Label = "Tutorials", Url = "/tutorials", SortOrder = 4 },
                new MenuItem { Label = "Snippets", Url = "/snippets", SortOrder = 5 },
                new MenuItem { Label = "Sourcecode", Url = "/sourcecode-archives", SortOrder = 6 },
                new MenuItem
                {
                    Label = "Code Repo",
                    Url = "https://garbagefile.io",
                    SortOrder = 7,
                    OpenInNewTab = true
                },
                new MenuItem { Label = "Contact", Url = "/contact", SortOrder = 8 }
            ]
        };

        var footer = new Menu
        {
            Name = "Footer Navigation",
            Location = MenuLocation.Footer,
            Items =
            [
                new MenuItem { Label = "About", Url = "/about", SortOrder = 1 },
                new MenuItem { Label = "Contact", Url = "/contact", SortOrder = 2 },
                new MenuItem { Label = "RSS", Url = "/feed.xml", SortOrder = 3 },
                new MenuItem { Label = "Sitemap", Url = "/sitemap.xml", SortOrder = 4 }
            ]
        };

        db.Menus.AddRange(primary, footer);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedWidgetsAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Widgets.AnyAsync(ct)) return;

        db.Widgets.AddRange(
            new Widget
            {
                Title = "Search",
                Type = WidgetType.Search,
                Area = WidgetArea.Sidebar,
                SortOrder = 1
            },
            new Widget
            {
                Title = "Recent Posts",
                Type = WidgetType.RecentPosts,
                Area = WidgetArea.Sidebar,
                SortOrder = 2,
                MaxItems = 5
            },
            new Widget
            {
                Title = "Free Development Tools",
                Type = WidgetType.LinkList,
                Area = WidgetArea.Sidebar,
                SortOrder = 3,
                Links =
                [
                    new WidgetLink { Label = "Visual Studio Code", Url = "https://code.visualstudio.com", SortOrder = 1 },
                    new WidgetLink { Label = "JetBrains Toolbox", Url = "https://www.jetbrains.com", SortOrder = 2 },
                    new WidgetLink { Label = "Eclipse IDE", Url = "https://www.eclipse.org", SortOrder = 3 },
                    new WidgetLink { Label = "Visual Studio Community", Url = "https://visualstudio.microsoft.com", SortOrder = 4 }
                ]
            },
            new Widget
            {
                Title = "Useful Sites",
                Type = WidgetType.LinkList,
                Area = WidgetArea.Sidebar,
                SortOrder = 4,
                Links =
                [
                    new WidgetLink { Label = "GitHub", Url = "https://github.com", SortOrder = 1 },
                    new WidgetLink { Label = "Stack Overflow", Url = "https://stackoverflow.com", SortOrder = 2 },
                    new WidgetLink { Label = "OWASP", Url = "https://owasp.org", SortOrder = 3 },
                    new WidgetLink { Label = "MDN Web Docs", Url = "https://developer.mozilla.org", SortOrder = 4 }
                ]
            },
            new Widget
            {
                Title = "Categories",
                Type = WidgetType.Categories,
                Area = WidgetArea.Sidebar,
                SortOrder = 5,
                MaxItems = 10
            },
            new Widget
            {
                Title = "Affiliates",
                Type = WidgetType.LinkList,
                Area = WidgetArea.Sidebar,
                SortOrder = 6
            });

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedHomePageAsync(AppDbContext db, ApplicationUser? owner, CancellationToken ct)
    {
        if (await db.Pages.AnyAsync(ct)) return;

        db.Pages.AddRange(
            new Page
            {
                Title = "About",
                Slug = "about",
                Status = ContentStatus.Published,
                PublishedAt = DateTimeOffset.UtcNow,
                AuthorId = owner?.Id,
                ContentHtml = "<p>Replace this from the admin, or run the WordPress importer.</p>"
            },
            new Page
            {
                Title = "Contact",
                Slug = "contact",
                Status = ContentStatus.Published,
                PublishedAt = DateTimeOffset.UtcNow,
                AuthorId = owner?.Id,
                Template = PageTemplate.Narrow,
                // The contact page renders its form from the route, not from content.
                ContentHtml = "<p>Questions, corrections or work enquiries are all welcome.</p>"
            });

        await db.SaveChangesAsync(ct);
    }
}
