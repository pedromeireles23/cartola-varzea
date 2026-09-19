import { PERFIS } from '../organizer/competition-area/competition-fixtures';
import {
  Ocupante,
  doRetrato,
  formacaoCurta,
  montarCampo,
  reservaDaPosicao,
  titularesDaPosicao,
} from './lineup-model';

const [FUT7, , CAMPO] = PERFIS;

function atleta(parcial: Partial<Ocupante>): Ocupante {
  return {
    kind: 'Athlete',
    assetId: parcial.name ?? 'a',
    name: 'Bia',
    position: 'Midfielder',
    realTeamName: 'União da Vila',
    role: 'Starter',
    price: 8,
    isCaptain: false,
    isAvailable: true,
    ...parcial,
  };
}

describe('montarCampo', () => {
  it('desenha todas as vagas da formação, do ataque ao gol, mesmo vazias', () => {
    const campo = montarCampo(FUT7!, []);

    expect(campo.linhas.map((linha) => [linha.posicao, linha.vagas.length])).toEqual([
      ['Forward', 2],
      ['Midfielder', 2],
      ['Defender', 2],
      ['Goalkeeper', 1],
    ]);
    expect(campo.banco.map((vaga) => vaga.posicao)).toEqual([
      'Goalkeeper',
      'Defender',
      'Midfielder',
      'Forward',
    ]);
    expect(campo.tecnico).toEqual({
      chave: 'Coach',
      papel: 'Coach',
      posicao: null,
      ocupante: null,
    });
    expect(
      [...campo.linhas.flatMap((linha) => linha.vagas), ...campo.banco].every(
        (vaga) => vaga.ocupante === null,
      ),
    ).toBe(true);
  });

  it('no futebol de campo tem quatro defensores na mesma linha', () => {
    const campo = montarCampo(CAMPO!, []);

    expect(campo.linhas.map((linha) => linha.vagas.length)).toEqual([3, 3, 4, 1]);
    expect(formacaoCurta(CAMPO!)).toBe('1-4-3-3');
    expect(formacaoCurta(FUT7!)).toBe('1-2-2-2');
  });

  it('põe cada um na vaga do seu papel e ordena pelo nome dentro da posição', () => {
    const campo = montarCampo(FUT7!, [
      atleta({ name: 'Zeca', position: 'Defender' }),
      atleta({ name: 'Baiano', position: 'Defender' }),
      atleta({ name: 'Tanque', position: 'Defender', role: 'Bench' }),
      atleta({ name: 'Gato', position: 'Goalkeeper', role: 'Bench' }),
      {
        ...atleta({ name: 'Seu Zé', role: 'Coach' }),
        kind: 'Coach',
        position: null,
      },
    ]);

    const defesa = campo.linhas.find((linha) => linha.posicao === 'Defender')!;
    expect(defesa.vagas.map((vaga) => vaga.ocupante?.name)).toEqual(['Baiano', 'Zeca']);
    expect(defesa.vagas.map((vaga) => vaga.chave)).toEqual([
      'Starter-Defender-0',
      'Starter-Defender-1',
    ]);
    expect(campo.banco.map((vaga) => vaga.ocupante?.name ?? null)).toEqual([
      'Gato',
      'Tanque',
      null,
      null,
    ]);
    expect(campo.tecnico.ocupante?.name).toBe('Seu Zé');

    // A troca com o banco só existe dentro da mesma posição.
    expect(reservaDaPosicao(campo, 'Defender')?.ocupante?.name).toBe('Tanque');
    expect(reservaDaPosicao(campo, 'Forward')).toBeNull();
    expect(titularesDaPosicao(campo, 'Defender').map((vaga) => vaga.ocupante?.name)).toEqual([
      'Baiano',
      'Zeca',
    ]);
    expect(titularesDaPosicao(campo, 'Goalkeeper')).toEqual([]);
  });

  it('o retrato congelado vale como estava, sem olhar a disponibilidade de hoje', () => {
    const ocupante = doRetrato({
      kind: 'Athlete',
      assetId: 'a1',
      name: 'Chicão',
      position: 'Forward',
      realTeamId: 't1',
      realTeamName: 'Estrela do Bairro',
      role: 'Starter',
      price: 6.5,
      isCaptain: true,
    });

    expect(ocupante).toMatchObject({ name: 'Chicão', price: 6.5, isAvailable: true });
  });
});
