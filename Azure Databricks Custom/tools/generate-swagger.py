"""Generate the Azure Databricks Custom swagger from the Databricks API reference pages.

Usage: python gen.py <pages dir> <existing swagger> <output swagger>
The existing swagger supplies info, host, security and the /mcp operation.
"""
import json
import os
import re
import sys
import hashlib
from collections import OrderedDict, defaultdict

PAGES, EXISTING, OUT = sys.argv[1], sys.argv[2], sys.argv[3]

MAX_DEPTH_REQUEST = 99
MAX_DEPTH_RESPONSE = 2
INCLUDE_STAGES = {"GA", "Public Preview"}
SKIP_FIELD_FLAGS = {"Beta", "Deprecated"}
DEDUPE_MIN_SIZE = 250
FIELD_DESC_LIMIT = 100

FIELD_RE = re.compile(r"^(\s*)- `([^`]+)` \((.+)\)\s*$")
ENUM_RE = re.compile(r"^\s*- `([^`]+)`\s*$")
ENDPOINT_RE = re.compile(r"(?m)^`(GET|POST|PUT|PATCH|DELETE) (/api/[^`\s]+)`")

# Operation IDs that already existed, plus names that read better than the page slug.
OPERATION_IDS = {
    "statement-execution/v1/execute-statement": "ExecuteStatement",
    "statement-execution/v1/get-statement-result": "GetStatement",
    "statement-execution/v1/cancel-statement": "CancelStatement",
    "statement-execution/v1/get-result-data": "GetStatementResultChunk",
    "query-history/v1/list-queries": "ListQueryHistory",
    "queries/v1/list-queries": "ListQueries",
    "jobs/v2/run-now": "RunJobNow",
    "jobs/v2/get-run": "GetJobRun",
    "jobs/v2/list-runs": "ListJobRuns",
    "jobs/v2/cancel-run": "CancelJobRun",
    "jobs/v2/cancel-all-runs": "CancelAllJobRuns",
    "jobs/v2/delete-run": "DeleteJobRun",
    "jobs/v2/export-run": "ExportJobRun",
    "jobs/v2/get-run-output": "GetJobRunOutput",
    "jobs/v2/repair": "RepairJobRun",
    "jobs/v2/submit-run": "SubmitJobRun",
    "jobs/v2/enforce-policy-compliance-for-job": "EnforceJobPolicyCompliance",
    "jobs/v2/get-policy-compliance-for-job": "GetJobPolicyCompliance",
    "jobs/v2/list-job-compliance-for-policy": "ListJobComplianceForPolicy",
    "genie/v1/genie-list-spaces": "ListGenieSpaces",
    "genie/v1/create-space": "CreateGenieSpace",
    "genie/v1/genie-get-space": "GetGenieSpace",
    "genie/v1/update-space": "UpdateGenieSpace",
    "genie/v1/genie-trash-space": "TrashGenieSpace",
    "genie/v1/genie-start-conversation": "StartGenieConversation",
    "genie/v1/genie-list-conversations": "ListGenieConversations",
    "genie/v1/genie-delete-conversation": "DeleteGenieConversation",
    "genie/v1/genie-create-conversation-message": "CreateGenieMessage",
    "genie/v1/genie-get-conversation-message": "GetGenieMessage",
    "genie/v1/genie-list-conversation-messages": "ListGenieMessages",
    "genie/v1/genie-delete-conversation-message": "DeleteGenieMessage",
    "genie/v1/genie-get-message-attachment-query-result": "GetGenieQueryResult",
    "genie/v1/genie-execute-message-attachment-query": "ExecuteGenieAttachmentQuery",
    "genie/v1/genie-generate-download-full-query-result": "StartGenieQueryResultDownload",
    "genie/v1/genie-get-download-full-query-result": "GetGenieQueryResultDownload",
    "genie/v1/download-message-attachment-visualization": "DownloadGenieVisualization",
    "genie/v1/genie-send-message-feedback": "SendGenieMessageFeedback",
    "genie/v1/genie-create-message-comment": "CreateGenieMessageComment",
    "genie/v1/genie-list-message-comments": "ListGenieMessageComments",
    "genie/v1/genie-list-conversation-comments": "ListGenieConversationComments",
    "genie/v1/genie-cancel-response": "CancelGenieAgentResponse",
    "lakeview/v1/create-schedule": "CreateDashboardSchedule",
    "lakeview/v1/delete-schedule": "DeleteDashboardSchedule",
    "lakeview/v1/get-schedule": "GetDashboardSchedule",
    "lakeview/v1/list-schedules": "ListDashboardSchedules",
    "lakeview/v1/update-schedule": "UpdateDashboardSchedule",
    "lakeview/v1/create-subscription": "CreateDashboardSubscription",
    "lakeview/v1/delete-subscription": "DeleteDashboardSubscription",
    "lakeview/v1/get-subscription": "GetDashboardSubscription",
    "lakeview/v1/list-subscriptions": "ListDashboardSubscriptions",
    "uc-tables/v1/table-exists": "TableExists",
    "warehouses/v1/get-workspace-warehouse-config": "GetWorkspaceWarehouseConfig",
    "warehouses/v1/set-workspace-warehouse-config": "SetWorkspaceWarehouseConfig",
}

