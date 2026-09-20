import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { TranslationService } from '../i18n/translation.service';
import { Settings } from './settings';

describe('Settings automatic backup line', () => {
  function render(status: object, rules: object[] = [], pushTopic: string | null = null): HTMLElement {
    TestBed.configureTestingModule({
      imports: [Settings],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(TranslationService).setLang('pt');
    const fixture = TestBed.createComponent(Settings);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.match('/api/settings').forEach((r) =>
      r.flush({
        globalTargetUtilizationPercent: 30, notifyDaysBeforeClosing: 3, notificationsEnabled: true,
        weeklyDigestEnabled: true, email: null, lastDigestSentDate: null, lowBalanceThreshold: 0, pushTopic,
      }),
    );
    http.expectOne('/api/backup/auto-status').flush(status);
    http.match('/api/category-rules').forEach((r) => r.flush(rules));
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the latest automatic backup and where the copies are', () => {
    const el = render({
      enabled: true, directory: 'D:\\backups', lastBackupUtc: '2026-09-19T12:30:45', count: 3, keepCount: 14,
    });

    const text = el.querySelector('.settings__auto-backup')?.textContent ?? '';
    expect(text).toContain('último em');
    expect(text).toContain('3 guardados');
    expect(text).toContain('D:\\backups');
    expect(text).toContain('tokens do Plaid');
  });

  it('says the first backup is still to come when there is none yet', () => {
    const el = render({ enabled: true, directory: 'D:\\b', lastBackupUtc: null, count: 0, keepCount: 14 });

    expect(el.querySelector('.settings__auto-backup')?.textContent).toContain('o primeiro será criado');
  });

  it('says so when automatic backup is turned off', () => {
    const el = render({ enabled: false, directory: 'D:\\b', lastBackupUtc: null, count: 0, keepCount: 14 });

    expect(el.querySelector('.settings__auto-backup')?.textContent).toContain('desligado');
  });

  it('lists category rules and lets the user remove one', () => {
    const el = render(
      { enabled: true, directory: 'D:\\b', lastBackupUtc: null, count: 0, keepCount: 14 },
      [{ id: 7, merchantKey: 'costco', category: 'FOOD_AND_DRINK' }],
    );

    expect(el.querySelector('.settings__rules')?.textContent).toContain('costco');

    (el.querySelector('.settings__rules button') as HTMLButtonElement).click();
    TestBed.inject(HttpTestingController).expectOne('/api/category-rules/7').flush(null);
  });

  describe('phone notifications (ntfy)', () => {
    const backup = { enabled: true, directory: 'D:\\b', lastBackupUtc: null, count: 0, keepCount: 14 };

    it('shows the saved topic and generates a hard-to-guess one on request', () => {
      const el = render(backup, [], 'meu-topico-123');
      const input = el.querySelector('input[maxlength="64"]') as HTMLInputElement;
      expect(input.value).toBe('meu-topico-123');

      const generate = Array.from(el.querySelectorAll('.settings__push-actions button'))[0] as HTMLButtonElement;
      generate.click();
      TestBed.inject(ApplicationRef).tick();

      expect(input.value).toMatch(/^t30-[0-9a-z]{20,}$/);
    });

    it('saves the settings with the topic and then sends the test', () => {
      const el = render(backup, [], 'meu-topico-123');
      const http = TestBed.inject(HttpTestingController);
      const test = Array.from(el.querySelectorAll('.settings__push-actions button'))[1] as HTMLButtonElement;

      test.click();

      const save = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/settings');
      expect(save.request.body.pushTopic).toBe('meu-topico-123');
      save.flush({});
      http.expectOne((r) => r.method === 'POST' && r.url === '/api/settings/push-test').flush(null);
    });

    it('keeps the test button disabled without a topic', () => {
      const el = render(backup);

      const test = Array.from(el.querySelectorAll('.settings__push-actions button'))[1] as HTMLButtonElement;
      expect(test.disabled).toBe(true);
    });
  });
});