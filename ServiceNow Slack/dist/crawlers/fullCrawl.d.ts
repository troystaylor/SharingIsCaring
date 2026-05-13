export interface CrawlResult {
    totalFetched: number;
    succeeded: number;
    failed: number;
    durationMs: number;
}
export declare function runFullCrawl(): Promise<CrawlResult>;
//# sourceMappingURL=fullCrawl.d.ts.map