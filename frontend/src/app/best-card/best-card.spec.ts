import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { TranslationService } from '../i18n/translation.service';
import { BestCard as BestCardItem, BestCardResponse } from '../cards/cards.service';
import { BestCard } from './best-card';

function makeItem(overrides: Partial<BestCardItem> = {}): BestCardItem {
  return {
    accountId: 'a1',
    name: 'Savor',
    nickname: null,
    institutionName: 'Capital One',
    nextClosingDate: '2026-10-20',
    daysUntilClosing: 20,
    currentBalance: 100,
    creditLimit: 1000,
    availableCredit: 900,
    utilizationPercent: 10,
    exclusionReason: null,
    tier: 'recommended',
    targetPercent: 30,
    overTarget: false,
    ...overrides,
  };
}

describe('BestCard page', () => {
  let fixture: ComponentFixture<BestCard>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BestCard],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    TestBed.inject(TranslationService).setLang('pt');
    fixture = TestBed.createComponent(BestCard);
  });

  function render(response: BestCardResponse): HTMLElement {
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/cards/best-today').flush(response);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('puts each card in the group of its tier, plus the excluded ones', () => {
    const ranking = [
      makeItem({ accountId: 'r', name: 'Rec', tier: 'recommended' }),
      makeItem({ accountId: 'a', name: 'Alt', tier: 'alternative', daysUntilClosing: 10 }),
      makeItem({ accountId: 'c', name: 'Caut', tier: 'caution', daysUntilClosing: 3 }),
    ];
    const excluded = [
      makeItem({ accountId: 'x', name: 'Maxed', tier: null, exclusionReason: 'limit_reached' }),
    ];
    const el = render({ recommended: ranking[0], ranking, excluded });

    const text = (sel: string) => el.querySelector(sel)?.textContent ?? '';
    expect(text('.best-group--recommended')).toContain('Rec');
    expect(text('.best-group--alternative')).toContain('Alt');
    expect(text('.best-group--caution')).toContain('Caut');
    expect(text('.best-group--excluded')).toContain('Maxed');
    expect(text('.best-group--excluded')).toContain('limite estourado');
    expect(text('.best-group--recommended')).not.toContain('Alt');
  });

  it('shows the nickname followed by the original name', () => {
    const item = makeItem({ name: 'Savor', nickname: 'Mercado' });
    const el = render({ recommended: item, ranking: [item], excluded: [] });

    expect(el.querySelector('.best-group--recommended strong')?.textContent).toBe('Mercado (Savor)');
  });

  it('flags a card that is over its utilization target', () => {
    const item = makeItem({
      tier: 'caution',
      overTarget: true,
      utilizationPercent: 50,
      targetPercent: 30,
    });
    const el = render({ recommended: null, ranking: [item], excluded: [] });

    expect(el.querySelector('.best-item__over')?.textContent).toContain('50');
    expect(el.querySelector('.best-item__over')?.textContent).toContain('acima da meta');
  });

  it('links to the Cards page when the bank did not report a limit', () => {
    const item = makeItem({ availableCredit: null, creditLimit: 0, utilizationPercent: null });
    const el = render({ recommended: item, ranking: [item], excluded: [] });

    expect(el.querySelector('.best-group--recommended a')?.getAttribute('href')).toBe('/cards');
  });

  it('shows an empty message when there are no cards', () => {
    const el = render({ recommended: null, ranking: [], excluded: [] });

    expect(el.textContent).toContain('Nenhum cartão de crédito conectado');
    expect(el.querySelector('.best-group')).toBeNull();
  });
});
