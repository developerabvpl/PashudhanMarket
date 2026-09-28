import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * The signed-in user's menu in a portal's toolbar: change password, sign out.
 *
 * Its own component so a portal can load it with <c>@defer</c>: Material's menu brings the CDK
 * overlay, which nothing else on a portal's first screen needs.
 */
@Component({
  selector: 'upb-account-menu',
  imports: [RouterLink, MatButtonModule, MatMenuModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button mat-button type="button" [matMenuTriggerFor]="menu">{{ displayName() }}</button>
    <mat-menu #menu="matMenu">
      <a mat-menu-item routerLink="/change-password">{{ 'changePassword.title' | transloco }}</a>
      <button mat-menu-item type="button" (click)="signOut.emit()">{{ 'nav.signOut' | transloco }}</button>
    </mat-menu>
  `,
})
export class AccountMenu {
  readonly displayName = input.required<string | null>();
  readonly signOut = output();
}
