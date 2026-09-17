import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { apiErrorInterceptor } from '../../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../../core/config/api-base-url';
import { CompetitionContext } from './competition-context';
import { campeonato } from './competition-fixtures';
import { CompetitionLayout } from './competition-layout';

@Component({ template: '<h1>Tela interna</h1>' })
class TelaInterna {}

const SETTINGS = '/api/v1/competitions/c1/settings';

describe('CompetitionLayout', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        provideRouter(
          [
            {
              path: 'organizar/c/:campeonato',
              component: CompetitionLayout,
              children: [
                { path: '', pathMatch: 'full', title: 'Resumo', component: TelaInterna },
                { path: 'fases', title: 'Fases', component: TelaInterna },
                { path: 'times', title: 'Times', component: TelaInterna },
                { path: 'atletas', title: 'Atletas', component: TelaInterna },
                { path: 'tecnicos', title: 'Técnicos', component: TelaInterna },
                { path: 'configuracao', title: 'Configuração', component: TelaInterna },
              ],
            },
          ],
          withComponentInputBinding(),
        ),
        { provide: API_BASE_URL, useValue: '/api/v1' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function abrir(resposta: object, status = 200): Promise<RouterTestingHarness> {
    const harness = await RouterTestingHarness.create();
    const navegacao = harness.navigateByUrl('/organizar/c/c1');
    await new Promise((resolve) => setTimeout(resolve, 0));
    http.expectOne(SETTINGS).flush(resposta, status === 200 ? {} : { status, statusText: 'Erro' });
    await navegacao;
    harness.detectChanges();
    await harness.fixture.whenStable();
    return harness;
  }

  function links(harness: RouterTestingHarness): string[] {
    const nav = (harness.routeNativeElement as HTMLElement).querySelector(
      'nav[aria-label="Navegação do campeonato"]',
    );
    return [...(nav?.querySelectorAll('a') ?? [])].map((link) => link.textContent?.trim() ?? '');
  }

  it('mostra o nome, a trilha e o título da aba com o campeonato', async () => {
    const harness = await abrir(campeonato());
    const elemento = harness.routeNativeElement as HTMLElement;

    expect(elemento.textContent).toContain('Copa da Várzea');
    expect(elemento.textContent).toContain('Rascunho');
    const trilha = elemento.querySelector('nav[aria-label="Trilha"]');
    expect(trilha?.querySelector('a[href="/organizar/o/o1/campeonatos"]')?.textContent).toContain(
      'Liga da Várzea',
    );
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(TestBed.inject(Title).getTitle()).toBe('Resumo · Copa da Várzea');
  });

  it('proprietário vê Configuração na navegação; auxiliar não', async () => {
    const dono = await abrir(campeonato());
    expect(links(dono)).toEqual([
      'Resumo',
      'Fases',
      'Times',
      'Atletas',
      'Técnicos',
      'Publicação',
      'Configuração',
    ]);
  });

  it('auxiliar navega só pelo que pode usar', async () => {
    const auxiliar = await abrir(campeonato({ viewerRole: 'Assistant' }));
    expect(links(auxiliar)).toEqual([
      'Resumo',
      'Fases',
      'Times',
      'Atletas',
      'Técnicos',
      'Publicação',
    ]);
  });

  it('guarda o aviso de criação para o resumo consumir uma única vez', async () => {
    const harness = await RouterTestingHarness.create();
    await TestBed.inject(Router).navigate(['/organizar/c', 'c1'], { state: { criado: true } });
    harness.detectChanges();
    await harness.fixture.whenStable();
    http.expectOne(SETTINGS).flush(campeonato());

    const contexto = harness.fixture.debugElement
      .query((elemento) => elemento.componentInstance instanceof CompetitionLayout)
      .injector.get(CompetitionContext);
    expect(contexto.consumirCriado()).toBe(true);
    expect(contexto.consumirCriado()).toBe(false);
  });

  it('campeonato alheio ou inexistente mostra o mesmo aviso', async () => {
    const harness = await abrir({ title: 'Proibido', status: 403 }, 403);

    expect(harness.routeNativeElement?.textContent).toContain(
      'Este campeonato não existe ou não pertence a uma organização sua.',
    );
    expect(links(harness)).toEqual([]);
  });
});
