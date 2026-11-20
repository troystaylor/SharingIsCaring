---
name: power-mcp-cards
description: "Use when the user asks to show, display, or collect something: an approval request, a form, a table or list of records, or job status. They need not say \"card\"."
---

# Power MCP Cards

Shows interactive cards in Copilot Studio agents. The agent gets each card from a skill and passes it to the connector.

Use this skill whenever the user asks to show, display, list, or collect something that one of the cards below fits, such as "show a table of..." or "show a form to...". They do not have to say "card".

## How to show a card

Pick the card below that fits the request, then call the `show_card` tool of the **Power MCP Cards** connector with two arguments:

- `card`: the JSON under that card's **Card:** line, exactly as written, as one string. Do not change, reformat, or add to it. Pass only that card's JSON.
- `data`: a JSON string of the values that card shows, using the `data` fields listed for it.

Build `data` from what the user told you and call the tool straight away; do not look for values elsewhere first. Leave out any optional field the user didn't give you, and never write values, messages, or step names of your own. Required fields are marked.

## After you show it

- The card is shown to the user, so do not repeat what is on it. Reply in one or two short sentences.
- If the tool says the card was not shown, do not try a different card. Tell the user it could not be shown and answer in text.
- To run an action, call the `run_action` tool of the **Power MCP Cards** connector with `action` set to the action's name and `input` set to a JSON string of its arguments.

## Card: approval (Approval card)

Show an approval card with the request details and Approve and Reject buttons. Use when the user must approve, reject, or sign off on something.

`data` fields:
  - `title` (string, required): What is being approved
  - `summary` (string): One or two sentences of context
  - `requester` (string): Who made the request
  - `requestId` (string): Identifier of the request
  - `facts` (list of objects): Key details shown as label and value rows. Each item has:
    - `label` (string): Row label
    - `value` (string): Row value
  - `approveLabel` (string): Text for the approve button. Default: Approve
  - `rejectLabel` (string): Text for the reject button. Default: Reject
  - `requireCommentOnReject` (boolean): Require a comment before the user can reject

Example `data`: `{"requestId":"REQ-1042","title":"Purchase order PO-7731","requester":"Avery Chen","summary":"Renew the annual analytics subscription.","facts":[{"label":"Amount","value":"$12,400"},{"label":"Vendor","value":"Contoso Analytics"},{"label":"Cost center","value":"CC-210"}],"approveLabel":"Approve","rejectLabel":"Reject","requireCommentOnReject":true}`

Card:

```json
{"version":"0.1","name":"approval-card","body":[{"type":"group","children":[{"type":"text","role":"title","value":"${title}"},{"type":"text","role":"caption","value":"Requested by ${requester}","when":"${requester}"}]},{"type":"text","value":"${summary}","when":"${summary}"},{"type":"facts","items":"${facts}"},{"type":"fields","items":[{"name":"comment","label":"Comment","type":"textarea","rows":2,"hideLabel":true,"placeholder":"${requireCommentOnReject ? 'Add a comment (required to reject)' : 'Add a comment (optional)'}"}]}],"actions":[{"id":"approve","label":"${approveLabel || 'Approve'}","style":"primary","do":[{"tool":"submit_decision","optional":true,"as":"record","args":{"requestId":"${requestId}","decision":"approved","comment":"${inputs.comment}"}},{"show":[{"type":"text","role":"title","value":"${title}"},{"type":"message","intent":"success","title":"Approved.","body":"${record.confirmationId ? 'Confirmation ' + record.confirmationId : ''}"},{"type":"text","role":"caption","value":"Comment: ${inputs.comment}","when":"${inputs.comment}"}]},{"message":"I approved ${title} (request ${requestId}).${inputs.comment ? ' Comment: ' + inputs.comment + '.' : ''}${record ? ' The decision is recorded as ' + record.confirmationId + '.' : ' Record it with the submit_decision tool.'}"}],"onError":{"title":"Your decision was not recorded.","body":"Select ${action.label} to try again."}},{"id":"reject","label":"${rejectLabel || 'Reject'}","requires":[{"field":"comment","when":"${requireCommentOnReject}","message":"Add a comment to reject."}],"do":[{"tool":"submit_decision","optional":true,"as":"record","args":{"requestId":"${requestId}","decision":"rejected","comment":"${inputs.comment}"}},{"show":[{"type":"text","role":"title","value":"${title}"},{"type":"message","intent":"success","title":"Rejected.","body":"${record.confirmationId ? 'Confirmation ' + record.confirmationId : ''}"},{"type":"text","role":"caption","value":"Comment: ${inputs.comment}","when":"${inputs.comment}"}]},{"message":"I rejected ${title} (request ${requestId}).${inputs.comment ? ' Comment: ' + inputs.comment + '.' : ''}${record ? ' The decision is recorded as ' + record.confirmationId + '.' : ' Record it with the submit_decision tool.'}"}],"onError":{"title":"Your decision was not recorded.","body":"Select ${action.label} to try again."}}]}
```

- The card's buttons run `submit_decision`. Run it yourself only when the user asks for it in chat, never to look up values for the card.
- If the user answers in chat instead of on the card, record their answer by running `submit_decision`. If their message already contains a confirmation ID, the card recorded it, so just acknowledge it.

## Card: form (Form card)

Show a form card that collects structured input with validation. Use when you need several values from the user at once.

