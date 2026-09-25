import {
  ApplicationConfig,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
  inject,
} from '@angular/core';
import {
  provideHttpClient,
  withFetch,
  withInterceptors,
  withXsrfConfiguration,
} from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { registerLocaleData } from '@angular/common';
import { LOCALE_ID } from '@angular/core';
import localePt from '@angular/common/locales/pt';

import { apiErrorInterceptor } from './core/api/api-error.interceptor';
import { AuthService } from './core/auth/auth.service';
import { READ_ONLY_MODE } from './core/demo/read-only-mode';
import { routes } from './app.routes';

registerLocaleData(localePt, 'pt-BR');

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(
      withFetch(),
      withInterceptors([apiErrorInterceptor]),
      // O backend publica o request token no cookie XSRF-TOKEN e espera o
      // cabeçalho X-XSRF-TOKEN de volta em toda mutação.
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
    ),
    // Carrega a sessão e o token antiforgery antes da primeira tela aparecer, para
    // que os guards não precisem decidir com o estado ainda desconhecido.
    provideAppInitializer(() => inject(AuthService).load()),
    { provide: LOCALE_ID, useValue: 'pt-BR' },
    // A conta pública de demonstração só lê: os botões de escrita ficam indisponíveis.
    { provide: READ_ONLY_MODE, useFactory: () => inject(AuthService).isDemoViewer },
  ],
};
