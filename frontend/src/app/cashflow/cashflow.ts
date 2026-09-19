import { Component, OnInit, signal } from '@angular/core';
import { Bill, CashFlowEntry, CashFlowService, DetectedSubscription } from './cashflow.service';
import { TranslationService } from '../i18n/translation.service';
import { TranslatePipe } from '../i18n/translate.pipe';
import { LOCALE_BY_LANG } from '../i18n/translations';

@Component({
  selector: 'app-cashflow',
  imports: [TranslatePipe],
  templateUrl: './cashflow.html',
  styleUrl: './cashflow.scss',
})
export class CashFlow implements OnInit {
  protected readonly entries = signal<CashFlowEntry[]>([]);
  protected readonly currentBalance = signal(0);
  protected readonly loading = signal(true);

  protected readonly bills = signal<Bill[]>([]);
  protected readonly showBillsManager = signal(false);
  protected readonly newBillDescription = signal('');
  protected readonly newBillAmount = signal<number | null>(null);
  protected readonly newBillDay = signal<number | null>(null);
  protected readonly savingBill = signal(false);
  protected readonly downloadingReport = signal(false);

  protected readonly detectedSubscriptions = signal<DetectedSubscription[]>([]);
  protected readonly addingSubscription = signal<string | null>(null);

  private readonly today = new Date().toISOString().slice(0, 10);

  constructor(
    private readonly cashFlowService: CashFlowService,
    protected readonly translationService: TranslationService,
  ) {}

  ngOnInit(): void {
    this.loadCashFlow();
    this.loadBills();
  }

  protected isFuture(entry: CashFlowEntry): boolean {
    return entry.date > this.today;
  }

  protected formatCurrency(value: number): string {
    const locale = LOCALE_BY_LANG[this.translationService.lang()];
    return new Intl.NumberFormat(locale, { style: 'currency', currency: 'USD' }).format(value);
  }

  protected toggleBillsManager(): void {
    const opening = !this.showBillsManager();
    this.showBillsManager.set(opening);
    if (opening) this.loadDetectedSubscriptions();
  }

  protected addDetectedSubscription(subscription: DetectedSubscription): void {
    this.addingSubscription.set(subscription.merchantName);
    this.cashFlowService
      .createBill({
        description: subscription.merchantName,
        amount: subscription.averageAmount,
        dayOfMonth: subscription.suggestedDayOfMonth,
      })
      .subscribe({
        next: () => {
          this.addingSubscription.set(null);
          this.loadBills();
          this.loadCashFlow();
          this.loadDetectedSubscriptions();
        },
        error: () => this.addingSubscription.set(null),
      });
  }

  protected setNewBillDescription(value: string): void {
    this.newBillDescription.set(value);
  }

  protected setNewBillAmount(value: string): void {
    this.newBillAmount.set(value ? Number(value) : null);
  }

  protected setNewBillDay(value: string): void {
    this.newBillDay.set(value ? Number(value) : null);
  }

  protected addBill(): void {
    const description = this.newBillDescription().trim();
    const amount = this.newBillAmount();
    const day = this.newBillDay();
    if (!description || amount === null || day === null) return;

    this.savingBill.set(true);
    this.cashFlowService.createBill({ description, amount, dayOfMonth: day }).subscribe({
      next: () => {
        this.savingBill.set(false);
        this.newBillDescription.set('');
        this.newBillAmount.set(null);
        this.newBillDay.set(null);
        this.loadBills();
        this.loadCashFlow();
      },
      error: () => this.savingBill.set(false),
    });
  }

  protected deleteBill(bill: Bill): void {
    this.cashFlowService.deleteBill(bill.id).subscribe(() => {
      this.loadBills();
      this.loadCashFlow();
    });
  }

  protected downloadReport(): void {
    this.downloadingReport.set(true);
    this.cashFlowService.downloadReport().subscribe({
      next: (blob) => {
        this.downloadingReport.set(false);
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `target30-fluxo-caixa-${new Date().toISOString().slice(0, 10)}.csv`;
        link.click();
        window.URL.revokeObjectURL(url);
      },
      error: () => this.downloadingReport.set(false),
    });
  }

  private loadCashFlow(): void {
    this.loading.set(true);
    this.cashFlowService.getCashFlow().subscribe({
      next: (response) => {
        this.entries.set(response.entries);
        this.currentBalance.set(response.currentBalance);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  private loadBills(): void {
    this.cashFlowService.getBills().subscribe((bills) => this.bills.set(bills));
  }

  private loadDetectedSubscriptions(): void {
    this.cashFlowService
      .getDetectedSubscriptions()
      .subscribe((subscriptions) => this.detectedSubscriptions.set(subscriptions));
  }
}
