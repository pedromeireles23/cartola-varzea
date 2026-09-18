import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../../core/config/api-base-url';
import { Athlete } from './athlete.service';
import { CompetitionAthletesPage } from './competition-athletes';
import { CompetitionContext } from './competition-context';
import { campeonato } from './competition-fixtures';
import { RealTeam } from './real-team.service';

const ATHLETES = '/api/v1/competitions/c1/athletes';
const TEAMS = '/api/v1/competitions/c1/teams';

function athlete(partial: Partial<Athlete> = {}): Athlete {
  return {
    id: 'a1',
    sportingName: 'Bia',
    position: 'Midfielder',
    realTeamId: 't1',
    realTeamName: 'União da Vila',
    priceTier: 'Star',
    initialPriceOverride: null,
    initialPrice: 11,
    isAvailable: true,
    isEliminated: false,
    status: 'Active',
    updatedAt: '2026-09-16T22:00:00Z',
    version: 'AAAAAAAAB9E=',
    ...partial,
  };
}

function team(partial: Partial<RealTeam> = {}): RealTeam {
  return {
    id: 't1',
    name: 'União da Vila',
    isArchived: false,
    updatedAt: '2026-09-16T12:00:00Z',
    version: 'AAAAAAAAB8E=',
    ...partial,
  };
}

async function settle(fixture: ComponentFixture<CompetitionAthletesPage>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

describe('CompetitionAthletesPage', () => {
  let http: HttpTestingController;

  beforeAll(() => {
    const prototype = HTMLDialogElement.prototype as HTMLDialogElement & {
      showModal?: () => void;
    };
    prototype.showModal ??= function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    };
    prototype.close = function (this: HTMLDialogElement) {
      this.removeAttribute('open');
    };
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CompetitionAthletesPage],
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
    athletes: Athlete[],
    teams: RealTeam[] = [team()],
    role: 'Owner' | 'Assistant' = 'Owner',
  ): Promise<ComponentFixture<CompetitionAthletesPage>> {
    TestBed.inject(CompetitionContext).replace(campeonato({ viewerRole: role }));
    const fixture = TestBed.createComponent(CompetitionAthletesPage);
    await fixture.whenStable();
    http.expectOne(ATHLETES).flush(athletes);
    http.expectOne(TEAMS).flush(teams);
    await fixture.whenStable();
    return fixture;
  }

  function text(fixture: ComponentFixture<CompetitionAthletesPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function button(
    fixture: ComponentFixture<CompetitionAthletesPage>,
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

  it('mostra posição, nível, preço e disponibilidade', async () => {
    const fixture = await open([
      athlete(),
      athlete({ id: 'a2', sportingName: 'Caio', status: 'Released', isAvailable: false }),
      athlete({ id: 'a3', sportingName: 'Duda', isAvailable: false, isEliminated: true }),
    ]);

    expect(text(fixture)).toContain('Bia');
    expect(text(fixture)).toContain('Meio-campista');
    expect(text(fixture)).toContain('Destaque');
    expect(text(fixture)).toContain('11,00 créditos');
    expect(text(fixture)).toContain('Disponível');
    expect(text(fixture)).toContain('Caio');
    expect(text(fixture)).toContain('Desligado');
    expect(text(fixture)).toContain('Time eliminado');
  });

  it('auxiliar só lê o catálogo', async () => {
    const fixture = await open([athlete()], [team()], 'Assistant');

    expect(text(fixture)).toContain('Bia');
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('button')).toHaveLength(0);
  });

  it('cria inscrição com posição e preço exato opcional', async () => {
    const fixture = await open([]);

    button(fixture, 'Adicionar atleta').click();
    await settle(fixture);
    const form = (fixture.nativeElement as HTMLElement).querySelector('form')!;
    const inputs = form.querySelectorAll('input');
    const selects = form.querySelectorAll('select');
    input(inputs[0]!, ' Bia ');
    select(selects[1]!, 'Midfielder');
    select(selects[2]!, 'Star');
    input(inputs[1]!, '10.25');
    form.dispatchEvent(new Event('submit'));
    await settle(fixture);

    const request = http.expectOne((item) => item.url === ATHLETES && item.method === 'POST');
    expect(request.request.body).toEqual({
      sportingName: 'Bia',
      position: 'Midfielder',
      realTeamId: 't1',
      priceTier: 'Star',
      initialPriceOverride: 10.25,
    });
    request.flush(athlete({ initialPriceOverride: 10.25, initialPrice: 10.25 }));
    await settle(fixture);
    http.expectOne(ATHLETES).flush([athlete({ initialPriceOverride: 10.25, initialPrice: 10.25 })]);
    http.expectOne(TEAMS).flush([team()]);
    await fixture.whenStable();

    expect(text(fixture)).toContain('Bia adicionado.');
  });

  it('desliga somente depois da confirmação e preserva o item na lista', async () => {
    const fixture = await open([athlete()]);

    button(fixture, 'Desligar Bia').click();
    await settle(fixture);
    expect(text(fixture)).toContain('Desligar Bia?');
    button(fixture, 'Desligar atleta').click();
    await settle(fixture);

    const request = http.expectOne(`${ATHLETES}/a1`);
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await settle(fixture);
    http.expectOne(ATHLETES).flush([athlete({ status: 'Released', isAvailable: false })]);
    http.expectOne(TEAMS).flush([team()]);
    await fixture.whenStable();

    expect(text(fixture)).toContain('Bia desligado.');
    expect(text(fixture)).toContain('Desligado');
  });
});
