import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { APIResponse, Page, expect } from '@playwright/test';

/** Massa fictícia versionada no repositório, a mesma que a demonstração usa. */
const DADOS_DEMO = join(
  dirname(fileURLToPath(import.meta.url)),
  '..',
  '..',
  '..',
  'infra',
  'dados-demo',
);

export interface CampeonatoPublicado {
  readonly slug: string;
  readonly competitionId: string;
  readonly roundId: string;
}

/**
 * Chamadas à API com a sessão da página, como o Angular faria: o cookie vai sozinho e
 * o token antiforgery é repetido no cabeçalho `X-XSRF-TOKEN`.
 */
export class ApiDaSessao {
  constructor(private readonly page: Page) {}

  async get<T>(caminho: string): Promise<T> {
    return (await this.conferir(await this.page.request.get(caminho))) as T;
  }

  async post<T>(caminho: string, corpo?: unknown): Promise<T> {
    return (await this.conferir(
      await this.page.request.post(caminho, { data: corpo, headers: await this.cabecalhos() }),
    )) as T;
  }

  async put<T>(caminho: string, corpo: unknown): Promise<T> {
    return (await this.conferir(
      await this.page.request.put(caminho, { data: corpo, headers: await this.cabecalhos() }),
    )) as T;
  }

  /** Sem conferir a resposta: é para o teste olhar a recusa do servidor. */
  async putBruto(caminho: string, corpo: unknown): Promise<APIResponse> {
    return this.page.request.put(caminho, { data: corpo, headers: await this.cabecalhos() });
  }

  async enviarArquivo(caminho: string, arquivo: string): Promise<void> {
    await this.conferir(
      await this.page.request.post(caminho, {
        headers: await this.cabecalhos(),
        multipart: {
          arquivo: {
            name: arquivo,
            mimeType: 'text/csv',
            buffer: readFileSync(join(DADOS_DEMO, arquivo)),
          },
        },
      }),
    );
  }

  private async cabecalhos(): Promise<Record<string, string>> {
    const cookies = await this.page.context().cookies();
    const token = cookies.find((cookie) => cookie.name === 'XSRF-TOKEN');
    expect(token, 'A sessão da página ainda não tem o token antiforgery').toBeDefined();
    return { 'X-XSRF-TOKEN': decodeURIComponent(token!.value) };
  }

  private async conferir(resposta: APIResponse): Promise<unknown> {
    expect(resposta.ok(), `${resposta.url()} respondeu ${resposta.status()}`).toBe(true);
    const texto = await resposta.text();
    return texto ? JSON.parse(texto) : null;
  }
}

/**
 * Campeonato publicado com os 6 times e 54 atletas de `infra/dados-demo`, uma fase de
 * grupo único e a Rodada 1 com o mercado aberto. A massa é de Fut7, mas tem atletas
 * suficientes por posição para publicar também um campeonato de futebol de campo.
 *
 * Tudo pela API, com a sessão de quem é dona da organização: a montagem pela tela já
 * tem os próprios E2E, e aqui o assunto é o jogo.
 *
 * `inicioLocal` é a data e hora da primeira partida no fuso do campeonato
 * (`2026-09-20T10:00`). Com antecedência zero, é também o fechamento do mercado.
 */
export async function publicarCampeonatoDemo(
  dona: Page,
  organizacao: string,
  nome: string,
  inicioLocal: string,
  modalidade: 'Fut7' | 'Futsal' | 'Field' = 'Fut7',
): Promise<CampeonatoPublicado> {
  const api = new ApiDaSessao(dona);
  const minhas = await api.get<{ id: string; name: string }[]>('/api/v1/organizations/mine');
  const organizacaoId = minhas.find((item) => item.name === organizacao)!.id;

  const { id: competitionId } = await api.post<{ id: string }>(
    `/api/v1/organizations/${organizacaoId}/competitions`,
    { name: nome, season: '2026', modality: modalidade },
  );
  for (const [tipo, arquivo] of [
    ['times', 'times-v1.csv'],
    ['atletas', 'atletas-v1.csv'],
    ['tecnicos', 'tecnicos-v1.csv'],
  ] as const) {
    await api.enviarArquivo(`/api/v1/competitions/${competitionId}/imports/${tipo}`, arquivo);
  }

  const fase = await api.post<{ id: string; version: string; groups: { id: string }[] }>(
    `/api/v1/competitions/${competitionId}/stages`,
    { name: 'Pontos corridos', format: 'Groups', groups: [{ name: 'Grupo único' }] },
  );
  const times = await api.get<{ id: string }[]>(`/api/v1/competitions/${competitionId}/teams`);
  await api.put(`/api/v1/competitions/${competitionId}/stages/${fase.id}/participants`, {
    participants: times.map((time) => ({ realTeamId: time.id, stageGroupId: fase.groups[0]!.id })),
    version: fase.version,
  });

  const prontidao = await api.get<{ version: string }>(
    `/api/v1/competitions/${competitionId}/readiness`,
  );
  const { slug } = await api.put<{ slug: string }>(
    `/api/v1/competitions/${competitionId}/publication`,
    { published: true, version: prontidao.version },
  );

  const rodada = await api.post<{ id: string; version: string }>(
    `/api/v1/competitions/${competitionId}/rounds`,
    { name: 'Rodada 1' },
  );
  const comPartida = await api.post<{ version: string }>(
    `/api/v1/competitions/${competitionId}/rounds/${rodada.id}/matches`,
    {
      stageId: fase.id,
      homeTeamId: times[0]!.id,
      awayTeamId: times[1]!.id,
      kickoffLocal: inicioLocal,
      version: rodada.version,
    },
  );
  await api.put(`/api/v1/competitions/${competitionId}/rounds/${rodada.id}/status`, {
    transition: 'OpenMarket',
    version: comPartida.version,
  });

  return { slug, competitionId, roundId: rodada.id };
}

