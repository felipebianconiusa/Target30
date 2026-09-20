import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { TranslationService } from '../../i18n/translation.service';
import { RewardRanking } from '../rewards.service';
import { RewardsPanel } from './rewards-panel';

function ranking(overrides: Partial<RewardRanking> = {}): RewardRanking {
  return {
    accountId: 'a', name: 'Savor', nickname: null, institutionName: 'Capital One', owner: null,
    ratePercent: 3, tier: 'recommended', daysUntilClosing: 20, nextClosingDate: '2026-10-10',
    availableCredit: 900, isBest: false, ...overrides,
  };
}

describe('RewardsPanel', () => {
  let fixture: ComponentFixture<RewardsPanel>;
  let http: HttpTestingController;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RewardsPanel],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    TestBed.inject(TranslationService).setLang('pt');
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RewardsPanel);
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  it('ranks the cards for the chosen category and highlights the best one', () => {
    const req = http.expectOne((r) => r.url === '/api/rewards/best-for');
    expect(req.request.params.get('category')).toBe('FOOD_AND_DRINK');
    req.flush([
      ranking({ accountId: 'a', name: 'Savor', ratePercent: 3, isBest: true }),
      ranking({ accountId: 'b', name: 'Quicksilver', ratePercent: 1.5 }),
      ranking({ accountId: 'c', name: 'Hot', ratePercent: 5, tier: 'caution' }),
    ]);
    fixture.detectChanges();

    const items = Array.from(el.querySelectorAll('.rewards__item'));
    expect(items.length).toBe(3);
    expect(items[0].classList).toContain('rewards__item--best');
    expect(items[0].textContent).toContain('Savor');
    expect(items[0].textContent).toContain('3%');
    expect(items[2].textContent).toContain('cuidado');
  });

  it('asks again when the category changes', () => {
    http.expectOne((r) => r.url === '/api/rewards/best-for').flush([]);

    (fixture.componentInstance as any).setCategory('TRAVEL');

    const req = http.expectOne((r) => r.url === '/api/rewards/best-for');
    expect(req.request.params.get('category')).toBe('TRAVEL');
  });

  it('saves the base rate and the category rates typed in the editor', () => {
    http.expectOne((r) => r.url === '/api/rewards/best-for').flush([]);
    const component = fixture.componentInstance as any;
    const card = { accountId: 'a', name: 'Savor', nickname: null } as any;

    component.toggleEditor();
    http.expectOne('/api/cards').flush([card]);
    http.expectOne('/api/rewards').flush([]);
    fixture.detectChanges();

    component.setBase(card, '1');
    component.addRow(card);
    component.setRowCategory(card, 0, 'FOOD_AND_DRINK');
    component.setRowRate(card, 0, '3');
    component.save(card);

    const put = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/rewards/a');
    expect(put.request.body).toEqual({
      rates: [
        { category: 'BASE', ratePercent: 1 },
        { category: 'FOOD_AND_DRINK', ratePercent: 3 },
      ],
    });
  });

  it('loads the saved rates into the editor', () => {
    http.expectOne((r) => r.url === '/api/rewards/best-for').flush([]);
    const component = fixture.componentInstance as any;

    component.toggleEditor();
    http.expectOne('/api/cards').flush([{ accountId: 'a', name: 'Savor', nickname: null }]);
    http.expectOne('/api/rewards').flush([
      { accountId: 'a', rates: [{ category: 'BASE', ratePercent: 1 }, { category: 'TRAVEL', ratePercent: 2 }] },
    ]);
    fixture.detectChanges();

    const inputs = Array.from(el.querySelectorAll('.rewards__card input')) as HTMLInputElement[];
    expect(inputs.map((i) => i.value)).toEqual(['1', '2']);
  });
});
