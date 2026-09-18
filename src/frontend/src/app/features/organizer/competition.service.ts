import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../core/config/api-base-url';
import { OrganizationRole } from './organization.service';

export type Modality = 'Fut7' | 'Futsal' | 'Field';

export type CompetitionStatus = 'Draft' | 'Published';

/** Regras vigentes de uma modalidade (01 §7 e §9). */
export interface ModalityProfile {
  readonly modality: Modality;
  readonly version: number;
  readonly starters: number;
  readonly formation: {
    readonly goalkeepers: number;
    readonly defenders: number;
    readonly midfielders: number;
    readonly forwards: number;
  };
  readonly benchSize: number;
  readonly squadAthletes: number;
  readonly budget: number;
  readonly minimumAthletesPerRealTeam: number;
  readonly realTeamLimits: readonly {
    readonly activeRealTeams: number;
    readonly maxStarters: number;
    readonly maxAthletes: number;
  }[];
}

export interface CompetitionSummary {
  readonly id: string;
  readonly name: string;
  readonly season: string;
  readonly modality: Modality;
  readonly status: CompetitionStatus;
  readonly updatedAt: string;
}

/** Os campos que a organização escolhe; o resto o servidor deriva. */
export interface CompetitionSettings {
  readonly name: string;
  readonly season: string;
  readonly modality: Modality;
  readonly timeZoneId: string;
  readonly marketCloseLeadTimeMinutes: number;
  readonly resultsSlaBusinessDays: number;
  readonly correctionWindowBusinessDays: number;
  /** Prazo de inscrição escolhido, como `2026-10-04T18:00`; vazio usa o padrão. */
  readonly registrationDeadlineLocal?: string | null;
}

/** O prazo de inscrição que vale agora, já no fuso do campeonato. */
export interface RegistrationWindow {
  readonly closesAtLocal: string | null;
  readonly source: 'Configured' | 'FirstStageLastRound' | 'NotYetDefined';
  readonly roundName: string | null;
  readonly isOpen: boolean;
}

export interface CompetitionDetails extends CompetitionSettings {
  readonly id: string;
  readonly organizationId: string;
  readonly organizationName: string;
  readonly viewerRole: OrganizationRole;
  readonly status: CompetitionStatus;
  readonly canChangeModality: boolean;
  readonly modalityProfile: ModalityProfile;
  readonly registrationWindow: RegistrationWindow;
  readonly createdAt: string;
  readonly updatedAt: string;
  /** Precisa voltar na edição: o servidor recusa salvar sobre uma leitura antiga. */
  readonly version: string;
}

/** Espelham as regras do servidor, que continua sendo quem decide. */
export const COMPETITION_NAME_MIN = 3;
export const COMPETITION_NAME_MAX = 120;
export const COMPETITION_SEASON_MAX = 40;
export const BUSINESS_DAYS_MIN = 1;
export const BUSINESS_DAYS_MAX = 10;
export const MARKET_CLOSE_LEAD_MAX_MINUTES = 72 * 60;

export const DEFAULT_SETTINGS: Omit<CompetitionSettings, 'name' | 'season' | 'modality'> = {
  timeZoneId: 'America/Sao_Paulo',
  marketCloseLeadTimeMinutes: 0,
  resultsSlaBusinessDays: 2,
  correctionWindowBusinessDays: 3,
};

/** Explica o prazo de inscrição em uma frase, do jeito que a organização decide. */
export function registrationWindowText(window: RegistrationWindow): string {
  const quando = window.closesAtLocal ? localText(window.closesAtLocal) : null;
  if (!window.isOpen) {
    return `Encerradas em ${quando}.`;
  }

  switch (window.source) {
    case 'Configured':
      return `Abertas até ${quando}, prazo definido pela organização.`;
    case 'FirstStageLastRound':
      return quando
        ? `Abertas até ${quando}, fechamento do mercado de ${window.roundName}.`
        : `Abertas até o fechamento do mercado de ${window.roundName}, que ainda não abriu.`;
    default:
      return 'Abertas. O prazo será o fechamento do mercado da última rodada da primeira fase.';
  }
}

/** `2026-10-04T18:00` vira `04/10/2026 18:00`, sem conta de fuso no navegador. */
function localText(local: string): string {
  const [data, hora] = local.split('T');
  const [ano, mes, dia] = data.split('-');
  return `${dia}/${mes}/${ano} ${hora}`;
}

export const MODALITY_LABELS: Readonly<Record<Modality, string>> = {
  Fut7: 'Fut7',
  Futsal: 'Futsal',
  Field: 'Futebol de campo',
};

export const STATUS_LABELS: Readonly<Record<CompetitionStatus, string>> = {
  Draft: 'Rascunho',
  Published: 'Publicado',
};

@Injectable({ providedIn: 'root' })
export class CompetitionService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  modalityProfiles(): Observable<ModalityProfile[]> {
    return this.http.get<ModalityProfile[]>(`${this.baseUrl}/modality-profiles`);
  }

  listByOrganization(organizationId: string): Observable<CompetitionSummary[]> {
    return this.http.get<CompetitionSummary[]>(this.organizationUrl(organizationId));
  }

  createDraft(
    organizationId: string,
    settings: CompetitionSettings,
  ): Observable<CompetitionDetails> {
    return this.http.post<CompetitionDetails>(this.organizationUrl(organizationId), settings);
  }

  get(competitionId: string): Observable<CompetitionDetails> {
    return this.http.get<CompetitionDetails>(this.settingsUrl(competitionId));
  }

  /** Responde 409 quando alguém salvou depois da leitura que gerou `version`. */
  updateSettings(
    competitionId: string,
    settings: CompetitionSettings,
    version: string,
  ): Observable<CompetitionDetails> {
    return this.http.put<CompetitionDetails>(this.settingsUrl(competitionId), {
      ...settings,
      version,
    });
  }

  private organizationUrl(organizationId: string): string {
    return `${this.baseUrl}/organizations/${encodeURIComponent(organizationId)}/competitions`;
  }

  private settingsUrl(competitionId: string): string {
    return `${this.baseUrl}/competitions/${encodeURIComponent(competitionId)}/settings`;
  }
}
