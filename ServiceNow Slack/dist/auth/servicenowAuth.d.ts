interface CachedToken {
    accessToken: string;
    expiresAt: number;
}
export declare function refreshAccessToken(): Promise<CachedToken>;
export declare function getAccessToken(): Promise<string>;
export {};
//# sourceMappingURL=servicenowAuth.d.ts.map