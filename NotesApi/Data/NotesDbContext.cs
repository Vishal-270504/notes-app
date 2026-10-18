using Microsoft.EntityFrameworkCore;
using NotesApi.Models;

namespace NotesApi.Data;

public class NotesDbContext : DbContext
{
    public NotesDbContext(DbContextOptions<NotesDbContext> options) : base(options) { }

    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Note>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            // Composite index on UserId and CreatedAtDescending.
            // Why it helps:
            // - Listing a user's notes is the hot path: WHERE UserId = ? ORDER BY CreatedAt DESC.
            // - Without an index, SQLite performs a full table scan and sorts in memory (O(n log n)).
            // - With a composite index (UserId, CreatedAt), SQLite can seek directly to the user's rows
            //   and read them in CreatedAt order without a separate sort (O(log n) seek + O(k) scan).
            // - This makes pagination fast even with millions of rows.
            //
            // Cost on writes:
            // - Each INSERT/UPDATE/DELETE must also update the index B-tree.
            // - This adds a small CPU and I/O overhead per write and increases storage size.
            // - For a read-heavy workload like notes listing, the tradeoff is strongly positive.
            // - For write-heavy workloads, consider write amplification.
            entity.HasIndex(e => new { e.UserId, e.CreatedAt })
                  .HasDatabaseName("IX_Notes_UserId_CreatedAt");
        });
    }
}
