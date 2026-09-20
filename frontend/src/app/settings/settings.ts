import { Component, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { AutoBackupStatus, PlaidService } from '../plaid.service';
import { AppSettings, CardsService } from '../cards/cards.service';
import { TranslationService } from '../i18n/translation.service';
import { TranslatePipe } from '../i18n/translate.pipe';
import { Lang, LANG_LABELS, LOCALE_BY_LANG, SUPPORTED_LANGS } from '../i18n/translations';
import { Theme, ThemeService } from '../theme/theme.service';

@Component({
  selector: 'app-settings',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class Settings implements OnInit {
  protected readonly langs = SUPPORTED_LANGS;
  protected readonly langLabels = LANG_LABELS;
  protected readonly themes: Theme[] = ['light', 'dark', 'system'];
  protected readonly deleting = signal(false);
  protected readonly deleteDone = signal(false);
  protected readonly downloadingBackup = signal(false);
  protected readonly autoBackup = signal<AutoBackupStatus | null>(null);

  protected readonly globalTargetUtilizationPercent = signal(30);
  protected readonly notifyDaysBeforeClosing = signal(3);
  protected readonly notificationsEnabled = signal(true);
  protected readonly weeklyDigestEnabled = signal(true);
  protected readonly lowBalanceThreshold = signal(0);
  protected readonly lastDigestSentDate = signal<string | null>(null);
  protected readonly savingSettings = signal(false);
  protected readonly settingsSaved = signal(false);

  constructor(
    protected readonly authService: AuthService,
    private readonly plaidService: PlaidService,
    private readonly cardsService: CardsService,
    protected readonly translationService: TranslationService,
    protected readonly themeService: ThemeService,
  ) {}

  ngOnInit(): void {
    this.plaidService.getAutoBackupStatus().subscribe({
      next: (status) => this.autoBackup.set(status),
      error: () => undefined,
    });
    this.cardsService.getSettings().subscribe((settings) => {
      this.globalTargetUtilizationPercent.set(settings.globalTargetUtilizationPercent);
      this.notifyDaysBeforeClosing.set(settings.notifyDaysBeforeClosing);
      this.notificationsEnabled.set(settings.notificationsEnabled);
      this.weeklyDigestEnabled.set(settings.weeklyDigestEnabled);
      this.lowBalanceThreshold.set(settings.lowBalanceThreshold ?? 0);
      this.lastDigestSentDate.set(settings.lastDigestSentDate);
    });
  }

  protected setGlobalTarget(value: string): void {
    this.globalTargetUtilizationPercent.set(Number(value));
  }

  protected setNotifyDays(value: string): void {
    this.notifyDaysBeforeClosing.set(Number(value));
  }

  protected setLowBalanceThreshold(value: string): void {
    this.lowBalanceThreshold.set(Math.max(Number(value) || 0, 0));
  }

  protected setNotificationsEnabled(value: boolean): void {
    this.notificationsEnabled.set(value);
  }

  protected setWeeklyDigestEnabled(value: boolean): void {
    this.weeklyDigestEnabled.set(value);
  }

  protected saveSettings(): void {
    this.savingSettings.set(true);
    this.settingsSaved.set(false);
    const payload: AppSettings = {
      globalTargetUtilizationPercent: this.globalTargetUtilizationPercent(),
      notifyDaysBeforeClosing: this.notifyDaysBeforeClosing(),
      notificationsEnabled: this.notificationsEnabled(),
      weeklyDigestEnabled: this.weeklyDigestEnabled(),
      email: null,
      lastDigestSentDate: null,
      lowBalanceThreshold: this.lowBalanceThreshold(),
    };
    this.cardsService.updateSettings(payload).subscribe({
      next: () => {
        this.savingSettings.set(false);
        this.settingsSaved.set(true);
      },
      error: () => this.savingSettings.set(false),
    });
  }

  protected formatDateTime(value: string): string {
    const locale = LOCALE_BY_LANG[this.translationService.lang()];
    // O servidor manda UTC sem sufixo de fuso: força Z pra converter certo pro horário local.
    const iso = /Z|[+-]\d\d:\d\d$/.test(value) ? value : value + 'Z';
    return new Intl.DateTimeFormat(locale, { dateStyle: 'short', timeStyle: 'short' }).format(new Date(iso));
  }

  protected setLang(lang: Lang): void {
    this.translationService.setLang(lang);
  }

  protected setTheme(theme: Theme): void {
    this.themeService.setTheme(theme);
  }

  protected themeLabelKey(theme: Theme): string {
    return `settings.theme${theme.charAt(0).toUpperCase()}${theme.slice(1)}`;
  }

  protected downloadBackup(): void {
    this.downloadingBackup.set(true);
    this.plaidService.downloadBackup().subscribe({
      next: (blob) => {
        this.downloadingBackup.set(false);
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `target30-backup-${new Date().toISOString().slice(0, 10)}.json`;
        link.click();
        window.URL.revokeObjectURL(url);
      },
      error: () => this.downloadingBackup.set(false),
    });
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
