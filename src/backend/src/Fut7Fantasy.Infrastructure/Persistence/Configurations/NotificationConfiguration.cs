using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Notifications;
using Fut7Fantasy.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Notifications", Fut7FantasyDbContext.NotificationsSchema);
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Id).ValueGeneratedNever();
        builder.Property(notification => notification.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Ignore(notification => notification.IsRead);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(notification => notification.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Competition>().WithMany().HasForeignKey(notification => notification.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Round>().WithMany().HasForeignKey(notification => notification.RoundId)
            .OnDelete(DeleteBehavior.NoAction);

        // Republicar a mesma revisão não duplica o aviso, do mesmo jeito que não grava
        // uma segunda apuração.
        builder.HasIndex(notification => new { notification.UserId, notification.RoundId, notification.Revision })
            .IsUnique()
            .HasFilter("[RoundId] IS NOT NULL");

        // A caixa é sempre lida por conta, da mais recente para a mais antiga.
        builder.HasIndex(notification => new { notification.UserId, notification.CreatedAt });
    }
}
