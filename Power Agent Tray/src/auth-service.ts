/**
 * Auth Service - MSAL-based authentication for Copilot Studio
 * Handles PKCE OAuth flow with secure token persistence via @azure/msal-node-extensions
 * (DPAPI on Windows, Keychain on macOS, libsecret on Linux)
 */

import {
  PublicClientApplication,
  Configuration,
  AuthenticationResult,
  LogLevel,
  ICachePlugin,
} from "@azure/msal-node";
import {
  DataProtectionScope,
  Environment,
  PersistenceCreator,
  PersistenceCachePlugin,
  IPersistence,
  IPersistenceConfiguration,
} from "@azure/msal-node-extensions";
import * as http from "http";
import * as path from "path";
import { URL } from "url";

const SERVICE_NAME = "PowerAgentTray";
const ACCOUNT_NAME = "copilot-tokens";

// Default scopes for Copilot Studio
const DEFAULT_SCOPES = [
  "https://api.powerplatform.com/.default",
  "offline_access",
];

export interface AuthConfig {
  clientId: string;
  tenantId: string;
  redirectUri?: string;
  scopes?: string[];
}

export interface TokenInfo {
  accessToken: string;
  expiresOn: Date;
  account?: string;
}

export class AuthService {
  private pca!: PublicClientApplication;
  private config: AuthConfig;
  private cachedToken: TokenInfo | null = null;
  private authServer: http.Server | null = null;
  private persistence: IPersistence | null = null;
  private pkceVerifier: string | null = null;
  private initialized = false;

  constructor(config: AuthConfig) {
    this.config = {
      ...config,
      redirectUri: config.redirectUri || "http://localhost:3847/auth/callback",
      scopes: config.scopes || DEFAULT_SCOPES,
    };
  }

  /**
   * Initialize MSAL with persistent cache plugin.
   * Must be called before any auth operations.
   */
  async initialize(): Promise<void> {
    if (this.initialized) return;

    const userRoot = Environment.getUserRootDirectory() || require("os").homedir();
    const cachePath = path.join(
      userRoot,
      `.${SERVICE_NAME}`,
      "msal-cache.json"
    );

    const persistenceConfig: IPersistenceConfiguration = {
      cachePath,
      dataProtectionScope: DataProtectionScope.CurrentUser,
      serviceName: SERVICE_NAME,
      accountName: ACCOUNT_NAME,
      usePlaintextFileOnLinux: false,
    };

    this.persistence = await PersistenceCreator.createPersistence(persistenceConfig);
    const cachePlugin: ICachePlugin = new PersistenceCachePlugin(this.persistence);

    const msalConfig: Configuration = {
      auth: {
        clientId: this.config.clientId,
        authority: `https://login.microsoftonline.com/${this.config.tenantId}`,
      },
      cache: {
        cachePlugin,
      },
      system: {
        loggerOptions: {
          loggerCallback: (level, message) => {
            if (level === LogLevel.Error) {
              console.error("[MSAL]", message);
            }
          },
          piiLoggingEnabled: false,
          logLevel: LogLevel.Error,
        },
      },
    };

    this.pca = new PublicClientApplication(msalConfig);
    this.initialized = true;
    console.log("[Auth] MSAL initialized with persistent cache at", cachePath);
  }

  /**
   * Ensure initialize() has been called
   */
  private async ensureInitialized(): Promise<void> {
    if (!this.initialized) {
      await this.initialize();
    }
  }

  /**
   * Get a valid access token, refreshing if necessary
   */
  async getAccessToken(): Promise<string> {
    await this.ensureInitialized();

    // Check in-memory cached token
    if (this.cachedToken && new Date() < this.cachedToken.expiresOn) {
      return this.cachedToken.accessToken;
    }

    // Try silent token acquisition from persisted cache
    try {
      const accounts = await this.pca.getTokenCache().getAllAccounts();
      if (accounts.length > 0) {
        const result = await this.pca.acquireTokenSilent({
          account: accounts[0],
          scopes: this.config.scopes!,
        });

        if (result) {
          this.updateMemoryCache(result);
          return result.accessToken;
        }
      }
    } catch {
      console.log("[Auth] Silent token acquisition failed, need login");
    }

    throw new Error("No valid token available. Please login first.");
  }

  /**
   * Check if user is authenticated
   */
  async isAuthenticated(): Promise<boolean> {
    try {
      await this.ensureInitialized();
      const accounts = await this.pca.getTokenCache().getAllAccounts();
      if (accounts.length === 0) {
        return false;
      }

      await this.getAccessToken();
      return true;
    } catch {
      return false;
    }
  }

