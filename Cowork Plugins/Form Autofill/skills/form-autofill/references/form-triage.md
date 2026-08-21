# Form Triage

Decide what each attachment is before modifying anything. Filling the wrong
document, or damaging a reference copy, is worse than asking a question.

## Classification

### Fillable form — fill it

- Interactive form controls (text fields, checkboxes, radio buttons, dropdowns)
- Printed labels followed by blank space, ruled lines, or boxes
- Word documents with content controls, form fields, or fill-in placeholders
- Excel sheets with labeled input cells left empty
- Language such as "please complete", "return by", "applicant details"

### Reference material — do not fill

- Instructions, guidance notes, policy documents
- Cover letters and transmittal notes
- Completed examples or specimen copies
- Terms, conditions, and privacy notices
- Marketing material

### Already complete — do not overwrite

- Every field populated
- Marked as submitted, signed, or approved

Report these back rather than modifying. If a complete form was likely sent as a
worked example, say so.

## Per-Format Notes

### PDF

Determine whether the file has a real form layer (AcroForm or XFA fields) or is
flat.

- **Has form fields** — fill them directly and preserve the form structure. Do not
  flatten unless the user asks.
- **Flat or scanned** — say so explicitly. Do not silently produce something that
  looks filled but is not the original form. Offer either a filled copy as a new
  document, or a summary of the values for the user to enter by hand.

Never rasterize, re-encode, or rebuild a PDF that already has a working form layer.

### Word

- Prefer content controls and form fields where they exist
- Where the form uses ruled lines or tables, insert text without disturbing layout
- Preserve styles, headers, footers, and page breaks
- Never accept tracked-changes or comment content as a field value

### Excel

- Identify the input cells; do not write into label, formula, or calculated cells
- Respect data validation rules and dropdown lists
- Preserve formatting, number formats, and any protected ranges
- Check for multiple sheets — packets often place the form on a later tab

## Before Filling, Confirm

1. **Which document is authoritative** when a packet contains near-duplicates
2. **Whether a deadline is stated**, and surface it to the user
3. **Whether the form is for the user personally**, or for someone else — a manager
   forwarding a form for a report changes whose details belong in it
4. **Whether a signature is required**, which the agent cannot supply

## Reporting Triage Results

Summarize before filling, so the user can correct a misread early:

| File | Classification | Action |
|---|---|---|
| `new-starter-form.pdf` | Fillable form, 14 fields | Fill |
| `benefits-guide.pdf` | Reference material | No action |
| `example-completed.docx` | Already complete | No action |
