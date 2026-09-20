import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

// 402 = a ação custa dinheiro (conectar/sincronizar bancos) e a assinatura não está em dia:
// leva o usuário pra tela de cobrança em vez de deixar o erro solto.
export const paymentRequiredInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 402) {
        void router.navigate(['/billing']);
      }
      return throwError(() => error);
    }),
  );
};
