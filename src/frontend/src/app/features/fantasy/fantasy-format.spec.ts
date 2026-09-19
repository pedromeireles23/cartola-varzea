import { closingText, countdownText, credits, teamInitials } from './fantasy-format';

describe('fantasy-format', () => {
  it('escreve créditos no formato das mensagens do servidor', () => {
    expect(credits(8)).toBe('C$ 8,00');
    expect(credits(12.5)).toBe('C$ 12,50');
  });

  it('mostra o fechamento no fuso do campeonato sem converter', () => {
    // 2026-09-20 é um domingo; o texto vem pronto do servidor no fuso do campeonato.
    expect(closingText('2026-09-20T19:00', 'America/Sao_Paulo')).toBe(
      'dom., 20/09 às 19:00 (Horário de Brasília)',
    );
    expect(closingText('2026-09-19T08:30', 'America/Manaus')).toBe(
      'sáb., 19/09 às 08:30 (Amazonas (Manaus))',
    );
  });

  it('conta o que falta do maior para o menor e trata zero como fechado', () => {
    const fecha = '2026-09-20T22:00:00Z';
    const agora = Date.parse(fecha);

    expect(countdownText(fecha, agora - (2 * 86_400 + 3 * 3_600) * 1000)).toBe('2 d 3 h');
    expect(countdownText(fecha, agora - (3 * 3_600 + 12 * 60) * 1000)).toBe('3 h 12 min');
    expect(countdownText(fecha, agora - (12 * 60 + 5) * 1000)).toBe('12 min 5 s');
    expect(countdownText(fecha, agora - 9_000)).toBe('9 s');
    expect(countdownText(fecha, agora)).toBeNull();
    expect(countdownText(fecha, agora + 1_000)).toBeNull();
  });

  it('usa até duas iniciais do time', () => {
    expect(teamInitials('União da Vila')).toBe('UD');
    expect(teamInitials('Estrela')).toBe('E');
  });
});