IMPORTANT = {
    "ExecuteStatement", "GetStatement", "RunJobNow", "GetJobRun", "GetJobRunOutput",
    "StartGenieConversation", "GetGenieMessage", "ListTableSummaries", "ListTables",
}

PATH_ID_NAMES = {"spaces": "space_id", "conversations": "conversation_id", "messages": "message_id",
                 "attachments": "attachment_id", "agents": "agent_id", "responses": "response_id"}

DYNAMIC = {
    "warehouse": {"operationId": "ListWarehouses", "value-collection": "warehouses", "value-path": "id", "value-title": "name"},
    "catalog": {"operationId": "ListCatalogs", "value-collection": "catalogs", "value-path": "name", "value-title": "name"},
    "schema": {"operationId": "ListSchemas", "value-collection": "schemas", "value-path": "name", "value-title": "name"},
    "job": {"operationId": "ListJobs", "value-collection": "jobs", "value-path": "job_id", "value-title": "settings/name"},
    "space": {"operationId": "ListGenieSpaces", "value-collection": "spaces", "value-path": "space_id", "value-title": "title"},
}


def pascal(slug):
    return "".join(w.capitalize() for w in re.split(r"[-_]", slug) if w)


def clean(text):
    text = re.sub(r"\[([^\]]+)\]\([^)]*\)", r"\1", text)
    text = text.replace("`", "").replace("\\_", "_")
    return re.sub(r"\s+", " ", text).strip()


def short(text, limit):
    text = clean(text)
    if len(text) <= limit:
        return text
    cut = text[:limit]
    dot = cut.rfind(". ")
    if dot > limit * 0.5:
        return cut[:dot + 1]
    return cut.rstrip(" ,;:") + "..."


def title_of(name):
    return name.replace("_", " ").replace(".", " ").strip().capitalize()


# ── page parsing ─────────────────────────────────────────────────────────

def split_sections(text):
    sections, cur, buf = {}, None, []
    for line in text.split("\n"):
        m = re.match(r"^## (.+)$", line)
        if m:
            if cur:
                sections[cur] = buf
            cur, buf = m.group(1).strip(), []
        elif cur:
            buf.append(line)
    if cur:
        sections[cur] = buf
    return sections


def parse_tree(lines):
    root = {"indent": -1, "children": []}
    stack, cur, mode = [root], None, None
    for ln in lines:
        m = FIELD_RE.match(ln)
        if m:
            indent, name, meta = len(m.group(1)), m.group(2), [x.strip() for x in m.group(3).split(",")]
            node = {"name": name, "type": meta[0], "meta": meta[1:], "desc": [], "enum": [], "children": [], "indent": indent}
            while stack[-1]["indent"] >= indent:
                stack.pop()
            stack[-1]["children"].append(node)
            stack.append(node)
            cur, mode = node, None
            continue
        if cur is None:
            continue
        s = ln.strip()
        if not s:
            continue
        if s.startswith("Possible values:"):
            mode = "enum"
            continue
        em = ENUM_RE.match(ln)
        if em and mode == "enum":
            cur["enum"].append(em.group(1))
            continue
        if re.match(r"^(Default|Example|Examples|Constraints):", s):
            mode = None
            continue
        mode = None
        cur["desc"].append(s)
    return root["children"]


