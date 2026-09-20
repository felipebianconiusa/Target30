import { NgTemplateOutlet } from '@angular/common';
import { Component, OnInit, computed, signal } from '@angular/core';
import {
  AppSettings,
  Card,
  CardHistoryPoint,
  CardsService,
  PayoffPlan,
} from './cards.service';
import { TranslationService } from '../i18n/translation.service';
import { TranslatePipe } from '../i18n/translate.pipe';
import { LOCALE_BY_LANG } from '../i18n/translations';
import { buildGoogleCalendarLink } from '../shared/google-calendar-link';
import { cardLabel } from '../shared/card-label';
import { CardSetup } from './card-setup/card-setup';

@Component({
  selector: 'app-cards',
  imports: [TranslatePipe, NgTemplateOutlet, CardSetup],
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
  protected readonly editNickname = signal('');
  protected readonly editOwner = signal('');
  protected readonly ownerFilter = signal('');
  protected readonly savingId = signal<string | null>(null);
  protected readonly downloadingReport = signal(false);

  protected readonly historyId = signal<string | null>(null);
  protected readonly historyPoints = signal<CardHistoryPoint[]>([]);
  protected readonly historyLoading = signal(false);

  protected readonly payoffAmount = signal<number | null>(null);
  protected readonly payoffPlan = signal<PayoffPlan | null>(null);
  protected readonly payoffLoading = signal(false);

  protected readonly simulateId = signal<string | null>(null);
  protected readonly simulateAmount = signal<number | null>(null);

  // Cartões com fatura já vencendo (valor certo, data certa) vs. ciclo ainda aberto (conta
  // recém-conectada ou sem 1º fechamento processado pelo Plaid ainda — sem valor definido).
  // Cartões visíveis depois do filtro de dono ('' = todos).
  private readonly visibleCards = computed(() =>
    this.cards().filter((c) => !this.ownerFilter() || c.owner === this.ownerFilter()),
  );

  protected readonly owners = computed(() =>
    [...new Set(this.cards().map((c) => c.owner).filter((o): o is string => !!o))].sort(),
  );

  // Sugestões pro campo "dono": quem já é dono de algum cartão + a última palavra do nome que se
  // repete em 2+ cartões (ex.: "Savor Layse", "Quicksilver Layse" → "Layse").
  protected readonly ownerSuggestions = computed(() => {
    const counts = new Map<string, number>();
    for (const c of this.cards()) {
      const word = c.name.trim().split(/\s+/).pop() ?? '';
      if (/^\p{L}{3,}$/u.test(word)) counts.set(word, (counts.get(word) ?? 0) + 1);
    }
    const repeated = [...counts].filter(([, n]) => n >= 2).map(([w]) => w);
    return [...new Set([...this.owners(), ...repeated])].sort();
  });

  protected readonly cardsWithDueDate = computed(() =>
    this.visibleCards().filter((c) => c.nextPaymentDueDate !== null),
  );
  protected readonly cardsWithoutDueDate = computed(() =>
    this.visibleCards().filter((c) => c.nextPaymentDueDate === null),
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
    this.editNickname.set(card.nickname ?? '');
    this.editOwner.set(card.owner ?? '');
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

  protected setEditOwner(value: string): void {
    this.editOwner.set(value);
  }

  protected setOwnerFilter(value: string): void {
    this.ownerFilter.set(value);
  }

  protected setEditNickname(value: string): void {
    this.editNickname.set(value);
  }

  protected cardLabel = cardLabel;

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
        nickname: this.editNickname().trim() || null,
        owner: this.editOwner().trim() || null,
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

  protected setPayoffAmount(value: string): void {
    this.payoffAmount.set(value ? Number(value) : null);
  }

  protected calculatePayoffPlan(): void {
    const amount = this.payoffAmount();
    if (amount === null || amount <= 0) return;

    this.payoffLoading.set(true);
    this.cardsService.getPayoffPlan(amount).subscribe({
      next: (plan) => {
        this.payoffPlan.set(plan);
        this.payoffLoading.set(false);
      },
      error: () => this.payoffLoading.set(false),
    });
  }

  protected toggleSimulate(card: Card): void {
    if (this.simulateId() === card.accountId) {
      this.simulateId.set(null);
      return;
    }
    this.simulateId.set(card.accountId);
    this.simulateAmount.set(null);
  }

  protected setSimulateAmount(value: string): void {
    this.simulateAmount.set(value ? Number(value) : null);
  }

  // Só matemática local — o objetivo é deixar você ver o efeito antes de decidir pagar, sem
  // precisar ir e voltar no servidor pra cada valor que você testar.
  protected simulatedBalance(card: Card): number {
    const amount = this.simulateAmount() ?? 0;
    return Math.max(0, card.currentBalance - amount);
  }

  protected simulatedUtilization(card: Card): number | null {
    if (card.creditLimit <= 0) return null;
    return Math.round((this.simulatedBalance(card) / card.creditLimit) * 1000) / 10;
  }

  protected googleCalendarLink(name: string, amount: number, date: string, currency: string | null): string {
    return buildGoogleCalendarLink({
      title: this.translationService.t('cards.calendarEventTitle', { name }),
      date,
      details: this.translationService.t('cards.calendarEventDetails', {
        amount: this.formatCurrency(amount, currency),
      }),
    });
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

  protected load(): void {
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
