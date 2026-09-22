using Fut7Fantasy.Domain.Notifications;

namespace Fut7Fantasy.UnitTests.Notifications;

public sealed class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirstRevisionAnnouncesTheResultAndTheNextOnesAnnounceTheCorrection()
    {
        Assert.Equal(NotificationKind.RoundPublished, ForRound(revision: 1).Kind);
        Assert.Equal(NotificationKind.RoundCorrected, ForRound(revision: 2).Kind);
        Assert.Equal(NotificationKind.RoundCorrected, ForRound(revision: 7).Kind);
    }

    [Fact]
    public void NotificationIsBornUnread()
    {
        var notification = ForRound();

        Assert.False(notification.IsRead);
        Assert.Null(notification.ReadAt);
        Assert.Equal(Now, notification.CreatedAt);
    }

    [Fact]
    public void ReadingTwiceKeepsTheFirstInstant()
    {
        var notification = ForRound();

        notification.MarkRead(Now);
        notification.MarkRead(Now.AddDays(1));

        Assert.True(notification.IsRead);
        Assert.Equal(Now, notification.ReadAt);
    }

    [Fact]
    public void NotificationWithoutSubjectIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => Notification.ForRound(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Notification.ForRound(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Now));
    }

    private static Notification ForRound(int revision = 1) =>
        Notification.ForRound(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), revision, Now);
}
