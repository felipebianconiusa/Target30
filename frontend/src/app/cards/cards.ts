import { Component, OnInit, signal } from '@angular/core';
import { AppSettings, Card, CardsService } from './cards.service';
import { TranslationService } from '../i18n/translation.service';
import { TranslatePipe } from '../i18n/translate.pipe';
import { LOCALE_BY_LANG } from '../i18n/translations';

@Component({
  selector: 'app-cards',
  imports: [TranslatePipe],
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
  protected readonly savingId = signal<string | null>(null);

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

  protected saveEdit(card: Card): void {
    this.savingId.set(card.accountId);
    this.cardsService
      .updateCard(card.accountId, {
        statementClosingDay: this.editClosingDay(),
        targetUtilizationPercent: this.editTarget(),
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

  protected formatCurrency(value: number, currency: string | null): string {
    const locale = LOCALE_BY_LANG[this.translationService.lang()];
    return new Intl.NumberFormat(locale, { style: 'currency', currency: currency ?? 'USD' }).format(
      value,
    );
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
