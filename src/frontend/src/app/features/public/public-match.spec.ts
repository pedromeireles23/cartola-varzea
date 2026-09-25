import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { MATCH_URL, PARTIDA_ID, SLUG, atleta, partida } from './fixtures-fixtures';
import { PublicMatch } from './public-fixture.service';
import { PublicMatchPage } from './public-match';

describe('PublicMatchPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PublicMatchPage],
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

  async function abrir(dados: PublicMatch): Promise<ComponentFixture<PublicMatchPage>> {
    const fixture = TestBed.createComponent(PublicMatchPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    fixture.componentRef.setInput('partidaId', PARTIDA_ID);
    await fixture.whenStable();
    http.expectOne(MATCH_URL).flush(dados);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<PublicMatchPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  it('mostra placar, rodada e os dois elencos', async () => {
    const fixture = await abrir(partida());

    expect(texto(fixture)).toContain('União da Vila × Estrela do Bairro');
    expect(texto(fixture)).toContain('Rodada 1 · Fase única · dom., 20/09 · 10:00');
    expect(texto(fixture)).toContain('Pedrinho');
    expect(texto(fixture)).toContain('Juca');
    expect(texto(fixture)).toContain('Atacante');
  });

  it('conta o que a pessoa fez, e só o que aconteceu', async () => {
    const fixture = await abrir(
      partida({
        teams: [
          {
            name: 'União da Vila',
            isHome: true,
            athletes: [
              atleta({ sportingName: 'Pedrinho', goals: 2, assists: 1 }),
              atleta({ sportingName: 'Zé', position: 'Defender' }),
            ],
          },
        ],
      }),
    );

    expect(texto(fixture)).toContain('2 gols · 1 assistência');
    // Quem só entrou em campo fica com nome e posição: uma fila de zeros diria menos.
    expect(texto(fixture)).toContain('Zé');
    expect(texto(fixture)).not.toContain('0 gols');
  });

  it('goleiro que não sofreu gol tem isso dito na linha dele', async () => {
    const fixture = await abrir(
      partida({
        teams: [
          {
            name: 'União da Vila',
            isHome: true,
            athletes: [
              atleta({
                sportingName: 'Vitor',
                position: 'Goalkeeper',
                playedAsGoalkeeper: true,
                goalkeeperSaves: 3,
              }),
            ],
          },
        ],
      }),
    );

    expect(texto(fixture)).toContain('3 defesas');
    expect(texto(fixture)).toContain('não sofreu gol');
    expect(texto(fixture)).toContain('Goleiro');
  });

  it('quem foi para o gol sem ser goleiro aparece nas duas condições', async () => {
    const fixture = await abrir(
      partida({
        teams: [
          {
            name: 'União da Vila',
            isHome: true,
            athletes: [
              atleta({
                sportingName: 'Rafa',
                position: 'Defender',
                playedAsGoalkeeper: true,
                goalsConceded: 2,
              }),
            ],
          },
        ],
      }),
    );

    expect(texto(fixture)).toContain('Defensor, no gol');
    expect(texto(fixture)).not.toContain('não sofreu gol');
  });

  it('resultado provisório é avisado junto dos números', async () => {
    const fixture = await abrir(partida({ provisional: true }));

    expect(texto(fixture)).toContain('Resultado provisório');
    expect(texto(fixture)).toContain('se a liga corrigir a súmula');
  });

  it('súmula ainda não publicada explica, em vez de parecer erro', async () => {
    const fixture = TestBed.createComponent(PublicMatchPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    fixture.componentRef.setInput('partidaId', PARTIDA_ID);
    await fixture.whenStable();
    http.expectOne(MATCH_URL).flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Esta súmula ainda não está disponível');
    expect(texto(fixture)).toContain('publicada junto com o resultado da rodada');
  });
});
