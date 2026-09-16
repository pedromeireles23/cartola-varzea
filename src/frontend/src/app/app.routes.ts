import { Routes } from '@angular/router';

import { anonymousGuard, authGuard } from './core/auth/auth.guard';
import { PublicLayout } from './layouts/public-layout/public-layout';

/**
 * Rotas em pt-BR porque aparecem para o usuario (02 §9.1). As areas do
 * organizador e do admin entram nas fases seguintes, com lazy loading por area.
 */
export const routes: Routes = [
  {
    path: '',
    component: PublicLayout,
    children: [
      {
        path: 'sistema',
        title: 'Estado do sistema',
        loadComponent: () =>
          import('./features/system-info/system-info').then((m) => m.SystemInfoPage),
      },
      {
        path: 'entrar',
        title: 'Entrar',
        canActivate: [anonymousGuard],
        loadComponent: () => import('./features/auth/login').then((m) => m.LoginPage),
      },
      {
        path: 'cadastro',
        title: 'Criar conta',
        canActivate: [anonymousGuard],
        loadComponent: () => import('./features/auth/register').then((m) => m.RegisterPage),
      },
      {
        path: 'verificar-email',
        title: 'Confirmação de e-mail',
        loadComponent: () => import('./features/auth/verify-email').then((m) => m.VerifyEmailPage),
      },
      {
        path: 'recuperar-senha',
        title: 'Recuperar senha',
        loadComponent: () =>
          import('./features/auth/password-recovery').then((m) => m.PasswordRecoveryPage),
      },
      {
        path: 'perfil',
        title: 'Seu perfil',
        canActivate: [authGuard],
        loadComponent: () => import('./features/profile/profile').then((m) => m.ProfilePage),
      },
      {
        // Por enquanto na casca pública, como o perfil. A navegação lateral da área de
        // organização chega junto com /organizar.
        path: 'organizar/solicitar',
        title: 'Organizar campeonatos',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/organizer/request-access').then((m) => m.RequestAccessPage),
      },
      {
        // Só exige login: quem não é Platform admin recebe 403 da API e a tela mostra o
        // estado sem permissão. A navegação própria da administração chega depois.
        path: 'admin/solicitacoes',
        title: 'Solicitações de organizador',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/platform-admin/organizer-applications').then(
            (m) => m.OrganizerApplicationsPage,
          ),
      },
      { path: '', pathMatch: 'full', redirectTo: 'sistema' },
    ],
  },
  { path: '**', redirectTo: '' },
];
