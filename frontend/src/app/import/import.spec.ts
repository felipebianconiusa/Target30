import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { TranslationService } from '../i18n/translation.service';
import { Import } from './import';
import { ImportAccount, ImportPreview } from './import.service';

const ACCOUNTS: ImportAccount[] = [
  { accountId: 'acc', name: 'Checking', nickname: null, institutionName: 'Bank of America', type: 'Depository', isManual: false },
];

const PREVIEW: ImportPreview = {
  rows: [
    { line: 2, date: '2026-09-01', description: 'Grocery Store', amount: 52.3, duplicate: false },
    { line: 3, date: '2026-09-02', description: 'Payroll', amount: -2000, duplicate: true },
  ],
  errors: [{ line: 4, message: 'Data inválida: "x".' }],
  newCount: 1,
  duplicateCount: 1,
  errorCount: 1,
};

describe('Import page', () => {
  let fixture: ComponentFixture<Import>;
  let http: HttpTestingController;
  let el: HTMLElement;
  let component: any;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Import],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    TestBed.inject(TranslationService).setLang('pt');
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Import);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    http.expectOne('/api/import/accounts').flush(ACCOUNTS);
    fixture.detectChanges();
  });

  function chooseAccountAndFile(): void {
    component.setAccount('acc');
    component.csv.set('Date,Description,Amount\n2026-09-01,Grocery Store,-52.30\n');
    fixture.detectChanges();
  }

  it('lists the accounts and keeps Preview disabled until an account and a file are chosen', () => {
    expect(el.querySelector('select option[value=acc]')?.textContent).toContain('Bank of America — Checking');
    const preview = el.querySelector('.import__primary') as HTMLButtonElement;
    expect(preview.disabled).toBe(true);

    chooseAccountAndFile();

    expect(preview.disabled).toBe(false);
  });

  it('previews with the chosen options, showing new rows, duplicates and errors', () => {
    chooseAccountAndFile();
    component.setConvention('expense_positive');
    component.setDateFormat('dmy');

    component.runPreview();
    const req = http.expectOne('/api/import/preview');
    expect(req.request.body).toEqual({
      csv: 'Date,Description,Amount\n2026-09-01,Grocery Store,-52.30\n',
      accountId: 'acc',
      convention: 'expense_positive',
      dateFormat: 'dmy',
    });
    req.flush(PREVIEW);
    fixture.detectChanges();

    expect(el.querySelector('.import__preview')?.textContent).toContain('1 nova(s), 1 já existente(s), 1 com erro');
    expect(el.querySelectorAll('.import__dup').length).toBe(1);
    expect(el.querySelector('.import__errors')?.textContent).toContain('Linha 4');
    // gasto aparece negativo, entrada positiva
    expect(el.querySelector('tbody tr')?.textContent).toContain('-US$');
  });

  it('commits after the preview and shows what was imported', () => {
    chooseAccountAndFile();
    component.runPreview();
    http.expectOne('/api/import/preview').flush(PREVIEW);
    fixture.detectChanges();

    (Array.from(el.querySelectorAll('.import__preview .import__primary'))[0] as HTMLButtonElement).click();
    http.expectOne('/api/import/commit').flush({ imported: 1, duplicates: 1, errors: 1 });
    fixture.detectChanges();

    expect(el.querySelector('.import__ok')?.textContent).toContain('Importado: 1 nova(s)');
    expect(el.querySelector('.import__preview')).toBeNull();
  });

  it('shows the API message when the file cannot be read', () => {
    chooseAccountAndFile();
    component.runPreview();
    http
      .expectOne('/api/import/preview')
      .flush({ code: 'unreadable_file', message: 'Não reconheci as colunas.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(el.querySelector('.import__error')?.textContent).toContain('Não reconheci as colunas');
  });

  it('creates a manual account and selects it', () => {
    component.toggleManual();
    fixture.detectChanges();
    component.setManualName('Nubank');
    component.createManualAccount();

    const req = http.expectOne((r) => r.method === 'POST' && r.url === '/api/import/accounts');
    expect(req.request.body.name).toBe('Nubank');
    expect(req.request.body.type).toBe('Credit');
    req.flush({ accountId: 'manual-1', name: 'Nubank', nickname: null, institutionName: 'Manual', type: 'Credit', isManual: true });
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/import/accounts').flush([
      ...ACCOUNTS,
      { accountId: 'manual-1', name: 'Nubank', nickname: null, institutionName: 'Manual', type: 'Credit', isManual: true },
    ]);
    fixture.detectChanges();

    expect(component.accountId()).toBe('manual-1');
  });
});
