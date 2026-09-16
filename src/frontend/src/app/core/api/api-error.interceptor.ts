import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

import { toApiFailure } from './problem-details';

/**
 * Traduz qualquer falha HTTP para {@link ApiFailure}, para que as features tratem
 * um tipo so em vez de inspecionar HttpErrorResponse em cada lugar.
 *
 * Nunca registra corpo de resposta nem cabecalho: eles podem conter dado pessoal.
 */
export const apiErrorInterceptor: HttpInterceptorFn = (request, next) =>
  next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        return throwError(() => toApiFailure(error.status, error.error));
      }

      return throwError(() => error);
    }),
  );
