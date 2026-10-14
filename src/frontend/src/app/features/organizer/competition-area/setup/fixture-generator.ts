/**
 * Gerador da tabela de jogos da montagem do campeonato.
 *
 * Ele só propõe: devolve os jogos pelo nome dos times, da fase e da rodada, que é como a
 * importação de partidas os encontra. Quem decide se a proposta vale é o servidor, com as
 * mesmas regras do cadastro pela tela — fase certa, times confirmados nela e, em grupos,
 * no mesmo grupo. Por isso o gerador vive no navegador: a prévia é instantânea e não há
 * uma segunda regra de partida para manter.
 */

/** Mandante e visitante, nessa ordem. */
export type Pair = readonly [home: string, away: string];

/** Turno único, ida e volta; no mata-mata, jogo único ou ida e volta. */
export type Legs = 1 | 2;

export interface ProposedMatch {
  readonly home: string;
  readonly away: string;
  /** Grupo dos dois times, só na fase de grupos com mais de um grupo. */
  readonly group: string | null;
  /** `2026-10-18` */
  readonly date: string;
  /** `09:30`, no fuso do campeonato. */
  readonly time: string;
}

export interface ProposedRound {
  readonly name: string;
  readonly date: string;
  readonly matches: readonly ProposedMatch[];
}

export interface ScheduleOptions {
  /** Dia da primeira rodada, `2026-10-18`. */
  readonly firstDate: string;
  readonly daysBetweenRounds: number;
  /** Início do primeiro jogo de cada rodada, `09:00`. */
  readonly firstKickoff: string;
  /** De um início ao próximo no mesmo campo, já com o intervalo entre os jogos. */
  readonly minutesPerMatch: number;
  /** Campos jogando ao mesmo tempo. */
  readonly fields: number;
}

/** Rodada antes de ganhar dia e horário: só os confrontos, e de que grupo são. */
export interface PlannedRound {
  readonly name: string;
  readonly matches: readonly { readonly pair: Pair; readonly group: string | null }[];
}

/**
 * Todos contra todos pelo método do círculo: um time fica parado e os outros giram, e
 * cada rodada junta o primeiro com o último, o segundo com o penúltimo... Com número
 * ímpar entra uma folga, e quem cai com ela não joga naquela rodada.
 *
 * O mando sai de uma conta, não da ordem dos jogos. Entre os times que giram — todos,
 * com número ímpar —, `a` recebe `b` quando `b` está até metade do círculo à frente de
 * `a`; assim cada um recebe exatamente metade dos jogos. O time parado, que só existe com
 * número par, recebe nas rodadas pares. No turno ninguém passa de um jogo de diferença
 * entre casa e fora, e no returno os confrontos se repetem com o mando invertido.
 */
export function roundRobin(teams: readonly string[], legs: Legs): Pair[][] {
  if (teams.length < 2) return [];

  const even = teams.length % 2 === 0;
  const fixed = even ? teams[0] : null;
  const rotating = even ? teams.slice(1) : [...teams];
  const position = new Map(rotating.map((team, index) => [team, index]));
  const half = (rotating.length - 1) / 2;

  const circle: (string | null)[] = even ? [...teams] : [...teams, null];
  const size = circle.length;
  const first: Pair[][] = [];

  for (let round = 0; round < size - 1; round++) {
    const pairs: Pair[] = [];
    for (let index = 0; index < size / 2; index++) {
      const a = circle[index];
      const b = circle[size - 1 - index];
      if (a === null || b === null) continue;

      if (a === fixed || b === fixed) {
        const other = a === fixed ? b : a;
        pairs.push(round % 2 === 0 ? [fixed!, other] : [other, fixed!]);
        continue;
      }

      const ahead = (position.get(b)! - position.get(a)! + rotating.length) % rotating.length;
      pairs.push(ahead >= 1 && ahead <= half ? [a, b] : [b, a]);
    }
    first.push(pairs);

    // O primeiro fica; o último passa para a segunda posição e o resto anda uma casa.
    circle.splice(1, 0, circle.pop()!);
  }

  if (legs === 1) return first;
  return [...first, ...first.map((pairs) => pairs.map(([a, b]): Pair => [b, a]))];
}

/**
 * Grupos jogam todos contra todos dentro de cada grupo, e a rodada `n` junta a rodada
 * `n` de todos os grupos: é um fim de semana só, com um mercado só. Os jogos dos grupos
 * se alternam na rodada (A, B, A, B), para nenhum grupo ficar só com o fim do dia.
 */
export function groupRounds(
  groups: readonly { readonly name: string | null; readonly teams: readonly string[] }[],
  legs: Legs,
): PlannedRound['matches'][] {
  const perGroup = groups.map((group) => ({
    name: group.name,
    rounds: roundRobin(group.teams, legs),
  }));
  const total = Math.max(0, ...perGroup.map((group) => group.rounds.length));

  return Array.from({ length: total }, (_, round) => {
    const lists = perGroup.map((group) =>
      (group.rounds[round] ?? []).map((pair) => ({ pair, group: group.name })),
    );
    const longest = Math.max(0, ...lists.map((list) => list.length));
    return Array.from({ length: longest }, (_, index) =>
      lists.map((list) => list[index]).filter((match) => match !== undefined),
    ).flat();
  });
}

export interface KnockoutDraw {
  readonly pairs: readonly Pair[];
  /** Quem passa direto, sem jogar esta fase, porque a chave não fecha. */
  readonly byes: readonly string[];
}

/**
 * Chaveamento do mata-mata pela ordem informada, que é a ordem de força: o primeiro
 * enfrenta o último, o segundo o penúltimo. Quando o número de times não é potência de
 * dois, os primeiros passam direto até a chave fechar — com 10 times, 6 folgam e 4
 * jogam, e a fase seguinte tem 8. O mais forte de cada confronto é o mandante.
 */
