import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { ROUND_ID, ROUND_URL, SLUG, rodadaApurada, vagaApurada } from './fantasy-fixtures';
import { FantasyScorePage } from './fantasy-score';
import { FantasyRoundScore } from './fantasy.service';

describe('FantasyScorePage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FantasyScorePage],
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api/v1' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function abrir(dados: FantasyRoundScore): Promise<ComponentFixture<FantasyScorePage>> {
    const fixture = TestBed.createComponent(FantasyScorePage);
    fixture.componentRef.setInput('campeonato', SLUG);
    fixture.componentRef.setInput('rodada', ROUND_ID);
    await fixture.whenStable();
    http.expectOne(ROUND_URL).flush(dados);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<FantasyScorePage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  it('explica cada ponto: eventos, capitão e a variação de preço pela média da posição', async () => {
    const fixture = await abrir(
      rodadaApurada({
        slots: [
          vagaApurada({
            name: 'Baiano',
            position: 'Defender',
            points: 13,
            isCaptain: true,
            lines: [
              { item: 'Goal', quantity: 1, points: 8 },
              { item: 'CleanSheet', quantity: 1, points: 5 },
            ],
            price: {
              average: 1.89,
              difference: 11.11,
              variation: 2,
              previousPrice: 7,
              newPrice: 9,
            },
          }),
        ],
      }),
    );

    expect(texto(fixture)).toMatch(/Sua pontuação na rodada\s*35,00 pts/);
    expect(texto(fixture)).toContain('Inclui 8,00 pts do capitão');
    expect(texto(fixture)).toContain('Resultado provisório: pode mudar até qua., 23/09 às 10:00');
    expect(texto(fixture)).toContain('(Horário de Brasília)');
    expect(texto(fixture)).toContain('Baiano');
    expect(texto(fixture)).toContain('Capitão');
    expect(texto(fixture)).toContain('Defensor · União da Vila');
    expect(texto(fixture)).toContain('1 gol +8');
    expect(texto(fixture)).toContain('1 jogo sem sofrer gol +5');
    expect(texto(fixture)).toContain(
      '13,00 pts · 11,11 acima da média dos defensores · +2 · C$ 7,00 → C$ 9,00',
    );
    expect(texto(fixture)).not.toContain('sem bônus');
  });

  it('mostra quem o banco cobriu e quem ficou de fora do total', async () => {
    const fixture = await abrir(
      rodadaApurada({
        captainBonus: 0,
        slots: [
          vagaApurada({
            assetId: 'g1',
            name: 'Pipoca',
            position: 'Goalkeeper',
            played: false,
            points: 0,
            counts: false,
            isCaptain: true,
            replacedBy: 'g2',
            price: { average: null, difference: null, variation: 0, previousPrice: 7, newPrice: 7 },
          }),
          vagaApurada({
            assetId: 'g2',
            name: 'Gato',
            position: 'Goalkeeper',
            role: 'Bench',
            points: 5,
            replaces: 'g1',
          }),
          vagaApurada({
            assetId: 'd2',
            name: 'Zeca',
            position: 'Defender',
            role: 'Bench',
            played: false,
            points: 0,
            counts: false,
          }),
          vagaApurada({
            assetId: 'c1',
            kind: 'Coach',
            name: 'Seu Zé',
            position: null,
            role: 'Coach',
            played: false,
            points: 0,
            counts: false,
          }),
        ],
      }),
    );

    expect(texto(fixture)).toContain(
      'Não jogou: o reserva da posição entrou no lugar dele, e os pontos dele não contam.',
    );
    expect(texto(fixture)).toContain('Entrou no lugar de um titular que não jogou');
    expect(texto(fixture)).toContain('Ficou no banco e não entrou em campo.');
    expect(texto(fixture)).toContain('Fora do total');
    // Uma frase por vaga: quem o reserva cobriu não repete que não entrou em campo.
    expect(texto(fixture)).not.toContain('Não entrou em campo nesta rodada');
    expect(texto(fixture)).toContain('Sem ninguém do time dele em campo, o técnico não pontuou.');
    expect(texto(fixture)).toContain('Sem jogar, o preço não muda: C$ 7,00');
    expect(texto(fixture)).toContain(
      'O capitão não entrou em campo, então a rodada ficou sem bônus',
    );
    expect(texto(fixture)).toContain('Reserva de goleiro');
  });

  it('o titular sem reserva em campo não é confundido com o técnico', async () => {
    const fixture = await abrir(
      rodadaApurada({
        slots: [
          vagaApurada({
            name: 'Cascão',
            position: 'Defender',
            played: false,
            points: 0,
            counts: false,
          }),
        ],
      }),
    );

    expect(texto(fixture)).toContain(
      'Não jogou, e nenhum reserva da posição entrou em campo: a vaga ficou sem pontos.',
    );
    expect(texto(fixture)).not.toContain('o técnico não pontuou');
  });

  it('volta para as rodadas e diz de quem é a pontuação', async () => {
    const fixture = await abrir(rodadaApurada());
    const voltar = (fixture.nativeElement as HTMLElement).querySelector('app-back-link a');

    expect(voltar?.getAttribute('href')).toBe(`/c/${SLUG}/rodadas`);
    expect(texto(fixture)).toContain('Sua pontuação');
    expect((fixture.nativeElement as HTMLElement).querySelector('h1')?.textContent).toBe(
      'Rodada 1',
    );
  });

  it('diz que o resultado consolidou quando a janela de correção acabou', async () => {
    const fixture = await abrir(rodadaApurada({ provisional: false }));

    expect(texto(fixture)).toContain('Resultado consolidado desde qua., 23/09 às 10:00');
  });

  it('explica a rodada que a conta não jogou', async () => {
    const fixture = await abrir(rodadaApurada({ played: false, total: 0, slots: [] }));

    expect(texto(fixture)).toContain('Você não jogou esta rodada');
    expect(texto(fixture)).not.toContain('Sua pontuação na rodada');
  });

  it('avisa que a rodada está em correção sem esconder os números que estão valendo', async () => {
    const fixture = await abrir(rodadaApurada({ underCorrection: true }));

    expect(texto(fixture)).toContain('A liga reabriu esta rodada para correção');
    expect(texto(fixture)).toContain('o que você lê aqui é o resultado que está valendo');
    expect(texto(fixture)).toContain('35,00 pts');

    // O aviso de provisório sairia errado aqui: quem manda é a correção em andamento.
    expect(texto(fixture)).not.toContain('Resultado provisório');
  });

  it('conta o que a correção fez com a pontuação da conta', async () => {
    const fixture = await abrir(
      rodadaApurada({
        revision: 2,
        total: 46,
        correction: {
          revision: 2,
          correctedAtLocal: '2026-09-24T10:00',
          reason: 'Gol lançado no atleta errado.',
          previousTotal: 42.5,
        },
      }),
    );

    expect(texto(fixture)).toContain('Rodada corrigida em qui., 24/09 às 10:00');
    expect(texto(fixture)).toContain('Motivo: Gol lançado no atleta errado.');
    expect(texto(fixture)).toContain('Sua pontuação foi de 42,50 pts para 46,00 pts.');
  });

  it('a correção que não mexeu nesta conta diz isso, em vez de uma seta entre números iguais', async () => {
    const fixture = await abrir(
      rodadaApurada({
        revision: 2,
        correction: {
          revision: 2,
          correctedAtLocal: '2026-09-24T10:00',
          reason: null,
          previousTotal: 35,
        },
      }),
    );

    expect(texto(fixture)).toContain('Sua pontuação não mudou: 35,00 pts.');
    expect(texto(fixture)).not.toContain('Motivo:');
  });

  it('a rodada recalculada de quem não jogou avisa sem comparar números', async () => {
    const fixture = await abrir(
      rodadaApurada({
        played: false,
        total: 0,
        slots: [],
        revision: 2,
        correction: {
          revision: 2,
          correctedAtLocal: '2026-09-24T10:00',
          reason: 'Recalculada porque a Rodada 1 foi corrigida.',
          previousTotal: null,
        },
      }),
    );

    expect(texto(fixture)).toContain('Motivo: Recalculada porque a Rodada 1 foi corrigida.');
    expect(texto(fixture)).not.toContain('Sua pontuação foi de');
  });

  it('não confunde rodada sem apuração com erro de rede', async () => {
    const fixture = TestBed.createComponent(FantasyScorePage);
    fixture.componentRef.setInput('campeonato', SLUG);
    fixture.componentRef.setInput('rodada', ROUND_ID);
    await fixture.whenStable();
    http.expectOne(ROUND_URL).flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Esta rodada ainda não foi apurada');
  });
});
