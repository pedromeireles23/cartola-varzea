import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { Account, AuthService } from '../../core/auth/auth.service';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { OrganizerArea, OrganizerCompetition } from './organizer-area';

const MINE = '/api/v1/organizations/mine';

function conta(roles: string[]): Account {
  return {
    id: 'u1',
    email: 'pessoa@exemplo.local',
    displayName: 'Pessoa',
    emailConfirmed: true,
    roles,
  };
}

const CAMPEONATO: OrganizerCompetition = {
  id: 'c1',
  name: 'Copa da Vila',
  status: 'Published',
  organizationId: 'o1',
  organizationName: 'Liga da Vila',
  owner: true,
};

describe('OrganizerArea', () => {
  let http: HttpTestingController;
  let atual: ReturnType<typeof signal<Account | null>>;

  function criar(account: Account | null): OrganizerArea {
    atual = signal<Account | null>(account);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '/api/v1' },
        { provide: AuthService, useValue: { current: atual } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const area = TestBed.inject(OrganizerArea);
    TestBed.tick();
    return area;
  }

  afterEach(() => http.verify());

  it('quem tem o papel de organizador entra sem perguntar pelas organizações', () => {
    const area = criar(conta(['Organizer']));

    expect(area.canOrganize()).toBe(true);
    http.expectNone(MINE);
  });

  it('o auxiliar, sem papel global, entra pela associação a uma organização', () => {
    const area = criar(conta([]));
    expect(area.canOrganize()).toBe(false);

    http.expectOne(MINE).flush([{ id: 'o1', name: 'Liga da Vila', role: 'Assistant' }]);

    expect(area.canOrganize()).toBe(true);
  });

  it('quem só joga não vê a área, e a falha da lista não inventa acesso', () => {
    const area = criar(conta([]));
    http.expectOne(MINE).flush(null, { status: 503, statusText: 'Unavailable' });

    expect(area.canOrganize()).toBe(false);
  });

  it('sem sessão não pergunta nada', () => {
    const area = criar(null);

    expect(area.canOrganize()).toBe(false);
    http.expectNone(MINE);
  });

  it('sair de um campeonato não apaga outro que já foi aberto no lugar', () => {
    const area = criar(conta(['Organizer']));

    area.enter(CAMPEONATO);
    area.enter({ ...CAMPEONATO, id: 'c2', name: 'Taça do Bairro' });
    area.leave('c1');
    expect(area.competition()?.name).toBe('Taça do Bairro');

    area.leave('c2');
    expect(area.competition()).toBeNull();
  });
});
