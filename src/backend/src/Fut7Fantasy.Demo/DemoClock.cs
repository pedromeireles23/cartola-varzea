namespace Fut7Fantasy.Demo;

/// <summary>
/// O relógio do seed. Os serviços da aplicação decidem tudo pelo <see cref="TimeProvider"/>
/// — se o mercado está aberto, se a partida já começou, se a correção ainda cabe —, e o
/// seed escreve o passado avançando este relógio passo a passo, em vez de gravar datas
/// à mão. Só anda para frente: voltar no tempo produziria uma história impossível.
/// </summary>
internal sealed class DemoClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void AdvanceTo(DateTimeOffset instant)
    {
        if (instant < _now)
        {
            throw new InvalidOperationException(
                $"O relógio do seed não volta: está em {_now:O} e pediram {instant:O}.");
        }

        _now = instant;
    }
}
