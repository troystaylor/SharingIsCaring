// ── GraphQL Client (#7) ──
// Salesforce GraphQL API for efficient multi-object queries with field selection.

import { sfFetch } from "./restClient";
import { getConfig } from "../config/connectorConfig";

export interface GraphqlResponse<T = Record<string, unknown>> {
  data: T;
  errors?: Array<{
    message: string;
    locations?: Array<{ line: number; column: number }>;
    path?: string[];
  }>;
}

export async function graphqlQuery<T = Record<string, unknown>>(
  query: string,
  variables?: Record<string, unknown>
): Promise<GraphqlResponse<T>> {
  const apiVersion = getConfig().salesforce.apiVersion;

  const body: Record<string, unknown> = { query };
  if (variables) {
    body.variables = variables;
  }

  const response = await sfFetch<GraphqlResponse<T>>(
    `/services/data/${apiVersion}/graphql`,
    {
      method: "POST",
      body,
    }
  );

  if (response.errors?.length) {
    console.error(
      "[GraphQL] Query errors:",
      response.errors.map((e) => e.message).join("; ")
    );
  }

  return response;
}

// ── Pre-built queries ──

export async function queryAccountsWithContacts(
  first = 50
): Promise<GraphqlResponse> {
  const query = `
    query AccountsWithContacts($first: Int) {
      uiapi {
        query {
          Account(first: $first) {
            edges {
              node {
                Id
                Name { value }
                Industry { value }
                Website { value }
                AnnualRevenue { value }
                Contacts {
                  edges {
                    node {
                      Id
                      Name { value }
                      Email { value }
                      Title { value }
                    }
                  }
                }
              }
            }
          }
        }
      }
    }
  `;
  return graphqlQuery(query, { first });
}

export async function queryOpportunitiesByStage(
  stageName: string,
  first = 50
): Promise<GraphqlResponse> {
  const query = `
    query OpportunitiesByStage($first: Int) {
      uiapi {
        query {
          Opportunity(
            first: $first,
            where: { StageName: { eq: "${stageName}" } }
          ) {
            edges {
              node {
                Id
                Name { value }
                StageName { value }
                Amount { value }
                CloseDate { value }
                Account {
                  Name { value }
                }
              }
            }
          }
        }
      }
    }
  `;
  return graphqlQuery(query, { first });
}

export async function queryCasesByStatus(
  status: string,
  first = 50
): Promise<GraphqlResponse> {
  const query = `
    query CasesByStatus($first: Int) {
      uiapi {
        query {
          Case(
            first: $first,
            where: { Status: { eq: "${status}" } }
          ) {
            edges {
              node {
                Id
                CaseNumber { value }
                Subject { value }
                Status { value }
                Priority { value }
                Account {
                  Name { value }
                }
                Contact {
                  Name { value }
                  Email { value }
                }
              }
            }
          }
        }
      }
    }
  `;
  return graphqlQuery(query, { first });
}
