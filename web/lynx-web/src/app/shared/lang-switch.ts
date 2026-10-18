import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { I18n } from '../core/i18n.service';

@Component({
  selector: 'lynx-lang-switch',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button type="button" [attr.aria-label]="i18n.t('Language')" [title]="i18n.t('Language')" (click)="i18n.toggle()">
      <span [class.on]="i18n.lang() === 'en'">EN</span><span [class.on]="i18n.lang() === 'ro'">RO</span>
    </button>
  `,
  styles: `
    button { display: inline-flex; border: 1px solid var(--border); background: var(--surface-2); border-radius: 999px; padding: 2px; cursor: pointer; font: inherit; font-size: 0.75rem; font-weight: 700; color: var(--muted); }
    span { padding: 3px 9px; border-radius: 999px; }
    span.on { background: var(--primary); color: #fff; }
  `,
})
export class LangSwitch {
  protected readonly i18n = inject(I18n);
}
