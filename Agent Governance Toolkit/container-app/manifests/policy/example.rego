package example

# Default lifecycle policy: allow everything.
# Replace this with real rules that inspect input.policy_target,
# input.snapshot, and (for tool intervention points) input.tool to
# implement allow / deny / warn / escalate / transform decisions.
default verdict := {
    "decision": "allow",
}
