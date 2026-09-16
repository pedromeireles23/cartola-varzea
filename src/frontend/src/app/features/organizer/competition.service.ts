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
}

export interface CompetitionDetails extends CompetitionSettings {
  readonly id: string;
  readonly organizationId: string;
  readonly organizationName: string;
  readonly viewerRole: OrganizationRole;
  readonly status: CompetitionStatus;
  readonly canChangeModality: boolean;
  readonly modalityProfile: ModalityProfile;
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
