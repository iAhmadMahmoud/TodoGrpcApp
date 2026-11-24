

using Microsoft.EntityFrameworkCore;
using TodoServer.Models;

namespace TodoServer.Data;

public class TodoDbContext : DbContext
{
    public TodoDbContext(DbContextOptions<TodoDbContext> options) : base(options) {}

    public DbSet<TodoEntity> Todos => Set<TodoEntity>();
    public DbSet<TodoAuditLog> TodoAuditLogs => Set<TodoAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TodoEntity>(entity =>
        {
            entity.ToTable("Todos");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Title)
                  .IsRequired()
                  .HasMaxLength(200);
            entity.Property(t => t.IsCompleted)
                  .HasDefaultValue(false);
            entity.Property(t => t.IsDeleted)
                  .HasDefaultValue(false);

            entity.HasQueryFilter(t => !t.IsDeleted);
        });

        modelBuilder.Entity<TodoAuditLog>(entity =>
        {
            entity.ToTable("TodoAuditLogs");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Action)
                  .IsRequired()
                  .HasMaxLength(32);
            entity.Property(a => a.TitleSnapshot)
                  .HasMaxLength(200);
            entity.HasIndex(a => a.TodoId);
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var pendingAudits = new List<(TodoEntity Entity, string Action)>();

        foreach (var entry in ChangeTracker.Entries<TodoEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = now;
                entry.Entity.UpdatedAtUtc = null;
                entry.Entity.IsDeleted = false;
                entry.Entity.DeletedAtUtc = null;

                pendingAudits.Add((entry.Entity, nameof(TodoAuditAction.Created)));
            }
            else if (entry.State == EntityState.Deleted)
            {
                // Convert physical delete into soft delete
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
                entry.Entity.DeletedAtUtc = now;
                entry.Entity.UpdatedAtUtc = now;

                pendingAudits.Add((entry.Entity, nameof(TodoAuditAction.SoftDeleted)));
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;

                if (entry.Entity.IsDeleted)
                {
                    entry.Entity.DeletedAtUtc ??= now;
                    pendingAudits.Add((entry.Entity, nameof(TodoAuditAction.SoftDeleted)));
                }
                else
                {
                    pendingAudits.Add((entry.Entity, nameof(TodoAuditAction.Updated)));
                }
            }
        }

        var affected = await base.SaveChangesAsync(cancellationToken);

        if (pendingAudits.Count > 0)
        {
            var logs = pendingAudits.Select(item => new TodoAuditLog
            {
                TodoId = item.Entity.Id,
                Action = item.Action,
                TitleSnapshot = item.Entity.Title,
                IsCompletedSnapshot = item.Entity.IsCompleted,
                OccurredAtUtc = now
            });

            TodoAuditLogs.AddRange(logs);
            await base.SaveChangesAsync(cancellationToken);
        }

        return affected;
    }
}
