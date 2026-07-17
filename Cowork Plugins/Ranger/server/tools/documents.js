// Document generation tools — Word/Excel/PPT via Python in sandboxes
import { createSandbox, executeCommand, readFile } from "./sandbox-client.js";

export function registerDocumentTools(server, z) {
    server.tool("create_word_doc", "Generate a Word document from structured content.", {
        session_id: z.string().optional().describe("Existing session. Omit to auto-create."),
        title: z.string().describe("Document title"),
        content: z.string().describe("Markdown or structured text for the document body"),
        filename: z.string().optional().describe("Output filename. Default: document.docx"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ session_id, title, content, filename }) => {
        const fname = filename || "document.docx";
        let sid = session_id;
        if (!sid) sid = await createSandbox("code", "1000m", "2Gi");

        // Install python-docx if not present
        await executeCommand(sid, `pip install -q python-docx 2>/dev/null`);
        const script = buildWordScript(title, content, fname);
        const b64 = Buffer.from(script).toString("base64");
        const result = await executeCommand(sid, `echo '${b64}' | base64 -d > /tmp/_doc.py && python3 /tmp/_doc.py 2>&1`);
        if (result.stdout && result.stdout.includes("Error")) {
            throw new Error(`Doc generation failed: ${result.stdout.substring(0, 200)}`);
        }
        const fileContent = await readFile(sid, `/workspace/${fname}`);
        return { content: [{ type: "text", text: JSON.stringify({ session_id: sid, file_path: `/workspace/${fname}`, file_name: fname, content: fileContent, size_bytes: Buffer.from(fileContent, "base64").length }) }] };
    });

    server.tool("create_excel", "Generate an Excel spreadsheet from data.", {
        session_id: z.string().optional().describe("Existing session. Omit to auto-create."),
        sheets: z.array(z.object({
            name: z.string().describe("Sheet name"),
            headers: z.array(z.string()).describe("Column headers"),
            rows: z.array(z.array(z.string())).describe("Data rows"),
        })).describe("Sheets with headers and data rows"),
        filename: z.string().optional().describe("Output filename. Default: workbook.xlsx"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ session_id, sheets, filename }) => {
        const fname = filename || "workbook.xlsx";
        let sid = session_id;
        if (!sid) sid = await createSandbox("code", "1000m", "2Gi");

        const script = buildExcelScript(sheets, fname);
        const b64 = Buffer.from(script).toString("base64");
        await executeCommand(sid, `pip install -q openpyxl 2>/dev/null`);
        await executeCommand(sid, `echo '${b64}' | base64 -d > /tmp/_xl.py && python3 /tmp/_xl.py 2>&1`);
        const fileContent = await readFile(sid, `/workspace/${fname}`);
        return { content: [{ type: "text", text: JSON.stringify({ session_id: sid, file_path: `/workspace/${fname}`, file_name: fname, content: fileContent, size_bytes: Buffer.from(fileContent, "base64").length }) }] };
    });

    server.tool("create_powerpoint", "Generate a PowerPoint presentation.", {
        session_id: z.string().optional().describe("Existing session. Omit to auto-create."),
        title: z.string().describe("Presentation title"),
        slides: z.array(z.object({
            title: z.string().describe("Slide title"),
            bullets: z.array(z.string()).optional().describe("Bullet points"),
            notes: z.string().optional().describe("Speaker notes"),
        })).describe("Slides"),
        filename: z.string().optional().describe("Output filename. Default: presentation.pptx"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ session_id, title, slides, filename }) => {
        const fname = filename || "presentation.pptx";
        let sid = session_id;
        if (!sid) sid = await createSandbox("code", "1000m", "2Gi");

        const script = buildPptxScript(title, slides, fname);
        const b64 = Buffer.from(script).toString("base64");
        await executeCommand(sid, `pip install -q python-pptx 2>/dev/null`);
        await executeCommand(sid, `echo '${b64}' | base64 -d > /tmp/_pptx.py && python3 /tmp/_pptx.py 2>&1`);
        const fileContent = await readFile(sid, `/workspace/${fname}`);
        return { content: [{ type: "text", text: JSON.stringify({ session_id: sid, file_path: `/workspace/${fname}`, file_name: fname, content: fileContent, size_bytes: Buffer.from(fileContent, "base64").length }) }] };
    });
}

function buildWordScript(title, content, filename) {
    const escapedTitle = title.replace(/'/g, "\\'");
    const escapedContent = content.replace(/'/g, "\\'").replace(/\n/g, "\\n");
    return `from docx import Document
doc = Document()
doc.add_heading('${escapedTitle}', 0)
for para in '''${escapedContent}'''.split('\\n'):
    if para.strip():
        if para.startswith('## '):
            doc.add_heading(para[3:], level=2)
        elif para.startswith('# '):
            doc.add_heading(para[2:], level=1)
        elif para.startswith('- '):
            doc.add_paragraph(para[2:], style='List Bullet')
        else:
            doc.add_paragraph(para)
doc.save('/workspace/${filename}')
print('OK')`;
}

function buildExcelScript(sheets, filename) {
    const sheetsJson = JSON.stringify(sheets);
    return `import openpyxl, json
wb = openpyxl.Workbook()
wb.remove(wb.active)
sheets = json.loads('''${sheetsJson}''')
for s in sheets:
    ws = wb.create_sheet(title=s['name'])
    ws.append(s['headers'])
    for row in s['rows']:
        ws.append(row)
wb.save('/workspace/${filename}')
print('OK')`;
}

function buildPptxScript(title, slides, filename) {
    const slidesJson = JSON.stringify(slides);
    const escapedTitle = title.replace(/'/g, "\\'");
    return `from pptx import Presentation
from pptx.util import Inches, Pt
import json
prs = Presentation()
# Title slide
slide = prs.slides.add_slide(prs.slide_layouts[0])
slide.shapes.title.text = '${escapedTitle}'
slides = json.loads('''${slidesJson}''')
for s in slides:
    layout = prs.slide_layouts[1]
    sl = prs.slides.add_slide(layout)
    sl.shapes.title.text = s['title']
    if s.get('bullets'):
        tf = sl.placeholders[1].text_frame
        tf.clear()
        for i, b in enumerate(s['bullets']):
            if i == 0:
                tf.text = b
            else:
                p = tf.add_paragraph()
                p.text = b
    if s.get('notes'):
        sl.notes_slide.notes_text_frame.text = s['notes']
prs.save('/workspace/${filename}')
print('OK')`;
}
