import { ExternalItem, ExternalConnection } from "../models/types";
export declare function createConnection(): Promise<void>;
export declare function getConnection(): Promise<ExternalConnection | null>;
export declare function deleteConnection(): Promise<void>;
export declare function registerSchema(): Promise<void>;
export declare function getSchemaStatus(): Promise<string>;
export declare function waitForSchemaReady(pollIntervalMs?: number, maxWaitMs?: number): Promise<void>;
export declare function upsertItem(item: ExternalItem): Promise<void>;
export declare function deleteItem(itemId: string): Promise<void>;
export declare function upsertItemsBatch(items: ExternalItem[], batchSize?: number, delayMs?: number): Promise<{
    succeeded: number;
    failed: number;
}>;
export declare function ensureConnection(): Promise<void>;
//# sourceMappingURL=connectionManager.d.ts.map