export function knockoutDraw(seeds: readonly string[]): KnockoutDraw {
  if (seeds.length < 2) return { pairs: [], byes: [...seeds] };

  const byes = bracketSize(seeds.length) - seeds.length;
  const playing = seeds.slice(byes);
  const pairs = Array.from({ length: playing.length / 2 }, (_, index): Pair => [
    playing[index],
    playing[playing.length - 1 - index],
  ]);
  return { pairs, byes: seeds.slice(0, byes) };
}

/** Rodadas de uma fase de mata-mata: uma no jogo único, ida e volta em duas. */
export function knockoutRounds(stage: string, draw: KnockoutDraw, legs: Legs): PlannedRound[] {
  if (draw.pairs.length === 0) return [];
  if (legs === 1) {
    return [{ name: stage, matches: draw.pairs.map((pair) => ({ pair, group: null })) }];
  }

  // O mais forte decide em casa: a ida é na casa do mais fraco.
  return [
    {
      name: `${stage} · ida`,
      matches: draw.pairs.map(([strong, weak]) => ({ pair: [weak, strong] as Pair, group: null })),
    },
    {
      name: `${stage} · volta`,
      matches: draw.pairs.map((pair) => ({ pair, group: null })),
    },
  ];
}

/** Menor potência de dois que comporta os times. */
export function bracketSize(teams: number): number {
  let size = 2;
  while (size < teams) size *= 2;
  return size;
}

/** Nome da fase pelo tamanho da chave, como a várzea chama. */
export function knockoutStageName(teams: number): string {
  const size = bracketSize(teams);
  if (size === 2) return 'Final';
  if (size === 4) return 'Semifinal';
  if (size === 8) return 'Quartas de final';
  if (size === 16) return 'Oitavas de final';
  return 'Primeira fase eliminatória';
}

/** Fases de mata-mata de uma chave até a final: 8 times dão quartas, semifinal e final. */
export function knockoutStageNames(teams: number): string[] {
  const names: string[] = [];
  for (let size = bracketSize(teams); size >= 2; size /= 2) {
    names.push(knockoutStageName(size));
  }
  return names;
}

/** Quantos times uma fase de mata-mata recebe, quando o nome é um dos que o gerador dá. */
export function knockoutStageSize(name: string): number | null {
  const sizes: Readonly<Record<string, number>> = {
    Final: 2,
    Semifinal: 4,
    'Quartas de final': 8,
    'Oitavas de final': 16,
  };
  return sizes[name] ?? null;
}

/** Os nomes de rodada `Rodada 1`, `Rodada 2`… que ainda não existem no campeonato. */
export function nextRoundNames(count: number, taken: readonly string[]): string[] {
  const used = new Set(taken.map((name) => name.trim().toLocaleLowerCase('pt-BR')));
  const names: string[] = [];
  for (let number = 1; names.length < count; number++) {
    const name = `Rodada ${number}`;
    if (!used.has(name.toLocaleLowerCase('pt-BR'))) names.push(name);
  }
  return names;
}

/** O nome pedido, ou o mesmo com um número no fim quando ele já existe. */
export function uniqueName(name: string, taken: readonly string[]): string {
  const used = new Set(taken.map((item) => item.trim().toLocaleLowerCase('pt-BR')));
  if (!used.has(name.toLocaleLowerCase('pt-BR'))) return name;
  for (let number = 2; ; number++) {
    const candidate = `${name} (${number})`;
    if (!used.has(candidate.toLocaleLowerCase('pt-BR'))) return candidate;
  }
}

/**
 * Dá dia e horário a cada rodada: uma rodada a cada `daysBetweenRounds` dias, e na
 * rodada os jogos em sequência a partir do primeiro horário, tantos ao mesmo tempo
 * quantos campos houver. Devolve `null` quando os jogos de uma rodada não cabem no dia.
 */
export function schedule(
  rounds: readonly PlannedRound[],
  options: ScheduleOptions,
): ProposedRound[] | null {
  const start = minutesOf(options.firstKickoff);
  const fields = Math.max(1, Math.floor(options.fields));
  const result: ProposedRound[] = [];

  for (const [index, round] of rounds.entries()) {
    const date = addDays(options.firstDate, index * options.daysBetweenRounds);
    const slots = Math.ceil(round.matches.length / fields);
    if (start + (slots - 1) * options.minutesPerMatch >= 24 * 60) return null;

    result.push({
      name: round.name,
      date,
      matches: round.matches.map(({ pair, group }, position) => ({
        home: pair[0],
        away: pair[1],
        group,
        date,
        time: clock(start + Math.floor(position / fields) * options.minutesPerMatch),
      })),
    });
  }

  return result;
}

/** `2026-10-18` mais `days` dias, sem passar por fuso nenhum. */
export function addDays(date: string, days: number): string {
  const [year, month, day] = date.split('-').map(Number);
  const moved = new Date(Date.UTC(year, month - 1, day + days));
  return moved.toISOString().slice(0, 10);
}

function minutesOf(time: string): number {
  const [hours, minutes] = time.split(':').map(Number);
  return hours * 60 + minutes;
}

function clock(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  return `${String(hours).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
}

/** Embaralha sem mexer na lista original, para o sorteio dos grupos e da chave. */
export function shuffle<T>(items: readonly T[], random: () => number = Math.random): T[] {
  const copy = [...items];
  for (let index = copy.length - 1; index > 0; index--) {
    const other = Math.floor(random() * (index + 1));
    [copy[index], copy[other]] = [copy[other], copy[index]];
  }
  return copy;
}
