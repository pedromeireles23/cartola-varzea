/**
 * Configuracao de producao. A API e servida na mesma origem do frontend
 * (03-arquitetura §2), o que mantem cookie e antiforgery simples.
 */
export const environment = {
  production: true,
  apiBaseUrl: '/api/v1',
} as const;
