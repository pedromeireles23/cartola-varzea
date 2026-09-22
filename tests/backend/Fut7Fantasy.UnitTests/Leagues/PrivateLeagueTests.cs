using Fut7Fantasy.Domain.Leagues;

namespace Fut7Fantasy.UnitTests.Leagues;

public sealed class PrivateLeagueTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewLeagueAcceptsWhoeverHasTheCode()
    {
        var league = Create();

        Assert.True(league.AcceptsJoin(Now));
        Assert.Equal(LeagueInviteCode.Length, league.InviteCode!.Length);
        Assert.Null(league.InviteExpiresAt);
    }

    [Fact]
    public void RotatingTheCodeStopsTheOldOneWithoutTouchingWhoIsAlreadyIn()
    {
        var league = Create();
        var anterior = league.InviteCode;

        league.RotateInvite(LeagueInviteCode.Generate(), Now.AddDays(1));

        Assert.NotEqual(anterior, league.InviteCode);
        Assert.True(league.AcceptsJoin(Now.AddDays(1)));
        Assert.Equal(Now.AddDays(1), league.UpdatedAt);
    }

    [Fact]
    public void ClosedOrExpiredInviteRefusesNewMembers()
    {
        var comPrazo = Create();
        comPrazo.RotateInvite(LeagueInviteCode.Generate(), Now, expiresAt: Now.AddDays(7));

        Assert.True(comPrazo.AcceptsJoin(Now.AddDays(7).AddTicks(-1)));
        Assert.False(comPrazo.AcceptsJoin(Now.AddDays(7)));

        var fechada = Create();
        fechada.CloseInvite(Now);
        Assert.False(fechada.AcceptsJoin(Now));
        Assert.Null(fechada.InviteCode);
    }

    [Fact]
    public void InviteDeadlineInThePastIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create().RotateInvite(LeagueInviteCode.Generate(), Now, expiresAt: Now));

    [Fact]
    public void LeagueNameFollowsTheDocumentedLimits()
    {
        Assert.Empty(new LeagueDefinition("  Liga da Firma  ").Validate());
        Assert.Equal("Liga da Firma", new LeagueDefinition("  Liga da Firma  ").Normalized().Name);
        Assert.NotEmpty(new LeagueDefinition("ab").Validate());
        Assert.NotEmpty(new LeagueDefinition(new string('a', LeagueDefinition.NameMaxLength + 1)).Validate());
    }

    [Fact]
    public void GeneratedCodeHasNoAmbiguousCharacters()
    {
        // Cem códigos bastam para pegar um alfabeto errado, e o teste continua rápido.
        for (var vez = 0; vez < 100; vez++)
        {
            var code = LeagueInviteCode.Generate();

            Assert.Equal(LeagueInviteCode.Length, code.Length);
            Assert.DoesNotContain(code, letra => "ILOU01".Contains(letra, StringComparison.Ordinal));
            Assert.Equal(code, LeagueInviteCode.Normalize(code));
        }
    }

    [Theory]
    [InlineData("abcd-efgh-jk", "ABCDEFGHJK")]
    [InlineData("  ABCD EFGH JK  ", "ABCDEFGHJK")]
    public void TypedCodeIsCleanedBeforeComparing(string digitado, string esperado) =>
        Assert.Equal(esperado, LeagueInviteCode.Normalize(digitado));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABCDEFGHJ")]
    [InlineData("ABCDEFGHJKL")]
    [InlineData("ABCDEFGHJ0")]
    [InlineData("ABCDEFGHJI")]
    public void WhatCannotBeACodeIsRefusedBeforeTouchingTheDatabase(string? digitado) =>
        Assert.Null(LeagueInviteCode.Normalize(digitado));

    [Fact]
    public void LeagueWithoutSubjectIsRefused()
    {
        Assert.Throws<ArgumentException>(() => PrivateLeague.Create(
            Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), new("Liga"), LeagueInviteCode.Generate(), Now));
        Assert.Throws<ArgumentException>(() => PrivateLeague.Create(
            Guid.CreateVersion7(), Guid.NewGuid(), Guid.NewGuid(), new("ab"), LeagueInviteCode.Generate(), Now));
    }

    private static PrivateLeague Create() => PrivateLeague.Create(
        Guid.CreateVersion7(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        new("Liga da Firma"),
        LeagueInviteCode.Generate(),
        Now);
}
