// Static mock data for the UI mock-up. Replaced by API calls as each feature is built.

export interface Account {
  id: number;
  name: string;
  bank: string;
  iban: string;
  type: 'Current' | 'Savings' | 'Credit card';
  balance: number;
  currency: string;
  status: 'ok' | 'expiring' | 'manual';
  lastSynced: string;
  consentDaysLeft?: number;
}

export interface Category {
  id: number;
  name: string;
  color: string;
}

export interface Transaction {
  id: number;
  date: string;
  payee: string;
  description: string;
  amount: number;
  categoryId: number | null;
  accountId: number;
}

export interface BudgetLine {
  categoryId: number;
  budget: number;
  spent: number;
}

export interface Goal {
  id: number;
  name: string;
  target: number;
  saved: number;
  targetDate: string;
  monthlyNeeded: number;
  onTrack: boolean;
}

export interface RecurringItem {
  id: number;
  payee: string;
  amount: number;
  frequency: 'Weekly' | 'Monthly' | 'Yearly';
  nextDue: string;
  confirmed: boolean;
}

export const CURRENCY = 'EUR';

export const CATEGORIES: Category[] = [
  { id: 1, name: 'Groceries', color: '#4f8cff' },
  { id: 2, name: 'Rent & Utilities', color: '#8b5cf6' },
  { id: 3, name: 'Transport', color: '#f59e0b' },
  { id: 4, name: 'Dining Out', color: '#ef4444' },
  { id: 5, name: 'Subscriptions', color: '#14b8a6' },
  { id: 6, name: 'Entertainment', color: '#ec4899' },
  { id: 7, name: 'Health', color: '#22c55e' },
  { id: 8, name: 'Salary', color: '#10b981' },
  { id: 9, name: 'Shopping', color: '#f97316' },
];

export const ACCOUNTS: Account[] = [
  { id: 1, name: 'Main Current Account', bank: 'ING', iban: 'NL•• •••• •••• 4821', type: 'Current', balance: 3248.55, currency: 'EUR', status: 'ok', lastSynced: '2 hours ago', consentDaysLeft: 74 },
  { id: 2, name: 'Rainy Day Savings', bank: 'ING', iban: 'NL•• •••• •••• 9033', type: 'Savings', balance: 12400.0, currency: 'EUR', status: 'ok', lastSynced: '2 hours ago', consentDaysLeft: 74 },
  { id: 3, name: 'Visa Credit Card', bank: 'Revolut', iban: 'LT•• •••• •••• 1175', type: 'Credit card', balance: -412.3, currency: 'EUR', status: 'expiring', lastSynced: '1 day ago', consentDaysLeft: 5 },
  { id: 4, name: 'Cash Wallet', bank: 'Manual', iban: '—', type: 'Current', balance: 85.0, currency: 'EUR', status: 'manual', lastSynced: 'never' },
];

export const TRANSACTIONS: Transaction[] = [
  { id: 1, date: '2026-10-10', payee: 'Albert Heijn', description: 'Card payment AH Amsterdam', amount: -64.2, categoryId: 1, accountId: 1 },
  { id: 2, date: '2026-10-09', payee: 'Spotify', description: 'Spotify Premium', amount: -11.99, categoryId: 5, accountId: 3 },
  { id: 3, date: '2026-10-08', payee: 'NS Reizigers', description: 'OV-chipkaart top-up', amount: -30.0, categoryId: 3, accountId: 1 },
  { id: 4, date: '2026-10-07', payee: 'Restaurant De Kas', description: 'Dinner', amount: -78.5, categoryId: 4, accountId: 3 },
  { id: 5, date: '2026-10-05', payee: 'Landlord BV', description: 'Rent October', amount: -1150.0, categoryId: 2, accountId: 1 },
  { id: 6, date: '2026-10-03', payee: 'Pathé', description: 'Cinema tickets', amount: -24.0, categoryId: 6, accountId: 3 },
  { id: 7, date: '2026-10-01', payee: 'Employer Ltd', description: 'Salary October', amount: 3650.0, categoryId: 8, accountId: 1 },
  { id: 8, date: '2026-09-29', payee: 'Jumbo', description: 'Groceries', amount: -52.8, categoryId: 1, accountId: 1 },
  { id: 9, date: '2026-09-27', payee: 'Apotheek', description: 'Pharmacy', amount: -18.4, categoryId: 7, accountId: 1 },
  { id: 10, date: '2026-09-25', payee: 'Zalando', description: 'Order 88213', amount: -89.95, categoryId: 9, accountId: 3 },
  { id: 11, date: '2026-09-24', payee: 'Netflix', description: 'Netflix Standard', amount: -13.99, categoryId: 5, accountId: 3 },
  { id: 12, date: '2026-09-22', payee: 'Unknown merchant', description: 'POS 4432 *PAYMENT', amount: -19.0, categoryId: null, accountId: 1 },
];

