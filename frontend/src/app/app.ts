import { Component, OnInit } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './auth/auth.service';
import { GoogleLoginButton } from './auth/google-login-button/google-login-button';
import { TranslationService } from './i18n/translation.service';
import { TranslatePipe } from './i18n/translate.pipe';

@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, GoogleLoginButton, TranslatePipe],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App implements OnInit {
  constructor(
    protected readonly authService: AuthService,
    protected readonly translationService: TranslationService,
  ) {}

  ngOnInit(): void {
    this.authService.checkSession();
  }
}
