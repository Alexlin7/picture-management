using Microsoft.EntityFrameworkCore;
using Pm.Data.Entities;

namespace Pm.Data;

public class PmDbContext(DbContextOptions<PmDbContext> options) : DbContext(options)
{
    public DbSet<LibraryRoot> LibraryRoots => Set<LibraryRoot>();
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<PhotoLocation> PhotoLocations => Set<PhotoLocation>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TagRelation> TagRelations => Set<TagRelation>();
    public DbSet<PhotoTag> PhotoTags => Set<PhotoTag>();
    public DbSet<PathTagRule> PathTagRules => Set<PathTagRule>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<TaggingJob> TaggingJobs => Set<TaggingJob>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<LibraryRoot>(e =>
        {
            e.ToTable("library_root");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
            e.Property(x => x.AbsPath).HasColumnName("abs_path").HasMaxLength(1024).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.AbsPath).IsUnique();
        });

        b.Entity<Photo>(e =>
        {
            e.ToTable("photo");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FileHash).HasColumnName("file_hash").HasMaxLength(64).IsRequired();
            e.Property(x => x.FileSize).HasColumnName("file_size");
            e.Property(x => x.Width).HasColumnName("width");
            e.Property(x => x.Height).HasColumnName("height");
            e.Property(x => x.Mime).HasColumnName("mime").HasMaxLength(64);
            e.Property(x => x.TakenAt).HasColumnName("taken_at");
            e.Property(x => x.CameraModel).HasColumnName("camera_model").HasMaxLength(128);
            e.Property(x => x.GpsLat).HasColumnName("gps_lat");
            e.Property(x => x.GpsLon).HasColumnName("gps_lon");
            e.Property(x => x.Exif).HasColumnName("exif");
            e.Property(x => x.ImportedAt).HasColumnName("imported_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.FileHash).IsUnique();
            e.HasIndex(x => x.TakenAt).HasDatabaseName("ix_photo_taken");
        });

        b.Entity<PhotoLocation>(e =>
        {
            e.ToTable("photo_location");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PhotoId).HasColumnName("photo_id");
            e.Property(x => x.LibraryRootId).HasColumnName("library_root_id");
            e.Property(x => x.RelPath).HasColumnName("rel_path").HasMaxLength(1024).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).HasDefaultValue("present");
            e.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasOne(x => x.Photo).WithMany(p => p.Locations).HasForeignKey(x => x.PhotoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.LibraryRoot).WithMany(r => r.Locations).HasForeignKey(x => x.LibraryRootId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.LibraryRootId, x.RelPath }).IsUnique();
            e.HasIndex(x => x.PhotoId).HasDatabaseName("ix_loc_photo");
        });

        b.Entity<Tag>(e =>
        {
            e.ToTable("tag");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
            e.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(32).HasDefaultValue("manual");
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<TagRelation>(e =>
        {
            e.ToTable("tag_relation", t =>
                t.HasCheckConstraint("ck_tagrel_no_self", "parent_tag_id <> child_tag_id"));
            e.HasKey(x => new { x.ParentTagId, x.ChildTagId });
            e.Property(x => x.ParentTagId).HasColumnName("parent_tag_id");
            e.Property(x => x.ChildTagId).HasColumnName("child_tag_id");
            e.HasOne<Tag>().WithMany().HasForeignKey(x => x.ParentTagId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Tag>().WithMany().HasForeignKey(x => x.ChildTagId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ChildTagId).HasDatabaseName("ix_tagrel_child");
        });

        b.Entity<PhotoTag>(e =>
        {
            e.ToTable("photo_tag");
            e.HasKey(x => new { x.PhotoId, x.TagId });
            e.Property(x => x.PhotoId).HasColumnName("photo_id");
            e.Property(x => x.TagId).HasColumnName("tag_id");
            e.Property(x => x.Source).HasColumnName("source").HasMaxLength(16).IsRequired();
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.HasOne<Photo>().WithMany(p => p.Tags).HasForeignKey(x => x.PhotoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TagId, x.PhotoId }).HasDatabaseName("ix_phototag_tag");
        });

        b.Entity<PathTagRule>(e =>
        {
            e.ToTable("path_tag_rule");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.LibraryRootId).HasColumnName("library_root_id");
            e.Property(x => x.Segment).HasColumnName("segment").HasMaxLength(256).IsRequired();
            e.Property(x => x.Action).HasColumnName("action").HasMaxLength(16).IsRequired();
            e.Property(x => x.TagId).HasColumnName("tag_id");
            e.HasOne<LibraryRoot>().WithMany().HasForeignKey(x => x.LibraryRootId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId);
            e.HasIndex(x => new { x.LibraryRootId, x.Segment }).IsUnique();
        });

        b.Entity<SavedSearch>(e =>
        {
            e.ToTable("saved_search");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
            e.Property(x => x.QueryJson).HasColumnName("query_json").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        b.Entity<TaggingJob>(e =>
        {
            e.ToTable("tagging_job");
            e.HasKey(x => x.PhotoId);
            e.Property(x => x.PhotoId).HasColumnName("photo_id").ValueGeneratedNever();
            e.Property(x => x.State).HasColumnName("state").HasMaxLength(16).HasDefaultValue("pending");
            e.Property(x => x.Attempts).HasColumnName("attempts").HasDefaultValue(0);
            e.Property(x => x.EnqueuedAt).HasColumnName("enqueued_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne<Photo>().WithMany().HasForeignKey(x => x.PhotoId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.State).HasDatabaseName("ix_job_state").HasFilter("state IN ('pending','error')");
        });
    }
}
