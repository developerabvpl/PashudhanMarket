import { Injectable, inject } from '@angular/core';
import {
  Api,
  PagedListOfUserSummaryDto,
  RoleDto,
  UserDto,
  apiV1AdminRolesGet,
  apiV1AdminUsersGet,
  apiV1AdminUsersPost,
  apiV1AdminUsersUserIdDelete,
  apiV1AdminUsersUserIdPut,
  apiV1AdminUsersUserIdRolesPut,
} from '@upbazaar/data-access';

/** One page of the staff list, as the screen asks for it. */
export interface StaffQuery {
  readonly page: number;
  readonly pageSize: number;
  readonly search: string;
  readonly userType: string;
}

/**
 * Staff administration calls, named for what they do.
 *
 * The generated operations are named after their routes; wrapping them keeps the components
 * readable and puts the "all types means no filter" translation in one place rather than at
 * every call site.
 */
@Injectable({ providedIn: 'root' })
export class StaffService {
  private readonly api = inject(Api);

  /** Server-side paging: the list never holds more than one page. */
  async list(query: StaffQuery): Promise<PagedListOfUserSummaryDto> {
    return await this.api.invoke(apiV1AdminUsersGet, {
      Page: query.page,
      PageSize: query.pageSize,
      Search: query.search.trim() === '' ? undefined : query.search.trim(),
      UserType: query.userType === '' ? undefined : query.userType,
    });
  }

  async roles(): Promise<RoleDto[]> {
    return await this.api.invoke(apiV1AdminRolesGet, {});
  }

  async create(
    email: string,
    password: string,
    displayName: string,
    preferredLanguage: string,
    roles: string[]
  ): Promise<UserDto> {
    return await this.api.invoke(apiV1AdminUsersPost, {
      body: { email, password, displayName, preferredLanguage, roles },
    });
  }

  async update(
    userId: string,
    displayName: string,
    preferredLanguage: string,
    status: string
  ): Promise<UserDto> {
    return await this.api.invoke(apiV1AdminUsersUserIdPut, {
      userId,
      body: { displayName, preferredLanguage, status },
    });
  }

  /** Replaces the user's roles with exactly this set. */
  async assignRoles(userId: string, roles: string[]): Promise<UserDto> {
    return await this.api.invoke(apiV1AdminUsersUserIdRolesPut, { userId, body: { roles } });
  }

  /** Deactivates rather than deletes; the API keeps the row for audit and order history. */
  async deactivate(userId: string): Promise<void> {
    await this.api.invoke(apiV1AdminUsersUserIdDelete, { userId });
  }
}
