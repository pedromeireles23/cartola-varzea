import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { PublicationProgress } from './publication-progress';
import { CompetitionReadiness, ReadinessItem } from './publication.service';

function prontidao(changes: Partial<CompetitionReadiness> = {}): CompetitionReadiness {
  return {
    competitionId: 'c1',
    status: 'Draft',
    publishedAt: null,
    slug: null,
    canPublish: true,
    items: [],
    version: 'v1',
    ...changes,
  };
}

function item(code: string, severity: ReadinessItem['severity'] = 'Blocker'): ReadinessItem {
  return { code, severity, message: code };
}

describe('PublicationProgress', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PublicationProgress],
      providers: [provideRouter([])],
    });
  });

  async function abrir(
    dados: CompetitionReadiness,
  ): Promise<ComponentFixture<PublicationProgress>> {
    const fixture = TestBed.createComponent(PublicationProgress);
    fixture.componentRef.setInput('prontidao', dados);
    await fixture.whenStable();
    return fixture;
  }

  function etapas(fixture: ComponentFixture<PublicationProgress>): string[] {
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll('.etapa')].map(
      (etapa) => etapa.textContent?.replace(/\s+/g, ' ').trim() ?? '',
    );
  }

  function texto(fixture: ComponentFixture<PublicationProgress>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  it('sem pendência e em rascunho, só falta publicar', async () => {
    const fixture = await abrir(prontidao());

    expect(texto(fixture)).toContain('5 de 6 etapas cumpridas');
    expect(etapas(fixture).at(-1)).toBe('Publicação pronta para publicar');
  });

  it('agrupa os códigos do checklist em etapas e diz o estado em texto', async () => {
    const fixture = await abrir(
      prontidao({
        canPublish: false,
        items: [
          item('no_stage_participants'),
          item('stage_without_participants'),
          item('thin_real_team_roster', 'Warning'),
        ],
      }),
    );

    expect(etapas(fixture)).toEqual([
      'Fases pronta',
      'Times nas fases pendente Resolver Times nas fases',
      'Times pronta',
      'Atletas com alerta Resolver Atletas',
      'Preços pronta',
      'Publicação pendente',
    ]);
    // Alerta não segura a publicação: conta como etapa cumprida.
    expect(texto(fixture)).toContain('4 de 6 etapas cumpridas');
  });

  it('o atalho de cada pendência leva à tela que a resolve', async () => {
    const fixture = await abrir(prontidao({ canPublish: false, items: [item('no_stages')] }));

    const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      '.etapa__link',
    );
    expect(link?.getAttribute('href')).toBe('/organizar/c/c1/fases');
  });

  it('publicado, todas as etapas estão cumpridas e a última diz que está no ar', async () => {
    const fixture = await abrir(prontidao({ status: 'Published' }));

    expect(texto(fixture)).toContain('6 de 6 etapas cumpridas');
    expect(etapas(fixture).at(-1)).toBe('Publicação no ar');
    expect((fixture.nativeElement as HTMLElement).querySelector('.etapa__link')).toBeNull();
  });

  it('o estado nunca depende só da cor: ícone e texto vêm juntos', async () => {
    const fixture = await abrir(prontidao({ items: [item('single_price_level', 'Warning')] }));

    const precos = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.etapa')][4];
    expect(precos.querySelector('svg')).not.toBeNull();
    expect(precos.querySelector('.etapa__estado')?.textContent?.trim()).toBe('com alerta');
  });
});
