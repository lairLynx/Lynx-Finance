import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorMessage } from '../core/auth.service';
import { FinanceService, TransactionDto, TransactionPage } from '../core/finance.service';
import { I18n } from '../core/i18n.service';
import { ImportPanel } from '../shared/import-panel';

const PAGE_SIZE = 50;

interface TxForm { accountId: number; date: string; payee: string; description: string; amount: number | null; categoryId: number | null; isTransfer: boolean }

@Component({
  selector: 'lynx-transactions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, ImportPanel],
  template: `
    <div class="row between">
      <div><h1>{{ i18n.t('Transactions') }}</h1><p class="sub">{{ i18n.t('{n} transactions', { n: result().total }) }}</p></div>
      <div class="row">
        <button class="btn" type="button" [disabled]="finance.accounts().length < 2" [title]="i18n.t('Find money moved between your own accounts')" (click)="detect()">{{ i18n.t('Detect transfers') }}</button>
        <button class="btn" type="button" [disabled]="!finance.accounts().length" (click)="showImport.set(true)">{{ i18n.t('Import file…') }}</button>
        <button class="btn primary" type="button" [disabled]="!finance.accounts().length" (click)="startNew()">{{ i18n.t('+ Add transaction') }}</button>
      </div>
    </div>

    @if (showImport()) {
      <lynx-import-panel (imported)="reload()" (closed)="showImport.set(false)" />
    }

    @if (notice()) { <div class="notice good" style="margin-bottom:16px">{{ i18n.t(notice(), { n: noticeCount() }) }}</div> }

    @if (form(); as f) {
      <form class="card" style="margin-bottom:16px" (ngSubmit)="save()">
        <h2>{{ editingId() === null ? i18n.t('New transaction') : i18n.t('Edit transaction') }}</h2>
        <div class="form-grid">
          <label>{{ i18n.t('Account') }}
            <select class="input" name="account" [(ngModel)]="f.accountId">
              @for (a of finance.accounts(); track a.id) { <option [ngValue]="a.id">{{ a.name }}</option> }
            </select>
          </label>
          <label>{{ i18n.t('Date') }}<input class="input" name="date" type="date" [(ngModel)]="f.date" required /></label>
          <label>{{ i18n.t('Payee') }}<input class="input" name="payee" [(ngModel)]="f.payee" required maxlength="200" /></label>
          <label>{{ i18n.t('Description') }}<input class="input" name="description" [(ngModel)]="f.description" maxlength="500" /></label>
          <label>{{ i18n.t('Amount') }} ({{ i18n.t('negative = expense') }})<input class="input" name="amount" type="number" step="0.01" [(ngModel)]="f.amount" required /></label>
          <label class="check"><input type="checkbox" name="transfer" [(ngModel)]="f.isTransfer" /> {{ i18n.t('Transfer between my accounts') }}</label>
          <label>{{ i18n.t('Category') }}
            <select class="input" name="category" [(ngModel)]="f.categoryId">
              <option [ngValue]="null">{{ i18n.t('Uncategorized') }}</option>
              @for (c of finance.categories(); track c.id) { <option [ngValue]="c.id">{{ i18n.t(c.name) }}</option> }
            </select>
          </label>
        </div>
        @if (error()) { <div class="notice bad">{{ i18n.t(error()) }}</div> }
        <div class="row" style="margin-top:12px">
          <button class="btn primary" type="submit" [disabled]="busy()">{{ i18n.t('Save') }}</button>
          <button class="btn" type="button" (click)="form.set(null)">{{ i18n.t('Cancel') }}</button>
        </div>
      </form>
    }

    <div class="card">
      <div class="row filters">
        <input type="search" class="input grow" [placeholder]="i18n.t('Search payee or description…')" (input)="onSearch($any($event.target).value)" />
        <select class="input" [ngModel]="accountFilter()" (ngModelChange)="setAccountFilter($event)" [attr.aria-label]="i18n.t('Account')">
          <option [ngValue]="null">{{ i18n.t('All accounts') }}</option>
          @for (a of finance.accounts(); track a.id) { <option [ngValue]="a.id">{{ a.name }}</option> }
        </select>
        <select class="input" [ngModel]="transferFilter()" (ngModelChange)="setTransferFilter($event)" [attr.aria-label]="i18n.t('Type')">
          <option value="all">{{ i18n.t('All transactions') }}</option>
          <option value="hide">{{ i18n.t('Excluding transfers') }}</option>
          <option value="only">{{ i18n.t('Transfers only') }}</option>
        </select>
        <select class="input" [ngModel]="categoryFilter()" (ngModelChange)="setCategoryFilter($event)" [attr.aria-label]="i18n.t('Category')">
          <option value="all">{{ i18n.t('All categories') }}</option>
          <option value="none">{{ i18n.t('Uncategorized') }}</option>
          @for (c of finance.categories(); track c.id) { <option [value]="c.id">{{ i18n.t(c.name) }}</option> }
        </select>
      </div>

      @if (loadError()) { <div class="notice bad">{{ i18n.t(loadError()) }}</div> }

      @if (!result().items.length && !loading()) {
        <div class="empty">{{ finance.accounts().length ? i18n.t('No transactions match. Import a statement or add one manually.') : i18n.t('Create an account first, then import or add transactions.') }}</div>
      } @else {
        <table class="table">
          <thead><tr><th>{{ i18n.t('Date') }}</th><th>{{ i18n.t('Payee') }}</th><th>{{ i18n.t('Account') }}</th><th>{{ i18n.t('Category') }}</th><th class="num">{{ i18n.t('Amount') }}</th><th></th></tr></thead>
          <tbody>
            @for (t of result().items; track t.id) {
              <tr>
                <td class="muted">{{ i18n.date(t.date) }}</td>
                <td><strong>{{ t.payee }}</strong>@if (t.isTransfer) { <span class="badge transfer">{{ i18n.t('Transfer') }}</span> }@if (t.description) { <div class="muted">{{ t.description }}</div> }</td>
                <td class="muted">{{ accountName(t) }}</td>
                <td>
                  <select class="input cat" [attr.aria-label]="i18n.t('Category')" (change)="assign(t, $any($event.target).value)">
                    <option value="" [selected]="t.categoryId === null">{{ i18n.t('Uncategorized') }}</option>
                    @for (c of finance.categories(); track c.id) { <option [value]="c.id" [selected]="c.id === t.categoryId">{{ i18n.t(c.name) }}</option> }
                  </select>
                </td>
                <td class="num" [class.pos]="t.amount > 0" [class.neg]="t.amount < 0">{{ i18n.money(t.amount, currency(t)) }}</td>
                <td class="num nowrap">
                  <button class="btn link" type="button" (click)="toggleTransfer(t)">{{ t.isTransfer ? i18n.t('Not a transfer') : i18n.t('Transfer') }}</button>
                  <button class="btn link" type="button" (click)="edit(t)">{{ i18n.t('Edit') }}</button>
                  <button class="btn link" type="button" (click)="remove(t)">{{ i18n.t('Delete') }}</button>
                </td>
              </tr>
            }
          </tbody>
        </table>
        @if (pages() > 1) {
          <div class="row between" style="margin-top:12px">
            <button class="btn" type="button" [disabled]="page() <= 1" (click)="goTo(page() - 1)">◀</button>
            <span class="muted">{{ i18n.t('Page {page} of {pages}', { page: page(), pages: pages() }) }}</span>
            <button class="btn" type="button" [disabled]="page() >= pages()" (click)="goTo(page() + 1)">▶</button>
          </div>
        }
      }
    </div>
  `,
  styles: `
    .filters { margin-bottom: 12px; flex-wrap: wrap; }
    .grow { flex: 1; min-width: 200px; }
    .cat { max-width: 180px; padding: 5px 8px; }
    .nowrap { white-space: nowrap; }
    .badge.transfer { margin-left: 8px; }
    label.check { flex-direction: row; align-items: center; gap: 8px; align-self: end; padding-bottom: 8px; }
  `,
})
export class Transactions implements OnInit {
  protected readonly finance = inject(FinanceService);
  protected readonly i18n = inject(I18n);