def usable(node, for_request):
    flags = set(node["meta"])
    if flags & SKIP_FIELD_FLAGS:
        return False
    if for_request and "Output only" in flags:
        return False
    if not for_request and "Input only" in flags:
        return False
    return True


def scalar(t, node, for_request):
    s = {}
    if t == "string":
        s["type"] = "string"
        if "date-time" in node["meta"]:
            s["format"] = "date-time"
        vals = list(OrderedDict.fromkeys(node["enum"]))
        if for_request and 0 < len(vals) <= 40:
            s["enum"] = vals
    elif t == "boolean":
        s["type"] = "boolean"
    elif t == "int32":
        s.update({"type": "integer", "format": "int32"})
    elif t == "int64":
        s.update({"type": "integer", "format": "int64"})
    elif t == "double":
        s.update({"type": "number", "format": "double"})
    else:
        s["type"] = "string"
    return s


def schema_of(node, for_request, depth, summary_depth):
    t = node["type"]
    s = {}
    if t.startswith("array of "):
        inner = t[len("array of "):]
        s["type"] = "array"
        s["items"] = items_of(inner, node, for_request, depth, summary_depth)
    elif t == "object":
        s = object_schema(node["children"], for_request, depth + 1, summary_depth)
    else:
        s = scalar(t, node, for_request)
    d = short(" ".join(node["desc"]), FIELD_DESC_LIMIT) if depth < (3 if for_request else 1) else ""
    if d:
        s["description"] = d
    if depth < summary_depth:
        s["x-ms-summary"] = title_of(node["name"])
    return s


def items_of(inner, node, for_request, depth, summary_depth):
    if inner.startswith("array of "):
        return {"type": "array", "items": items_of(inner[len("array of "):], node, for_request, depth, summary_depth)}
    if inner == "object":
        return object_schema(node["children"], for_request, depth + 1, summary_depth)
    return scalar(inner, node, for_request)


def object_schema(children, for_request, depth, summary_depth):
    s = {"type": "object"}
    kids = [c for c in children if usable(c, for_request)]
    if not kids or depth > (MAX_DEPTH_REQUEST if for_request else MAX_DEPTH_RESPONSE):
        return s
    props = OrderedDict()
    required = []
    for c in kids:
        props[c["name"]] = schema_of(c, for_request, depth, summary_depth)
        if for_request and "required" in c["meta"]:
            required.append(c["name"])
    s["properties"] = props
    if required:
        s["required"] = required
    return s


# ── parameters ───────────────────────────────────────────────────────────

def query_params(children, prefix=""):
    out = []
    for c in children:
        if not usable(c, True):
            continue
        name = prefix + c["name"]
        t = c["type"]
        required = "required" in c["meta"]
        if t == "object":
            out.extend(query_params(c["children"], name + "."))
            continue
        p = {"name": name, "in": "query", "required": required, "x-ms-summary": title_of(name)}
        if t.startswith("array of "):
            inner = t[len("array of "):]
            if inner == "object" or inner.startswith("array"):
                continue
            p["type"] = "array"
            p["items"] = scalar(inner, c, True)
            p["collectionFormat"] = "multi"
        else:
            p.update(scalar(t, c, True))
        d = short(" ".join(c["desc"]), 200)
        p["description"] = d or title_of(name) + "."
        out.append(p)
    return out


def path_params(children, rename):
    out = []
    for c in children:
        name = rename.get(c["name"], c["name"])
        p = {"name": name, "in": "path", "required": True, "x-ms-summary": title_of(name), "x-ms-url-encoding": "single"}
        p.update(scalar(c["type"], c, True))
        p["description"] = short(" ".join(c["desc"]), 200) or title_of(name) + "."
        out.append(p)
    return out


