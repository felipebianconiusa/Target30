import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { Privacy } from './privacy';
import { Terms } from './terms';

describe('Legal pages', () => {
  function render<T>(component: new () => T): HTMLElement {
    TestBed.configureTestingModule({ imports: [component as never], providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(component);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('the terms say the service is not financial advice and warn that the text is a draft', () => {
    const el = render(Terms);

    expect(el.querySelector('h1')?.textContent).toBe('Termos de Uso');
    expect(el.textContent).toContain('Não é aconselhamento financeiro');
    expect(el.querySelector('.legal__draft')).not.toBeNull();
  });

  it('the privacy policy names Plaid and Stripe and links the Plaid policy', () => {
    const el = render(Privacy);

    expect(el.textContent).toContain('Plaid');
    expect(el.textContent).toContain('Stripe');
    expect(el.querySelector('a[href*="plaid.com/legal"]')).not.toBeNull();
  });
});
