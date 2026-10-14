import { AthletePosition } from '../athlete.service';

export interface ListedAthlete {
  readonly name: string;
  /** `null` quando a linha não diz a posição: a tela pede para escolher. */
  readonly position: AthletePosition | null;
}

/**
 * Como a várzea escreve a posição numa lista de WhatsApp, sem acento e em minúsculas.
 * Entram as posições do futsal (fixo, ala, pivô), que caem na posição do fantasy mais
 * próxima.
 */
const POSITIONS: Readonly<Record<string, AthletePosition>> = {
  goleiro: 'Goalkeeper',
  goleira: 'Goalkeeper',
  gol: 'Goalkeeper',
  goal: 'Goalkeeper',
  arqueiro: 'Goalkeeper',
  gk: 'Goalkeeper',
  g: 'Goalkeeper',
  defensor: 'Defender',
  defensora: 'Defender',
  defesa: 'Defender',
  zagueiro: 'Defender',
  zagueira: 'Defender',
  zaga: 'Defender',
  zag: 'Defender',
  lateral: 'Defender',
  beque: 'Defender',
  fixo: 'Defender',
  def: 'Defender',
  d: 'Defender',
  'meio-campista': 'Midfielder',
  'meio campista': 'Midfielder',
  meiocampista: 'Midfielder',
  'meio-campo': 'Midfielder',
  'meio campo': 'Midfielder',
  meio: 'Midfielder',
  meia: 'Midfielder',
  volante: 'Midfielder',
  armador: 'Midfielder',
  ala: 'Midfielder',
  mc: 'Midfielder',
  m: 'Midfielder',
  atacante: 'Forward',
  ataque: 'Forward',
  centroavante: 'Forward',
  'centro-avante': 'Forward',
  ponta: 'Forward',
  pivo: 'Forward',
  ata: 'Forward',
  a: 'Forward',
};

/** Separadores entre nome e posição: ponto e vírgula, tabulação, vírgula ou traço. */
const SEPARATOR = /\s*(?:;|\t|,|\s[-–—]\s|\s\/\s)\s*/;

/** Numeração da lista ou da camisa antes do nome: `1.`, `10 -`, `7)`. */
const NUMBERING = /^\d{1,3}\s*(?:[.)º°-]|\s)\s*/;

/**
 * Lê uma lista colada, um atleta por linha. A posição pode vir depois de um separador
 * (`Bia - meia`, `Bia; goleiro`) ou como última palavra (`Bia meia`); a numeração da
 * lista ou da camisa no começo é ignorada. Linha sem posição reconhecida fica inteira
 * como nome — melhor pedir a posição do que cortar um sobrenome.
 */
export function parseRosterList(text: string): ListedAthlete[] {
  return text
    .split(/\r?\n/)
    .map((line) => line.trim().replace(NUMBERING, '').trim())
    .filter((line) => line.length > 0)
    .map(parseLine);
}

function parseLine(line: string): ListedAthlete {
  const parts = line.split(SEPARATOR).filter((part) => part.length > 0);
  if (parts.length >= 2) {
    const position = positionOf(parts.at(-1)!);
    if (position) {
      return { name: parts.slice(0, -1).join(' ').trim(), position };
    }
  }

  // Sem separador: a posição pode ser a última palavra, ou as duas últimas ("meio campo").
  // Depois de "de", "da"… a palavra é do apelido ("Tanque de Gol"), e letra solta só vale
  // depois de um separador: "Zé A" é mais provável que seja um nome.
  const words = line.split(/\s+/);
  for (const size of [2, 1]) {
    const tail = words.slice(-size).join(' ');
    const before = words.at(-size - 1)?.toLocaleLowerCase('pt-BR') ?? '';
    if (words.length > size && tail.length > 1 && !CONNECTORS.has(before)) {
      const position = positionOf(tail);
      if (position) {
        return { name: words.slice(0, -size).join(' '), position };
      }
    }
  }

  return { name: line, position: null };
}

const CONNECTORS = new Set(['de', 'da', 'do', 'das', 'dos', 'e']);

/** Posição reconhecida no texto, sem acento, maiúscula nem ponto final. */
export function positionOf(text: string): AthletePosition | null {
  const key = text
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLocaleLowerCase('pt-BR')
    .replace(/[.()]/g, '')
    .trim();
  return POSITIONS[key] ?? null;
}
