// ── Pub/Sub API Client (#4) ──
// gRPC-based Change Data Capture (CDC) subscription for real-time incremental updates.
// Salesforce Pub/Sub API: https://developer.salesforce.com/docs/platform/pub-sub-api/overview

import * as grpc from "@grpc/grpc-js";
import * as protoLoader from "@grpc/proto-loader";
import { getAccessToken, getInstanceUrl } from "../auth/salesforceAuth";
import { ChangeEvent } from "../models/salesforceTypes";
import * as path from "path";

// Topic format for Change Data Capture
// /data/ChangeEvents = all CDC events
// /data/<ObjectName>ChangeEvent = specific object CDC

export type CdcEventHandler = (event: ChangeEvent) => Promise<void>;

interface PubSubConfig {
  pubsubHost?: string;
  pubsubPort?: number;
}

const DEFAULT_PUBSUB_HOST = "api.pubsub.salesforce.com";
const DEFAULT_PUBSUB_PORT = 7443;

let pubsubClient: unknown = null;

async function getPubSubClient(config: PubSubConfig = {}): Promise<unknown> {
  if (pubsubClient) return pubsubClient;

  const host = config.pubsubHost || DEFAULT_PUBSUB_HOST;
  const port = config.pubsubPort || DEFAULT_PUBSUB_PORT;

  // Load the Pub/Sub API proto definition
  const protoPath = path.resolve(__dirname, "../../proto/pubsub_api.proto");

  const packageDefinition = await protoLoader.load(protoPath, {
    keepCase: true,
    longs: String,
    enums: String,
    defaults: true,
    oneofs: true,
  });

  const protoDescriptor = grpc.loadPackageDefinition(packageDefinition);
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const PubSubService = (protoDescriptor as any).eventbus.v1.PubSub;

  const channelCreds = grpc.credentials.createSsl();
  pubsubClient = new PubSubService(
    `${host}:${port}`,
    channelCreds
  );

  return pubsubClient;
}

function buildMetadata(): grpc.Metadata {
  const metadata = new grpc.Metadata();
  // Token and instance URL will be set when subscribing
  return metadata;
}

export async function subscribe(
  topic: string,
  handler: CdcEventHandler,
  numRequested = 100,
  replayPreset: "LATEST" | "EARLIEST" | "CUSTOM" = "LATEST",
  replayId?: Uint8Array
): Promise<{ cancel: () => void }> {
  const client = await getPubSubClient();
  const token = await getAccessToken();
  const instanceUrl = await getInstanceUrl();

  const metadata = new grpc.Metadata();
  metadata.set("accesstoken", token);
  metadata.set("instanceurl", instanceUrl);
  metadata.set("tenantid", new URL(instanceUrl).hostname.split(".")[0]);

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const stream = (client as any).Subscribe(metadata);

  // Send initial fetch request
  const fetchRequest: Record<string, unknown> = {
    topic_name: topic,
    num_requested: numRequested,
    replay_preset: replayPreset === "LATEST" ? 2 : replayPreset === "EARLIEST" ? 1 : 3,
  };
  if (replayId) {
    fetchRequest.replay_id = replayId;
  }

  stream.write(fetchRequest);

  stream.on("data", async (response: { events?: Array<{ event: { payload: Uint8Array; id: string } }> }) => {
    if (!response.events?.length) {
      // Keep-alive: request more events
      stream.write({ topic_name: topic, num_requested: numRequested });
      return;
    }

    for (const evt of response.events) {
      try {
        // Decode the Avro payload (simplified — production should use avsc)
        const decoded = JSON.parse(
          Buffer.from(evt.event.payload).toString("utf-8")
        ) as ChangeEvent;
        await handler(decoded);
      } catch (err) {
        console.error(`[PubSub] Error processing event: ${err}`);
      }
    }

    // Request more events
    stream.write({ topic_name: topic, num_requested: numRequested });
  });

  stream.on("error", (err: Error) => {
    console.error(`[PubSub] Stream error: ${err.message}`);
  });

  stream.on("end", () => {
    console.log("[PubSub] Stream ended");
  });

  console.log(`[PubSub] Subscribed to topic: ${topic}`);

  return {
    cancel: () => {
      stream.cancel();
      console.log(`[PubSub] Unsubscribed from topic: ${topic}`);
    },
  };
}

export function getCdcTopic(objectName: string): string {
  return `/data/${objectName}ChangeEvent`;
}

export function getAllCdcTopic(): string {
  return "/data/ChangeEvents";
}
