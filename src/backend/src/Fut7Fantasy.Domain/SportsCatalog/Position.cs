namespace Fut7Fantasy.Domain.SportsCatalog;

/// <summary>
/// Posição cadastrada de um atleta. É a posição que vale no fantasy, mesmo quando o
/// atleta atua em outra função na partida (01 §7).
/// </summary>
public enum Position
{
    Goalkeeper = 1,
    Defender = 2,
    Midfielder = 3,
    Forward = 4,
}