def expand_name_path(path):
    m = re.search(r"\{name=([^}]+)\}", path)
    if not m:
        return path, []
    parts = m.group(1).split("/")
    out, params = [], []
    i = 0
    while i < len(parts):
        if i + 1 < len(parts) and parts[i + 1] == "*":
            pid = PATH_ID_NAMES.get(parts[i], parts[i].rstrip("s") + "_id")
            out.append(parts[i] + "/{" + pid + "}")
            params.append(pid)
            i += 2
        else:
            out.append(parts[i])
            i += 1
    return path.replace(m.group(0), "/".join(out)), params


# ── operations ───────────────────────────────────────────────────────────

def build_operations():
    ops = []
    for fname in sorted(os.listdir(PAGES)):
        slug = fname[:-3].replace("__", "/")
        text = open(os.path.join(PAGES, fname), encoding="utf-8").read()
        m = ENDPOINT_RE.search(text)
        if not m:
            continue
        stage = (re.search(r"(?m)^Launch stage:\s*(.+)$", text) or [None, ""])[1].strip()
        if stage not in INCLUDE_STAGES:
            continue
        method, path = m.group(1), m.group(2).replace("\\_", "_")
        title = re.search(r"(?m)^# (.+)$", text).group(1).strip()
        body_start = text.index(m.group(0)) + len(m.group(0))
        desc_end = min([i for i in (text.find("API scopes:", body_start), text.find("\n## ", body_start)) if i >= 0] or [len(text)])
        desc = short(text[body_start:desc_end], 300)
        ops.append({"slug": slug, "method": method, "path": path, "stage": stage, "title": title, "desc": desc,
                    "sections": split_sections(text)})
    return ops


def operation_id(slug):
    if slug in OPERATION_IDS:
        return OPERATION_IDS[slug]
    return pascal(slug.split("/", 2)[2])


def build_paths():
    paths = OrderedDict()
    seen = {}
    for op in build_operations():
        oid = operation_id(op["slug"])
        if oid in seen:
            raise SystemExit("duplicate operationId %s (%s, %s)" % (oid, seen[oid], op["slug"]))
        seen[oid] = op["slug"]

        path = op["path"]
        rename = {"name_arg": "name", "full_name_arg": "full_name"}
        for old, new in rename.items():
            path = path.replace("{" + old + "}", "{" + new + "}")
        path, expanded = expand_name_path(path)
        path = path.replace("{name=", "{")

        params = []
        sec = op["sections"]
        have = {}
        for c in parse_tree(sec.get("Path parameters", [])):
            have[rename.get(c["name"], c["name"])] = c
        for pname in re.findall(r"\{([^}]+)\}", path):
            c = have.get(pname)
            if c is None:
                c = {"name": pname, "type": "string", "meta": ["required"], "desc": ["The " + pname.replace("_", " ") + "."], "enum": [], "children": []}
            params.append(path_params([c], rename)[0])
        params.extend(query_params(parse_tree(sec.get("Query parameters", []))))

        body_children = [c for c in parse_tree(sec.get("Request body", [])) if usable(c, True)]
        if op["method"] in ("POST", "PUT", "PATCH") and "Request body" in sec:
            body = object_schema(body_children, True, 0, 1)
            body.setdefault("properties", OrderedDict())
            params.append({"name": "body", "in": "body", "required": True, "schema": body})

        returns = [c for c in parse_tree(sec.get("Returns", [])) if usable(c, False)]
        resp = {"description": "Success"}
        if returns:
            resp["schema"] = object_schema(returns, False, 0, 1)

        entry = OrderedDict()
        entry["summary"] = op["title"]
        entry["description"] = op["desc"] or op["title"] + "."
        entry["operationId"] = oid
        if oid in IMPORTANT:
            entry["x-ms-visibility"] = "important"
        entry["parameters"] = params
        entry["responses"] = {"200": resp}
        paths.setdefault(path, OrderedDict())[op["method"].lower()] = entry
    return paths


# ── post-processing ──────────────────────────────────────────────────────

