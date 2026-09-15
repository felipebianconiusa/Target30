import { AfterViewInit, Component, ElementRef, ViewChild } from '@angular/core';
import { AuthService } from '../auth.service';
import { GOOGLE_CLIENT_ID } from '../google-client-id';

@Component({
  selector: 'app-google-login-button',
  imports: [],
  template: '<div #buttonContainer></div>',
})
export class GoogleLoginButton implements AfterViewInit {
  @ViewChild('buttonContainer', { static: true }) buttonContainer!: ElementRef<HTMLElement>;

  constructor(private readonly authService: AuthService) {}

  ngAfterViewInit(): void {
    this.renderWhenReady();
  }

  private renderWhenReady(): void {
    if (!window.google) {
      // O script do Google Identity Services (carregado no index.html) pode ainda
      // não ter terminado de carregar quando este componente monta.
      setTimeout(() => this.renderWhenReady(), 200);
      return;
    }

    window.google.accounts.id.initialize({
      client_id: GOOGLE_CLIENT_ID,
      callback: (response) => {
        this.authService.loginWithGoogle(response.credential).subscribe();
      },
    });

    window.google.accounts.id.renderButton(this.buttonContainer.nativeElement, {
      type: 'standard',
      theme: 'outline',
      size: 'large',
      text: 'signin_with',
      shape: 'pill',
    });
  }
}
