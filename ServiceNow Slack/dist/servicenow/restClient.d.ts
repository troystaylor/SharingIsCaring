export declare function query<T>(tableName: string, params?: Record<string, string | number | boolean>): Promise<T[]>;
export declare function queryAll<T>(tableName: string, params?: Record<string, string | number | boolean>, pageSize?: number): Promise<T[]>;
export declare function getRecord<T>(tableName: string, sysId: string, params?: Record<string, string | number | boolean>): Promise<T | null>;
export declare function createRecord<T>(tableName: string, body: Record<string, unknown>): Promise<T>;
//# sourceMappingURL=restClient.d.ts.map