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

  it('explica o bloqueio da conta de demonstração pelo código, não pelo título', () => {
    const falha = toApiFailure(403, {
      status: 403,
      title: 'Modo demonstração',
      code: 'demo_read_only',
    });

    expect(falha.code).toBe('demo_read_only');
    expect(falha.message).toContain('conta de demonstração');
    expect(toApiFailure(403, { status: 403 }).message).toBe('Você não tem permissão para isso.');
  });
});
