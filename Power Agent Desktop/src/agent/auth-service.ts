import { PublicClientApplication, DeviceCodeRequest, AuthenticationResult, AccountInfo } from '@azure/msal-node';
import keytar from 'keytar';

const SERVICE_NAME = 'power-agent-desktop';
const ACCOUNT_NAME = 'azure-token';
const REFRESH_TOKEN_NAME = 'refresh-token';

interface AuthConfig {
  clientId: string;
  tenantId: string;
  scopes?: string[];
}

interface DeviceCodeCallback {
  (userCode: string, verificationUri: string, message: string): void;
}

export class AuthService {
  private msalClient: PublicClientApplication;
  private config: AuthConfig;
  private scopes: string[];
  private cachedAccount: AccountInfo | null = null;

  constructor(config: AuthConfig) {
    this.config = config;
    this.scopes = config.scopes || [
      'https://api.powerplatform.com/.default',
      'User.Read'
    ];

    const msalConfig = {
      auth: {
        clientId: config.clientId,
        authority: `https://login.microsoftonline.com/${config.tenantId}`,
      },
      cache: {
        cachePlugin: undefined, // We'll use keytar for secure storage
      },
    };

    this.msalClient = new PublicClientApplication(msalConfig);
  }

  /**
   * Get a valid access token, refreshing if needed
   */
  async getAccessToken(): Promise<string | null> {
    // Try silent acquisition first
    const cachedToken = await this.tryGetCachedToken();
    if (cachedToken) {
      return cachedToken;
    }

    // Need interactive auth
    return null;
  }

  /**
   * Try to get token from cache or refresh token
   */
  private async tryGetCachedToken(): Promise<string | null> {
    try {
      // Check for cached account
      const accounts = await this.msalClient.getTokenCache().getAllAccounts();
      if (accounts.length > 0) {
        this.cachedAccount = accounts[0];
        
        const silentRequest = {
          account: this.cachedAccount,
          scopes: this.scopes,
        };

        const result = await this.msalClient.acquireTokenSilent(silentRequest);
        return result.accessToken;
      }

      // Try refresh token from keytar
      const refreshToken = await keytar.getPassword(SERVICE_NAME, REFRESH_TOKEN_NAME);
      if (refreshToken) {
        // MSAL doesn't expose direct refresh token usage, but the cache handles it
        return null;
      }
    } catch (error) {
      console.log('Silent token acquisition failed:', error);
    }
    return null;
  }

  /**
   * Authenticate using device code flow
   */
  async authenticateWithDeviceCode(callback: DeviceCodeCallback): Promise<AuthenticationResult> {
    const deviceCodeRequest: DeviceCodeRequest = {
      scopes: this.scopes,
      deviceCodeCallback: (response) => {
        callback(
          response.userCode,
          response.verificationUri,
          response.message
        );
      },
    };

    const result = await this.msalClient.acquireTokenByDeviceCode(deviceCodeRequest);
    
    if (!result) {
      throw new Error('Authentication failed - no result received');
    }
    
    // Cache the account
    this.cachedAccount = result.account;
    
    // Store tokens securely
    await this.storeTokens(result);

    return result;
  }

  /**
   * Store tokens securely in system credential manager
   */
  private async storeTokens(result: AuthenticationResult): Promise<void> {
    try {
      // Store access token (short-lived, but useful for quick access)
      await keytar.setPassword(SERVICE_NAME, ACCOUNT_NAME, result.accessToken);
      
      // Store account info for cache restoration
      if (result.account) {
        await keytar.setPassword(
          SERVICE_NAME, 
          'account-info', 
          JSON.stringify({
            homeAccountId: result.account.homeAccountId,
            environment: result.account.environment,
            tenantId: result.account.tenantId,
            username: result.account.username,
          })
        );
      }
    } catch (error) {
      console.error('Failed to store tokens:', error);
    }
  }

  /**
   * Get token for Azure Speech service
   */
  async getSpeechToken(): Promise<string | null> {
    try {
      const speechScopes = ['https://cognitiveservices.azure.com/.default'];
      
      if (this.cachedAccount) {
        const result = await this.msalClient.acquireTokenSilent({
          account: this.cachedAccount,
          scopes: speechScopes,
        });
        return result.accessToken;
      }
    } catch (error) {
      console.error('Failed to get speech token:', error);
    }
    return null;
  }

  /**
   * Check if user is authenticated
   */
  async isAuthenticated(): Promise<boolean> {
    const token = await this.getAccessToken();
    return token !== null;
  }

  /**
   * Get current user info
   */
  async getUserInfo(): Promise<{ name: string; email: string } | null> {
    if (this.cachedAccount) {
      return {
        name: this.cachedAccount.name || this.cachedAccount.username,
        email: this.cachedAccount.username,
      };
    }
    return null;
  }

  /**
   * Sign out and clear all cached credentials
   */
  async signOut(): Promise<void> {
    try {
      // Clear MSAL cache
      const accounts = await this.msalClient.getTokenCache().getAllAccounts();
      for (const account of accounts) {
        await this.msalClient.getTokenCache().removeAccount(account);
      }

      // Clear keytar credentials
      await keytar.deletePassword(SERVICE_NAME, ACCOUNT_NAME);
      await keytar.deletePassword(SERVICE_NAME, REFRESH_TOKEN_NAME);
      await keytar.deletePassword(SERVICE_NAME, 'account-info');

      this.cachedAccount = null;
    } catch (error) {
      console.error('Error during sign out:', error);
      throw error;
    }
  }

  /**
   * Get the MSAL client for advanced usage
   */
  getMsalClient(): PublicClientApplication {
    return this.msalClient;
  }
}

// Factory function for easy creation
export function createAuthService(clientId: string, tenantId: string): AuthService {
  return new AuthService({ clientId, tenantId });
}