def dyn_for(name, op_id, path, siblings):
    n = name
    if n in ("warehouse_id",) or (n == "id" and "/sql/warehouses/" in path):
        kind = "warehouse"
    elif n == "catalog_name":
        kind = "catalog"
    elif n == "schema_name":
        kind = "schema"
    elif n == "job_id":
        kind = "job"
    elif n == "space_id":
        kind = "space"
    else:
        return None
    own = {"warehouse": "ListWarehouses", "catalog": "ListCatalogs", "schema": "ListSchemas", "job": "ListJobs", "space": "ListGenieSpaces"}[kind]
    if op_id == own:
        return None
    d = OrderedDict(DYNAMIC[kind])
    if kind == "schema":
        if "catalog_name" not in siblings:
            return None
        d["parameters"] = {"catalog_name": {"parameter": "catalog_name"}}
    return d


def apply_dynamic_values(paths):
    for path, methods in paths.items():
        for method, op in methods.items():
            op_id = op["operationId"]
            flat = {p["name"] for p in op["parameters"] if p["in"] != "body"}
            for p in op["parameters"]:
                if p["in"] == "body":
                    body_props = p["schema"].get("properties", {})
                    for k, v in body_props.items():
                        if v.get("type") == "string" and "enum" not in v:
                            d = dyn_for(k, op_id, path, flat)
                            if d and "parameters" not in d:
                                v["x-ms-dynamic-values"] = d
                elif p.get("type") in ("string", "integer"):
                    d = dyn_for(p["name"], op_id, path, flat)
                    if d:
                        p["x-ms-dynamic-values"] = d
    # catalog/schema in ExecuteStatement use different property names
    for method, op in paths["/api/2.0/sql/statements"].items():
        if op["operationId"] == "ExecuteStatement":
            props = [p for p in op["parameters"] if p["in"] == "body"][0]["schema"]["properties"]
            if "catalog" in props:
                props["catalog"]["x-ms-dynamic-values"] = OrderedDict(DYNAMIC["catalog"])


def tune_execute_statement(paths):
    op = paths["/api/2.0/sql/statements"]["post"]
    schema = [p for p in op["parameters"] if p["in"] == "body"][0]["schema"]
    schema["required"] = [r for r in schema.get("required", []) if r != "warehouse_id"]
    props = schema["properties"]
    props["warehouse_id"]["description"] = "The SQL warehouse to run on. Leave blank to use the warehouse set for this environment, or the first running warehouse you can use."
    for name, default in (("wait_timeout", "30s"), ("on_wait_timeout", "CONTINUE"), ("disposition", "INLINE"), ("format", "JSON_ARRAY")):
        if name in props:
            props[name]["default"] = default
    op["description"] = ("Runs a SQL statement on a SQL warehouse. Leave SQL warehouse blank to use the environment's warehouse. "
                         "If the statement is still running after the wait time, use Get status, manifest, and result first chunk with the returned statement ID.")


def canon(node):
    def strip(n):
        if isinstance(n, dict):
            return {k: strip(v) for k, v in n.items() if k not in ("description", "x-ms-summary")}
        if isinstance(n, list):
            return [strip(v) for v in n]
        return n
    return json.dumps(strip(node), sort_keys=True, separators=(",", ":"))


def is_obj(n):
    return isinstance(n, dict) and n.get("type") == "object" and "properties" in n


