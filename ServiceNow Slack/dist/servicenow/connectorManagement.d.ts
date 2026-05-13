interface ConnectorRecord {
    sys_id: string;
    name: string;
    source_type: string;
    state: string;
    sys_updated_on: string;
    sys_created_on: string;
}
interface CrawlRecord {
    sys_id: string;
    connector: string;
    state: string;
    crawl_type: string;
    start_time: string;
    end_time: string;
    items_crawled: string;
    items_indexed: string;
    errors: string;
    sys_updated_on: string;
}
export declare function listSlackConnectors(): Promise<ConnectorRecord[]>;
export declare function listCrawls(connectorSysId?: string): Promise<CrawlRecord[]>;
export declare function triggerCrawl(connectorSysId: string, crawlType?: string): Promise<CrawlRecord>;
export {};
//# sourceMappingURL=connectorManagement.d.ts.map