  /**
   * Start interactive login flow
   * Returns the authorization URL to open in browser
   */
  async startLogin(): Promise<{ url: string; port: number }> {
    await this.ensureInitialized();
    const port = 3847;

    // Generate PKCE codes
    const crypto = await import("crypto");
    const verifier = crypto.randomBytes(32).toString("base64url");
    const challenge = crypto
      .createHash("sha256")
      .update(verifier)
      .digest("base64url");

    const authCodeUrl = await this.pca.getAuthCodeUrl({
      scopes: this.config.scopes!,
      redirectUri: `http://localhost:${port}/auth/callback`,
      codeChallenge: challenge,
      codeChallengeMethod: "S256",
    });

    // Store verifier in memory (short-lived, only needed until callback)
    this.pkceVerifier = verifier;

    return { url: authCodeUrl, port };
  }

  /**
   * Start local server to handle OAuth callback
   */
  startCallbackServer(port: number): Promise<AuthenticationResult> {
    return new Promise((resolve, reject) => {
      this.authServer = http.createServer(async (req, res) => {
        const url = new URL(req.url!, `http://localhost:${port}`);

        if (url.pathname === "/auth/callback") {
          const code = url.searchParams.get("code");
          const error = url.searchParams.get("error");

          if (error) {
            res.writeHead(400, { "Content-Type": "text/html" });
            res.end(
              `<html><body><h1>Authentication Failed</h1><p>${error}</p><script>setTimeout(() => window.close(), 2000)</script></body></html>`
            );
            this.stopCallbackServer();
            reject(new Error(error));
            return;
          }

          if (code) {
            try {
              if (!this.pkceVerifier) {
                throw new Error("PKCE verifier not found");
              }

              const result = await this.pca.acquireTokenByCode({
                code,
                scopes: this.config.scopes!,
                redirectUri: `http://localhost:${port}/auth/callback`,
                codeVerifier: this.pkceVerifier,
              });

              this.updateMemoryCache(result);
              this.pkceVerifier = null;

              res.writeHead(200, { "Content-Type": "text/html" });
              res.end(
                `<html><body style="font-family: system-ui; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; background: #f5f5f5;"><div style="text-align: center;"><h1 style="color: #0078d4;">&#10003; Authentication Successful</h1><p>You can close this window.</p></div><script>setTimeout(() => window.close(), 1500)</script></body></html>`
              );

              this.stopCallbackServer();
              resolve(result);
            } catch (err) {
              res.writeHead(500, { "Content-Type": "text/html" });
              res.end(
                `<html><body><h1>Error</h1><p>${err}</p></body></html>`
              );
              this.stopCallbackServer();
              reject(err);
            }
          }
        }
      });

      this.authServer.listen(port, () => {
        console.log(`[Auth] Callback server listening on port ${port}`);
      });

      // Timeout after 5 minutes
      setTimeout(() => {
        this.stopCallbackServer();
        reject(new Error("Authentication timed out"));
      }, 5 * 60 * 1000);
    });
  }

  /**
   * Stop the callback server
   */
  private stopCallbackServer(): void {
    if (this.authServer) {
      this.authServer.close();
      this.authServer = null;
    }
  }

  /**
   * Logout and clear tokens
   */
  async logout(): Promise<void> {
    await this.ensureInitialized();
    try {
      const accounts = await this.pca.getTokenCache().getAllAccounts();
      for (const account of accounts) {
        await this.pca.getTokenCache().removeAccount(account);
      }
      this.cachedToken = null;
      this.pkceVerifier = null;
      console.log("[Auth] Logged out and cleared persistent cache");
    } catch (err) {
      console.error("[Auth] Error during logout:", err);
    }
  }

  /**
   * Update in-memory token cache (persistence is handled by the plugin automatically)
   */
  private updateMemoryCache(result: AuthenticationResult): void {
    this.cachedToken = {
      accessToken: result.accessToken,
      expiresOn: result.expiresOn || new Date(Date.now() + 3600 * 1000),
      account: result.account?.username,
    };
  }

  /**
   * Get current user info
   */
  async getCurrentUser(): Promise<string | null> {
    try {
      await this.ensureInitialized();
      const accounts = await this.pca.getTokenCache().getAllAccounts();
      return accounts[0]?.username || null;
    } catch {
      return null;
    }
  }
}
