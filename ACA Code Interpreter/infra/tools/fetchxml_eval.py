#!/usr/bin/env python3
"""fetchxml_eval.py — Validate FetchXML and optionally convert to OData $filter.

Usage: python3 /opt/tools/fetchxml_eval.py "<fetchxml>"
"""
import sys
import json
from lxml import etree

def validate_fetchxml(xml_string):
    errors = []
    warnings = []
    odata_parts = {"filter": [], "select": [], "entity": "", "top": None}

    try:
        root = etree.fromstring(xml_string.encode("utf-8"))
    except etree.XMLSyntaxError as e:
        return {"valid": False, "errors": [f"XML parse error: {str(e)}"], "warnings": [], "odata": ""}

    # Must be <fetch> root
    if root.tag != "fetch":
        errors.append(f"Root element must be 'fetch', got '{root.tag}'")
        return {"valid": False, "errors": errors, "warnings": warnings, "odata": ""}

    # Check for top attribute
    top = root.get("top")
    if top:
        odata_parts["top"] = top

    # Find entity
    entity = root.find("entity")
    if entity is None:
        errors.append("Missing <entity> element")
        return {"valid": False, "errors": errors, "warnings": warnings, "odata": ""}

    entity_name = entity.get("name")
    if not entity_name:
        errors.append("<entity> missing 'name' attribute")
    else:
        odata_parts["entity"] = entity_name

    # Process attributes (→ $select)
    for attr in entity.findall("attribute"):
        attr_name = attr.get("name")
        if attr_name:
            odata_parts["select"].append(attr_name)
        else:
            warnings.append("<attribute> element missing 'name'")

    # Process filters (→ $filter)
    for filter_el in entity.findall(".//filter"):
        filter_type = filter_el.get("type", "and")
        conditions = []
        for cond in filter_el.findall("condition"):
            attr_name = cond.get("attribute")
            operator = cond.get("operator")
            value = cond.get("value")

            if not attr_name:
                errors.append("<condition> missing 'attribute'")
                continue
            if not operator:
                errors.append(f"<condition attribute='{attr_name}'> missing 'operator'")
                continue

            odata_op = convert_operator(operator, attr_name, value)
            if odata_op:
                conditions.append(odata_op)

        if conditions:
            joiner = f" {filter_type} "
            odata_parts["filter"].append(f"({joiner.join(conditions)})")

    # Check for link-entity (joins)
    links = entity.findall("link-entity")
    if links:
        warnings.append(f"Contains {len(links)} link-entity join(s) — OData conversion is approximate")

    # Build OData query string
    odata = build_odata(odata_parts)

    return {
        "valid": len(errors) == 0,
        "errors": errors,
        "warnings": warnings,
        "odata": odata,
        "entity": entity_name or "",
        "attributeCount": len(odata_parts["select"]),
        "filterCount": len(odata_parts["filter"])
    }


def convert_operator(operator, attr_name, value):
    op_map = {
        "eq": f"{attr_name} eq '{value}'",
        "ne": f"{attr_name} ne '{value}'",
        "gt": f"{attr_name} gt '{value}'",
        "ge": f"{attr_name} ge '{value}'",
        "lt": f"{attr_name} lt '{value}'",
        "le": f"{attr_name} le '{value}'",
        "like": f"contains({attr_name},'{value}')" if value else None,
        "not-like": f"not contains({attr_name},'{value}')" if value else None,
        "null": f"{attr_name} eq null",
        "not-null": f"{attr_name} ne null",
        "in": None,  # Complex — skip
        "not-in": None,
    }
    return op_map.get(operator, f"{attr_name} {operator} '{value}'")


def build_odata(parts):
    segments = []
    if parts["entity"]:
        segments.append(f"/{parts['entity']}s")

    query_params = []
    if parts["select"]:
        query_params.append(f"$select={','.join(parts['select'])}")
    if parts["filter"]:
        query_params.append(f"$filter={' and '.join(parts['filter'])}")
    if parts["top"]:
        query_params.append(f"$top={parts['top']}")

    if query_params:
        segments.append("?" + "&".join(query_params))

    return "".join(segments)


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(json.dumps({"valid": False, "errors": ["No FetchXML input provided"], "warnings": [], "odata": ""}))
        sys.exit(1)

    xml_input = sys.argv[1]
    result = validate_fetchxml(xml_input)
    print(json.dumps(result, indent=2))
    sys.exit(0 if result["valid"] else 1)
