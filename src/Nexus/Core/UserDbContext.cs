using Microsoft.EntityFrameworkCore;

namespace Nexus.Core
{
    internal class UserDbContext : DbContext
    {
        public UserDbContext(DbContextOptions<UserDbContext> options)
            : base(options)
        {
            //
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // this is required, otherwise when deleting claims or refresh tokens, they just get their OwnerId = null
            // https://learn.microsoft.com/en-us/ef/core/modeling/relationships?tabs=fluent-api%2Cfluent-api-simple-key%2Csimple-key#required-and-optional-relationships
            modelBuilder
                .Entity<RefreshToken>()
                .HasOne(token => token.Owner)
                .WithMany(user => user.RefreshTokens)
                .IsRequired();

            modelBuilder
                .Entity<NexusClaim>()
                .HasOne(claim => claim.Owner)
                .WithMany(user => user.Claims)
                .IsRequired();
        }

        public DbSet<NexusUser> Users { get; set; } = default!;

        public DbSet<RefreshToken> RefreshTokens { get; set; } = default!;

        public DbSet<NexusClaim> Claims { get; set; } = default!;
    }
}
