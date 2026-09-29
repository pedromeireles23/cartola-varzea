import { Routes } from '@angular/router';

export const developmentRoutes: Routes = [
  {
    path: 'sistema/visual',
    title: 'Laboratório visual',
    loadComponent: () =>
      import('./features/system-info/design-system-showcase').then(
        (module) => module.DesignSystemShowcasePage,
      ),
  },
];
