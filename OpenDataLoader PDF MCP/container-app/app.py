"""
OpenDataLoader PDF REST API Service
Wraps the opendataloader-pdf library with HTTP endpoints for the Power Platform connector.
"""

import base64
import json
import os
import tempfile
import urllib.request
from functools import wraps

from flask import Flask, jsonify, request

import opendataloader_pdf

app = Flask(__name__)

API_KEY = os.environ.get("API_KEY", "")


def require_api_key(f):
    @wraps(f)
    def decorated(*args, **kwargs):
        if not API_KEY:
            return f(*args, **kwargs)
        key = request.headers.get("X-API-Key", "")
        if key != API_KEY:
            return jsonify({"error": "Unauthorized"}), 401
        return f(*args, **kwargs)
    return decorated


def get_pdf_path(data):
    """Download or decode PDF source to a temporary file."""
    source = data.get("source")
    if not source:
        raise ValueError("Missing required field: source")

    source_type = data.get("sourceType", "url")
    tmp = tempfile.NamedTemporaryFile(suffix=".pdf", delete=False)

    try:
        if source_type == "base64":
            tmp.write(base64.b64decode(source))
        else:
            # Validate URL scheme
            if not source.startswith(("https://", "http://")):
                raise ValueError("URL must start with https:// or http://")
            urllib.request.urlretrieve(source, tmp.name)
        tmp.close()
        return tmp.name
    except Exception:
        tmp.close()
        if os.path.exists(tmp.name):
            os.unlink(tmp.name)
        raise


@app.route("/health", methods=["GET"])
def health():
    return jsonify({"status": "healthy"})


@app.route("/api/info", methods=["GET"])
@require_api_key
def info():
    version = getattr(opendataloader_pdf, "__version__", "unknown")
    return jsonify({
        "version": version,
        "hybridAvailable": False,
        "ocrAvailable": False,
        "supportedFormats": ["markdown", "json", "html", "text"],
        "supportedLanguages": []
    })


@app.route("/api/convert", methods=["POST"])
@require_api_key
def convert():
    data = request.get_json(force=True)
    pdf_path = get_pdf_path(data)

    try:
        fmt = data.get("format", "markdown")
        if fmt not in ("markdown", "json", "html", "text"):
            return jsonify({"error": f"Unsupported format: {fmt}"}), 400

        with tempfile.TemporaryDirectory() as out_dir:
            kwargs = {
                "input_path": [pdf_path],
                "output_dir": out_dir,
                "format": fmt
            }
            if data.get("useStructTree"):
                kwargs["use_struct_tree"] = True
            if data.get("hybrid"):
                kwargs["hybrid"] = "docling-fast"
            if data.get("pages"):
                kwargs["pages"] = data["pages"]

            opendataloader_pdf.convert(**kwargs)

            content = ""
            for f in os.listdir(out_dir):
                fpath = os.path.join(out_dir, f)
                if os.path.isfile(fpath):
                    with open(fpath, "r", encoding="utf-8") as fh:
                        content = fh.read()
                    break

            return jsonify({
                "content": content,
                "format": fmt
            })
    finally:
        if os.path.exists(pdf_path):
            os.unlink(pdf_path)


@app.route("/api/tables", methods=["POST"])
@require_api_key
def tables():
    data = request.get_json(force=True)
    pdf_path = get_pdf_path(data)

    try:
        with tempfile.TemporaryDirectory() as out_dir:
            kwargs = {
                "input_path": [pdf_path],
                "output_dir": out_dir,
                "format": "json"
            }
            if data.get("hybrid"):
                kwargs["hybrid"] = "docling-fast"
            if data.get("pages"):
                kwargs["pages"] = data["pages"]

            opendataloader_pdf.convert(**kwargs)

            table_elements = []
            for f in os.listdir(out_dir):
                if f.endswith(".json"):
                    fpath = os.path.join(out_dir, f)
                    with open(fpath, "r", encoding="utf-8") as fh:
                        elements = json.load(fh)
                        if isinstance(elements, list):
                            table_elements = [e for e in elements if e.get("type") == "table"]
                    break

            return jsonify({
                "tables": table_elements,
                "tableCount": len(table_elements)
            })
    finally:
        if os.path.exists(pdf_path):
            os.unlink(pdf_path)


@app.route("/api/elements", methods=["POST"])
@require_api_key
def elements():
    data = request.get_json(force=True)
    pdf_path = get_pdf_path(data)

    try:
        with tempfile.TemporaryDirectory() as out_dir:
            opendataloader_pdf.convert(
                input_path=[pdf_path],
                output_dir=out_dir,
                format="json"
            )

            all_elements = []
            for f in os.listdir(out_dir):
                if f.endswith(".json"):
                    fpath = os.path.join(out_dir, f)
                    with open(fpath, "r", encoding="utf-8") as fh:
                        parsed = json.load(fh)
                        if isinstance(parsed, list):
                            all_elements = parsed
                    break

            # Filter by element types if specified
            type_filter = data.get("elementTypes")
            if type_filter:
                types = [t.strip() for t in type_filter.split(",")]
                all_elements = [e for e in all_elements if e.get("type") in types]

            return jsonify({
                "elements": all_elements,
                "elementCount": len(all_elements)
            })
    finally:
        if os.path.exists(pdf_path):
            os.unlink(pdf_path)


@app.route("/api/accessibility", methods=["POST"])
@require_api_key
def accessibility():
    data = request.get_json(force=True)
    pdf_path = get_pdf_path(data)

    try:
        with tempfile.TemporaryDirectory() as out_dir:
            opendataloader_pdf.convert(
                input_path=[pdf_path],
                output_dir=out_dir,
                format="json",
                use_struct_tree=True
            )

            elements = []
            for f in os.listdir(out_dir):
                if f.endswith(".json"):
                    fpath = os.path.join(out_dir, f)
                    with open(fpath, "r", encoding="utf-8") as fh:
                        parsed = json.load(fh)
                        if isinstance(parsed, list):
                            elements = parsed
                    break

            has_tags = len(elements) > 0
            issues = []
            if not has_tags:
                issues.append({
                    "severity": "error",
                    "message": "PDF has no structure tags. Consider auto-tagging for accessibility compliance."
                })

            return jsonify({
                "isTagged": has_tags,
                "tagCount": len(elements),
                "pageCount": len(set(e.get("page number", 0) for e in elements)) if elements else 0,
                "hasTitle": any(e.get("type") == "heading" and e.get("heading level", 0) == 1 for e in elements),
                "hasLanguage": False,
                "summary": "PDF has structure tags" if has_tags else "PDF is untagged — no accessibility tags found",
                "issues": issues
            })
    finally:
        if os.path.exists(pdf_path):
            os.unlink(pdf_path)


if __name__ == "__main__":
    port = int(os.environ.get("PORT", "8080"))
    app.run(host="0.0.0.0", port=port)