/**
 * Lança a súmula do único jogo da rodada e manda a rodada para revisão: mandante 1 × 0,
 * gol do primeiro atacante do mandante, um goleiro em campo de cada lado. Devolve o nome
 * de quem marcou.
 */
export async function lancarSumulaDaRodada(
  api: ApiDaSessao,
  competitionId: string,
  roundId: string,
): Promise<string> {
  type Atleta = {
    athleteId: string;
    realTeamId: string;
    sportingName: string;
    position: string;
  };
  const rodadas = await api.get<{ id: string; version: string; matches: { id: string }[] }[]>(
    `/api/v1/competitions/${competitionId}/rounds`,
  );
  const rodada = rodadas.find((item) => item.id === roundId)!;
  const partida = rodada.matches[0]!.id;
  const caminho = `/api/v1/competitions/${competitionId}/matches/${partida}/sheet`;
  const sumula = await api.get<{ homeTeamId: string; awayTeamId: string; athletes: Atleta[] }>(
    caminho,
  );

  const doTime = (time: string, posicao: string) =>
    sumula.athletes.filter((item) => item.realTeamId === time && item.position === posicao);
  const goleiros = [
    doTime(sumula.homeTeamId, 'Goalkeeper')[0]!,
    doTime(sumula.awayTeamId, 'Goalkeeper')[0]!,
  ];
  const foraDeCampo = [
    doTime(sumula.homeTeamId, 'Goalkeeper')[1]!.athleteId,
    doTime(sumula.awayTeamId, 'Goalkeeper')[1]!.athleteId,
  ];
  const artilheiro = doTime(sumula.homeTeamId, 'Forward')[0]!;

  await api.put(caminho, {
    homeScore: 1,
    awayScore: 0,
    version: null,
    appearances: sumula.athletes.map((atleta) => ({
      athleteId: atleta.athleteId,
      didPlay: !foraDeCampo.includes(atleta.athleteId),
      playedAsGoalkeeper: goleiros.some((item) => item.athleteId === atleta.athleteId),
      goalsConceded: atleta.athleteId === goleiros[1]!.athleteId ? 1 : 0,
      goals: atleta.athleteId === artilheiro.athleteId ? 1 : 0,
      assists: 0,
      goalkeeperSaves: 0,
      penaltySaves: 0,
      yellowCards: 0,
      redCards: 0,
      redCardReason: null,
      ownGoals: 0,
      penaltyMisses: 0,
    })),
  });

  const atual = await api.get<{ id: string; version: string }[]>(
    `/api/v1/competitions/${competitionId}/rounds`,
  );
  await api.put(`/api/v1/competitions/${competitionId}/rounds/${roundId}/status`, {
    transition: 'SendToReview',
    version: atual.find((item) => item.id === roundId)!.version,
  });
  return artilheiro.sportingName;
}

/** Data e hora no fuso do campeonato (Brasília), no formato que a API espera. */
export function horarioDeBrasilia(instante: Date): string {
  const partes = Object.fromEntries(
    new Intl.DateTimeFormat('en-CA', {
      timeZone: 'America/Sao_Paulo',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23',
    })
      .formatToParts(instante)
      .map((parte) => [parte.type, parte.value]),
  );
  return `${partes['year']}-${partes['month']}-${partes['day']}T${partes['hour']}:${partes['minute']}`;
}
