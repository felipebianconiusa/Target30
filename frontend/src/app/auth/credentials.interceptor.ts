import { HttpInterceptorFn } from '@angular/common/http';

// A API roda numa porta diferente (localhost:7059/5008) do dev server do Angular
// (localhost:4200). Sem isso o cookie de sessão não é enviado nem aceito nas respostas.
export const credentialsInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req.clone({ withCredentials: true }));
};
