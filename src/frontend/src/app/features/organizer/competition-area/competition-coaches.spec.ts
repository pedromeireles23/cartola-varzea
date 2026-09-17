import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../../core/config/api-base-url';
import { Coach } from './coach.service';
import { CompetitionCoachesPage } from './competition-coaches';
import { CompetitionContext } from './competition-context';
import { campeonato } from './competition-fixtures';

const COACHES = '/api/v1/competitions/c1/coaches';

function coach(partial: Partial<Coach> = {}): Coach {
  return {
    id: 'c1',
    displayName: null,
    effectiveName: 'Técnico do União da Vila',
    realTeamId: 't1',
    realTeamName: 'União da Vila',
    priceTier: 'Regular',
    initialPriceOverride: null,
    initialPrice: 8,
    isAvailable: true,
    updatedAt: '2026-09-17T00:00:00Z',
    version: 'AAAAAAAAB9E=',
    ...partial,
  };
}

async function settle(fixture: ComponentFixture<CompetitionCoachesPage>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

describe('CompetitionCoachesPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CompetitionCoachesPage],
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

  async function open(
    coaches: Coach[],
    role: 'Owner' | 'Assistant' = 'Owner',
  ): Promise<ComponentFixture<CompetitionCoachesPage>> {
    TestBed.inject(CompetitionContext).replace(campeonato({ viewerRole: role }));
    const fixture = TestBed.createComponent(CompetitionCoachesPage);
    await fixture.whenStable();
    http.expectOne(COACHES).flush(coaches);
    await fixture.whenStable();
    return fixture;
  }

  function text(fixture: ComponentFixture<CompetitionCoachesPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function button(
    fixture: ComponentFixture<CompetitionCoachesPage>,
    label: string,
  ): HTMLButtonElement {
    const found = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (item) => item.textContent?.replace(/\s+/g, ' ').trim() === label,
    );
    expect(found, `Botão "${label}" não encontrado`).toBeDefined();
    return found!;
  }

  function input(element: HTMLInputElement, value: string): void {
    element.value = value;
    element.dispatchEvent(new Event('input'));
  }

  function select(element: HTMLSelectElement, value: string): void {
    element.value = value;
    element.dispatchEvent(new Event('change'));
  }

  it('mostra o fallback automático, time, nível, preço e disponibilidade', async () => {
    const fixture = await open([coach()]);

    expect(text(fixture)).toContain('Técnico do União da Vila');
    expect(text(fixture)).toContain('Nome automático');
    expect(text(fixture)).toContain('Regular');
    expect(text(fixture)).toContain('8,00 créditos');
    expect(text(fixture)).toContain('Disponível');
  });

  it('auxiliar lê o catálogo sem poder editar', async () => {
    const fixture = await open([coach()], 'Assistant');

    expect(text(fixture)).toContain('Técnico do União da Vila');
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('button')).toHaveLength(0);
  });

  it('salva nome pessoal, nível e preço exato', async () => {
    const fixture = await open([coach()]);

    button(fixture, 'Editar Técnico do União da Vila').click();
    await settle(fixture);
    const form = (fixture.nativeElement as HTMLElement).querySelector('form')!;
    input(form.querySelector('input[type="text"]')!, ' Ana Lima ');
    select(form.querySelector('select')!, 'Star');
    input(form.querySelector('input[type="number"]')!, '10.25');
    form.dispatchEvent(new Event('submit'));
    await settle(fixture);

    const request = http.expectOne(`${COACHES}/c1`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      displayName: 'Ana Lima',
      priceTier: 'Star',
      initialPriceOverride: 10.25,
      version: 'AAAAAAAAB9E=',
    });
    request.flush(
      coach({
        displayName: 'Ana Lima',
        effectiveName: 'Ana Lima',
        priceTier: 'Star',
        initialPriceOverride: 10.25,
        initialPrice: 10.25,
      }),
    );
    await settle(fixture);
    http
      .expectOne(COACHES)
      .flush([coach({ displayName: 'Ana Lima', effectiveName: 'Ana Lima', priceTier: 'Star' })]);
    await fixture.whenStable();

    expect(text(fixture)).toContain('Ana Lima salvo.');
  });

  it('envia nome nulo ao limpar o campo e restaura o fallback', async () => {
    const named = coach({ displayName: 'Ana Lima', effectiveName: 'Ana Lima' });
    const fixture = await open([named]);

    button(fixture, 'Editar Ana Lima').click();
    await settle(fixture);
    const form = (fixture.nativeElement as HTMLElement).querySelector('form')!;
    input(form.querySelectorAll('input')[0]!, '  ');
    form.dispatchEvent(new Event('submit'));
    await settle(fixture);

    const request = http.expectOne(`${COACHES}/c1`);
    expect(request.request.body.displayName).toBeNull();
    request.flush(coach());
    await settle(fixture);
    http.expectOne(COACHES).flush([coach()]);
    await fixture.whenStable();

    expect(text(fixture)).toContain('Técnico do União da Vila salvo.');
  });
});
