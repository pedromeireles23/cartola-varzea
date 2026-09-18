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

  preview(competitionId: string, kind: ImportKind, file: File): Observable<ImportResult> {
    return this.http.post<ImportResult>(
      `${this.importsUrl(competitionId)}/${kind}/preview`,
      body(file),
    );
  }

  commit(competitionId: string, kind: ImportKind, file: File): Observable<ImportResult> {
    return this.http.post<ImportResult>(`${this.importsUrl(competitionId)}/${kind}`, body(file));
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
