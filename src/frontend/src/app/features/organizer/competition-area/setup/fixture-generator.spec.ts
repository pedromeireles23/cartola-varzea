import {
  Pair,
  addDays,
  bracketSize,
  groupRounds,
  knockoutDraw,
  knockoutRounds,
  knockoutStageName,
  knockoutStageNames,
  nextRoundNames,
  roundRobin,
  schedule,
  shuffle,
  uniqueName,
} from './fixture-generator';

const times = (quantidade: number): string[] =>
  Array.from({ length: quantidade }, (_, indice) => `Time ${indice + 1}`);

const confronto = ([a, b]: Pair): string => [a, b].sort().join(' x ');

describe('fixture-generator', () => {
  for (const quantidade of [2, 3, 4, 5, 6, 7, 8, 10, 12, 16]) {
    it(`com ${quantidade} times, todos se enfrentam uma vez e ninguém joga duas vezes na rodada`, () => {
      const rodadas = roundRobin(times(quantidade), 1);
      const pares = quantidade % 2 === 0 ? quantidade : quantidade + 1;

      expect(rodadas.length).toBe(pares - 1);
      const jogos = rodadas.flat();
      expect(jogos.length).toBe((quantidade * (quantidade - 1)) / 2);
      expect(new Set(jogos.map(confronto)).size).toBe(jogos.length);

      for (const rodada of rodadas) {
        const naRodada = rodada.flat();
        expect(new Set(naRodada).size).toBe(naRodada.length);
      }

      // Mando equilibrado: no turno, ninguém passa de um jogo de diferença.
      for (const time of times(quantidade)) {
        const emCasa = jogos.filter(([casa]) => casa === time).length;
        const fora = jogos.filter(([, visitante]) => visitante === time).length;
        expect(Math.abs(emCasa - fora)).toBeLessThanOrEqual(1);
      }
    });
  }

  it('com número ímpar, cada time folga exatamente uma rodada', () => {
    const rodadas = roundRobin(times(5), 1);

    for (const time of times(5)) {
      const folgas = rodadas.filter((rodada) => !rodada.flat().includes(time)).length;
      expect(folgas).toBe(1);
    }
  });

  it('o returno repete o turno com o mando invertido', () => {
    const rodadas = roundRobin(times(4), 2);

    expect(rodadas.length).toBe(6);
    expect(rodadas[3]).toEqual(rodadas[0].map(([a, b]) => [b, a]));
    for (const time of times(4)) {
      const jogos = rodadas.flat();
      expect(jogos.filter(([casa]) => casa === time).length).toBe(3);
      expect(jogos.filter(([, visitante]) => visitante === time).length).toBe(3);
    }
  });

  it('menos de dois times não gera jogo', () => {
    expect(roundRobin([], 1)).toEqual([]);
    expect(roundRobin(['Sozinho'], 2)).toEqual([]);
  });

  it('os grupos jogam na mesma rodada, alternando os jogos de cada grupo', () => {
    const rodadas = groupRounds(
      [
        { name: 'Grupo A', teams: ['A1', 'A2', 'A3', 'A4'] },
        { name: 'Grupo B', teams: ['B1', 'B2', 'B3'] },
      ],
      1,
    );

    // Quatro times dão três rodadas; três times também (com uma folga).
    expect(rodadas.length).toBe(3);
    expect(rodadas[0].map((jogo) => jogo.group)).toEqual(['Grupo A', 'Grupo B', 'Grupo A']);
    for (const rodada of rodadas) {
      for (const jogo of rodada) {
        const grupo = jogo.group === 'Grupo A' ? 'A' : 'B';
        expect(jogo.pair.every((time) => time.startsWith(grupo))).toBe(true);
      }
    }
    expect(rodadas.flat().length).toBe(6 + 3);
  });

  it('o mata-mata cruza o primeiro com o último e o mais forte é o mandante', () => {
    const chave = knockoutDraw(times(8));

    expect(chave.byes).toEqual([]);
    expect(chave.pairs).toEqual([
      ['Time 1', 'Time 8'],
      ['Time 2', 'Time 7'],
      ['Time 3', 'Time 6'],
      ['Time 4', 'Time 5'],
    ]);
  });

  it('chave incompleta dá folga aos primeiros até fechar a fase seguinte', () => {
    const chave = knockoutDraw(times(10));

    expect(bracketSize(10)).toBe(16);
    expect(chave.byes).toEqual(times(6));
    expect(chave.pairs).toEqual([
      ['Time 7', 'Time 10'],
      ['Time 8', 'Time 9'],
    ]);
    // 6 que folgam mais 2 vencedores: a fase seguinte tem 8.
    expect(chave.byes.length + chave.pairs.length).toBe(8);
  });

  it('ida e volta: a ida é na casa do mais fraco', () => {
    const rodadas = knockoutRounds('Semifinal', knockoutDraw(times(4)), 2);

    expect(rodadas.map((rodada) => rodada.name)).toEqual(['Semifinal · ida', 'Semifinal · volta']);
    expect(rodadas[0].matches[0].pair).toEqual(['Time 4', 'Time 1']);
    expect(rodadas[1].matches[0].pair).toEqual(['Time 1', 'Time 4']);
    expect(knockoutRounds('Final', knockoutDraw(times(2)), 1)).toEqual([
      { name: 'Final', matches: [{ pair: ['Time 1', 'Time 2'], group: null }] },
    ]);
  });

  it('nomeia as fases do mata-mata pelo tamanho da chave', () => {
    expect(knockoutStageName(2)).toBe('Final');
    expect(knockoutStageName(3)).toBe('Semifinal');
    expect(knockoutStageName(8)).toBe('Quartas de final');
    expect(knockoutStageName(10)).toBe('Oitavas de final');
    expect(knockoutStageNames(8)).toEqual(['Quartas de final', 'Semifinal', 'Final']);
    expect(knockoutStageNames(2)).toEqual(['Final']);
  });

  it('dá dia e horário: rodada a cada semana, jogos em sequência e campos em paralelo', () => {
    const rodadas = schedule(
      [
        {
          name: 'Rodada 1',
          matches: [
            { pair: ['A', 'B'], group: null },
            { pair: ['C', 'D'], group: null },
            { pair: ['E', 'F'], group: null },
          ],
        },
        { name: 'Rodada 2', matches: [{ pair: ['B', 'C'], group: null }] },
      ],
      {
        firstDate: '2026-12-27',
        daysBetweenRounds: 7,
        firstKickoff: '09:00',
        minutesPerMatch: 90,
        fields: 2,
      },
    )!;

    expect(rodadas.map((rodada) => rodada.date)).toEqual(['2026-12-27', '2027-01-03']);
    expect(rodadas[0].matches.map((jogo) => jogo.time)).toEqual(['09:00', '09:00', '10:30']);
    expect(rodadas[1].matches[0]).toEqual({
      home: 'B',
      away: 'C',
      group: null,
      date: '2027-01-03',
      time: '09:00',
    });
  });

  it('recusa a rodada que não cabe no dia', () => {
    const muitos = Array.from({ length: 10 }, () => ({ pair: ['A', 'B'] as Pair, group: null }));
    const opcoes = {
      firstDate: '2026-10-18',
      daysBetweenRounds: 7,
      firstKickoff: '20:00',
      minutesPerMatch: 60,
      fields: 1,
    };

    expect(schedule([{ name: 'Rodada 1', matches: muitos }], opcoes)).toBeNull();
    expect(
      schedule([{ name: 'Rodada 1', matches: muitos }], { ...opcoes, fields: 3 }),
    ).not.toBeNull();
  });

  it('não repete nome de rodada que o campeonato já tem', () => {
    expect(nextRoundNames(3, ['Rodada 1', 'rodada 3'])).toEqual([
      'Rodada 2',
      'Rodada 4',
      'Rodada 5',
    ]);
    expect(uniqueName('Final', ['Semifinal'])).toBe('Final');
    expect(uniqueName('Final', ['final', 'Final (2)'])).toBe('Final (3)');
  });

  it('soma dias no calendário, atravessando mês e ano', () => {
    expect(addDays('2026-10-18', 7)).toBe('2026-10-25');
    expect(addDays('2026-12-30', 3)).toBe('2027-01-02');
    expect(addDays('2028-02-28', 1)).toBe('2028-02-29');
  });

  it('o sorteio mantém os mesmos itens e não mexe na lista original', () => {
    const original = times(6);
    const sorteado = shuffle(original, () => 0);

    expect([...sorteado].sort()).toEqual([...original].sort());
    expect(original).toEqual(times(6));
  });
});
