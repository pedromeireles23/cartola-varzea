using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Identity.Configurations;

/// <summary>Mapeamento de <see cref="UserSession"/>.</summary>
public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("UserSessions", Fut7FantasyDbContext.IdentitySchema);
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();
        builder.Property(session => session.Ticket).IsRequired();
        builder.Property(session => session.ExpiresAt).IsRequired();

        // Excluir a conta leva as sessões junto.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // A limpeza das vencidas filtra pelo prazo.
        builder.HasIndex(session => session.ExpiresAt);
    }
}
