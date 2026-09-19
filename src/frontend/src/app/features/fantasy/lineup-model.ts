import { AthletePosition } from '../organizer/competition-area/athlete.service';
import { ModalityProfile } from '../organizer/competition.service';
import { POSITION_ORDER } from './fantasy-format';
import { AssetKind, FrozenSlot, SquadRole, SquadSlot } from './fantasy.service';

/** Quem ocupa uma vaga, venha do elenco corrente ou do retrato congelado no fechamento. */
export interface Ocupante {
  readonly kind: AssetKind;
  readonly assetId: string;
  readonly name: string;
  readonly position: AthletePosition | null;
  readonly realTeamName: string;
  readonly role: SquadRole;
  readonly price: number;
  readonly isCaptain: boolean;
  /** No retrato é sempre verdadeiro: ele guarda o que valeu, não o catálogo de hoje. */
  readonly isAvailable: boolean;
}

export interface Vaga {
  /** Estável entre leituras: a vaga continua a mesma quando quem a ocupa muda. */
  readonly chave: string;
  readonly papel: SquadRole;
  /** Nula na vaga do técnico. */
  readonly posicao: AthletePosition | null;
  readonly ocupante: Ocupante | null;
}

export interface LinhaDoCampo {
  readonly posicao: AthletePosition;
  readonly vagas: readonly Vaga[];
}

/** O campo desenhado a partir da formação da modalidade (02 §8, "Lineup pitch"). */
export interface Campo {
  /** Do ataque para o gol, como o campo aparece na tela. */
  readonly linhas: readonly LinhaDoCampo[];
  /** Um reserva por posição, do gol ao ataque. */
  readonly banco: readonly Vaga[];
  readonly tecnico: Vaga;
}

const FORMACAO: Readonly<Record<AthletePosition, keyof ModalityProfile['formation']>> = {
  Goalkeeper: 'goalkeepers',
  Defender: 'defenders',
  Midfielder: 'midfielders',
  Forward: 'forwards',
};

export function doElenco(slot: SquadSlot): Ocupante {
  return {
    kind: slot.kind,
    assetId: slot.assetId,
    name: slot.name,
    position: slot.position,
    realTeamName: slot.realTeamName,
    role: slot.role,
    price: slot.currentPrice,
    isCaptain: slot.isCaptain,
    isAvailable: slot.isAvailable,
  };
}

export function doRetrato(slot: FrozenSlot): Ocupante {
  return {
    kind: slot.kind,
    assetId: slot.assetId,
    name: slot.name,
    position: slot.position,
    realTeamName: slot.realTeamName,
    role: slot.role,
    price: slot.price,
    isCaptain: slot.isCaptain,
    isAvailable: true,
  };
}

/** "1-2-2-2", do gol ao ataque, como o regulamento escreve a formação. */
export function formacaoCurta(profile: ModalityProfile): string {
  return POSITION_ORDER.map((posicao) => profile.formation[FORMACAO[posicao]]).join('-');
}

/**
 * Distribui o elenco nas vagas da formação. Toda vaga existe, ocupada ou não: é a vaga
 * vazia que abre o mercado filtrado. Dentro da mesma posição a ordem é pelo nome, para
 * que o campo não mude de lugar a cada leitura.
 */
export function montarCampo(profile: ModalityProfile, ocupantes: readonly Ocupante[]): Campo {
  const porNome = [...ocupantes].sort((a, b) => a.name.localeCompare(b.name, 'pt-BR'));
  const vagasDe = (papel: SquadRole, posicao: AthletePosition, quantidade: number): Vaga[] => {
    const daPosicao = porNome.filter(
      (ocupante) =>
        ocupante.kind === 'Athlete' && ocupante.role === papel && ocupante.position === posicao,
    );
    return Array.from({ length: quantidade }, (_, indice) => ({
      chave: `${papel}-${posicao}-${indice}`,
      papel,
      posicao,
      ocupante: daPosicao[indice] ?? null,
    }));
  };

  return {
    linhas: [...POSITION_ORDER]
      .reverse()
      .map((posicao) => ({
        posicao,
        vagas: vagasDe('Starter', posicao, profile.formation[FORMACAO[posicao]]),
      }))
      .filter((linha) => linha.vagas.length > 0),
    banco: POSITION_ORDER.flatMap((posicao) => vagasDe('Bench', posicao, 1)),
    tecnico: {
      chave: 'Coach',
      papel: 'Coach',
      posicao: null,
      ocupante: porNome.find((ocupante) => ocupante.kind === 'Coach') ?? null,
    },
  };
}

/** Titulares da mesma posição do reserva: é com eles que o banco troca. */
export function titularesDaPosicao(campo: Campo, posicao: AthletePosition | null): Vaga[] {
  return (
    campo.linhas
      .find((linha) => linha.posicao === posicao)
      ?.vagas.filter((vaga) => vaga.ocupante !== null) ?? []
  );
}

/** O reserva da posição, se houver alguém no banco. */
export function reservaDaPosicao(campo: Campo, posicao: AthletePosition | null): Vaga | null {
  return campo.banco.find((vaga) => vaga.posicao === posicao && vaga.ocupante !== null) ?? null;
}
