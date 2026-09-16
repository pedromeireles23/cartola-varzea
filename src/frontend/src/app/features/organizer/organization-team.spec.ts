import { HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { OrganizationTeamPage } from './organization-team';
import { OrganizationInvitation, OrganizationTeam } from './organization.service';

const TEAM = '/api/v1/organizations/o1/team';
const INVITE = '/api/v1/organizations/o1/team/invitations';

function convite(parcial: Partial<OrganizationInvitation>): OrganizationInvitation {
  return {
    id: 'c1',
    organizationId: 'o1',
    invitedEmail: 'auxiliar@exemplo.local',
    status: 'Pending',
    createdAt: '2026-09-16T12:00:00Z',
    expiresAt: '2026-09-23T12:00:00Z',
    acceptedAt: null,
    revokedAt: null,
    ...parcial,
  };
}

function equipe(parcial: Partial<OrganizationTeam> = {}): OrganizationTeam {
  return {
    organizationId: 'o1',
    organizationName: 'Liga da Várzea',
    assistants: [],
    invitations: [],
    ...parcial,
  };
}

async function estabilizar(fixture: ComponentFixture<OrganizationTeamPage>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

describe('OrganizationTeamPage', () => {
  let http: HttpTestingController;

  beforeAll(() => {
    // O jsdom não implementa o modal nativo; foco e Esc ficam com o E2E.
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
      imports: [OrganizationTeamPage],
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

  async function abrirCom(
    resposta: OrganizationTeam,
  ): Promise<ComponentFixture<OrganizationTeamPage>> {
    const fixture = TestBed.createComponent(OrganizationTeamPage);
    fixture.componentRef.setInput('organizacao', 'o1');
    await fixture.whenStable();
    http.expectOne(TEAM).flush(resposta);
    await fixture.whenStable();
    return fixture;
  }

  function botao(fixture: ComponentFixture<OrganizationTeamPage>, texto: string) {
    const botoes = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')];
    const encontrado = botoes.find((item) => item.textContent?.trim() === texto);
    expect(encontrado, `Botão "${texto}" não encontrado`).toBeDefined();
    return encontrado!;
  }

  function escreverEmail(fixture: ComponentFixture<OrganizationTeamPage>, email: string): void {
    const campo = (fixture.nativeElement as HTMLElement).querySelector(
      'input[type="email"]',
    ) as HTMLInputElement;
    campo.value = email;
    campo.dispatchEvent(new Event('input'));
  }

  it('mostra o nome da organização, auxiliares e convites com a situação em texto', async () => {
    const fixture = await abrirCom(
      equipe({
        assistants: [
          {
            userId: 'u1',
            displayName: 'Ana Auxiliar',
            email: 'ana@exemplo.local',
            joinedAt: '2026-09-16T12:00:00Z',
          },
        ],
        invitations: [convite({}), convite({ id: 'c2', status: 'Revoked' })],
      }),
    );
    const texto = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(texto).toContain('Equipe de Liga da Várzea');
    expect(texto).toContain('Ana Auxiliar');
    expect(texto).toContain('Pendente');
    expect(texto).toContain('Revogado');
  });

  it('recusa e-mail inválido sem chamar a API', async () => {
    const fixture = await abrirCom(equipe());

    escreverEmail(fixture, 'nao-e-email');
    botao(fixture, 'Enviar convite').click();
    await estabilizar(fixture);

    http.expectNone(INVITE);
    const campo = (fixture.nativeElement as HTMLElement).querySelector('input[type="email"]');
    expect(campo?.getAttribute('aria-invalid')).toBe('true');
  });

  it('convida, confirma o envio e recarrega a equipe pela API', async () => {
    const fixture = await abrirCom(equipe());

    escreverEmail(fixture, '  nova@exemplo.local ');
    botao(fixture, 'Enviar convite').click();
    await estabilizar(fixture);

    const envio = http.expectOne(INVITE);
    expect(envio.request.body).toEqual({ email: 'nova@exemplo.local' });
    envio.flush(convite({ invitedEmail: 'nova@exemplo.local' }));
    await estabilizar(fixture);

    http
      .expectOne(TEAM)
      .flush(equipe({ invitations: [convite({ invitedEmail: 'nova@exemplo.local' })] }));
    await estabilizar(fixture);

    const texto = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(texto).toContain('Convite enviado para nova@exemplo.local.');
    expect(texto).toContain('nova@exemplo.local');
  });

  it('revoga um convite pendente depois de confirmar', async () => {
    const fixture = await abrirCom(equipe({ invitations: [convite({})] }));

    botao(fixture, 'Revogar').click();
    await estabilizar(fixture);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Revogar convite de auxiliar@exemplo.local?',
    );

    botao(fixture, 'Revogar convite').click();
    await estabilizar(fixture);

    const revogacao = http.expectOne(`${INVITE}/c1`);
    expect(revogacao.request.method).toBe('DELETE');
    revogacao.flush(convite({ status: 'Revoked' }));
    await estabilizar(fixture);

    http.expectOne(TEAM).flush(equipe({ invitations: [convite({ status: 'Revoked' })] }));
    await estabilizar(fixture);

    const texto = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(texto).toContain('Convite de auxiliar@exemplo.local revogado.');
    expect(texto).toContain('Revogado');
  });

  it('quem não é proprietário vê o aviso de acesso restrito', async () => {
    const fixture = TestBed.createComponent(OrganizationTeamPage);
    fixture.componentRef.setInput('organizacao', 'o1');
    await fixture.whenStable();
    http
      .expectOne(TEAM)
      .flush({ status: 403 }, new HttpErrorResponse({ status: 403, statusText: 'Forbidden' }));
    await fixture.whenStable();

    const texto = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(texto).toContain('Só quem é proprietário da organização gerencia a equipe.');
    expect(texto).not.toContain('Enviar convite');
  });
});