def dedupe(paths):
    groups = defaultdict(list)

    def collect(n, hint):
        if isinstance(n, dict):
            if is_obj(n):
                h = canon(n)
                if len(h) >= DEDUPE_MIN_SIZE:
                    groups[h].append(hint)
            for k, v in n.items():
                if k == "properties":
                    for pk, pv in v.items():
                        collect(pv, pk)
                elif k == "items":
                    collect(v, hint)
                elif isinstance(v, (dict, list)) and k not in ("x-ms-dynamic-values",):
                    collect(v, hint)
        elif isinstance(n, list):
            for v in n:
                collect(v, hint)

    for methods in paths.values():
        for op in methods.values():
            for p in op["parameters"]:
                if p["in"] == "body":
                    collect(p["schema"], op["operationId"] + "Request")
            r = op["responses"]["200"].get("schema")
            if r:
                collect(r, op["operationId"] + "Response")

    dup = {h: hints for h, hints in groups.items() if len(hints) >= 2}
    definitions, names = OrderedDict(), {}

    def name_for(h, hint):
        if h in names:
            return names[h]
        base = pascal(hint) if "_" in hint else hint[:1].upper() + hint[1:]
        base = re.sub(r"[^A-Za-z0-9]", "", base) or "Object"
        n, i = base, 2
        while n in definitions:
            n, i = base + str(i), i + 1
        names[h] = n
        definitions[n] = None
        return n

    def replace(n, hint, top=False):
        if isinstance(n, dict):
            if is_obj(n) and not top:
                h = canon(n)
                if h in dup:
                    nm = name_for(h, hint)
                    if definitions[nm] is None:
                        definitions[nm] = replace(json.loads(json.dumps(n)), hint, top=True)
                    return {"$ref": "#/definitions/" + nm}
            out = OrderedDict()
            for k, v in n.items():
                if k == "properties":
                    out[k] = OrderedDict((pk, replace(pv, pk)) for pk, pv in v.items())
                elif k == "items":
                    out[k] = replace(v, hint)
                else:
                    out[k] = v
            return out
        return n

    for methods in paths.values():
        for op in methods.values():
            for p in op["parameters"]:
                if p["in"] == "body":
                    p["schema"] = replace(p["schema"], op["operationId"] + "Request", top=True)
            r = op["responses"]["200"].get("schema")
            if r:
                op["responses"]["200"]["schema"] = replace(r, op["operationId"] + "Response", top=True)
    return definitions


UC_READ_ONLY = {
    "metastore_id", "full_name", "created_at", "created_by", "updated_at", "updated_by", "schema_id", "catalog_id",
    "browse_only", "effective_predictive_optimization_flag", "securable_type", "securable_kind", "function_id",
    "volume_id", "table_id", "provisioning_info", "catalog_type", "storage_location",
}
UC_NOT_REQUIRED = {"properties", "options"}


def prune_unity_catalog(paths):
    """Unity Catalog request pages repeat the whole entity, including fields the server fills in.
    Drop those, and relax requirements that only apply to the response."""
    def prune(schema, is_update, keep_location):
        if not isinstance(schema, dict) or "properties" not in schema:
            return
        for name in list(schema["properties"]):
            if name in UC_READ_ONLY and not (name == "storage_location" and keep_location):
                del schema["properties"][name]
        if "required" in schema:
            drop = set(UC_NOT_REQUIRED)
            if is_update:
                drop |= {"name", "catalog_name", "schema_name"}
            schema["required"] = [r for r in schema["required"] if r not in drop and r in schema["properties"]]
            if not schema["required"]:
                del schema["required"]
        for v in schema["properties"].values():
            prune(v, is_update, keep_location)
            if isinstance(v, dict) and isinstance(v.get("items"), dict):
                prune(v["items"], is_update, keep_location)

    for path, methods in paths.items():
        if "/unity-catalog/" not in path:
            continue
        for method, op in methods.items():
            if method not in ("post", "patch", "put"):
                continue
            for p in op["parameters"]:
                if p["in"] == "body":
                    prune(p["schema"], method == "patch", "/volumes" in path)


def main():
    existing = json.load(open(EXISTING, encoding="utf-8"), object_pairs_hook=OrderedDict)
    paths = build_paths()
    tune_execute_statement(paths)
    prune_unity_catalog(paths)
    apply_dynamic_values(paths)
    definitions = dedupe(paths)

    out = OrderedDict()
    for k, v in existing.items():
        if k == "paths":
            merged = OrderedDict()
            merged["/mcp"] = v["/mcp"]
            for pk in sorted(paths):
                merged[pk] = paths[pk]
            out[k] = merged
        elif k == "definitions":
            out[k] = definitions
        else:
            out[k] = v
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(out, f, indent=4, ensure_ascii=True)
        f.write("\n")
    ops = sum(len(m) for m in paths.values())
    print("operations: %d, definitions: %d, bytes: %d" % (ops, len(definitions), os.path.getsize(OUT)))


main()
