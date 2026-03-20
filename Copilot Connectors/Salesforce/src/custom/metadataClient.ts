// ── Metadata API Client (#10) ──
// Fetches org metadata: object descriptions, field info, custom objects.

import { sfFetch } from "./restClient";
import { getConfig } from "../config/connectorConfig";
import { SObjectDescribe, SObjectField } from "../models/salesforceTypes";

interface GlobalDescribeResponse {
  encoding: string;
  maxBatchSize: number;
  sobjects: Array<{
    name: string;
    label: string;
    labelPlural: string;
    custom: boolean;
    queryable: boolean;
    searchable: boolean;
    createable: boolean;
    updateable: boolean;
    keyPrefix?: string;
  }>;
}

interface SObjectDescribeResponse {
  name: string;
  label: string;
  labelPlural: string;
  custom: boolean;
  queryable: boolean;
  searchable: boolean;
  createable: boolean;
  updateable: boolean;
  keyPrefix?: string;
  fields: Array<{
    name: string;
    label: string;
    type: string;
    length?: number;
    custom: boolean;
    nillable: boolean;
    filterable: boolean;
    sortable: boolean;
    referenceTo?: string[];
    relationshipName?: string;
  }>;
}

// ── Global Describe ──

export async function describeGlobal(): Promise<SObjectDescribe[]> {
  const apiVersion = getConfig().salesforce.apiVersion;
  const response = await sfFetch<GlobalDescribeResponse>(
    `/services/data/${apiVersion}/sobjects`
  );

  return response.sobjects.map((obj) => ({
    name: obj.name,
    label: obj.label,
    labelPlural: obj.labelPlural,
    custom: obj.custom,
    queryable: obj.queryable,
    searchable: obj.searchable,
    createable: obj.createable,
    updateable: obj.updateable,
    keyPrefix: obj.keyPrefix,
  }));
}

// ── Object Describe ──

export async function describeObject(sobjectName: string): Promise<SObjectDescribe> {
  const apiVersion = getConfig().salesforce.apiVersion;
  const response = await sfFetch<SObjectDescribeResponse>(
    `/services/data/${apiVersion}/sobjects/${sobjectName}/describe`
  );

  return {
    name: response.name,
    label: response.label,
    labelPlural: response.labelPlural,
    custom: response.custom,
    queryable: response.queryable,
    searchable: response.searchable,
    createable: response.createable,
    updateable: response.updateable,
    keyPrefix: response.keyPrefix,
    fields: response.fields.map(mapField),
  };
}

// ── List Queryable Objects ──

export async function listQueryableObjects(): Promise<string[]> {
  const objects = await describeGlobal();
  return objects
    .filter((obj) => obj.queryable)
    .map((obj) => obj.name);
}

// ── List Custom Objects ──

export async function listCustomObjects(): Promise<SObjectDescribe[]> {
  const objects = await describeGlobal();
  return objects.filter((obj) => obj.custom);
}

// ── Get Fields for Object ──

export async function getObjectFields(sobjectName: string): Promise<SObjectField[]> {
  const describe = await describeObject(sobjectName);
  return describe.fields || [];
}

// ── Get Relationship Map ──

export interface ObjectRelationship {
  fieldName: string;
  referenceTo: string[];
  relationshipName?: string;
}

export async function getObjectRelationships(
  sobjectName: string
): Promise<ObjectRelationship[]> {
  const fields = await getObjectFields(sobjectName);
  return fields
    .filter((f) => f.referenceTo && f.referenceTo.length > 0)
    .map((f) => ({
      fieldName: f.name,
      referenceTo: f.referenceTo!,
      relationshipName: f.relationshipName,
    }));
}

function mapField(raw: SObjectDescribeResponse["fields"][number]): SObjectField {
  return {
    name: raw.name,
    label: raw.label,
    type: raw.type,
    length: raw.length,
    custom: raw.custom,
    nillable: raw.nillable,
    filterable: raw.filterable,
    sortable: raw.sortable,
    referenceTo: raw.referenceTo,
    relationshipName: raw.relationshipName,
  };
}
