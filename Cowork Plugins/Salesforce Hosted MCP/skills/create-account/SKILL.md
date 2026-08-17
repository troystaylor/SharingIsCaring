---
name: create-account
description: |
  Creates a new account record in Salesforce. Use when the user asks "create an
  account in Salesforce", "add [company] to Salesforce", "set up a new
  Salesforce customer", "register this company in Salesforce", or "add a new
  logo to the CRM".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Sales
cowork.icon: BuildingAdd
---

# Create Account

## What This Skill Does

Creates an Account record in Salesforce after checking for duplicates and
confirming the field values with the user.

## When to Activate

- User asks to create, add, or register a company or account in Salesforce
- User provides company details and wants them stored in the CRM

## Workflow

1. **Check for an existing account first.** Never create a duplicate:

   ```
   FIND {Globex*} IN NAME FIELDS RETURNING Account(Id, Name, Type, BillingCity, Owner.Name, Website LIMIT 10)
   ```

   If a plausible match exists, show it and ask whether to use it, update it, or
   still create a new one.

2. **Collect the fields.** `Name` is the only field Salesforce requires. Ask for
   what is genuinely useful and stop there — do not interrogate the user:

   | Field | Notes |
   |---|---|
   | `Name` | Required |
   | `Type` | Picklist — e.g. Prospect, Customer - Direct, Partner |
   | `Industry` | Picklist |
   | `Website` | |
   | `Phone` | |
   | `BillingStreet` / `BillingCity` / `BillingState` / `BillingPostalCode` / `BillingCountry` | |
   | `AnnualRevenue` | Number, unquoted |
   | `NumberOfEmployees` | Integer, unquoted |
   | `Description` | |
   | `OwnerId` | Defaults to the signed-in user if omitted |

3. **Verify picklist values.** Call `getObjectSchema` with
   `object-name: Account` and confirm `Type` and `Industry` values match the
   org exactly. Do not send a value you have not verified.

4. **Confirm before writing.** Show the complete record you are about to create
   as a field/value list and ask for an explicit yes. Never create on an
   inferred instruction.

5. **Create it** with `createSobjectRecord`:

   ```json
   {
     "sobject-name": "Account",
     "body": {
       "Name": "Globex Corporation",
       "Type": "Prospect",
       "Industry": "Manufacturing",
       "Website": "https://globex.com",
       "BillingCity": "Chicago",
       "BillingState": "IL",
       "BillingCountry": "USA",
       "NumberOfEmployees": 1200
     }
   }
   ```

6. **Confirm the result.** Report the new Account Id and offer the natural next
   steps: add a contact (`add-contact`) or create an opportunity
   (`create-opportunity`).

## Output Format

**Created: Globex Corporation** — Id `001XXXXXXXXXXXXXXX`

| Field | Value |
|---|---|
| Type | Prospect |
| Industry | Manufacturing |
| Location | Chicago, IL, USA |
| Owner | Jamie Chen |

Next: add a contact, or create the first opportunity on this account?

## Handling Edge Cases

- **Duplicate rules:** If the create fails with `DUPLICATES_DETECTED`, show the
  matching record from the error and ask whether to use it instead. Do not
  bypass the rule.
- **Validation rules:** On `FIELD_CUSTOM_VALIDATION_EXCEPTION`, relay the org's
  error message verbatim and ask for the missing information.
- **Required custom fields:** If the create fails with
  `REQUIRED_FIELD_MISSING`, read the schema, identify the required custom
  fields, and ask the user for those values.
- **Person Accounts:** If the user is describing an individual rather than a
  company and the org has Person Accounts enabled, confirm which record type
  they want before creating.
- **Currency and number fields:** Send `AnnualRevenue` and `NumberOfEmployees`
  as unquoted JSON numbers, not strings.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce to create that account. You
   should see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm.
3. If they are already connected and still get an auth error, suggest
   disconnecting and reconnecting the Salesforce connector from Sources &
   Skills.
4. On `INSUFFICIENT_ACCESS_OR_READONLY`, explain that their Salesforce profile
   does not permit creating accounts — this is an org permission, not a
   connector problem.
