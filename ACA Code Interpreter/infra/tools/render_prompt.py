#!/usr/bin/env python3
"""render_prompt.py — Render prompt templates with variable substitution.

Supports Copilot Studio-style {Topic.VariableName} and Jinja2 {{ variable }} syntax.

Usage: python3 /opt/tools/render_prompt.py "<json-input>" [--vars /path/to/vars.json]

Input JSON: {"template": "...", "variables": {"key": "value"}}
Or: template string as first arg + --vars file for variables
"""
import sys
import json
import re


def render_copilot_studio_vars(template, variables):
    """Replace {Topic.VarName} and {VarName} placeholders."""
    def replacer(match):
        full_key = match.group(1)
        # Try full key first (e.g., Topic.CustomerName)
        if full_key in variables:
            return str(variables[full_key])
        # Try without prefix (e.g., CustomerName from Topic.CustomerName)
        parts = full_key.split(".")
        if len(parts) > 1:
            short_key = parts[-1]
            if short_key in variables:
                return str(variables[short_key])
        # Try case-insensitive
        for k, v in variables.items():
            if k.lower() == full_key.lower():
                return str(v)
        return match.group(0)  # Leave unresolved

    pattern = r"\{([^}]+)\}"
    return re.sub(pattern, replacer, template)


def render_jinja2_vars(template, variables):
    """Replace {{ variable }} placeholders."""
    try:
        from jinja2 import Template
        t = Template(template)
        return t.render(**variables)
    except ImportError:
        # Fallback: simple regex replacement
        def replacer(match):
            key = match.group(1).strip()
            return str(variables.get(key, match.group(0)))
        return re.sub(r"\{\{\s*(\w+)\s*\}\}", replacer, template)


def main():
    input_str = sys.argv[1] if len(sys.argv) > 1 else ""
    vars_file = None

    # Check for --vars flag
    if "--vars" in sys.argv:
        idx = sys.argv.index("--vars")
        if idx + 1 < len(sys.argv):
            vars_file = sys.argv[idx + 1]

    # Parse input
    variables = {}
    template = ""

    try:
        data = json.loads(input_str)
        template = data.get("template", input_str)
        variables = data.get("variables", {})
    except (json.JSONDecodeError, TypeError):
        template = input_str

    # Load vars from file if specified
    if vars_file:
        try:
            with open(vars_file, "r") as f:
                file_vars = json.load(f)
                variables.update(file_vars)
        except (FileNotFoundError, json.JSONDecodeError) as e:
            print(json.dumps({"error": f"Failed to load vars file: {e}", "rendered": ""}))
            sys.exit(1)

    # Detect template style and render
    has_copilot_style = re.search(r"\{[A-Za-z]", template)
    has_jinja_style = "{{" in template

    if has_jinja_style:
        rendered = render_jinja2_vars(template, variables)
    elif has_copilot_style:
        rendered = render_copilot_studio_vars(template, variables)
    else:
        rendered = template

    # Find unresolved variables
    unresolved = re.findall(r"\{([^}]+)\}", rendered)
    unresolved_jinja = re.findall(r"\{\{\s*(\w+)\s*\}\}", rendered)

    result = {
        "rendered": rendered,
        "templateLength": len(template),
        "renderedLength": len(rendered),
        "variablesUsed": len(variables),
        "unresolvedVariables": unresolved + unresolved_jinja
    }

    print(json.dumps(result, indent=2))
    sys.exit(0)


if __name__ == "__main__":
    main()
