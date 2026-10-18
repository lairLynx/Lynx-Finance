import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorMessage } from '../core/auth.service';
import { FinanceService } from '../core/finance.service';
import { I18n } from '../core/i18n.service';
import { Goal, GoalStatus, PlanningService } from '../core/planning.service';

interface GoalForm { name: string; currency: string; target: number | null; targetDate: string; accountId: number | null; saved: number | null }

const STATUS_LABELS: Record<GoalStatus, string> = { Completed: 'Completed', OnTrack: 'On track', Behind: 'Behind', NoDate: 'No deadline' };

@Component({
  selector: 'lynx-goals',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    <div class="row between">
      <div><h1>{{ i18n.t('Savings goals') }}</h1><p class="sub">{{ i18n.t('Progress and the monthly amount needed to hit each target') }}</p></div>
      <button class="btn primary" type="button" (click)="startNew()">{{ i18n.t('+ New goal') }}</button>
    </div>

    @if (form(); as f) {
      <form class="card" style="margin-bottom:16px" (ngSubmit)="save()">
        <h2>{{ editingId() === null ? i18n.t('New goal') : i18n.t('Edit goal') }}</h2>
        <div class="form-grid">
          <label>{{ i18n.t('Name') }}<input class="input" name="name" [(ngModel)]="f.name" required maxlength="100" /></label>
          <label>{{ i18n.t('Target amount') }}<input class="input" name="target" type="number" min="1" step="0.01" [(ngModel)]="f.target" required /></label>
          <label>{{ i18n.t('Target date') }} ({{ i18n.t('optional') }})<input class="input" name="date" type="date" [(ngModel)]="f.targetDate" /></label>
          <label>{{ i18n.t('Track with account') }}
            <select class="input" name="account" [ngModel]="f.accountId" (ngModelChange)="pickAccount(f, $event)">
              <option [ngValue]="null">{{ i18n.t('None (add money manually)') }}</option>
              @for (a of finance.accounts(); track a.id) { <option [ngValue]="a.id">{{ a.name }} ({{ a.currency }})</option>}
            </select>
          </label>
          <label>{{ i18n.t('Currency') }}
            <select class="input" name="currency" [(ngModel)]="f.currency" [disabled]="f.accountId !== null">
              @for (c of currencies(); track c) { <option [value]="c">{{ c }}</option> }
            </select>
          </label>
          @if (f.accountId === null) {
            <label>{{ i18n.t('Saved so far') }}<input class="input" name="saved" type="number" min="0" step="0.01" [(ngModel)]="f.saved" /></label>
          }
        </div>
        @if (error()) { <div class="notice bad">{{ i18n.t(error()) }}</div> }
        <div class="row" style="margin-top:12px">
          <button class="btn primary" type="submit" [disabled]="busy()">{{ i18n.t('Save') }}</button>
          <button class="btn" type="button" (click)="form.set(null)">{{ i18n.t('Cancel') }}</button>
        </div>
      </form>
    }

    @if (loaded() && !goals().length && !form()) {
      <div class="card empty">{{ i18n.t('No goals yet. Create one to start tracking your savings.') }}</div>
    }

    <div class="grid cols-3">
      @for (g of goals(); track g.id) {
        <div class="card">
          <div class="row between">
            <strong>{{ g.name }}</strong>
            <span class="badge" [class.good]="g.status === 'Completed' || g.status === 'OnTrack'" [class.bad]="g.status === 'Behind'">{{ i18n.t(statusLabel(g.status)) }}</span>
          </div>
          <div class="stat-value">{{ i18n.money(g.saved, g.currency) }}</div>
          <div class="muted" style="margin-bottom:10px">
            {{ i18n.t('of {target}', { target: i18n.money(g.target, g.currency) }) }}@if (g.targetDate) { · {{ i18n.t('by {date}', { date: i18n.date(g.targetDate) }) }} }
          </div>
          <div class="progress"><span [style.width.%]="percent(g)"></span></div>
          @if (g.status !== 'Completed') {
            <div class="muted" style="margin-top:10px">
              {{ i18n.t('{amount} to go', { amount: i18n.money(g.remaining, g.currency) }) }}
              @if (g.monthlyNeeded !== null) { · {{ i18n.t('Needs {amount} / month', { amount: i18n.money(g.monthlyNeeded, g.currency) }) }} }
            </div>
          }
          @if (g.accountId === null) {
            <div class="row" style="margin-top:12px">
              <input class="input amount" type="number" min="0" step="0.01" [placeholder]="i18n.t('Amount')" [attr.aria-label]="i18n.t('Amount')" #amount />
              <button class="btn" type="button" (click)="contribute(g, amount, 1)">{{ i18n.t('Add') }}</button>
              <button class="btn" type="button" (click)="contribute(g, amount, -1)">{{ i18n.t('Withdraw') }}</button>
            </div>
          } @else {
            <div class="muted note">{{ i18n.t('Follows the balance of {account}', { account: accountName(g.accountId) }) }}</div>
          }
          <div class="row" style="margin-top:8px">
            <button class="btn link" type="button" (click)="edit(g)">{{ i18n.t('Edit') }}</button>
            <button class="btn link" type="button" (click)="remove(g)">{{ i18n.t('Delete') }}</button>
          </div>
        </div>
      }
    </div>
  `,
  styles: `
    .amount { width: 110px; padding: 6px 8px; }
    .note { font-size: 0.8rem; margin-top: 12px; }
  `,
})
export class Goals implements OnInit {
  protected readonly finance = inject(FinanceService);
  protected readonly planning = inject(PlanningService);
  protected readonly i18n = inject(I18n);

  protected readonly goals = signal<Goal[]>([]);
  protected readonly loaded = signal(false);
  protected readonly editingId = signal<number | null>(null);
  protected readonly form = signal<GoalForm | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  /** Currencies the user has accounts in, plus any already used by a goal. */
  protected readonly currencies = computed(() => {
    const set = new Set<string>(this.finance.accounts().map((a) => (a.currency === 'LEI' ? 'RON' : a.currency)));
    this.goals().forEach((g) => set.add(g.currency));
    return set.size ? [...set].sort() : ['EUR', 'RON'];
  });

  async ngOnInit() {
    await Promise.all([this.finance.refreshAccounts(), this.reload()]).catch(() => undefined);
    this.loaded.set(true);
  }

  protected statusLabel(s: GoalStatus) { return STATUS_LABELS[s]; }
  protected percent(g: Goal) { return Math.min(100, g.target > 0 ? (g.saved / g.target) * 100 : 0); }
  protected accountName(id: number) { return this.finance.accountById().get(id)?.name ?? ''; }

  protected startNew() {
    this.editingId.set(null);
    this.error.set('');
    this.form.set({ name: '', currency: this.currencies()[0], target: null, targetDate: '', accountId: null, saved: 0 });
  }

  protected edit(g: Goal) {
    this.editingId.set(g.id);
    this.error.set('');
    this.form.set({ name: g.name, currency: g.currency, target: g.target, targetDate: g.targetDate ?? '', accountId: g.accountId, saved: g.accountId === null ? g.saved : 0 });
  }

  protected pickAccount(f: GoalForm, accountId: number | null) {
    f.accountId = accountId;
    const account = accountId === null ? null : this.finance.accountById().get(accountId);
    if (account) f.currency = account.currency === 'LEI' ? 'RON' : account.currency;
  }

  protected async save() {
    const f = this.form();
    if (!f || f.target === null) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.planning.saveGoal(this.editingId(), {
        name: f.name,
        currency: f.currency,
        target: Number(f.target),
        targetDate: f.targetDate || null,
        accountId: f.accountId,
        saved: f.accountId === null ? Number(f.saved) || 0 : null,
      });
      this.form.set(null);
      await this.reload();
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.busy.set(false);
    }
  }

  protected async contribute(g: Goal, input: HTMLInputElement, sign: 1 | -1) {
    const amount = Number(input.value);
    if (!amount || amount < 0) return;
    try {
      await this.planning.contribute(g.id, sign * amount);
      input.value = '';
      await this.reload();
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }

  protected async remove(g: Goal) {
    if (!confirm(this.i18n.t('Delete goal "{name}"?', { name: g.name }))) return;
    await this.planning.deleteGoal(g.id);
    await this.reload();
  }

  private async reload() {
    this.goals.set(await this.planning.goals());
  }
}
