import { Injectable, inject } from '@angular/core';
import {
  Api,
  AuthResultDto,
  AuthTokensDto,
  TotpSetupDto,
  apiV1AuthChangePasswordPost,
  apiV1AuthForgotPasswordPost,
  apiV1AuthLoginPost,
  apiV1AuthLogoutPost,
  apiV1AuthRefreshPost,
  apiV1AuthRegisterPost,
  apiV1AuthRequestOtpPost,
  apiV1AuthResetPasswordPost,
  apiV1AuthVerify2FaPost,
  apiV1AuthVerifyOtpPost,
  apiV1UsersMe2FaSetupPost,
  apiV1UsersMe2FaVerifyPost,
} from '@upbazaar/data-access';
import { AuthTokenStore } from './auth-token-store';
import { CurrentUserStore } from './current-user-store';

/**
 * Every authentication call the apps make, in one place.
 *
 * A façade over the generated client rather than a replacement for it: the generated
 * operations are named after their routes (`apiV1AuthVerifyOtpPost`), which is fine for a
 * generator and unreadable in a component. This also puts "store the tokens, then load the
 * profile" in one spot, so no screen can sign someone in and forget half of it.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(Api);
  private readonly tokens = inject(AuthTokenStore);
  private readonly currentUser = inject(CurrentUserStore);

  /** Sends a one-time code to a mobile number. */
  async requestOtp(mobile: string): Promise<void> {
    await this.api.invoke(apiV1AuthRequestOtpPost, { body: { mobile } });
  }

  /** Verifies a code and signs the buyer in, creating the account if it is their first time. */
  async verifyOtp(mobile: string, code: string, displayName?: string): Promise<void> {
    const tokens = await this.api.invoke(apiV1AuthVerifyOtpPost, {
      body: { mobile, code, displayName: displayName ?? null },
    });

    await this.acceptAsync(tokens);
  }

  /**
   * Signs in with a password.
   *
   * Returns the raw result rather than a boolean, because a staff account with TOTP enabled
   * answers with a challenge instead of tokens and the caller has to route accordingly.
   */
  async login(email: string, password: string): Promise<AuthResultDto> {
    const result = await this.api.invoke(apiV1AuthLoginPost, { body: { email, password } });

    if (result.tokens) {
      await this.acceptAsync(result.tokens);
    }

    return result;
  }

  /** Completes a sign-in that stopped at the two-factor prompt. */
  async verifyTwoFactor(twoFactorToken: string, code: string): Promise<void> {
    const tokens = await this.api.invoke(apiV1AuthVerify2FaPost, {
      body: { twoFactorToken, code },
    });

    await this.acceptAsync(tokens);
  }

  /** Registers a buyer with an email and password, and signs them in. */
  async register(
    email: string,
    password: string,
    displayName: string,
    preferredLanguage: string
  ): Promise<void> {
    const tokens = await this.api.invoke(apiV1AuthRegisterPost, {
      body: { email, password, displayName, preferredLanguage },
    });

    await this.acceptAsync(tokens);
  }

  /**
   * Exchanges the refresh token for a new pair.
   *
   * Returns false rather than throwing when there is nothing to refresh or the exchange is
   * refused, because the caller is the interceptor and its next move is the same either way.
   */
  async refresh(): Promise<boolean> {
    const refreshToken = this.tokens.refreshToken();

    if (refreshToken === null || !this.tokens.canRefresh()) {
      return false;
    }

    try {
      const tokens = await this.api.invoke(apiV1AuthRefreshPost, { body: { refreshToken } });
      this.tokens.set(tokens);

      return true;
    } catch {
      // Includes reuse detection, where the API has already revoked the whole family.
      this.signOutLocally();

      return false;
    }
  }

  /** Ends the session on the server and locally. */
  async logout(): Promise<void> {
    const refreshToken = this.tokens.refreshToken();

    if (refreshToken !== null) {
      try {
        await this.api.invoke(apiV1AuthLogoutPost, { body: { refreshToken } });
      } catch {
        // A server that will not take the revocation must not strand the user signed in.
      }
    }

    this.signOutLocally();
  }

  /** Changes the password. Every other session ends, so the caller keeps its own tokens. */
  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    await this.api.invoke(apiV1AuthChangePasswordPost, {
      body: { currentPassword, newPassword },
    });
  }

  /** Starts a password reset. Always succeeds, whether or not the address is registered. */
  async forgotPassword(email: string): Promise<void> {
    await this.api.invoke(apiV1AuthForgotPasswordPost, { body: { email } });
  }

  /** Completes a password reset with the emailed token. */
  async resetPassword(email: string, token: string, newPassword: string): Promise<void> {
    await this.api.invoke(apiV1AuthResetPasswordPost, { body: { email, token, newPassword } });
  }

  /** Begins TOTP enrolment and returns the secret plus otpauth URI. */
  async setupTwoFactor(): Promise<TotpSetupDto> {
    return await this.api.invoke(apiV1UsersMe2FaSetupPost, {});
  }

  /** Confirms TOTP enrolment with a generated code. */
  async confirmTwoFactor(code: string): Promise<void> {
    await this.api.invoke(apiV1UsersMe2FaVerifyPost, { body: { code } });

    await this.currentUser.refresh();
  }

  /** Clears local state without calling the server. */
  signOutLocally(): void {
    this.tokens.clear();
    this.currentUser.clear();
  }

  private async acceptAsync(tokens: AuthTokensDto): Promise<void> {
    this.tokens.set(tokens);

    // Loaded now rather than lazily: the screen the user lands on almost always needs the
    // display name or a permission, and a guard awaiting this is one less flash of empty UI.
    await this.currentUser.refresh();
  }
}
