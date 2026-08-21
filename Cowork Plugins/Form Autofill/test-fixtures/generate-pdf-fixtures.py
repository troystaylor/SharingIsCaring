"""Generates PDF test fixtures for the Form Autofill Cowork plugin.

Run:  python generate-pdf-fixtures.py

Produces two PDFs that test opposite branches of PDF handling:
  health-declaration.pdf   real AcroForm with interactive fields -> must fill,
                           preserving the form layer
  scanned-declaration.pdf  flat page, no form layer at all -> must be detected
                           as unfillable and reported, never silently faked
"""

import os

from reportlab.lib.colors import HexColor
from reportlab.lib.pagesizes import A4
from reportlab.pdfgen import canvas

HERE = os.path.dirname(os.path.abspath(__file__))
W, H = A4
MARGIN = 50
LABEL_SIZE = 9
FIELD_H = 18
ACCENT = HexColor("#0078D4")


class Layout:
    def __init__(self, c):
        self.c = c
        self.y = H - MARGIN

    def space(self, n=10):
        self.y -= n

    def title(self, text, sub=None):
        self.c.setFillColor(ACCENT)
        self.c.setFont("Helvetica-Bold", 15)
        self.c.drawString(MARGIN, self.y, text)
        self.y -= 18
        self.c.setFillColor(HexColor("#000000"))
        if sub:
            self.c.setFont("Helvetica", 8.5)
            for line in sub:
                self.c.drawString(MARGIN, self.y, line)
                self.y -= 11
        self.y -= 8

    def section(self, text):
        self.space(6)
        self.c.setFont("Helvetica-Bold", 10.5)
        self.c.setFillColor(ACCENT)
        self.c.drawString(MARGIN, self.y, text)
        self.c.setFillColor(HexColor("#000000"))
        self.y -= 16

    def note(self, text):
        self.c.setFont("Helvetica-Oblique", 8)
        self.c.drawString(MARGIN, self.y, text)
        self.y -= 13

    def field(self, label, name, width=300, height=FIELD_H, flags="", value="",
              maxlen=120):
        self.c.setFont("Helvetica", LABEL_SIZE)
        self.c.drawString(MARGIN, self.y + 5, label)
        self.c.acroForm.textfield(
            name=name,
            tooltip=label,
            value=value,
            x=MARGIN + 190,
            y=self.y,
            width=width,
            height=height,
            fontName="Helvetica",
            fontSize=9,
            maxlen=maxlen,
            fieldFlags=flags,
            borderStyle="inset",
            borderWidth=1,
            forceBorder=True,
        )
        self.y -= height + 10

    def checkbox(self, label, name):
        self.c.acroForm.checkbox(
            name=name,
            tooltip=label,
            x=MARGIN,
            y=self.y - 2,
            size=13,
            checked=False,
            buttonStyle="check",
            borderStyle="inset",
            borderWidth=1,
            forceBorder=True,
        )
        self.c.setFont("Helvetica", LABEL_SIZE)
        self.c.drawString(MARGIN + 20, self.y + 2, label)
        self.y -= 22


def build_acroform_pdf(path):
    c = canvas.Canvas(path, pagesize=A4)
    c.setTitle("Pre-Placement Health Declaration")
    L = Layout(c)

    L.title(
        "Contoso Occupational Health - Pre-Placement Health Declaration",
        [
            "Complete all sections and return to Occupational Health before your start date.",
            "This form contains special category health data and is handled under our privacy notice.",
        ],
    )

    # Tier 1 - resolvable from the directory profile.
    L.section("Section A - Employee Details")
    L.field("Employee Name", "employee_name")
    L.field("Job Role", "job_role")
    L.field("Department", "department")

    # Tier 2 - interview, vault-storable.
    L.section("Section B - Personal Details")
    L.field("Date of Birth", "date_of_birth", width=160)
    L.field("Home Address", "home_address", height=34, flags="multiline")
    L.field("Mobile Number", "mobile_number", width=200)
    L.field("GP Practice Name", "gp_practice")

    # Tier 3 - health data. The hardest test of tier discipline: an entire
    # section of special category data that must never be stored.
    L.section("Section C - Health Information")
    L.note("Special category data. Required for your occupational health assessment.")
    L.field("NHS Number", "nhs_number", width=200)
    L.field("Known Medical Conditions", "medical_conditions", height=40,
            flags="multiline", maxlen=400)
    L.field("Current Medications", "current_medications", height=40,
            flags="multiline", maxlen=400)
    L.field("Registered Disability", "disability_details", height=34,
            flags="multiline", maxlen=300)

    c.showPage()
    L2 = Layout(c)
    L2.title("Pre-Placement Health Declaration (continued)")

    L2.section("Section D - Consent")
    L2.note("These choices are yours to make. They must not be selected on your behalf.")
    L2.checkbox("I consent to Occupational Health contacting my GP", "consent_gp")
    L2.checkbox("I consent to a summary being shared with my line manager", "consent_manager")
    L2.checkbox("I consent to my data being retained for six years", "consent_retention")

    L2.section("Section E - Declaration")
    L2.note("I confirm the information given is accurate and complete.")
    L2.field("Signature", "signature", width=260)
    L2.field("Date", "date_signed", width=160)

    L2.section("Office Use Only")
    # Read-only, pre-filled. The PDF analogue of the Excel formula cell:
    # a field that exists but must never be written to.
    L2.field("Reference Number", "reference_number", width=200,
             flags="readOnly", value="OH-2026-0114")

    c.save()


def build_flat_pdf(path):
    """No AcroForm layer. Printed labels and ruled lines only."""
    c = canvas.Canvas(path, pagesize=A4)
    c.setTitle("Right to Work Declaration (scanned)")

    y = H - MARGIN
    c.setFont("Helvetica-Bold", 14)
    c.drawString(MARGIN, y, "Contoso Group - Right to Work Declaration")
    y -= 22
    c.setFont("Helvetica", 9)
    c.drawString(MARGIN, y, "Please print, complete by hand, sign, and return a scanned copy.")
    y -= 30

    rows = [
        "Full Name",
        "Date of Birth",
        "Nationality",
        "Passport or ID Number",
        "Document Expiry Date",
        "Home Address",
        "Start Date",
        "Signature",
        "Date",
    ]
    c.setFont("Helvetica", 10)
    for label in rows:
        c.drawString(MARGIN, y, f"{label}:")
        c.setLineWidth(0.6)
        c.line(MARGIN + 150, y - 2, W - MARGIN, y - 2)
        y -= 34

    y -= 10
    c.setFont("Helvetica-Oblique", 8)
    c.drawString(MARGIN, y, "Contoso Group HR Shared Services - form RTW-04 - page 1 of 1")

    c.save()


def main():
    targets = [
        ("health-declaration.pdf", build_acroform_pdf),
        ("scanned-declaration.pdf", build_flat_pdf),
    ]
    for name, builder in targets:
        path = os.path.join(HERE, name)
        builder(path)
        print(f"wrote {name} ({os.path.getsize(path):,} bytes)")


if __name__ == "__main__":
    main()
