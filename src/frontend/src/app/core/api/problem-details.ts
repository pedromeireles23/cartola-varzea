/**
 * Erro em RFC 9457 Problem Details, como o backend responde (03-arquitetura §9).
 */
export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly instance?: string;
  /** Correlaciona a falha com o log do servidor. */
  readonly traceId?: string;
  /** Codigo estavel de erro de dominio, quando houver. */
  readonly code?: string;
}

/** Falha de API ja traduzida para algo que a interface consegue mostrar. */
export interface ApiFailure {
  /** Mensagem em pt-BR para o usuario. */
  readonly message: string;
  readonly status: number;
  readonly traceId?: string;
  readonly code?: string;
}

/**
 * Mensagens por status. O texto do servidor nao e exibido direto: em producao ele
 * e generico de proposito, e o tom aqui segue o 02-design-system §3.
 */
const MESSAGES: Readonly<Record<number, string>> = {
  0: 'Não foi possível falar com o servidor. Verifique sua conexão.',
  400: 'Os dados enviados não são válidos.',
  401: 'Entre na sua conta para continuar.',
  403: 'Você não tem permissão para isso.',
  404: 'Não encontramos o que você procura.',
  409: 'Alguém alterou esses dados enquanto você editava. Atualize e tente de novo.',
  429: 'Muitas tentativas em pouco tempo. Aguarde um instante.',
  500: 'Algo deu errado do nosso lado. Tente de novo em instantes.',
};

const FALLBACK_MESSAGE = 'Algo deu errado. Tente de novo em instantes.';

/** Códigos estáveis da API que pedem uma mensagem própria, mais precisa que a do status. */
export const DEMO_READ_ONLY = 'demo_read_only';

const CODE_MESSAGES: Readonly<Record<string, string>> = {
  [DEMO_READ_ONLY]: 'Esta é uma conta de demonstração: dá para navegar por tudo, mas nada é salvo.',
};

export function isProblemDetails(value: unknown): value is ProblemDetails {
  return typeof value === 'object' && value !== null && ('title' in value || 'status' in value);
}

export function toApiFailure(status: number, body: unknown): ApiFailure {
  const problem = isProblemDetails(body) ? body : undefined;

  return {
    message:
      (problem?.code ? CODE_MESSAGES[problem.code] : undefined) ??
      MESSAGES[status] ??
      FALLBACK_MESSAGE,
    status,
    traceId: problem?.traceId,
    code: problem?.code,
  };
}
