import { SelectOption } from '../../../shared/ui';
import { ModalityProfile } from '../competition.service';

/**
 * Fusos oferecidos na configuração. São os de horário distinto no Brasil, público do
 * MVP; o servidor aceita qualquer identificador IANA, e um valor fora da lista
 * continua aparecendo para não ser trocado sem querer.
 */
export const TIME_ZONE_OPTIONS: readonly SelectOption[] = [
  { value: 'America/Sao_Paulo', label: 'Horário de Brasília' },
  { value: 'America/Noronha', label: 'Fernando de Noronha' },
  { value: 'America/Manaus', label: 'Amazonas (Manaus)' },
  { value: 'America/Cuiaba', label: 'Mato Grosso (Cuiabá)' },
  { value: 'America/Campo_Grande', label: 'Mato Grosso do Sul (Campo Grande)' },
  { value: 'America/Porto_Velho', label: 'Rondônia (Porto Velho)' },
  { value: 'America/Boa_Vista', label: 'Roraima (Boa Vista)' },
  { value: 'America/Rio_Branco', label: 'Acre (Rio Branco)' },
];

/** Antecedências comuns; o limite do servidor é de 72 horas. */
const LEAD_TIMES_IN_MINUTES = [0, 30, 60, 120, 180, 360, 720, 1440, 2880, 4320] as const;

export function timeZoneLabel(timeZoneId: string): string {
  return TIME_ZONE_OPTIONS.find((option) => option.value === timeZoneId)?.label ?? timeZoneId;
}

export function timeZoneOptions(current: string): readonly SelectOption[] {
  return TIME_ZONE_OPTIONS.some((option) => option.value === current)
    ? TIME_ZONE_OPTIONS
    : [...TIME_ZONE_OPTIONS, { value: current, label: current }];
}

export function leadTimeLabel(minutes: number): string {
  if (minutes === 0) {
    return 'No início da primeira partida';
  }

  if (minutes % 1440 === 0) {
    const dias = minutes / 1440;
    return `${dias} ${dias === 1 ? 'dia' : 'dias'} antes da primeira partida`;
  }

  if (minutes % 60 === 0) {
    const horas = minutes / 60;
    return `${horas} ${horas === 1 ? 'hora' : 'horas'} antes da primeira partida`;
  }

  return `${minutes} minutos antes da primeira partida`;
}

export function leadTimeOptions(current: number): readonly SelectOption[] {
  const valores: number[] = LEAD_TIMES_IN_MINUTES.includes(
    current as (typeof LEAD_TIMES_IN_MINUTES)[number],
  )
    ? [...LEAD_TIMES_IN_MINUTES]
    : [...LEAD_TIMES_IN_MINUTES, current].sort((a, b) => a - b);

  return valores.map((minutos) => ({ value: String(minutos), label: leadTimeLabel(minutos) }));
}

function plural(quantidade: number, singular: string, plural: string): string {
  return `${quantidade} ${quantidade === 1 ? singular : plural}`;
}

/** "1 goleiro, 2 defensores, 2 meio-campistas e 2 atacantes". */
export function formationText(profile: ModalityProfile): string {
  const { goalkeepers, defenders, midfielders, forwards } = profile.formation;
  return (
    `${plural(goalkeepers, 'goleiro', 'goleiros')}, ` +
    `${plural(defenders, 'defensor', 'defensores')}, ` +
    `${plural(midfielders, 'meio-campista', 'meio-campistas')} e ` +
    `${plural(forwards, 'atacante', 'atacantes')}`
  );
}

export function businessDaysText(dias: number): string {
  return plural(dias, 'dia útil', 'dias úteis');
}
