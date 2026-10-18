import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CategoryTotal, FinanceService } from './finance.service';

export interface BudgetLine {
  id: number;
  categoryId: number;
  name: string;
  color: string;
  budget: number;
  spent: number;
}

export interface BudgetsData {
  currencies: string[];
  currency: string | null;
  month: string;
  firstMonth: string | null;
  lastMonth: string | null;
  budgeted: number;
  spent: number;
  totalSpent: number;
  canCopy: boolean;
  lines: BudgetLine[];
  unbudgeted: CategoryTotal[];
}

export type GoalStatus = 'Completed' | 'OnTrack' | 'Behind' | 'NoDate';

export interface Goal {
  id: number;
  name: string;
  currency: string;
  target: number;
  saved: number;
  remaining: number;
  targetDate: string | null;
  accountId: number | null;
  monthlyNeeded: number | null;
  status: GoalStatus;
}

export interface GoalInput {
  name: string;
  currency: string;
  target: number;
  targetDate: string | null;
  accountId: number | null;
  saved: number | null;
}

/** Budgets (per month, category and currency) and savings goals. */
@Injectable({ providedIn: 'root' })
export class PlanningService {
  private readonly http = inject(HttpClient);
  private readonly finance = inject(FinanceService);

  budgets() {
    let params = new HttpParams();
    const currency = this.finance.currencyChoice();
    const month = this.finance.monthChoice();
    if (currency) params = params.set('currency', currency);
    if (month) params = params.set('month', month);
    return firstValueFrom(this.http.get<BudgetsData>('/api/budgets', { params }));
  }

  /** Sets the budget for a category in a month; an amount of 0 removes it. */
  setBudget(month: string, categoryId: number, currency: string, amount: number) {
    return firstValueFrom(this.http.put('/api/budgets', { month, categoryId, currency, amount }));
  }

  copyBudgets(fromMonth: string, toMonth: string, currency: string) {
    return firstValueFrom(this.http.post<{ copied: number }>('/api/budgets/copy', { fromMonth, toMonth, currency }));
  }

  goals() {
    return firstValueFrom(this.http.get<Goal[]>('/api/goals'));
  }

  saveGoal(id: number | null, input: GoalInput) {
    return firstValueFrom(id === null ? this.http.post<Goal>('/api/goals', input) : this.http.put<Goal>(`/api/goals/${id}`, input));
  }

  deleteGoal(id: number) {
    return firstValueFrom(this.http.delete(`/api/goals/${id}`));
  }

  recurring() {
    let params = new HttpParams();
    const currency = this.finance.currencyChoice();
    if (currency) params = params.set('currency', currency);
    return firstValueFrom(this.http.get<RecurringList>('/api/recurring', { params }));
  }

  saveRecurring(id: number | null, input: RecurringInput) {
    return firstValueFrom(id === null ? this.http.post<RecurringItem>('/api/recurring', input) : this.http.put<RecurringItem>(`/api/recurring/${id}`, input));
  }

  deleteRecurring(id: number) {
    return firstValueFrom(this.http.delete(`/api/recurring/${id}`));
  }

  detectRecurring() {
    return firstValueFrom(this.http.post<{ added: number }>('/api/recurring/detect', null));
  }

  forecast(days: number, includeVariable: boolean) {
    let params = new HttpParams().set('days', days).set('includeVariable', includeVariable);
    const currency = this.finance.currencyChoice();
    if (currency) params = params.set('currency', currency);
    return firstValueFrom(this.http.get<Forecast>('/api/forecast', { params }));
  }

  contribute(id: number, amount: number) {
    return firstValueFrom(this.http.post<Goal>(`/api/goals/${id}/contribute`, { amount }));
  }
}

export type RecurringFrequency = 'Weekly' | 'Biweekly' | 'Monthly' | 'Quarterly' | 'Yearly';
export type RecurringStatus = 'Detected' | 'Confirmed' | 'Dismissed';

export interface RecurringItem {
  id: number;
  accountId: number;
  payee: string;
  currency: string;
  amount: number;
  frequency: RecurringFrequency;
  nextDue: string;
  status: RecurringStatus;
  lastSeen: string | null;
  isManual: boolean;
}

export interface RecurringList {
  currencies: string[];
  currency: string | null;
  items: RecurringItem[];
}

export interface RecurringInput {
  accountId?: number;
  payee?: string;
  amount?: number;
  frequency?: RecurringFrequency;
  nextDue?: string;
  status?: RecurringStatus;
}

export interface Forecast {
  currencies: string[];
  currency: string | null;
  days: number;
  startDate: string;
  startBalance: number;
  endBalance: number;
  lowest: number;
  lowestDate: string;
  dailyVariable: number;
  points: number[];
  events: { date: string; payee: string; amount: number }[];
}
