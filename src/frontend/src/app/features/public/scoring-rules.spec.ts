import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { PERFIS } from '../organizer/competition-area/competition-fixtures';
import { ModalityRules, ScoringRules } from './scoring-rules.service';
import { ScoringRulesPage } from './scoring-rules';

const URL = '/api/v1/public/scoring-rules';

function modalidade(changes: Partial<ModalityRules> = {}): ModalityRules {
  return {
    modality: 'Fut7',
    version: 1,
    profile: PERFIS[0]!,
    goalPoints: [
      { position: 'Goalkeeper', points: 10 },
      { position: 'Defender', points: 8 },
      { position: 'Midfielder', points: 6 },
      { position: 'Forward', points: 5 },
    ],
    assist: 4,
    cleanSheet: 5,
    goalConceded: -1,
    goalkeeperSave: 1,
    penaltySave: 7,
    yellowCard: -2,
    redCard: -5,
    ownGoal: -3,
    penaltyMiss: -4,
    captainMultiplier: 2,
    valuation: {
      falls: [
        { threshold: -6, variation: -2 },
        { threshold: -3, variation: -1 },
      ],
      rises: [
        { threshold: 1, variation: 0.5 },
        { threshold: 3, variation: 1 },
      ],
      floor: 1,
      ceiling: 30,
    },
    ...changes,
  };
}

function regras(changes: Partial<ScoringRules> = {}): ScoringRules {
  return {
    modalities: [
      modalidade(),
      modalidade({ modality: 'Futsal', profile: PERFIS[1]!, assist: 3, cleanSheet: 8 }),
    ],
    ...changes,
  };
}

describe('ScoringRulesPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ScoringRulesPage],
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

  async function abrir(
    dados: ScoringRules = regras(),
  ): Promise<ComponentFixture<ScoringRulesPage>> {
    const fixture = TestBed.createComponent(ScoringRulesPage);
    await fixture.whenStable();
    http.expectOne(URL).flush(dados);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<ScoringRulesPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function clicar(fixture: ComponentFixture<ScoringRulesPage>, rotulo: string): void {
    [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .find((button) => button.textContent?.trim() === rotulo)!
      .click();
  }

  it('mostra quanto vale cada evento, com o gol pela posição', async () => {
    const fixture = await abrir();

    expect(texto(fixture)).toContain('Gol de goleiro');
    expect(texto(fixture)).toContain('10,00 pts');
    expect(texto(fixture)).toContain('Gol de atacante');
    expect(texto(fixture)).toContain('5,00 pts');
    expect(texto(fixture)).toContain('Assistência');
    expect(texto(fixture)).toContain('4,00 pts');
    expect(texto(fixture)).toContain('vale pela posição cadastrada');
  });

  it('eventos negativos mantêm o sinal, que é o que explica a perda', async () => {
    const fixture = await abrir();

    expect(texto(fixture)).toContain('Cartão vermelho');
    expect(texto(fixture)).toContain('-5,00 pts');
    expect(texto(fixture)).toContain('Gol sofrido (goleiro)');
    expect(texto(fixture)).toContain('-1,00 pts');
  });

  it('explica o capitão e a regra do segundo amarelo', async () => {
    const fixture = await abrir();

    expect(texto(fixture)).toContain('multiplica os próprios pontos por 2');
    expect(texto(fixture)).toContain('para cima e para baixo');
    expect(texto(fixture)).toContain('o segundo vira vermelho');
  });

  it('mostra as faixas de valorização com sinal, piso e teto', async () => {
    const fixture = await abrir();

    expect(texto(fixture)).toContain('-6,00 pts ou menos que a média');
    expect(texto(fixture)).toContain('-2,00');
    expect(texto(fixture)).toContain('Entre as duas faixas');
    expect(texto(fixture)).toContain('preço mantido');
    expect(texto(fixture)).toContain('3,00 pts ou mais que a média');
    expect(texto(fixture)).toContain('+1,00');
    expect(texto(fixture)).toContain('C$ 1,00 a C$ 30,00');
  });

  it('troca de modalidade sem recarregar a página', async () => {
    const fixture = await abrir();

    expect(texto(fixture)).toContain('5,00 pts');
    clicar(fixture, 'Futsal');
    await fixture.whenStable();

    expect(texto(fixture)).toContain('3,00 pts');
    expect(texto(fixture)).toContain('8,00 pts');
    http.expectNone(URL);
  });

  it('a escolha não depende só de cor', async () => {
    const fixture = await abrir();
    const botoes = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')];
    const fut7 = botoes.find((button) => button.textContent?.trim() === 'Fut7')!;
    const futsal = botoes.find((button) => button.textContent?.trim() === 'Futsal')!;

    expect(fut7.getAttribute('aria-pressed')).toBe('true');
    expect(futsal.getAttribute('aria-pressed')).toBe('false');
  });

  it('diz a versão da regra, porque recalibrar não muda resultado antigo', async () => {
    const fixture = await abrir();

    expect(texto(fixture)).toContain('Regra versão 1 de Fut7');
    expect(texto(fixture)).toContain('guarda a versão que usou');
  });

  it('avisa quando as regras não carregam', async () => {
    const fixture = TestBed.createComponent(ScoringRulesPage);
    await fixture.whenStable();
    http.expectOne(URL).flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Algo deu errado do nosso lado');
  });
});
