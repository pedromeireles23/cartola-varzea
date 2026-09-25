import { InjectionToken, Signal, signal } from '@angular/core';

/**
 * Verdadeiro quando a conta só lê: a conta pública de demonstração (02 §9.1, 04 §15).
 *
 * É um token, e não o `AuthService`, para a UI compartilhada não depender da sessão:
 * sem provedor, ninguém está em modo somente leitura. A aplicação liga o token ao papel
 * `DemoViewer`; o servidor continua sendo quem recusa a escrita.
 */
export const READ_ONLY_MODE = new InjectionToken<Signal<boolean>>('READ_ONLY_MODE', {
  providedIn: 'root',
  factory: () => signal(false),
});

/** O texto que explica por que uma ação de escrita está indisponível. */
export const READ_ONLY_NOTICE_ID = 'aviso-demo';
