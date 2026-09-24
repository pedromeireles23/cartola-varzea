import { computed, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { Account, AuthService } from '../../core/auth/auth.service';
import { OrganizerArea } from '../organizer/organizer-area';
import { ProfilePage } from './profile';

const PESSOA: Account = {
  id: 'u1',
  displayName: 'Rafa Souza',
  email: 'rafa@exemplo.local',
  emailConfirmed: true,
  roles: [],
};

describe('ProfilePage', () => {
  let conta: ReturnType<typeof signal<Account | null>>;
  let organiza: ReturnType<typeof signal<boolean>>;
  let logout: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    conta = signal<Account | null>(PESSOA);
    organiza = signal(false);
    logout = vi.fn().mockResolvedValue(undefined);
    TestBed.configureTestingModule({
      imports: [ProfilePage],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            current: conta,
            logout,
            isPlatformAdmin: computed(() => conta()?.roles.includes('PlatformAdmin') ?? false),
          },
        },
        { provide: OrganizerArea, useValue: { canOrganize: organiza } },
      ],
    });
  });

  async function abrir(): Promise<ComponentFixture<ProfilePage>> {
    const fixture = TestBed.createComponent(ProfilePage);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<ProfilePage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function links(fixture: ComponentFixture<ProfilePage>): string[] {
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll('a')].map((a) =>
      a.textContent!.trim(),
    );
  }

  it('chama a tela de Conta, como a navegação, e mostra o e-mail uma vez só', async () => {
    const fixture = await abrir();

    expect((fixture.nativeElement as HTMLElement).querySelector('h1')?.textContent).toBe('Conta');
    expect(texto(fixture).split('rafa@exemplo.local')).toHaveLength(2);
    expect(texto(fixture)).toContain('E-mail confirmado');
    expect(texto(fixture)).toContain('RS');
  });

  it('quem só joga recebe o caminho para pedir acesso de organizador', async () => {
    const fixture = await abrir();

    expect(links(fixture)).toEqual(['Quero organizar']);
  });

  it('quem organiza vai para as organizações, e pedir outra fica em segundo plano', async () => {
    organiza.set(true);
    const fixture = await abrir();

    expect(links(fixture)).toEqual(['Minhas organizações', 'Pedir acesso para outra organização']);
    expect(texto(fixture)).not.toContain('Quero organizar');
  });

  it('a administração da plataforma ganha o atalho para as solicitações', async () => {
    conta.set({ ...PESSOA, roles: ['PlatformAdmin'] });
    organiza.set(true);
    const fixture = await abrir();

    expect(links(fixture)).toContain('Ver solicitações');
  });

  it('encerrar a sessão sai e volta para a entrada', async () => {
    const fixture = await abrir();
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    const botao = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (item) => item.textContent?.includes('Encerrar sessão'),
    )!;
    botao.click();
    await fixture.whenStable();

    expect(logout).toHaveBeenCalledOnce();
    expect(navegar).toHaveBeenCalledWith('/entrar');
  });
});
