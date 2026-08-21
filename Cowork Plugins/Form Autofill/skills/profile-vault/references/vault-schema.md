# Vault Schema Reference

Canonical field list for `/Documents/Personal/form-profile.md`.

## Storable Fields

| Section | Field | Format | Re-confirm |
|---|---|---|---|
| Identity | Legal name | Text | 24 months |
| Identity | Preferred name | Text | 24 months |
| Identity | Date of birth | ISO 8601 (`YYYY-MM-DD`) | Never |
| Identity | Nationality | Text | Never |
| Contact | Home address | Structured, see below | 12 months |
| Contact | Mailing address | Structured, see below | 12 months |
| Contact | Personal mobile | E.164 (`+447700900123`) | 12 months |
| Contact | Personal email | Email | 12 months |
| Emergency Contact | Name | Text | 12 months |
| Emergency Contact | Relationship | Text | 12 months |
| Emergency Contact | Phone | E.164 | 12 months |
| Preferences | Dietary requirements | Text | 12 months |
| Preferences | Accessibility needs | Text | 12 months |
| Preferences | Shirt size | Text | 12 months |

## Address Structure

Store components separately so they can be assembled to match any form's layout:

```
Street line 1 | Street line 2 | City | State/Region | Postcode | Country
```

Render as a single line only when the target form provides a single field.

## Column Definitions

| Column | Meaning |
|---|---|
| `Field` | Canonical name from the table above |
| `Value` | The stored value, normalized |
| `Source` | Always `interview` — the only legitimate origin |
| `Confirmed` | ISO 8601 date the user last confirmed it |

`Source` exists to make an illegitimate write visible. Any value whose source is
not `interview` was not confirmed by the user and must be re-confirmed before use.

## Prohibited Fields

Never add these sections or fields, under any circumstances:

- National ID, SSN, National Insurance number, SIN
- Tax ID, TIN, EIN, VAT number, tax file number
- Passport number, visa number, work permit, residence permit
- Driver's licence number
- Bank account, IBAN, sort code, routing number
- Payment card number, expiry, CVV
- Health conditions, disabilities, medications, medical history
- Passwords, PINs, security answers, access codes
- Signature images or reproductions

If any of these are found in the file, treat it as a defect: flag it, offer removal,
and do not use the value.

## Integrity Checks

Before writing, confirm:

1. Every section header matches this schema
2. Every table has exactly the four defined columns
3. No field name matches the prohibited list
4. Every `Confirmed` value is a valid ISO 8601 date, not in the future
5. The **Never Stored** section is present

After writing, re-read and confirm the file parses and the intended values are
present. If any check fails, stop and report rather than repairing silently.
