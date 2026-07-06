#!/usr/bin/env python3
"""regex_test.py — Test regex patterns against sample strings.

Usage: python3 /opt/tools/regex_test.py "<json-input>" [--vars /path/to/vars.json]

Input JSON: {"pattern": "...", "testStrings": ["..."], "flags": "i"}
Or: pattern as first arg + --vars file for test strings
"""
import sys
import json
import re


def main():
    input_str = sys.argv[1] if len(sys.argv) > 1 else ""
    vars_file = None

    if "--vars" in sys.argv:
        idx = sys.argv.index("--vars")
        if idx + 1 < len(sys.argv):
            vars_file = sys.argv[idx + 1]

    # Parse input
    try:
        data = json.loads(input_str)
        pattern = data.get("pattern", "")
        test_strings = data.get("testStrings", data.get("test_strings", []))
        flags_str = data.get("flags", "")
    except (json.JSONDecodeError, TypeError):
        pattern = input_str
        test_strings = []
        flags_str = ""

    # Load test strings from vars file if needed
    if vars_file:
        try:
            with open(vars_file, "r") as f:
                file_data = json.load(f)
                if "testStrings" in file_data:
                    test_strings = file_data["testStrings"]
                elif "test_strings" in file_data:
                    test_strings = file_data["test_strings"]
                if "pattern" in file_data and not pattern:
                    pattern = file_data["pattern"]
                if "flags" in file_data:
                    flags_str = file_data["flags"]
        except (FileNotFoundError, json.JSONDecodeError) as e:
            print(json.dumps({"error": f"Failed to load vars file: {e}"}))
            sys.exit(1)

    if not pattern:
        print(json.dumps({"error": "No regex pattern provided"}))
        sys.exit(1)

    # Parse flags
    flags = 0
    if "i" in flags_str:
        flags |= re.IGNORECASE
    if "m" in flags_str:
        flags |= re.MULTILINE
    if "s" in flags_str:
        flags |= re.DOTALL

    # Compile pattern
    try:
        compiled = re.compile(pattern, flags)
    except re.error as e:
        print(json.dumps({
            "error": f"Invalid regex: {e}",
            "pattern": pattern,
            "matches": []
        }, indent=2))
        sys.exit(1)

    # Test each string
    matches = []
    for test in test_strings:
        match = compiled.search(test)
        if match:
            groups = {}
            # Named groups
            for name, value in match.groupdict().items():
                groups[name] = value
            # Numbered groups
            for i, g in enumerate(match.groups(), 1):
                if str(i) not in groups.values():
                    groups[f"group{i}"] = g

            matches.append({
                "input": test,
                "matched": True,
                "fullMatch": match.group(0),
                "start": match.start(),
                "end": match.end(),
                "groups": groups
            })
        else:
            matches.append({
                "input": test,
                "matched": False,
                "fullMatch": None,
                "start": -1,
                "end": -1,
                "groups": {}
            })

    # Find all matches in each string
    all_matches_count = sum(len(compiled.findall(s)) for s in test_strings)

    result = {
        "pattern": pattern,
        "flags": flags_str,
        "testCount": len(test_strings),
        "matchCount": sum(1 for m in matches if m["matched"]),
        "totalOccurrences": all_matches_count,
        "matches": matches
    }

    print(json.dumps(result, indent=2))
    sys.exit(0)


if __name__ == "__main__":
    main()
