import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../../core/config/api-base-url';

/** Tipos importáveis pela tela do campeonato. Estatísticas são importadas na rodada. */
export type ImportKind = 'times' | 'atletas' | 'tecnicos' | 'partidas';

export interface ImportColumn {
  readonly name: string;
  readonly required: boolean;
  readonly description: string;
  readonly example: string;
}

export interface ImportTemplate {
  /** Nome em inglês do domínio (`Teams`, `Athletes`, `Coaches`, `Matches`). */
  readonly kind: string;
  readonly version: number;
  readonly label: string;
  readonly fileName: string;
  readonly columns: readonly ImportColumn[];
}

export interface ImportSummary {
  readonly rows: number;
  readonly created: number;
  readonly updated: number;
  readonly unchanged: number;
}

export interface ImportIssue {
  readonly line: number;
  readonly column: string;
  readonly message: string;
}

export interface ImportResult {
  readonly outcome: 'Completed' | 'Invalid' | 'Rejected' | 'NotFound';
  readonly summary: ImportSummary;
  readonly issues: readonly ImportIssue[];
  readonly rejectionMessage: string | null;
  /** O que as contagens não mostram, como rodadas que serão criadas. */
  readonly notes?: readonly string[];
}

/** Códigos estáveis do Problem Details da importação. */
export const IMPORT_REJECTED_CODE = 'import_file_rejected';
export const IMPORT_INVALID_CODE = 'import_rows_invalid';

export const KIND_LABELS: Readonly<Record<ImportKind, string>> = {
  times: 'Times',
  atletas: 'Atletas',
  tecnicos: 'Técnicos',
  partidas: 'Partidas',
};

/** O domínio nomeia em inglês; a rota e a interface, em português. */
export const KIND_BY_DOMAIN: Readonly<Record<string, ImportKind>> = {
  Teams: 'times',
  Athletes: 'atletas',
  Coaches: 'tecnicos',
  Matches: 'partidas',
};

/** Espelha o limite do servidor, para avisar antes de subir o arquivo à toa. */
export const MAX_FILE_BYTES = 1024 * 1024;

@Injectable({ providedIn: 'root' })
export class ImportService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  templates(): Observable<ImportTemplate[]> {
    return this.http.get<ImportTemplate[]>(`${this.baseUrl}/import-templates`);
  }

  /** URL do modelo; o download é um link comum, para o navegador salvar o arquivo. */
  templateUrl(competitionId: string, kind: ImportKind): string {
    return `${this.importsUrl(competitionId)}/${kind}/template`;
  }

  previewUrl(competitionId: string, kind: ImportKind): string {
    return `${this.importsUrl(competitionId)}/${kind}/preview`;
  }

  commitUrl(competitionId: string, kind: ImportKind): string {
    return `${this.importsUrl(competitionId)}/${kind}`;
  }

  /** Modelo da rodada, já preenchido com os jogos, os elencos e o que foi lançado. */
  statisticsTemplateUrl(competitionId: string, roundId: string): string {
    return `${this.statisticsUrl(competitionId, roundId)}/template`;
  }

  statisticsPreviewUrl(competitionId: string, roundId: string): string {
    return `${this.statisticsUrl(competitionId, roundId)}/preview`;
  }

  statisticsCommitUrl(competitionId: string, roundId: string): string {
    return this.statisticsUrl(competitionId, roundId);
  }

  /** Envia o arquivo para conferir ou importar; o endereço diz qual dos dois. */
  send(url: string, file: File): Observable<ImportResult> {
    return this.http.post<ImportResult>(url, body(file));
  }

  private statisticsUrl(competitionId: string, roundId: string): string {
    const round = `rounds/${encodeURIComponent(roundId)}`;
    return `${this.baseUrl}/competitions/${encodeURIComponent(competitionId)}/${round}/imports/estatisticas`;
  }

  private importsUrl(competitionId: string): string {
    return `${this.baseUrl}/competitions/${encodeURIComponent(competitionId)}/imports`;
  }
}

function body(file: File): FormData {
  const form = new FormData();
  form.append('arquivo', file, file.name);
  return form;
}
