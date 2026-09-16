import { isProblemDetails, toApiFailure } from './problem-details';

describe('problem-details', () => {
  it('reconhece um corpo de Problem Details', () => {
    expect(isProblemDetails({ title: 'Not Found', status: 404 })).toBe(true);
    expect(isProblemDetails('erro em texto')).toBe(false);
    expect(isProblemDetails(null)).toBe(false);
  });

  it('preserva o traceId para correlacionar com o log do servidor', () => {
    const falha = toApiFailure(500, { status: 500, traceId: '00-abc-def-00' });

    expect(falha.traceId).toBe('00-abc-def-00');
    expect(falha.status).toBe(500);
  });

  it('usa mensagem em pt-BR por status, sem repassar texto do servidor', () => {
    const detalheInterno = 'System.InvalidOperationException na tabela Users';

    const falha = toApiFailure(500, { status: 500, detail: detalheInterno });

    expect(falha.message).toBe('Algo deu errado do nosso lado. Tente de novo em instantes.');
    expect(falha.message).not.toContain(detalheInterno);
  });

  it('trata status desconhecido e resposta sem corpo', () => {
    expect(toApiFailure(418, null).message).toBe('Algo deu errado. Tente de novo em instantes.');
    expect(toApiFailure(0, null).message).toContain('Verifique sua conexão');
  });
});