  protected readonly result = signal<TransactionPage>({ items: [], total: 0, page: 1, pageSize: PAGE_SIZE });
  protected readonly loading = signal(false);
  protected readonly loadError = signal('');
  protected readonly page = signal(1);
  protected readonly pages = computed(() => Math.max(1, Math.ceil(this.result().total / PAGE_SIZE)));

  protected readonly query = signal('');
  protected readonly accountFilter = signal<number | null>(null);
  /** 'all', 'none' (uncategorized) or a category id as a string. */
  protected readonly categoryFilter = signal('all');
  protected readonly transferFilter = signal<'all' | 'hide' | 'only'>('all');
  protected readonly notice = signal('');
  protected readonly noticeCount = signal(0);

  protected readonly showImport = signal(false);
  protected readonly editingId = signal<number | null>(null);
  protected readonly form = signal<TxForm | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private loadSeq = 0;

  async ngOnInit() {
    await Promise.all([this.finance.refreshAccounts(), this.finance.refreshCategories()]).catch(() => undefined);
    await this.reload();
  }

  protected async reload() {
    const seq = ++this.loadSeq; // ignore out-of-order responses while typing
    this.loading.set(true);
    try {
      const result = await this.finance.transactions({
        accountId: this.accountFilter(),
        uncategorized: this.categoryFilter() === 'none',
        categoryId: this.categoryFilter() === 'all' || this.categoryFilter() === 'none' ? null : Number(this.categoryFilter()),
        transfer: this.transferFilter() === 'all' ? null : this.transferFilter() === 'only',
        q: this.query().trim(),
        page: this.page(),
        pageSize: PAGE_SIZE,
      });
      if (seq !== this.loadSeq) return;
      this.result.set(result);
      this.loadError.set('');
    } catch (err) {
      if (seq === this.loadSeq) this.loadError.set(errorMessage(err));
    } finally {
      if (seq === this.loadSeq) this.loading.set(false);
    }
  }

