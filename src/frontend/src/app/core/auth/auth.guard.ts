import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthService } from './auth.service';

/**
 * Impede a navegação para áreas privadas e preserva o destino.
 *
 * É apenas experiência de navegação: quem autoriza de verdade é a API. Um guard
 * burlado no browser não abre nenhuma porta no servidor.
 */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.ready()) {
    await auth.load();
  }

  if (auth.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/entrar'], { queryParams: { destino: state.url } });
};

/** Evita que quem já entrou caia nas telas de cadastro e entrada. */
export const anonymousGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.ready()) {
    await auth.load();
  }

  return auth.isAuthenticated() ? router.createUrlTree(['/inicio']) : true;
};
