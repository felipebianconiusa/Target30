import { Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { PlaidService } from '../plaid.service';
import { TranslationService } from '../i18n/translation.service';
import { TranslatePipe } from '../i18n/translate.pipe';
import { Lang, LANG_LABELS, SUPPORTED_LANGS } from '../i18n/translations';

@Component({
  selector: 'app-settings',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class Settings {
  protected readonly langs = SUPPORTED_LANGS;
  protected readonly langLabels = LANG_LABELS;
  protected readonly deleting = signal(false);
  protected readonly deleteDone = signal(false);

  constructor(
    protected readonly authService: AuthService,
    private readonly plaidService: PlaidService,
    protected readonly translationService: TranslationService,
  ) {}

  protected setLang(lang: Lang): void {
    this.translationService.setLang(lang);
  }

  protected deleteAllData(): void {
    const confirmed = confirm(this.translationService.t('settings.deleteAllDataConfirm'));
    if (!confirmed) return;

    this.deleting.set(true);
    this.plaidService.getItems().subscribe({
      next: (items) => {
        if (items.length === 0) {
          this.deleting.set(false);
          this.deleteDone.set(true);
          return;
        }

        let remaining = items.length;
        for (const item of items) {
          this.plaidService.removeItem(item.itemId).subscribe({
            next: () => {
              remaining -= 1;
              if (remaining === 0) {
                this.deleting.set(false);
                this.deleteDone.set(true);
              }
            },
            error: () => {
              remaining -= 1;
              if (remaining === 0) this.deleting.set(false);
            },
          });
        }
      },
      error: () => this.deleting.set(false),
    });
  }
}
