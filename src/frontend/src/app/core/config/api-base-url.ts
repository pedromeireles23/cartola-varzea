import { InjectionToken } from '@angular/core';

import { environment } from '../../../environments/environment';

/**
 * Base da API. E um token injetavel para que testes de componente apontem para
 * outro endereco sem depender do arquivo de ambiente.
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => environment.apiBaseUrl,
});
