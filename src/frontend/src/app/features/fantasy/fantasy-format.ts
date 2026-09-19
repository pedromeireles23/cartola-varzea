import { AthletePosition } from '../organizer/competition-area/athlete.service';
import { timeZoneLabel } from '../organizer/competition-area/competition-format';
import { AssetKind } from './fantasy.service';

export const POSITION_LABELS: Readonly<Record<AthletePosition, string>> = {
  Goalkeeper: 'Goleiro',
  Defender: 'Defensor',
  Midfielder: 'Meio-campista',
  Forward: 'Atacante',
};

export const POSITION_ORDER: readonly AthletePosition[] = [
  'Goalkeeper',
  'Defender',
  'Midfielder',
  'Forward',
];

/** Posição do atleta ou "Técnico"; é o rótulo que o participante lê no mercado e no campo. */
export function assetRoleLabel(kind: AssetKind, position: AthletePosition | null): string {
  return kind === 'Coach' || position === null ? 'Técnico' : POSITION_LABELS[position];
}

/** "C$ 12,50", o mesmo formato das mensagens do servidor. */
export function credits(value: number): string {
  return `C$ ${value.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

const WEEKDAYS = ['dom.', 'seg.', 'ter.', 'qua.', 'qui.', 'sex.', 'sáb.'] as const;

/**
 * "sáb., 20/09 às 19:00 (Horário de Brasília)".
 *
 * O servidor já manda o instante no fuso do campeonato; a tela só reorganiza os campos
 * e nunca converte fuso, para que todo mundo leia o mesmo horário do regulamento.
 */
export function closingText(closesAtLocal: string, timeZoneId: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(closesAtLocal);
  if (!match) {
    return closesAtLocal;
  }

  const [, year, month, day, hour, minute] = match;
  const weekday = new Date(Date.UTC(Number(year), Number(month) - 1, Number(day))).getUTCDay();
  return `${WEEKDAYS[weekday]}, ${day}/${month} às ${hour}:${minute} (${timeZoneLabel(timeZoneId)})`;
}

/**
 * Quanto falta até o fechamento, do maior para o menor: "2 d 3 h", "3 h 12 min",
 * "12 min 5 s". Zero ou negativo vira `null`: a tela deve tratar como fechado.
 */
export function countdownText(closesAt: string, now: number): string | null {
  const remaining = Math.floor((Date.parse(closesAt) - now) / 1000);
  if (!Number.isFinite(remaining) || remaining <= 0) {
    return null;
  }

  const days = Math.floor(remaining / 86_400);
  const hours = Math.floor((remaining % 86_400) / 3_600);
  const minutes = Math.floor((remaining % 3_600) / 60);
  const seconds = remaining % 60;
  if (days > 0) {
    return `${days} d ${hours} h`;
  }
  if (hours > 0) {
    return `${hours} h ${minutes} min`;
  }
  return minutes > 0 ? `${minutes} min ${seconds} s` : `${seconds} s`;
}

/** Até duas iniciais do nome do time, como o escudo de fallback da área de organização. */
export function teamInitials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0])
    .join('')
    .toLocaleUpperCase('pt-BR');
}
