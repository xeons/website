using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XeonProductions.Infrastructure;
using XeonProductions.Infrastructure.Data;
using XeonProductions.Infrastructure.Services;

// One-shot migration tool. Reads the public WordPress REST API and writes into this schema.
//
//   dotnet run --project tools/XeonProductions.WpImporter -- --dry-run
//   dotnet run --project tools/XeonProductions.WpImporter -- --source https://xeonproductions.com
//
// The connection string comes from ConnectionStrings__Default, exactly as the web app reads it.

var flags = args
    .Where(a => a.StartsWith("--"))
    .Select(a => a.TrimStart('-').ToLowerInvariant())
    .ToHashSet();

string? ValueOf(string name)
{
    var index = Array.FindIndex(args, a =>
        string.Equals(a, $"--{name}", StringComparison.OrdinalIgnoreCase));

    return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--")
        ? args[index + 1]
        : null;
}

if (flags.Contains("help") || flags.Contains("h"))
{
    Console.WriteLine("""
        WordPress importer

          --source <url>     WordPress site to read from (default https://xeonproductions.com)
          --dry-run          Report what would be imported, write nothing
          --overwrite        Replace content that already exists locally
          --skip-media       Do not download attachments
          --help             Show this text
        """);
    return 0;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.AddXeonInfrastructure(builder.Configuration);

builder.Services.AddHttpClient<WordPressImporter>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(3);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("XeonProductions-Importer/1.0");
});

using var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Importer");

var options = new ImportOptions
{
    SourceUrl = ValueOf("source") ?? "https://xeonproductions.com",
    DryRun = flags.Contains("dry-run"),
    Overwrite = flags.Contains("overwrite"),
    ImportMedia = !flags.Contains("skip-media")
};

using var scope = host.Services.CreateScope();

try
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // The importer writes into the live schema, so make sure it exists first.
    await db.Database.MigrateAsync();

    var importer = scope.ServiceProvider.GetRequiredService<WordPressImporter>();
    var report = await importer.RunAsync(options);

    Console.WriteLine();
    Console.WriteLine(options.DryRun ? "Dry run complete." : "Import complete.");
    Console.WriteLine(report.ToString());

    if (report.Warnings.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Warnings:");

        foreach (var warning in report.Warnings)
        {
            Console.WriteLine($"  {warning}");
        }
    }

    return 0;
}
catch (Exception ex)
{
    logger.LogError(ex, "Import failed.");
    Console.Error.WriteLine($"Import failed: {ex.Message}");
    return 1;
}
