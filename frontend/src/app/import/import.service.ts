import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface ImportAccount {
  accountId: string;
  name: string;
  nickname: string | null;
  institutionName: string | null;
  type: 'Depository' | 'Credit' | string;
  isManual: boolean;
}

export interface ImportRequest {
  csv: string;
  accountId: string;
  convention: 'expense_negative' | 'expense_positive';
  dateFormat: 'auto' | 'iso' | 'mdy' | 'dmy';
}

export interface ImportRow {
  line: number;
  date: string;
  description: string;
  amount: number;
  duplicate: boolean;
}

export interface ImportPreview {
  rows: ImportRow[];
  errors: { line: number; message: string }[];
  newCount: number;
  duplicateCount: number;
  errorCount: number;
}

export interface ImportResult {
  imported: number;
  duplicates: number;
  errors: number;
}

export interface ManualAccountRequest {
  name: string;
  type: 'Depository' | 'Credit';
  institutionName: string | null;
  currentBalance: number | null;
  creditLimit: number | null;
}

@Injectable({ providedIn: 'root' })
export class ImportService {
  constructor(private readonly http: HttpClient) {}

  getAccounts(): Observable<ImportAccount[]> {
    return this.http.get<ImportAccount[]>('/api/import/accounts');
  }

  createManualAccount(request: ManualAccountRequest): Observable<ImportAccount> {
    return this.http.post<ImportAccount>('/api/import/accounts', request);
  }

  preview(request: ImportRequest): Observable<ImportPreview> {
    return this.http.post<ImportPreview>('/api/import/preview', request);
  }

  commit(request: ImportRequest): Observable<ImportResult> {
    return this.http.post<ImportResult>('/api/import/commit', request);
  }
}
