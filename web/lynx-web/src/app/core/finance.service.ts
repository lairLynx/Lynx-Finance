import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type AccountType = 'Current' | 'Savings' | 'CreditCard' | 'Cash';

export interface AccountDto {
  id: number;
  name: string;
  bank: string | null;
  iban: string | null;
  type: AccountType;
  currency: string;
  balance: number;
  openingBalance: number;
  transactionCount: number;
  lastTransactionDate: string | null;
  lastImportedAt: string | null;
}

export interface AccountInput {
  name: string;
  bank: string;
  iban: string;
  type: AccountType;
  currency: string;
  openingBalance: number;
}

export interface CategoryDto {
  id: number;
  name: string;
  color: string;
  isIncome: boolean;
}

export interface TransactionDto {
  id: number;
  accountId: number;
  date: string;
  payee: string;
  description: string;
  amount: number;
  categoryId: number | null;
  isTransfer: boolean;
}

export interface TransactionInput {
  accountId: number;
  date: string;
  payee: string;
  description: string;
  amount: number;
  categoryId: number | null;
  isTransfer?: boolean;
}

export interface TransactionQuery {
  accountId?: number | null;
  categoryId?: number | null;
  uncategorized?: boolean;
  transfer?: boolean | null;
  q?: string;
  page?: number;
  pageSize?: number;
}

export interface TransactionPage {
  items: TransactionDto[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ImportResult {
  total: number;
  imported: number;
  duplicates: number;
  transfersMarked: number;
}

export interface MonthPoint { month: string; income: number; expense: number }
export interface BalancePoint { month: string; balance: number }
export interface CategoryTotal { categoryId: number | null; name: string; color: string; amount: number }
export interface PayeeTotal { payee: string; amount: number; count: number }

/** Everything for one currency. Currencies are never converted or summed together. */
export interface Overview {
  currencies: string[];
  currency: string | null;
  month: string;
  firstMonth: string | null;
  lastMonth: string | null;
  income: number;
  expense: number;
  netWorth: number;
  previousNetWorth: number;
  transactionCount: number;
  uncategorizedCount: number;
  rangeIncome: number;
  rangeExpense: number;
  transfers: number;
  months: MonthPoint[];
  balances: BalancePoint[];
  categories: CategoryTotal[];
  topPayees: PayeeTotal[];
  accounts: AccountDto[];
  recent: TransactionDto[];
}

const CURRENCY_KEY = 'lynx-currency';

/** Talks to the accounts/categories/transactions API and caches accounts and categories for the pages. */
@Injectable({ providedIn: 'root' })
export class FinanceService {
  private readonly http = inject(HttpClient);

  readonly accounts = signal<AccountDto[]>([]);
  readonly categories = signal<CategoryDto[]>([]);
  /** Selected currency and month for the dashboard/reports; null lets the server pick (busiest currency, latest month). */
  readonly currencyChoice = signal<string | null>(readStoredCurrency());
  readonly monthChoice = signal<string | null>(null);

  chooseCurrency(currency: string) {
    this.currencyChoice.set(currency);
    this.monthChoice.set(null);
    try { localStorage.setItem(CURRENCY_KEY, currency); } catch { /* storage unavailable */ }
  }

  overview(months: number) {
    let params = new HttpParams().set('months', months);
    const currency = this.currencyChoice();
    const month = this.monthChoice();
    if (currency) params = params.set('currency', currency);
    if (month) params = params.set('month', month);
    return firstValueFrom(this.http.get<Overview>('/api/overview', { params }));
  }

  readonly accountById = computed(() => new Map(this.accounts().map((a) => [a.id, a])));
  readonly categoryById = computed(() => new Map(this.categories().map((c) => [c.id, c])));

  async refreshAccounts() {
    this.accounts.set(await firstValueFrom(this.http.get<AccountDto[]>('/api/accounts')));
  }

  async refreshCategories() {
    this.categories.set(await firstValueFrom(this.http.get<CategoryDto[]>('/api/categories')));
  }

  async saveAccount(id: number | null, input: AccountInput) {
    await firstValueFrom(id === null ? this.http.post('/api/accounts', input) : this.http.put(`/api/accounts/${id}`, input));
    await this.refreshAccounts();
  }

  async deleteAccount(id: number) {
    await firstValueFrom(this.http.delete(`/api/accounts/${id}`));
    await this.refreshAccounts();
  }

  importFile(accountId: number, file: File) {
    const form = new FormData();
    form.append('file', file, file.name);
    return firstValueFrom(this.http.post<ImportResult>(`/api/accounts/${accountId}/import`, form)).finally(() => this.refreshAccounts());
  }

  async saveCategory(id: number | null, input: { name: string; color: string; isIncome: boolean }) {
    await firstValueFrom(id === null ? this.http.post('/api/categories', input) : this.http.put(`/api/categories/${id}`, input));
    await this.refreshCategories();
  }

  async deleteCategory(id: number) {
    await firstValueFrom(this.http.delete(`/api/categories/${id}`));
    await this.refreshCategories();
  }

  transactions(query: TransactionQuery) {
    let params = new HttpParams();
    if (query.accountId) params = params.set('accountId', query.accountId);
    if (query.categoryId) params = params.set('categoryId', query.categoryId);
    if (query.uncategorized) params = params.set('uncategorized', true);
    if (query.transfer !== null && query.transfer !== undefined) params = params.set('transfer', query.transfer);
    if (query.q) params = params.set('q', query.q);
    params = params.set('page', query.page ?? 1).set('pageSize', query.pageSize ?? 50);
    return firstValueFrom(this.http.get<TransactionPage>('/api/transactions', { params }));
  }

  async saveTransaction(id: number | null, input: TransactionInput) {
    await firstValueFrom(id === null ? this.http.post('/api/transactions', input) : this.http.put(`/api/transactions/${id}`, input));
    await this.refreshAccounts();
  }

  setTransfer(id: number, isTransfer: boolean, applyToPayee: boolean) {
    return firstValueFrom(this.http.patch<{ updated: number }>(`/api/transactions/${id}/transfer`, { isTransfer, applyToPayee }));
  }

  detectTransfers() {
    return firstValueFrom(this.http.post<{ marked: number }>('/api/transfers/detect', null));
  }

  assignCategory(id: number, categoryId: number | null) {
    return firstValueFrom(this.http.patch<TransactionDto>(`/api/transactions/${id}/category`, { categoryId }));
  }

  async deleteTransaction(id: number) {
    await firstValueFrom(this.http.delete(`/api/transactions/${id}`));
    await this.refreshAccounts();
  }
}

function readStoredCurrency(): string | null {
  try { return localStorage.getItem(CURRENCY_KEY); } catch { return null; }
}
