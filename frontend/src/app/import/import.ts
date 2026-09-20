import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslationService } from '../i18n/translation.service';
import { TranslatePipe } from '../i18n/translate.pipe';
import { LOCALE_BY_LANG } from '../i18n/translations';
import { cardLabel } from '../shared/card-label';
import { ImportAccount, ImportPreview, ImportRequest, ImportResult, ImportService } from './import.service';

const MAX_FILE_BYTES = 2_000_000;

@Component({
  selector: 'app-import',
  imports: [TranslatePipe, RouterLink],
  templateUrl: './import.html',
  styleUrl: './import.scss',
})
export class Import implements OnInit {
  protected readonly accounts = signal<ImportAccount[]>([]);
  protected readonly accountId = signal('');
  protected readonly convention = signal<ImportRequest['convention']>('expense_negative');
  protected readonly dateFormat = signal<ImportRequest['dateFormat']>('auto');

  protected readonly fileName = signal('');
  protected readonly csv = signal('');

  protected readonly preview = signal<ImportPreview | null>(null);
  protected readonly result = signal<ImportResult | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  // Conta manual (banco/cartão que o Plaid não cobre)
  protected readonly showManual = signal(false);
  protected readonly manualName = signal('');
  protected readonly manualType = signal<'Depository' | 'Credit'>('Credit');

  protected readonly cardLabel = cardLabel;

  constructor(
    private readonly importService: ImportService,
    private readonly translationService: TranslationService,
  ) {}

  ngOnInit(): void {
    this.loadAccounts();
  }

  protected accountLabel(a: ImportAccount): string {
    const base = cardLabel(a.nickname, a.name);
    return a.institutionName ? `${a.institutionName} — ${base}` : base;
  }

  protected setAccount(value: string): void {
    this.accountId.set(value);
    this.resetOutput();
  }

  protected setConvention(value: string): void {
    this.convention.set(value as ImportRequest['convention']);
    this.resetOutput();
  }

  protected setDateFormat(value: string): void {
    this.dateFormat.set(value as ImportRequest['dateFormat']);
    this.resetOutput();
  }

  protected async onFileSelected(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    this.resetOutput();
    if (!file) return;

    if (file.size > MAX_FILE_BYTES) {
      this.error.set(this.translationService.t('import.tooBig'));
      return;
    }
    this.fileName.set(file.name);
    this.csv.set(await file.text());
  }

  protected canPreview(): boolean {
    return !!this.csv() && !!this.accountId() && !this.busy();
  }

  protected runPreview(): void {
    this.busy.set(true);
    this.error.set('');
    this.result.set(null);
    this.importService.preview(this.request()).subscribe({
      next: (p) => {
        this.preview.set(p);
        this.busy.set(false);
      },
      error: (err) => this.fail(err),
    });
  }

  protected runCommit(): void {
    this.busy.set(true);
    this.error.set('');
    this.importService.commit(this.request()).subscribe({
      next: (r) => {
        this.result.set(r);
        this.preview.set(null);
        this.csv.set('');
        this.fileName.set('');
        this.busy.set(false);
      },
      error: (err) => this.fail(err),
    });
  }

  protected toggleManual(): void {
    this.showManual.update((v) => !v);
  }

  protected createManualAccount(): void {
    const name = this.manualName().trim();
    if (!name) return;

    this.busy.set(true);
    this.importService
      .createManualAccount({
        name,
        type: this.manualType(),
        institutionName: null,
        currentBalance: null,
        creditLimit: null,
      })
      .subscribe({
        next: (account) => {
          this.busy.set(false);
          this.showManual.set(false);
          this.manualName.set('');
          this.loadAccounts(account.accountId);
        },
        error: (err) => this.fail(err),
      });
  }

  protected setManualName(value: string): void {
    this.manualName.set(value);
  }

  protected setManualType(value: string): void {
    this.manualType.set(value as 'Depository' | 'Credit');
  }

  protected formatAmount(amount: number): string {
    const locale = LOCALE_BY_LANG[this.translationService.lang()];
    // Plaid: positivo = gasto. Mostra do jeito natural: gasto negativo, entrada positiva.
    return new Intl.NumberFormat(locale, { style: 'currency', currency: 'USD' }).format(-amount);
  }

  private request(): ImportRequest {
    return {
      csv: this.csv(),
      accountId: this.accountId(),
      convention: this.convention(),
      dateFormat: this.dateFormat(),
    };
  }

  private resetOutput(): void {
    this.preview.set(null);
    this.result.set(null);
    this.error.set('');
  }

  private fail(err: HttpErrorResponse): void {
    this.busy.set(false);
    const detail = typeof err.error?.message === 'string' ? err.error.message : '';
    this.error.set(detail || this.translationService.t('import.error'));
  }

  private loadAccounts(select?: string): void {
    this.importService.getAccounts().subscribe((list) => {
      this.accounts.set(list);
      if (select) this.accountId.set(select);
    });
  }
}
