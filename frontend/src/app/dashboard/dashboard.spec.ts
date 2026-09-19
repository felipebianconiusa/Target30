import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, beforeEach, expect, it } from 'vitest';
import { Dashboard } from './dashboard';
import { Budget } from './budgets.service';
import { TranslationService } from '../i18n/translation.service';

function makeBudget(category: string): Budget {
  return { id: 1, category, monthlyLimit: 100, currentSpend: 0, percentUsed: 0 };
}

describe('Dashboard', () => {
  let fixture: ComponentFixture<Dashboard>;
  let component: any;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(Dashboard);
    TestBed.inject(TranslationService).setLang('pt');
    component = fixture.componentInstance;
  });

  describe('categoryOptions', () => {
    it('excludes categories that already have a budget', () => {
      component.budgets.set([makeBudget('FOOD_AND_DRINK')]);

      const values = component.categoryOptions().map((o: { value: string }) => o.value);

      expect(values).not.toContain('FOOD_AND_DRINK');
      expect(values).toContain('ENTERTAINMENT');
    });

    it('includes every category when no budgets exist yet', () => {
      component.budgets.set([]);
      expect(component.categoryOptions().length).toBeGreaterThan(0);
    });
  });

  describe('net', () => {
    it('is income minus expenses', () => {
      component.summary.set({
        totalIncome: 500,
        totalExpenses: 300,
        categoryTotals: [],
        recentTransactions: [],
      });
      expect(component.net()).toBe(200);
    });
  });
});