export const BUDGET: BudgetLine[] = [
  { categoryId: 1, budget: 450, spent: 312.4 },
  { categoryId: 2, budget: 1350, spent: 1280.0 },
  { categoryId: 3, budget: 120, spent: 96.5 },
  { categoryId: 4, budget: 150, spent: 171.5 },
  { categoryId: 5, budget: 40, spent: 38.97 },
  { categoryId: 6, budget: 80, spent: 24.0 },
  { categoryId: 7, budget: 60, spent: 18.4 },
  { categoryId: 9, budget: 150, spent: 89.95 },
];

export const GOALS: Goal[] = [
  { id: 1, name: 'Emergency fund', target: 15000, saved: 12400, targetDate: '2027-03-01', monthlyNeeded: 520, onTrack: true },
  { id: 2, name: 'Summer holiday', target: 3000, saved: 950, targetDate: '2027-06-15', monthlyNeeded: 228, onTrack: false },
  { id: 3, name: 'New laptop', target: 1800, saved: 1500, targetDate: '2026-12-20', monthlyNeeded: 150, onTrack: true },
];

export const RECURRING: RecurringItem[] = [
  { id: 1, payee: 'Landlord BV', amount: -1150, frequency: 'Monthly', nextDue: '2026-11-05', confirmed: true },
  { id: 2, payee: 'Employer Ltd', amount: 3650, frequency: 'Monthly', nextDue: '2026-11-01', confirmed: true },
  { id: 3, payee: 'Spotify', amount: -11.99, frequency: 'Monthly', nextDue: '2026-11-09', confirmed: true },
  { id: 4, payee: 'Netflix', amount: -13.99, frequency: 'Monthly', nextDue: '2026-10-24', confirmed: false },
  { id: 5, payee: 'Gym Membership', amount: -34.5, frequency: 'Monthly', nextDue: '2026-10-15', confirmed: false },
  { id: 6, payee: 'Car Insurance', amount: -412, frequency: 'Yearly', nextDue: '2027-01-12', confirmed: false },
];

/** 90-day projected balance of all current accounts (deterministic pseudo-data). */
export const FORECAST: number[] = (() => {
  const points: number[] = [];
  let balance = 3248.55;
  for (let day = 0; day < 90; day++) {
    balance -= 38; // average daily discretionary spend
    if (day % 30 === 20) balance += 3650; // salary
    if (day % 30 === 4) balance -= 1150; // rent
    points.push(Math.round(balance * 100) / 100);
  }
  return points;
})();

export const NET_WORTH_HISTORY: number[] = [9800, 10150, 10400, 10950, 11200, 11800, 12350, 12900, 13600, 14400, 15100, 15321.25];

export const MONTHLY_INCOME_EXPENSE: { month: string; income: number; expense: number }[] = [
  { month: 'May', income: 3650, expense: 2890 },
  { month: 'Jun', income: 3650, expense: 3120 },
  { month: 'Jul', income: 3950, expense: 3340 },
  { month: 'Aug', income: 3650, expense: 2780 },
  { month: 'Sep', income: 3650, expense: 3010 },
  { month: 'Oct', income: 3650, expense: 2590 },
];

export function categoryById(id: number | null): Category | undefined {
  return CATEGORIES.find((c) => c.id === id);
}
