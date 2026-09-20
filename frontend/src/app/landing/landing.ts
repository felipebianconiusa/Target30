import { Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { GoogleLoginButton } from '../auth/google-login-button/google-login-button';
import { TranslationService } from '../i18n/translation.service';
import { TranslatePipe } from '../i18n/translate.pipe';
import { WaitlistService } from './waitlist.service';

// Página inicial pública (antes do login): o que o app faz, entrada na lista de espera e login.
@Component({
  selector: 'app-landing',
  imports: [TranslatePipe, GoogleLoginButton, RouterLink],
  templateUrl: './landing.html',
  styleUrl: './landing.scss',
})
export class Landing {
  protected readonly email = signal('');
  protected readonly note = signal('');
  // Campo-isca: humano não vê nem preenche; robô de formulário costuma preencher tudo.
  protected readonly website = signal('');
  protected readonly sending = signal(false);
  protected readonly joined = signal(false);
  protected readonly error = signal('');

  protected readonly features = ['utilization', 'bestCard', 'cashflow', 'privacy'] as const;

  constructor(
    private readonly waitlist: WaitlistService,
    protected readonly translationService: TranslationService,
  ) {}

  protected join(): void {
    const email = this.email().trim();
    if (!email || this.sending()) return;

    this.sending.set(true);
    this.error.set('');
    this.waitlist.join(email, this.note().trim(), this.translationService.lang(), this.website()).subscribe({
      next: () => {
        this.sending.set(false);
        this.joined.set(true);
      },
      error: (err) => {
        this.sending.set(false);
        this.error.set(
          this.translationService.t(err?.status === 429 ? 'landing.waitlistTooMany' : 'landing.waitlistError'),
        );
      },
    });
  }
}
