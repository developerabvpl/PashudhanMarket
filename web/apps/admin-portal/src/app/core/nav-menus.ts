import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { TranslocoPipe } from '@jsverse/transloco';
import { HasPermissionDirective } from '@upbazaar/auth';
import { CatalogPermissions } from './permissions';

/*
 * The toolbar's drop-down menus, as components of their own so the shell can load them with
 * @defer: Material's menu brings the CDK overlay, which nothing else on the first screen needs.
 */

/** Settlements: payouts and commission rates. */
@Component({
  selector: 'upb-settlements-menu',
  imports: [RouterLink, MatButtonModule, MatMenuModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button mat-button type="button" [matMenuTriggerFor]="menu">{{ 'nav.settlements' | transloco }}</button>
    <mat-menu #menu="matMenu">
      <a mat-menu-item routerLink="/settlements/payouts">{{ 'nav.payouts' | transloco }}</a>
      <a mat-menu-item routerLink="/settlements/rates">{{ 'nav.settlementRates' | transloco }}</a>
    </mat-menu>
  `,
})
export class SettlementsMenu {}

/** Catalogue: products, and categories and the review queue for those allowed. */
@Component({
  selector: 'upb-catalogue-menu',
  imports: [RouterLink, MatButtonModule, MatMenuModule, TranslocoPipe, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button mat-button type="button" [matMenuTriggerFor]="menu">{{ 'nav.catalogue' | transloco }}</button>
    <mat-menu #menu="matMenu">
      <a mat-menu-item routerLink="/catalog/products">{{ 'nav.products' | transloco }}</a>
      <a *hasPermission="categoriesWrite" mat-menu-item routerLink="/catalog/categories">{{ 'nav.categories' | transloco }}</a>
      <a *hasPermission="productsWrite" mat-menu-item routerLink="/catalog/review">{{ 'nav.listingReview' | transloco }}</a>
    </mat-menu>
  `,
})
export class CatalogueMenu {
  protected readonly categoriesWrite = CatalogPermissions.CategoriesWrite;
  protected readonly productsWrite = CatalogPermissions.ProductsWrite;
}
