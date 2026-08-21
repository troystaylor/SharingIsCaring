# Question Patterns

Worked phrasings for field types that are commonly asked badly.

## Names

Ask for parts, and check whether legal and preferred names differ.

> **Good:** "What's your legal name as printed on your ID? If you go by something
> different day to day, tell me that too — some forms ask for both."
>
> **Bad:** "What's your name?" — ambiguous between legal and preferred, and produces
> a single string that then has to be split, often wrongly.

Do not assume name order or structure. Not everyone has a middle name, not every
name splits into first and last, and ordering conventions vary.

## Dates

Never accept an ambiguous numeric date silently.

> **Good:** "What's your date of birth? Any format is fine — if you write it
> numerically, tell me the order so I don't mix up day and month."
>
> **Bad:** Accepting `03/04/1990` and assuming a locale.

If a numeric date arrives ambiguous and both readings are plausible, ask which.
If only one reading is plausible — `25/12/1990` — take it without asking.

## Addresses

Ask for structured parts, and clarify which address is wanted.

> **Good:** "What's your home address? Street, city, state or region, postcode, and
> country. If your mailing address is different, I'll need that separately."
>
> **Bad:** "Address?" — produces a blob that won't map to a form's discrete fields.

## Phone numbers

Always capture the country code.

> **Good:** "What's the best mobile number to reach you on, including country code?"

## Emergency contacts

Ask for the whole set together — the pieces are useless individually.

> **Good:** "For the emergency contact section: who should be contacted, what's
> their relationship to you, and what number should be used?"

## Sensitive fields

Lead with the hand-fill option, state the purpose, and be accurate about retention.

> **Good:** "The payroll form needs your National Insurance number. My suggestion is
> to leave it blank and write it in yourself before sending — that keeps it out of
> this conversation entirely. If you'd rather I filled it in, say so. I won't add it
> to your saved profile, though I can't speak for your organisation's Copilot
> memory or chat history settings."
>
> **Bad:** "What's your NI number?" — no purpose, no retention statement, no exit.
>
> **Also bad:** "I won't save it anywhere" — a promise this skill cannot keep. It
> controls the profile file, not chat history retention.

## Confirming stale values

State the value and its age, and make confirming cheap.

> **Good:** "I have your home address from 14 months ago as 12 Oak Lane, Bristol,
> BS1 4TR. Still current? Reply 'yes' and I'll use it."

## Confirming captured values

Group as asked, mask sensitive entries.

> Here's what I have. Tell me if anything needs correcting.
>
> **Identity**
> - Legal name: Alexandra Jane Okafor
> - Date of birth: 14 March 1990
>
> **Contact**
> - Home address: 12 Oak Lane, Bristol, BS1 4TR, United Kingdom
> - Mobile: +44 7700 900123
>
> **Sensitive** *(not saved)*
> - National Insurance number: ●●●●●123D

## Tone

- Plain language over form jargon — "your address" beats "residential domicile"
- Neutral about refusals — no implied disapproval
- Brief — the user wants to finish, not to read
- No apologizing for asking; the questions are necessary and the user knows it
