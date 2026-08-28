using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using XeonProductions.Domain.Entities;
using XeonProductions.Infrastructure.Data;

namespace XeonProductions.Infrastructure.Services;

/// <summary>
/// Turns an address and user agent into an identifier that counts visitors without
/// identifying them.
///
/// The address is hashed with a server secret and the current date and is never stored. The
/// date in the input is what makes the result useless for tracking: the same visitor hashes
/// to a different value tomorrow, so nothing can be followed across days, and there is no
/// value to correlate against if the table is ever exposed.
///
/// The secret is generated once and kept in site_settings so restarts do not split a day's
/// visitors into two.
/// </summary>
public class VisitorHasher(
    IDbContextFactory<AppDbContext> dbFactory,
    IMemoryCache cache) : IVisitorHasher
{
    private const string SecretKey = "StatsVisitorSecret";
    private const string CacheKey = "stats-visitor-secret";

    public async Task<string> VisitorAsync(
        IPAddress? address, string? userAgent, DateTimeOffset when, CancellationToken ct = default)
    {
        var secret = await SecretAsync(ct);

        var ip = address is null
            ? "unknown"
            : (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();

        var material = $"{ip}\n{userAgent}\n{when.UtcDateTime:yyyy-MM-dd}";

        var digest = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(material));

        return Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
    }

    public string Session(string visitorHash, DateTimeOffset when, TimeSpan window)
    {
        var minutes = Math.Max(1, (long)window.TotalMinutes);
        var bucket = when.ToUnixTimeSeconds() / 60 / minutes;

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{visitorHash}\n{bucket}"));

        return Convert.ToHexString(digest.AsSpan(0, 12)).ToLowerInvariant();
    }

    private async Task<byte[]> SecretAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out byte[]? cached) && cached is not null) return cached;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var row = await db.SiteSettings.FirstOrDefaultAsync(s => s.Key == SecretKey, ct);

        if (row?.Value is null)
        {
            var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

            if (row is null)
            {
                db.SiteSettings.Add(new SiteSetting { Key = SecretKey, Value = generated });
            }
            else
            {
                row.Value = generated;
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Another instance inserted it first; read theirs rather than fight over it.
                await using var retry = await dbFactory.CreateDbContextAsync(ct);
                generated = (await retry.SiteSettings.FirstAsync(s => s.Key == SecretKey, ct)).Value!;
            }

            row = new SiteSetting { Key = SecretKey, Value = generated };
        }

        var secret = Convert.FromBase64String(row.Value!);

        cache.Set(CacheKey, secret, TimeSpan.FromHours(12));
        return secret;
    }
}
