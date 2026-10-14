import { parseRosterList, positionOf } from './roster-list';

describe('roster-list', () => {
  it('lê nome e posição com os separadores de uma lista de WhatsApp', () => {
    expect(
      parseRosterList(
        ['Bia - meia', 'Nena; goleira', 'Tatá, atacante', 'Duda\tzagueiro', 'Pinga / volante'].join(
          '\n',
        ),
      ),
    ).toEqual([
      { name: 'Bia', position: 'Midfielder' },
      { name: 'Nena', position: 'Goalkeeper' },
      { name: 'Tatá', position: 'Forward' },
      { name: 'Duda', position: 'Defender' },
      { name: 'Pinga', position: 'Midfielder' },
    ]);
  });

  it('ignora a numeração da lista e da camisa e as linhas em branco', () => {
    expect(parseRosterList('1. Bia meia\r\n\r\n10 - Tatá - ata\n7) Nena gol\n')).toEqual([
      { name: 'Bia', position: 'Midfielder' },
      { name: 'Tatá', position: 'Forward' },
      { name: 'Nena', position: 'Goalkeeper' },
    ]);
  });

  it('reconhece a posição como última palavra, inclusive em duas palavras', () => {
    expect(parseRosterList('Zé da Silva meio campo\nMão de Ferro goleiro')).toEqual([
      { name: 'Zé da Silva', position: 'Midfielder' },
      { name: 'Mão de Ferro', position: 'Goalkeeper' },
    ]);
  });

  it('linha sem posição fica inteira como nome, para a tela pedir a posição', () => {
    expect(parseRosterList('Baixinho\nGol\nZé A')).toEqual([
      { name: 'Baixinho', position: null },
      { name: 'Gol', position: null },
      { name: 'Zé A', position: null },
    ]);
  });

  it('a palavra depois de "de" é do apelido, não a posição', () => {
    expect(parseRosterList('Tanque de Gol\nTanque de Gol - atacante')).toEqual([
      { name: 'Tanque de Gol', position: null },
      { name: 'Tanque de Gol', position: 'Forward' },
    ]);
  });

  it('as posições do futsal caem na posição mais próxima do fantasy', () => {
    expect(positionOf('Fixo')).toBe('Defender');
    expect(positionOf('ala')).toBe('Midfielder');
    expect(positionOf('Pivô')).toBe('Forward');
    expect(positionOf('MEIO-CAMPISTA')).toBe('Midfielder');
    expect(positionOf('técnico')).toBeNull();
  });
});
