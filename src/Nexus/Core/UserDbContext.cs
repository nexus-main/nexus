using Microsoft.EntityFrameworkCore;

namespace Nexus.Core;

internal class UserDbContext : DbContext
{
    public UserDbContext(DbContextOptions<UserDbContext> options)
        : base(options)
    {
        //
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<NexusClaim>()
            .HasOne(claim => claim.Owner)
            .WithMany(user => user.Claims)
            .IsRequired();
    }

    public DbSet<NexusUser> Users { get; set; } = default!;

    public DbSet<NexusClaim> Claims { get; set; } = default!;
}
