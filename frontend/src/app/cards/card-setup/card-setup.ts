import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { cardLabel } from '../../shared/card-label';
import { Card, CardsService } from '../cards.service';
import { CardSetupField, cardSetupNeeds } from '../card-setup';

interface SetupDraft {
  closingDay: number | null;
  limit: number | null;
  dueDate: string | null;
}

// Checklist "cartões que precisam de dados": só mostra os campos que faltam em cada cartão e
// salva sem abrir o painel de edição completo.
@Component({
  selector: 'app-card-setup',
  imports: [TranslatePipe],
  templateUrl: './card-setup.html',
  styleUrl: './card-setup.scss',
})
export class CardSetup {
  @Input({ required: true }) cards: Card[] = [];
  @Output() saved = new EventEmitter<void>();

  protected readonly cardLabel = cardLabel;
  protected readonly drafts = signal<Record<string, SetupDraft>>({});
  protected readonly savingId = signal<string | null>(null);

  constructor(private readonly cardsService: CardsService) {}

  protected get pending(): { card: Card; needs: CardSetupField[] }[] {
    return this.cards
      .map((card) => ({ card, needs: cardSetupNeeds(card) }))
      .filter((p) => p.needs.length > 0);
  }

  protected draft(card: Card): SetupDraft {
    return this.drafts()[card.accountId] ?? { closingDay: null, limit: null, dueDate: null };
  }

  protected setDraft(card: Card, patch: Partial<SetupDraft>): void {
    this.drafts.update((d) => ({ ...d, [card.accountId]: { ...this.draft(card), ...patch } }));
  }

  protected canSave(card: Card, needs: CardSetupField[]): boolean {
    const d = this.draft(card);
    return needs.some(
      (n) =>
        (n === 'closingDay' && d.closingDay !== null) ||
        (n === 'limit' && d.limit !== null) ||
        (n === 'dueDate' && d.dueDate !== null),
    );
  }

  protected save(card: Card): void {
    const d = this.draft(card);
    this.savingId.set(card.accountId);
    // O PUT substitui tudo: reenvia o que já estava configurado e só troca o que foi preenchido.
    this.cardsService
      .updateCard(card.accountId, {
        statementClosingDay: d.closingDay ?? card.statementClosingDay,
        targetUtilizationPercent: card.targetIsCustom ? card.targetPercent : null,
        manualCreditLimit: d.limit ?? card.manualCreditLimit,
        manualNextPaymentDueDate: d.dueDate ?? card.manualNextPaymentDueDate,
        nickname: card.nickname,
        owner: card.owner ?? null,
      })
      .subscribe({
        next: () => {
          this.savingId.set(null);
          this.drafts.update((all) => {
            const { [card.accountId]: _removed, ...rest } = all;
            return rest;
          });
          this.saved.emit();
        },
        error: () => this.savingId.set(null),
      });
  }
}
