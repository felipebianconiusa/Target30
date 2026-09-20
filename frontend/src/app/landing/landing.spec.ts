import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { TranslationService } from '../i18n/translation.service';
import { Landing } from './landing';

describe('Landing', () => {
  let fixture: ComponentFixture<Landing>;
  let http: HttpTestingController;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Landing],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    TestBed.inject(TranslationService).setLang('pt');
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Landing);
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  function fill(name: string, value: string): void {
    const input = el.querySelector(`input[name=${name}]`) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('presents the product, the login button and links to the legal pages', () => {
    expect(el.querySelector('h1')?.textContent).toBe('Target30');
    expect(el.querySelectorAll('.landing__feature').length).toBe(4);
    expect(el.querySelector('app-google-login-button')).not.toBeNull();
    const links = Array.from(el.querySelectorAll('.landing__footer a')).map((a) => a.getAttribute('href'));
    expect(links).toEqual(['/terms', '/privacy']);
    expect(el.textContent).toContain('não é aconselhamento financeiro');
  });

  it('sends the email to the waitlist and confirms', () => {
    fill('email', ' ana@example.com ');
    fill('note', 'cartões');
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const req = http.expectOne('/api/waitlist');
    expect(req.request.body).toEqual({ email: 'ana@example.com', note: 'cartões', language: 'pt', website: '' });
    req.flush(null);
    fixture.detectChanges();

    expect(el.querySelector('.landing__ok')?.textContent).toContain('Você está na lista');
    expect(el.querySelector('form')).toBeNull();
  });

  it('does not send anything without an email', () => {
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    http.expectNone('/api/waitlist');
  });

  it('shows a friendly message when the signup fails or is rate limited', () => {
    fill('email', 'ana@example.com');
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    http.expectOne('/api/waitlist').flush({}, { status: 429, statusText: 'Too Many Requests' });
    fixture.detectChanges();

    expect(el.querySelector('.landing__error')?.textContent).toContain('Muitas tentativas');
  });

  it('keeps the honeypot field out of view but still submits its value when a bot fills it', () => {
    fill('email', 'bot@example.com');
    fill('website', 'http://spam.example');
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    expect(http.expectOne('/api/waitlist').request.body.website).toBe('http://spam.example');
    expect((el.querySelector('.landing__trap') as HTMLElement).getAttribute('tabindex')).toBe('-1');
  });
});
