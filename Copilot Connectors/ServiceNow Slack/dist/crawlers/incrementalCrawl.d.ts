export interface IncrementalCrawlResult {
    totalFetched: number;
    succeeded: number;
    failed: number;
    durationMs: number;
    sinceDate: string;
}
export declare function runIncrementalCrawl(sinceDate: string): Promise<IncrementalCrawlResult>;
//# sourceMappingURL=incrementalCrawl.d.ts.map