  protected onSearch(value: string) {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => { this.query.set(value); this.page.set(1); this.reload(); }, 250);
  }

  protected setAccountFilter(id: number | null) { this.accountFilter.set(id); this.page.set(1); this.reload(); }
  protected setTransferFilter(value: 'all' | 'hide' | 'only') { this.transferFilter.set(value); this.page.set(1); this.reload(); }
  protected setCategoryFilter(value: string) { this.categoryFilter.set(value); this.page.set(1); this.reload(); }
  protected goTo(page: number) { this.page.set(page); this.reload(); }

  protected accountName(t: TransactionDto) { return this.finance.accountById().get(t.accountId)?.name ?? ''; }
  protected currency(t: TransactionDto) { return this.finance.accountById().get(t.accountId)?.currency ?? 'EUR'; }

  protected startNew() {
    this.editingId.set(null);
    this.error.set('');
    this.form.set({
      accountId: this.accountFilter() ?? this.finance.accounts()[0]?.id ?? 0,
      date: new Date().toISOString().slice(0, 10),
      payee: '', description: '', amount: null, categoryId: null, isTransfer: false,
    });
  }

  protected edit(t: TransactionDto) {
    this.editingId.set(t.id);
    this.error.set('');
    this.form.set({ accountId: t.accountId, date: t.date, payee: t.payee, description: t.description, amount: t.amount, categoryId: t.categoryId, isTransfer: t.isTransfer });
  }

  protected async save() {
    const f = this.form();
    if (!f || f.amount === null) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.finance.saveTransaction(this.editingId(), { ...f, amount: Number(f.amount) });
      this.form.set(null);
      await this.reload();
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.busy.set(false);
    }
  }

  protected async assign(t: TransactionDto, value: string) {
    const categoryId = value === '' ? null : Number(value);
    try {
      const updated = await this.finance.assignCategory(t.id, categoryId);
      this.result.update((r) => ({ ...r, items: r.items.map((x) => (x.id === updated.id ? updated : x)) }));
      // The row may no longer match the active category filter.
      if (this.categoryFilter() !== 'all') await this.reload();
    } catch (err) {
      this.loadError.set(errorMessage(err));
      await this.reload(); // reset the dropdown to what the server has
    }
  }

  protected async detect() {
    try {
      const { marked } = await this.finance.detectTransfers();
      this.notice.set(marked > 0 ? '{n} transactions were marked as transfers between your accounts.' : 'No new transfers found.');
      this.noticeCount.set(marked);
      await this.reload();
    } catch (err) {
      this.loadError.set(errorMessage(err));
    }
  }

  protected async toggleTransfer(t: TransactionDto) {
    const marking = !t.isTransfer;
    // When marking, offer to apply it to every transaction from the same payee (e.g. all moves to your own savings account).
    const all = marking && confirm(this.i18n.t('Mark every transaction from "{payee}" as a transfer?', { payee: t.payee }));
    try {
      await this.finance.setTransfer(t.id, marking, all);
      await this.reload();
    } catch (err) {
      this.loadError.set(errorMessage(err));
    }
  }

  protected async remove(t: TransactionDto) {
    if (!confirm(this.i18n.t('Delete this transaction?'))) return;
    await this.finance.deleteTransaction(t.id);
    await this.reload();
  }
}
