import { SlackContentRecord } from "../models/types";
export declare function getAllSlackContent(): Promise<SlackContentRecord[]>;
export declare function getSlackContentSince(sinceDate: string): Promise<SlackContentRecord[]>;
export declare function getSlackContentCount(): Promise<number>;
//# sourceMappingURL=slackContentClient.d.ts.map