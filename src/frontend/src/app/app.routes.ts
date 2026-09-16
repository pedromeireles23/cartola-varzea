import { Routes } from '@angular/router';

import { PublicLayout } from './layouts/public-layout/public-layout';

/**
 * Rotas em pt-BR porque aparecem para o usuario (02 §9.1). As areas do
 * participante, do organizador e do admin entram nas fases seguintes, com lazy
 * loading por area funcional.
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
      { path: '', pathMatch: 'full', redirectTo: 'sistema' },
    ],
  },
  { path: '**', redirectTo: '' },
];
