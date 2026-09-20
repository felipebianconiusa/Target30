import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { TranslationService } from '../i18n/translation.service';
import { Settings } from './settings';

describe('Settings automatic backup line', () => {
  function render(status: object): HTMLElement {
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
        weeklyDigestEnabled: true, email: null, lastDigestSentDate: null, lowBalanceThreshold: 0,
      }),
    );
    http.expectOne('/api/backup/auto-status').flush(status);
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
});
