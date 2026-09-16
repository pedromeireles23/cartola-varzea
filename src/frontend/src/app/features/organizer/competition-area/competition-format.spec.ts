import { PERFIS } from './competition-fixtures';
import {
  businessDaysText,
  formationText,
  leadTimeLabel,
  leadTimeOptions,
  timeZoneOptions,
} from './competition-format';

describe('competition-format', () => {
  it('descreve a antecedência do fechamento em linguagem de quem organiza', () => {
    expect(leadTimeLabel(0)).toBe('No início da primeira partida');
    expect(leadTimeLabel(30)).toBe('30 minutos antes da primeira partida');
    expect(leadTimeLabel(60)).toBe('1 hora antes da primeira partida');
    expect(leadTimeLabel(180)).toBe('3 horas antes da primeira partida');
    expect(leadTimeLabel(1440)).toBe('1 dia antes da primeira partida');
    expect(leadTimeLabel(4320)).toBe('3 dias antes da primeira partida');
    expect(leadTimeLabel(90)).toBe('90 minutos antes da primeira partida');
  });

  it('mantém um valor fora das opções comuns em ordem', () => {
    const valores = leadTimeOptions(90).map((opcao) => opcao.value);

    expect(valores).toContain('90');
    expect(valores.indexOf('90')).toBe(valores.indexOf('60') + 1);
    expect(leadTimeOptions(60).map((opcao) => opcao.value)).not.toContain('90');
  });

  it('mantém um fuso fora da lista em vez de trocá-lo', () => {
    expect(timeZoneOptions('America/Sao_Paulo').map((opcao) => opcao.value)).not.toContain(
      'Europe/Lisbon',
    );
    expect(timeZoneOptions('Europe/Lisbon').at(-1)).toEqual({
      value: 'Europe/Lisbon',
      label: 'Europe/Lisbon',
    });
  });

  it('descreve formação e dias úteis no singular e no plural', () => {
    expect(formationText(PERFIS[2])).toBe(
      '1 goleiro, 4 defensores, 3 meio-campistas e 3 atacantes',
    );
    expect(businessDaysText(1)).toBe('1 dia útil');
    expect(businessDaysText(3)).toBe('3 dias úteis');
  });
});
