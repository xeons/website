using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using XeonProductions.Domain.Entities;

namespace XeonProductions.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<MediaItem> Media => Set<MediaItem>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Widget> Widgets => Set<Widget>();
    public DbSet<WidgetLink> WidgetLinks => Set<WidgetLink>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<Redirect> Redirects => Set<Redirect>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Identity tables keep their default shape but get readable snake_case names.
        b.Entity<ApplicationUser>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.DisplayName).HasMaxLength(120);
            e.Property(x => x.Slug).HasMaxLength(160);
            e.Property(x => x.AvatarUrl).HasMaxLength(500);
            e.Property(x => x.WebsiteUrl).HasMaxLength(500);
            e.HasIndex(x => x.Slug).IsUnique();
        });

        b.Entity<Post>(e =>
        {
            e.ToTable("posts");
            e.Property(x => x.Slug).HasMaxLength(200).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Excerpt).HasMaxLength(1000);
            e.Property(x => x.SeoTitle).HasMaxLength(300);
            e.Property(x => x.SeoDescription).HasMaxLength(500);
            e.Property(x => x.CanonicalUrl).HasMaxLength(500);
            e.Property(x => x.SocialImageUrl).HasMaxLength(500);

            e.HasIndex(x => x.Slug).IsUnique();
            // Drives the blog index, the archives and the feeds.
            e.HasIndex(x => new { x.Status, x.PublishedAt });

            e.HasOne(x => x.Author)
                .WithMany(u => u.Posts)
                .HasForeignKey(x => x.AuthorId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.FeaturedImage)
                .WithMany()
                .HasForeignKey(x => x.FeaturedImageId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasMany(x => x.Categories)
                .WithMany(c => c.Posts)
                .UsingEntity(j => j.ToTable("post_categories"));

            e.HasMany(x => x.Tags)
                .WithMany(t => t.Posts)
                .UsingEntity(j => j.ToTable("post_tags"));
        });

        b.Entity<Page>(e =>
        {
            e.ToTable("pages");
            e.Property(x => x.Slug).HasMaxLength(200).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.SeoTitle).HasMaxLength(300);
            e.Property(x => x.SeoDescription).HasMaxLength(500);
            e.Property(x => x.CanonicalUrl).HasMaxLength(500);
            e.Property(x => x.SocialImageUrl).HasMaxLength(500);

            // Slugs are unique per parent, so /snippets/foo and /tutorials/foo can coexist.
            // Nulls must compare equal here: without that, Postgres treats every top-level
            // page as distinct and the constraint would not apply to them at all.
            e.HasIndex(x => new { x.ParentId, x.Slug })
                .IsUnique()
                .AreNullsDistinct(false);

            e.HasOne(x => x.Parent)
                .WithMany(p => p.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.FeaturedImage)
                .WithMany()
                .HasForeignKey(x => x.FeaturedImageId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.Author)
                .WithMany()
                .HasForeignKey(x => x.AuthorId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Category>(e =>
        {
            e.ToTable("categories");
            e.Property(x => x.Slug).HasMaxLength(200).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasOne(x => x.Parent)
                .WithMany(c => c.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Tag>(e =>
        {
            e.ToTable("tags");
            e.Property(x => x.Slug).HasMaxLength(200).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Slug).IsUnique();
        });

        b.Entity<MediaItem>(e =>
        {
            e.ToTable("media");
            e.Property(x => x.FileName).HasMaxLength(300).IsRequired();
            e.Property(x => x.RelativePath).HasMaxLength(500).IsRequired();
            e.Property(x => x.ThumbnailPath).HasMaxLength(500);
            e.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
            e.Property(x => x.AltText).HasMaxLength(500);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.SourceUrl).HasMaxLength(1000);
            e.HasIndex(x => x.RelativePath).IsUnique();
            e.HasIndex(x => x.SourceUrl);
            e.HasOne(x => x.UploadedBy)
                .WithMany()
                .HasForeignKey(x => x.UploadedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Menu>(e =>
        {
            e.ToTable("menus");
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasIndex(x => x.Location).IsUnique();
        });

        b.Entity<MenuItem>(e =>
        {
            e.ToTable("menu_items");
            e.Property(x => x.Label).HasMaxLength(200).IsRequired();
            e.Property(x => x.Url).HasMaxLength(1000).IsRequired();
            e.Property(x => x.CssClass).HasMaxLength(200);
            e.HasOne(x => x.Menu)
                .WithMany(m => m.Items)
                .HasForeignKey(x => x.MenuId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Parent)
                .WithMany(p => p.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Widget>(e =>
        {
            e.ToTable("widgets");
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.FeedUrl).HasMaxLength(1000);
            e.HasIndex(x => new { x.Area, x.SortOrder });
        });

        b.Entity<WidgetLink>(e =>
        {
            e.ToTable("widget_links");
            e.Property(x => x.Label).HasMaxLength(200).IsRequired();
            e.Property(x => x.Url).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasOne(x => x.Widget)
                .WithMany(w => w.Links)
                .HasForeignKey(x => x.WidgetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Comment>(e =>
        {
            e.ToTable("comments");
            e.Property(x => x.AuthorName).HasMaxLength(150).IsRequired();
            e.Property(x => x.AuthorEmail).HasMaxLength(300).IsRequired();
            e.Property(x => x.AuthorUrl).HasMaxLength(500);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(500);
            e.HasIndex(x => new { x.PostId, x.Status });
            e.HasOne(x => x.Post)
                .WithMany(p => p.Comments)
                .HasForeignKey(x => x.PostId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Parent)
                .WithMany(c => c.Replies)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ContactMessage>(e =>
        {
            e.ToTable("contact_messages");
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(300).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(300);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(500);
            e.HasIndex(x => new { x.IsArchived, x.CreatedAt });
        });

        b.Entity<Redirect>(e =>
        {
            e.ToTable("redirects");
            e.Property(x => x.FromPath).HasMaxLength(500).IsRequired();
            e.Property(x => x.ToUrl).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => x.FromPath).IsUnique();
        });

        b.Entity<SiteSetting>(e =>
        {
            e.ToTable("site_settings");
            e.Property(x => x.Key).HasMaxLength(150).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
        });
    }
}
