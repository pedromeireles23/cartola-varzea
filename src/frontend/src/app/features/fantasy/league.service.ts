import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../core/config/api-base-url';

/** A liga chegou ao teto de participantes; o servidor responde 409 com este código. */
export const LEAGUE_LIMIT_CODE = 'league_member_limit';

/** Os mesmos limites que `LeagueDefinition` cobra no servidor (01 §9). */
export const LEAGUE_NAME_MIN = 3;
export const LEAGUE_NAME_MAX = 60;

/** Dez caracteres, como `LeagueInviteCode`; o servidor aceita minúsculas e hífens. */
export const LEAGUE_CODE_LENGTH = 10;

/**
 * Uma liga na lista da conta. `inviteCode` só vem para o dono, e `position` é nula
 * enquanto nenhuma rodada do campeonato foi apurada.
 */
export interface LeagueSummary {
  readonly id: string;
  readonly name: string;
  readonly competitionSlug: string;
  readonly members: number;
  readonly isOwner: boolean;
  readonly position: number | null;
  readonly inviteCode: string | null;
  readonly inviteExpiresAtLocal: string | null;
  readonly version: string;
}

/**
 * Uma linha do ranking da liga. `membershipId` vem só para quem pode agir naquela
 * linha — o dono em qualquer uma, e a própria conta na dela.
 */
export interface LeagueMember {
  readonly membershipId: string | null;
  readonly position: number;
  readonly tied: boolean;
  readonly displayName: string;
  readonly totalPoints: number;
  readonly netWorth: number;
  readonly lastRoundPoints: number | null;
  readonly isViewer: boolean;
  readonly isOwner: boolean;
}

/** A liga aberta, com o ranking dela e a lista de quem está dentro. */
export interface League {
  readonly id: string;
  readonly name: string;
  readonly competitionName: string;
  readonly competitionSlug: string;
  readonly isOwner: boolean;
  readonly inviteCode: string | null;
  readonly inviteExpiresAtLocal: string | null;
  readonly rounds: number;
  readonly lastRoundName: string | null;
  readonly provisional: boolean;
  readonly members: readonly LeagueMember[];
  readonly version: string;
}

/**
 * Ligas privadas do participante (01 §9).
 *
 * Toda rota é da conta da sessão: não existe leitura da liga de outra pessoa. A
 * entrada pelo código é a única fora do slug, porque quem recebe um código ainda não
 * sabe de que campeonato ele é.
 */
@Injectable({ providedIn: 'root' })
export class LeagueService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  mine(slug: string): Observable<LeagueSummary[]> {
    return this.http.get<LeagueSummary[]>(this.leaguesUrl(slug));
  }

  create(slug: string, name: string): Observable<LeagueSummary> {
    return this.http.post<LeagueSummary>(this.leaguesUrl(slug), { name });
  }

  get(slug: string, leagueId: string): Observable<League> {
    return this.http.get<League>(`${this.leaguesUrl(slug)}/${leagueId}`);
  }

  join(code: string): Observable<LeagueSummary> {
    return this.http.post<LeagueSummary>(
      `${this.baseUrl}/league-invites/${encodeURIComponent(code)}/accept`,
      {},
    );
  }

  /** Troca o código por outro; com `close`, fecha a liga para novas entradas. */
  rotateInvite(
    slug: string,
    leagueId: string,
    version: string,
    close: boolean,
  ): Observable<LeagueSummary> {
    return this.http.put<LeagueSummary>(`${this.leaguesUrl(slug)}/${leagueId}/invite`, {
      version,
      close,
    });
  }

  /** O dono remove qualquer membro; qualquer membro sai sozinho pela própria linha. */
  removeMember(slug: string, leagueId: string, membershipId: string): Observable<void> {
    return this.http.delete<void>(`${this.leaguesUrl(slug)}/${leagueId}/members/${membershipId}`);
  }

  remove(slug: string, leagueId: string, version: string): Observable<void> {
    return this.http.delete<void>(`${this.leaguesUrl(slug)}/${leagueId}`, {
      params: new HttpParams().set('version', version),
    });
  }

  private leaguesUrl(slug: string): string {
    return `${this.baseUrl}/fantasy/${encodeURIComponent(slug)}/leagues`;
  }
}
