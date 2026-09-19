import { NgTemplateOutlet } from '@angular/common';
import { Component, OnInit, computed, signal } from '@angular/core';
import { AppSettings, Card, CardHistoryPoint, CardsService } from './cards.service';
import { TranslationService } from '../i18n/translation.service';
import { TranslatePipe } from '../i18n/translate.pipe';
import { LOCALE_BY_LANG } from '../i18n/translations';

@Component({
  selector: 'app-cards',
  imports: [TranslatePipe, NgTemplateOutlet],
  templateUrl: './cards.html',
  styleUrl: './cards.scss',
})
export class Cards implements OnInit {
  protected readonly cards = signal<Card[]>([]);
  protected readonly settings = signal<AppSettings | null>(null);
  protected readonly loading = signal(true);

  protected readonly editingId = signal<string | null>(null);
  protected readonly editClosingDay = signal<number | null>(null);
  protected readonly editTarget = signal<number | null>(null);
  protected readonly editManualLimit = signal<number | null>(null);
  protected readonly editManualDueDate = signal<string | null>(null);
  protected readonly savingId = signal<string | null>(null);
  protected readonly downloadingReport = signal(false);

  protected readonly historyId = signal<string | null>(null);
  protected readonly historyPoints = signal<CardHistoryPoint[]>([]);
  protected readonly historyLoading = signal(false);

  // Cartões com fatura já vencendo (valor certo, data certa) vs. ciclo ainda aberto (conta
  // recém-conectada ou sem 1º fechamento processado pelo Plaid ainda — sem valor definido).
  protected readonly cardsWithDueDate = computed(() =>
    this.cards().filter((c) => c.nextPaymentDueDate !== null),
  );
  protected readonly cardsWithoutDueDate = computed(() =>
    this.cards().filter((c) => c.nextPaymentDueDate === null),
  );

  constructor(
    private readonly cardsService: CardsService,
    protected readonly translationService: TranslationService,
  ) {}

  ngOnInit(): void {
    this.load();
  }

  protected startEdit(card: Card): void {
    this.editingId.set(card.accountId);
    this.editClosingDay.set(card.statementClosingDay);
    this.editTarget.set(card.targetIsCustom ? card.targetPercent : null);
    this.editManualLimit.set(card.manualCreditLimit);
    this.editManualDueDate.set(card.manualNextPaymentDueDate);
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
  }

  protected setEditClosingDay(value: string): void {
    this.editClosingDay.set(value ? Number(value) : null);
  }

  protected setEditTarget(value: string): void {
    this.editTarget.set(value ? Number(value) : null);
  }

  protected setEditManualLimit(value: string): void {
    this.editManualLimit.set(value ? Number(value) : null);
  }

  protected setEditManualDueDate(value: string): void {
    this.editManualDueDate.set(value || null);
  }

  protected saveEdit(card: Card): void {
    this.savingId.set(card.accountId);
    this.cardsService
      .updateCard(card.accountId, {
        statementClosingDay: this.editClosingDay(),
        targetUtilizationPercent: this.editTarget(),
        manualCreditLimit: this.editManualLimit(),
        manualNextPaymentDueDate: this.editManualDueDate(),
      })
      .subscribe({
        next: () => {
          this.savingId.set(null);
          this.editingId.set(null);
          this.load();
        },
        error: () => this.savingId.set(null),
      });
  }

  protected toggleHistory(card: Card): void {
    if (this.historyId() === card.accountId) {
      this.historyId.set(null);
      return;
    }
    this.historyId.set(card.accountId);
    this.historyLoading.set(true);
    this.cardsService.getHistory(card.accountId).subscribe({
      next: (points) => {
        this.historyPoints.set(points);
        this.historyLoading.set(false);
      },
      error: () => this.historyLoading.set(false),
    });
  }

  protected historyPath(): string {
    const points = this.historyPoints().filter((p) => p.utilizationPercent !== null);
    if (points.length < 2) return '';

    const width = 600;
    const height = 160;
    const values = points.map((p) => p.utilizationPercent as number);
    const maxValue = Math.max(100, ...values);

    return points
      .map((p, i) => {
        const x = (i / (points.length - 1)) * width;
        const y = height - (((p.utilizationPercent as number) / maxValue) * height);
        return `${i === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`;
      })
      .join(' ');
  }

  protected formatCurrency(value: number, currency: string | null): string {
    const locale = LOCALE_BY_LANG[this.translationService.lang()];
    return new Intl.NumberFormat(locale, { style: 'currency', currency: currency ?? 'USD' }).format(
      value,
    );
  }

  protected downloadReport(): void {
    this.downloadingReport.set(true);
    this.cardsService.downloadReport().subscribe({
      next: (blob) => {
        this.downloadingReport.set(false);
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `target30-cartoes-${new Date().toISOString().slice(0, 10)}.csv`;
        link.click();
        window.URL.revokeObjectURL(url);
      },
      error: () => this.downloadingReport.set(false),
    });
  }

  protected utilizationState(card: Card): 'over' | 'under' | 'unknown' {
    if (card.utilizationPercent === null) return 'unknown';
    return card.utilizationPercent > card.targetPercent ? 'over' : 'under';
  }

  private load(): void {
    this.loading.set(true);
    this.cardsService.getSettings().subscribe((settings) => this.settings.set(settings));
    this.cardsService.getCards().subscribe({
      next: (cards) => {
        this.cards.set(cards);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
