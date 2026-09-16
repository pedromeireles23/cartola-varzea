import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../core/config/api-base-url';

/** Resposta de GET /api/v1/system/info. */
export interface SystemInfo {
  readonly version: string;
  readonly environment: string;
  readonly serverTimeUtc: string;
  readonly startupCount: number;
  readonly lastStartedAt: string | null;
}

@Injectable({ providedIn: 'root' })
export class SystemInfoService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  get(): Observable<SystemInfo> {
    return this.http.get<SystemInfo>(`${this.baseUrl}/system/info`);
  }
}
