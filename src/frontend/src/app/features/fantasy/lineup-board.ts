import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { Plus, TriangleAlert } from 'lucide';

import { Icon } from '../../shared/ui';
import { assetRoleLabel, credits, positionGroupLabel, teamInitials } from './fantasy-format';
import { Campo, LinhaDoCampo, Vaga } from './lineup-model';

export type LineupView = 'campo' | 'lista';

/** Abreviação visível na vaga; o nome completo vai junto para o leitor de tela. */
const SIGLAS = {
  Goalkeeper: 'GOL',
  Defender: 'DEF',
  Midfielder: 'MEI',
  Forward: 'ATA',
} as const;

/** "Goleiro titular", "Reserva de defensor" ou "Técnico": o papel da vaga por extenso. */
export function descricaoDaVaga(vaga: Vaga): string {
  if (vaga.papel === 'Coach' || vaga.posicao === null) {
    return 'Técnico';
  }
  const posicao = assetRoleLabel('Athlete', vaga.posicao);
  return vaga.papel === 'Bench'
    ? `Reserva de ${posicao.toLocaleLowerCase('pt-BR')}`
    : `${posicao} titular`;
}

export function siglaDaVaga(vaga: Vaga): string {
  return vaga.posicao === null ? 'TEC' : SIGLAS[vaga.posicao];
}

/**
 * O time desenhado (02 §8 "Lineup pitch", 06 §3): o gramado com as linhas da formação,
 * o banco colado embaixo e o técnico ao lado do banco, ou a mesma escalação em lista.
 *
 * Toda vaga é um botão do mesmo tamanho — ocupada abre as ações do atleta, vazia abre a
 * escolha já filtrada pela posição. Nada depende de arrastar nem de passar o cursor. O
 * leitor de tela ouve uma frase por vaga ("Atacante titular: Bia, União da Vila, C$ 8,00,
 * capitão"), e as partes visuais ficam escondidas dele para não saírem coladas.
 */
@Component({
  selector: 'app-lineup-board',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, NgTemplateOutlet],
  templateUrl: './lineup-board.html',
  styleUrls: ['./lineup-board.scss', './lineup-board-list.scss'],
})
export class LineupBoard {
  readonly campo = input.required<Campo>();
  readonly formacao = input.required<string>();
  readonly modo = input<LineupView>('campo');
  /** Com o mercado fechado as vagas viram leitura: nada abre. */
  readonly editavel = input(false);
  /** A vaga com o painel aberto, marcada com o anel de seleção. */
  readonly selecionada = input<string | null>(null);

  readonly escolher = output<Vaga>();

  protected readonly icons = { plus: Plus, alert: TriangleAlert } as const;

  /** Titulares do gol ao ataque, na ordem em que a lista é lida. */
  protected readonly titularesEmLista = computed(() =>
    [...this.campo().linhas].reverse().flatMap((linha) => linha.vagas),
  );

  protected rotuloDaLinha(linha: LinhaDoCampo): string {
    return positionGroupLabel(linha.posicao, linha.vagas.length);
  }

  protected sigla(vaga: Vaga): string {
    return siglaDaVaga(vaga);
  }

  protected descricao(vaga: Vaga): string {
    return descricaoDaVaga(vaga);
  }

  protected minusculas(texto: string): string {
    return texto.toLocaleLowerCase('pt-BR');
  }

  protected iniciais(time: string): string {
    return teamInitials(time);
  }

  protected creditos(valor: number): string {
    return credits(valor);
  }

  /** "Atacante titular: Bia, União da Vila, C$ 8,00, capitão". */
  protected rotuloAcessivel(vaga: Vaga): string {
    const ocupante = vaga.ocupante!;
    return [
      `${this.descricao(vaga)}: ${ocupante.name}`,
      ocupante.realTeamName,
      credits(ocupante.price),
      ...(ocupante.isCaptain ? ['capitão'] : []),
      ...(ocupante.isAvailable ? [] : ['indisponível']),
    ].join(', ');
  }
}
