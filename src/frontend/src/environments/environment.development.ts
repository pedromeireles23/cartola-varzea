/**
 * Configuracao de desenvolvimento. O caminho continua relativo: o dev-server
 * encaminha /api para o backend via proxy.conf.json, entao o codigo nao precisa
 * saber a porta da API nem lidar com CORS.
 */
export const environment = {
  production: false,
  apiBaseUrl: '/api/v1',
} as const;
