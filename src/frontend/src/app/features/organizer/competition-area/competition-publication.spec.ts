import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../../core/config/api-base-url';
import { CompetitionContext } from './competition-context';
import { campeonato } from './competition-fixtures';
import { CompetitionPublicationPage } from './competition-publication';
import { CompetitionReadiness, ReadinessItem } from './publication.service';

const READINESS = '/api/v1/competitions/c1/readiness';
const PUBLICATION = '/api/v1/competitions/c1/publication';
const SETTINGS = '/api/v1/competitions/c1/settings';

function checklist(parcial: Partial<CompetitionReadiness> = {}): CompetitionReadiness {
  return {
    competitionId: 'c1',
    status: 'Draft',
    publishedAt: null,
    canPublish: true,
    items: [],
    version: 'AAAAAAAAB9E=',
    ...parcial,
  };
}

function impedimento(parcial: Partial<ReadinessItem> = {}): ReadinessItem {
  return {
    code: 'no_stages',
    severity: 'Blocker',
    message: 'Crie ao menos uma fase: as rodadas acontecem dentro de uma fase.',
    ...parcial,
  };
}

describe('CompetitionPublicationPage', () => {
  let http: HttpTestingController;

  beforeAll(() => {
    const prototipo = HTMLDialogElement.prototype as HTMLDialogElement & {
      showModal?: () => void;
    };
    prototipo.showModal ??= function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    };
    prototipo.close = function (this: HTMLDialogElement) {
      this.removeAttribute('open');
    };
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CompetitionPublicationPage],
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api/v1' },
        CompetitionContext,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function abrir(
    resposta: CompetitionReadiness,
    papel: 'Owner' | 'Assistant' = 'Owner',
  ): Promise<ComponentFixture<CompetitionPublicationPage>> {
    TestBed.inject(CompetitionContext).replace(campeonato({ viewerRole: papel }));
    const fixture = TestBed.createComponent(CompetitionPublicationPage);
    await fixture.whenStable();
    http.expectOne(READINESS).flush(resposta);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<CompetitionPublicationPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function botao(
    fixture: ComponentFixture<CompetitionPublicationPage>,
    rotulo: string,
  ): HTMLButtonElement | undefined {
    return [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ].find((item) => item.textContent?.trim() === rotulo);
  }

  it('separa impedimentos de alertas e desabilita a publicação', async () => {
    const fixture = await abrir(
      checklist({
        canPublish: false,
        items: [
          impedimento(),
          { code: 'single_price_level', severity: 'Warning', message: 'Todos custam o mesmo.' },
        ],
      }),
    );

    expect(texto(fixture)).toContain('Impedimentos');
    expect(texto(fixture)).toContain('Crie ao menos uma fase');
    expect(texto(fixture)).toContain('Alertas');
    expect(texto(fixture)).toContain('Todos custam o mesmo.');
    expect(botao(fixture, 'Publicar campeonato')?.disabled).toBe(true);
    expect(texto(fixture)).toContain('Resolva os impedimentos abaixo');
  });

  it('aponta onde resolver cada impedimento', async () => {
    const fixture = await abrir(checklist({ canPublish: false, items: [impedimento()] }));

    const atalho = (fixture.nativeElement as HTMLElement).querySelector('.lista__item a');
    expect(atalho?.textContent?.trim()).toBe('Criar fase');
    expect(atalho?.getAttribute('href')).toContain('fases');
  });

  it('avisa quando nada está pendente', async () => {
    const fixture = await abrir(checklist());

    expect(texto(fixture)).toContain('Nada pendente');
    expect(botao(fixture, 'Publicar campeonato')?.disabled).toBe(false);
  });

  it('publica pelo diálogo e atualiza a casca', async () => {
    const fixture = await abrir(checklist());

    botao(fixture, 'Publicar campeonato')?.click();
    await fixture.whenStable();
    expect(texto(fixture)).toContain('A modalidade fica travada');

    // O botão do diálogo é só "Publicar"; o da página nomeia o campeonato.
    botao(fixture, 'Publicar')?.click();
    await fixture.whenStable();
    const requisicao = http.expectOne(PUBLICATION);
    expect(requisicao.request.method).toBe('PUT');
    expect(requisicao.request.body).toEqual({ published: true, version: 'AAAAAAAAB9E=' });
    requisicao.flush(
      checklist({ status: 'Published', publishedAt: '2026-09-17T12:00:00Z', version: 'NOVA' }),
    );
    await fixture.whenStable();

    // A casca mostra o status e guarda a versão da configuração: ela recarrega.
    http.expectOne(SETTINGS).flush(campeonato({ status: 'Published' }));
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Campeonato publicado.');
    expect(botao(fixture, 'Voltar para rascunho')).toBeDefined();
    expect(botao(fixture, 'Publicar campeonato')).toBeUndefined();
  });

  it('recarrega o checklist quando o servidor recusa por falta de prontidão', async () => {
    const fixture = await abrir(checklist());

    botao(fixture, 'Publicar campeonato')?.click();
    await fixture.whenStable();
    botao(fixture, 'Publicar')?.click();
    await fixture.whenStable();
    http
      .expectOne(PUBLICATION)
      .flush(
        { title: 'Campeonato ainda não pode ser publicado', code: 'competition_not_ready' },
        { status: 409, statusText: 'Conflict' },
      );
    await fixture.whenStable();
    http.expectOne(READINESS).flush(checklist({ canPublish: false, items: [impedimento()] }));
    await fixture.whenStable();

    expect(texto(fixture)).toContain('ainda há impedimentos');
    expect(texto(fixture)).toContain('Crie ao menos uma fase');
  });

  it('auxiliar acompanha o checklist sem receber o botão de publicar', async () => {
    const fixture = await abrir(checklist(), 'Assistant');

    expect(texto(fixture)).toContain('Somente quem é proprietário da organização publica');
    expect(botao(fixture, 'Publicar campeonato')).toBeUndefined();
  });
});