`data` fields:
  - `formId` (string): Identifier of the form
  - `title` (string, required): Form title
  - `description` (string): One sentence about the form
  - `submitLabel` (string): Text for the submit button. Default: Submit
  - `fields` (list of objects, required): The fields to collect. Each item has:
    - `name` (string, required): Key used for this field in the submitted values
    - `label` (string, required): Label shown to the user
    - `type` (string): text, email, number, date, textarea, choice, or checkbox
    - `placeholder` (string): Hint shown in an empty field
    - `defaultValue` (string): Initial value. Use true for a checked checkbox
    - `required` (boolean): Whether the user must fill the field
    - `options` (list of strings): Choices for a choice field

Example `data`: `{"formId":"FRM-1","title":"New support request","description":"Tell us what you need help with.","submitLabel":"Send request","fields":[{"name":"summary","label":"Summary","type":"text","required":true,"placeholder":"One line description"},{"name":"email","label":"Contact email","type":"email","required":true},{"name":"priority","label":"Priority","type":"choice","options":["Low","Normal","High"],"defaultValue":"Normal"},{"name":"details","label":"Details","type":"textarea"},{"name":"notify","label":"Email me updates","type":"checkbox","defaultValue":"true"}]}`

Card:

```json
{"version":"0.1","name":"form-card","submit":"submit","body":[{"type":"group","children":[{"type":"text","role":"title","value":"${title}"},{"type":"text","role":"caption","value":"${description}","when":"${description}"}]},{"type":"fields","items":"${fields}"}],"actions":[{"id":"submit","label":"${submitLabel || 'Submit'}","style":"primary","validate":true,"do":[{"tool":"submit_form","optional":true,"as":"record","args":{"formId":"${formId}","valuesJson":"${json(inputs)}"}},{"show":[{"type":"text","role":"title","value":"${title}"},{"type":"message","intent":"success","title":"Submitted.","body":"${record.confirmationId ? 'Confirmation ' + record.confirmationId : ''}"}]},{"message":"I submitted the form ${title} (${formId}). ${summary()}. ${record ? 'It is recorded as ' + record.confirmationId + '.' : 'Record it with the submit_form tool.'}"}],"onError":{"title":"The form was not submitted.","body":"Check your entries and select ${action.label} to try again."}}]}
```

- The card's buttons run `submit_form`. Run it yourself only when the user asks for it in chat, never to look up values for the card.
- If the user answers in chat instead of on the card, record their answer by running `submit_form`. If their message already contains a confirmation ID, the card recorded it, so just acknowledge it.

## Card: table (Table card)

Show a sortable, filterable table with optional row selection. Use for lists of records the user may search or pick from.

`data` fields:
  - `title` (string): Table title
  - `columns` (list of objects, required): Columns, in order. Each item has:
    - `key` (string, required): Property name in each row
    - `label` (string): Column heading
  - `rows` (list of objects, required): Rows. Each is an object keyed by column key
  - `selectable` (boolean): Let the user select rows
  - `pageSize` (integer): Rows shown in the inline preview. Default: 5

Card:

```json
{"version":"0.1","name":"table-card","modes":["inline","fullscreen"],"body":[{"type":"text","role":"title","value":"${title || 'Results'}"},{"type":"table","id":"t","title":"${title}","columns":"${columns}","rows":"${rows}","selectable":"${selectable}","pageSize":"${pageSize}","onSelect":{"text":"The user has selected ${t.selectedCount} row(s) in the table ${title}.","data":{"selectedRows":"${t.selected}"}}}],"actions":[{"id":"send","label":"Send selection","style":"primary","when":"${selectable}","enabledWhen":"${t.selectedCount > 0}","do":[{"message":"I selected ${t.selectedCount} row(s) from ${title}: ${t.selectedText}.","required":true},{"note":{"intent":"success","title":"Sent to the agent."}}],"onError":{"title":"The selection was not sent.","body":"Select Send selection to try again."}}]}
```

## Card: status (Status card)

Show a status card with progress and steps for a long-running job.

`data` fields:
  - `statusId` (string): Identifier of the job
  - `title` (string, required): Job name
  - `status` (string, required): Job status. One of: queued, running, succeeded, failed, warning
  - `percentComplete` (integer): Progress from 0 to 100
  - `message` (string): One sentence about what is happening
  - `refreshable` (boolean): Show a Refresh button
  - `steps` (list of objects): Steps in order. Each item has:
    - `label` (string, required): Step name
    - `state` (string): pending, running, done, or failed

Example `data`: `{"title":"Nightly data sync","status":"running","percentComplete":50,"statusId":"JOB-88","refreshable":true,"message":"Copying tables to the warehouse.","steps":[{"label":"Export from source","state":"done"},{"label":"Transform","state":"done"},{"label":"Load to warehouse","state":"running"},{"label":"Verify row counts","state":"pending"}]}`

Card:

```json
{"version":"0.1","name":"status-card","body":[{"type":"row","children":[{"type":"text","role":"title","value":"${title}"},{"type":"badge","value":"${status || 'queued'}"}]},{"type":"progress","value":"${percentComplete}","tone":"${status || 'queued'}","caption":"${clamp(percentComplete, 0, 100)}% complete${message ? ' - ' + message : ''}"},{"type":"steps","items":"${steps}","forceState":"${status == 'succeeded' ? 'done' : ''}"}],"actions":[{"id":"refresh","label":"Refresh","when":"${refreshable && statusId && !host.standalone}","do":[{"tool":"refresh_status","requireResult":true,"as":"latest","args":{"statusId":"${statusId}","percentComplete":"${percentComplete || 0}"}},{"update":{"status":"${latest.status}","percentComplete":"${latest.percentComplete}","message":"${latest.message}"}}],"onError":{"title":"The status was not refreshed.","body":"Select Refresh to try again."}}]}
```

- The card's buttons run `refresh_status`. Run it yourself only when the user asks for it in chat, never to look up values for the card.
