# Field Synonyms and Tier Classification

Normalize every form label to a canonical concept before filling or asking. This
prevents asking the same question multiple times when a packet contains several
forms that word the same field differently.

## Tier 1 — Directory profile, filled silently, never stored

| Canonical concept | Common labels |
|---|---|
| `full_name` | Full Name, Name, Employee Name, Applicant Name |
| `first_name` | First Name, Given Name, Forename |
| `last_name` | Last Name, Surname, Family Name |
| `job_title` | Title, Job Title, Position, Role |
| `department` | Department, Dept, Division, Business Unit |
| `office_location` | Office, Location, Work Site, Building |
| `work_email` | Email, Work Email, Business Email, Company Email |
| `work_phone` | Phone, Work Phone, Office Phone, Business Telephone |
| `manager_name` | Manager, Supervisor, Reports To, Line Manager |
| `employee_id` | Employee ID, Staff Number, Personnel Number |

If the directory profile is unavailable, these become Tier 2 for that session.
Collect them in the interview rather than guessing.

## Tier 2 — Interview, stored in the vault

| Canonical concept | Common labels | Format |
|---|---|---|
| `legal_name` | Legal Name, Name as it appears on ID | Text |
| `preferred_name` | Preferred Name, Goes By, Nickname | Text |
| `date_of_birth` | DOB, Date of Birth, Birthdate | ISO 8601 |
| `home_address` | Home Address, Residential Address, Street Address | Multi-line |
| `mailing_address` | Mailing Address, Postal Address | Multi-line |
| `personal_phone` | Mobile, Cell, Personal Phone, Home Phone | E.164 |
| `personal_email` | Personal Email, Alternate Email | Email |
| `emergency_contact_name` | Emergency Contact, In Case of Emergency, ICE | Text |
| `emergency_contact_relationship` | Relationship, Relation to Employee | Text |
| `emergency_contact_phone` | Emergency Phone, Contact Number | E.164 |
| `nationality` | Nationality, Citizenship, Country of Citizenship | Text |
| `dietary_requirements` | Dietary, Food Allergies, Meal Preference | Text |
| `accessibility_needs` | Accessibility, Accommodations, Special Requirements | Text |
| `shirt_size` | Shirt Size, T-Shirt Size, Apparel Size | Text |

## Tier 3 — Always ask, never store, never infer

Treat any field matching these concepts as Tier 3 regardless of how it is labeled.

| Canonical concept | Common labels |
|---|---|
| `national_id` | SSN, Social Security Number, National Insurance Number, NI Number, SIN |
| `tax_id` | Tax ID, TIN, EIN, VAT Number, Tax File Number |
| `passport_number` | Passport, Passport No, Travel Document Number |
| `visa_number` | Visa Number, Work Permit Number, Residence Permit |
| `drivers_license` | Driver's License, License Number, DL Number |
| `bank_account` | Account Number, IBAN, Sort Code, Routing Number |
| `payment_card` | Card Number, Credit Card, Debit Card, CVV, Security Code |
| `health_info` | Medical Condition, Health Information, Disability Details, Medications |
| `credentials` | Password, PIN, Security Answer, Access Code |
| `signature` | Signature, Sign Here, Authorized Signature |

`signature` is Tier 3 because a signature is an act of authorization, not a data
field. Never reproduce a signature from any source. Leave it blank and tell the
user the form needs signing.

## Classification Rules

1. **Match on meaning, not exact wording.** Form labels vary endlessly; the tables
   above are representative, not exhaustive.
2. **When unsure between two tiers, choose the more protective one.** An
   unnecessary question costs a moment. A leaked identifier does not.
3. **A field is Tier 3 if disclosure to the wrong party would cause real harm** —
   financial loss, identity theft, or exposure of protected personal data.
4. **Context can promote a field.** A "reference number" is ordinarily harmless,
   but on a financial form it may be an account identifier. Read the surrounding
   fields before deciding.
5. **Never demote a Tier 3 field** because the user already supplied it earlier
   in the session or on another form in the same packet.

## Format Normalization

Normalize on capture so the same answer serves forms that want different layouts:

- **Dates** — store ISO 8601, render to match the form's printed format
- **Phone numbers** — store E.164, render to match the form's expected pattern
- **Addresses** — store as structured parts, assemble to fit the form's fields
- **Names** — store discrete parts, compose for forms wanting a single field
