# Epic FHIR

Read patient demographics, clinical notes and other documents, encounters, and insurance coverage from Epic through the FHIR R4 API. The connector runs as the signed-in user via SMART on FHIR OAuth 2.0 and works two ways:

- **Power Automate and Power Apps** — nine typed actions, one for each supported Epic API
- **Copilot Studio** — an MCP endpoint that exposes the same capabilities as agent tools, with compact results and plain-text clinical notes

The connector is read-only. It never creates or changes data in Epic.

## Supported Epic APIs

| Action | Epic API | Request |
|--------|----------|---------|
| Search patients | Patient.Search (Demographics) (R4) | `GET /Patient` |
| Get patient | Patient.Read (Demographics) (R4) | `GET /Patient/{id}` |
| Search document references | DocumentReference.Search (Clinical Notes) (R4) | `GET /DocumentReference` |
| Get document reference | DocumentReference.Read (Clinical Notes) (R4) | `GET /DocumentReference/{id}` |
| Get binary content | Binary.Read (Clinical Notes) (R4) | `GET /Binary/{id}` |
| Search encounters | Encounter.Search (Patient Chart) (R4) | `GET /Encounter` |
| Get encounter | Encounter.Read (Patient Chart) (R4) | `GET /Encounter/{id}` |
| Search coverages | Coverage.Search (Patient Insurance Information) (R4) | `GET /Coverage` |
| Get coverage | Coverage.Read (Patient Insurance Information) (R4) | `GET /Coverage/{id}` |
| Invoke Epic FHIR MCP | — | `POST /` (MCP endpoint for Copilot Studio) |

Full specifications for each API are on [Epic on FHIR](https://fhir.epic.com/Specifications).

### MCP tools

| Tool | Description |
|------|-------------|
| `search_patients` | Find patients by identifier (such as MRN) or demographics |
| `get_patient` | Patient demographics by FHIR ID |
| `search_document_references` | A patient's clinical notes, filtered by type, date range, status, or encounter |
| `get_document_reference` | Note metadata and Binary content references |
| `get_binary` | Note content, with HTML and RTF converted to plain text |
| `search_encounters` | A patient's encounters, filtered by date range, status, class, or type |
| `get_encounter` | Encounter details by FHIR ID |
| `search_coverages` | A patient's insurance coverages |
| `get_coverage` | Coverage details by FHIR ID |
| `get_next_page` | The next page of a previous search |

## Prerequisites

- An [Epic on FHIR](https://fhir.epic.com/) developer account
- For production use, an Epic organization that activates your app and gives you its FHIR R4 base URL
- A Power Platform environment where you can create custom connectors
- [Power Platform CLI](https://learn.microsoft.com/power-platform/developer/cli/introduction) (`pac`), signed in to your environment

## Step 1: Register your app with Epic

1. Sign in to [Epic on FHIR](https://fhir.epic.com/Developer/Apps) and create an app.
2. Set the application audience to **Clinicians or Administrative Users**. The connector acts on behalf of the user who signs in.
3. Under incoming APIs, select the nine R4 APIs in the [Supported Epic APIs](#supported-epic-apis) table.
4. Configure the app as a **confidential client** that uses a client secret.
5. Enable **refresh tokens** (persistent access). Without them, the connection stops working when the access token expires, and you must reconnect.
6. For the redirect URI, enter `https://global.consent.azure-apim.net/redirect` for now. You add the connector's own redirect URI in Step 4.
7. Save the app and copy its **client ID**. Use the non-production client ID for the Epic sandbox and the production client ID for an Epic organization.
8. Generate or obtain the **client secret** for the environment you're connecting to. For production, work with the Epic organization that activates your app.

## Step 2: Point the connector at your Epic FHIR server

The connector is set up for the Epic sandbox. To connect to a different Epic organization, update the values below. You can find an organization's FHIR R4 base URL in Epic's [endpoint directory](https://open.epic.com/MyApps/Endpoints). Its authorization and token endpoints are listed at `{FHIR base URL}/.well-known/smart-configuration`.

| File | Setting | Sandbox value | Change to |
|------|---------|---------------|-----------|
| `apiDefinition.swagger.json` | `host` | `fhir.epic.com` | The host of your FHIR R4 base URL |
| `apiDefinition.swagger.json` | `basePath` | `/interconnect-fhir-oauth/api/FHIR/R4` | The path of your FHIR R4 base URL |
| `apiProperties.json` | `AuthorizationUrl` | `https://fhir.epic.com/interconnect-fhir-oauth/oauth2/authorize?aud=...` | Your `authorization_endpoint`, followed by `?aud=` and your URL-encoded FHIR R4 base URL |
| `apiProperties.json` | `TokenUrl`, `RefreshUrl` | `https://fhir.epic.com/interconnect-fhir-oauth/oauth2/token` | Your `token_endpoint` |
| `apiProperties.json` | `clientId` | `[YOUR_CLIENT_ID]` | The client ID from Step 1 |

To match the new server, also update the `authorizationUrl` and `tokenUrl` values in `securityDefinitions` in `apiDefinition.swagger.json`.

Epic requires the `aud` parameter on authorization requests, and its value must be the FHIR R4 base URL. For example, if your base URL is `https://fhir.example.org/FHIRProxy/api/FHIR/R4`, the authorization URL is:

```
https://fhir.example.org/FHIRProxy/oauth2/authorize?aud=https%3A%2F%2Ffhir.example.org%2FFHIRProxy%2Fapi%2FFHIR%2FR4
```

The connector takes its FHIR base URL from `host` and `basePath`. You don't need to change `script.csx`.

### Scopes

The connector requests SMART v1 user-level read scopes:

```
offline_access user/Patient.read user/DocumentReference.read user/Binary.read user/Encounter.read user/Coverage.read
```

If you registered your Epic app for SMART v2 scopes, change each `.read` to `.rs` in `apiProperties.json` and in `securityDefinitions` in `apiDefinition.swagger.json`. `offline_access` is required for refresh tokens.

## Step 3: Deploy the connector

From this folder, run:

```powershell
pac connector create `
    --environment <YOUR_ENVIRONMENT_ID> `
    --api-definition-file apiDefinition.swagger.json `
    --api-properties-file apiProperties.json `
    --script-file script.csx
```

The command returns the new connector's ID. Use it in the next step.

## Step 4: Register the connector's redirect URI with Epic

Power Platform generates a redirect URI for each connector when you deploy it. To find it:

```powershell
pac connector download --connector-id <YOUR_CONNECTOR_ID> --outputDirectory ./deployed
```

Open `./deployed/apiProperties.json` and copy `properties.connectionParameters.token.oAuthSettings.redirectUrl`. It starts with `https://global.consent.azure-apim.net/redirect/`, followed by an identifier for this connector and environment.

You can also find it on the connector's **Security** tab in Power Apps or Power Automate.

Add this URI to your Epic app's redirect URIs. Each environment where you deploy the connector has its own URI, so register each one.

## Step 5: Add the client secret and create a connection

1. In [Power Automate](https://make.powerautomate.com), go to **Custom connectors** and edit **Epic FHIR**.
2. On the **Security** tab, enter your client secret and select **Update connector**.
3. Go to **Connections** > **New connection** > **Epic FHIR**, and sign in with your Epic credentials.

For the Epic sandbox, sign in with the sandbox user credentials from Epic's [test data](https://fhir.epic.com/Documentation?docId=testpatients) page. The same page lists test patients you can search for.

## Use in Copilot Studio

1. In your agent, go to **Tools** > **Add a tool** > **Model Context Protocol**, and select **Epic FHIR**.
2. Select the connection you created.
3. Try prompts such as:
   - "Find the patient with MRN `<MRN>`."
   - "Summarize Camila Lopez's most recent progress note."
   - "List this patient's encounters since January 1."
   - "What insurance does this patient have on file?"

The agent finds the patient first, confirms the patient's identity, and then uses that patient's FHIR ID with the other tools. To read a note, it gets the Binary reference from `search_document_references` and passes it to `get_binary`, which returns the note as plain text.

To keep agent context small, MCP results remove each resource's generated HTML narrative and move server warnings into a separate `warnings` list. Note text is capped at 40,000 characters by default; set `max_chars` to change the cap.

## Use in Power Automate

A typical flow to read a patient's latest notes:

1. **Search patients** with the patient's MRN in **Identifier**. Take `entry[].resource.id` from the patient you want.
2. **Search document references** with that ID in **Patient FHIR ID** and a **Created From** date.
3. For each note, read `content[].attachment.url` (for example, `Binary/abc123`). The part after `Binary/` is the Binary ID.
4. **Get binary content** with that ID and **Include Decoded Text** set to **Yes**. **Decoded Text** contains the note's HTML or RTF.

Actions return the Epic FHIR response unchanged, apart from the added **Decoded Text** field.

## How the connector behaves

- **Patient search minimums.** By default, Epic returns patients only when the search includes one of these combinations: an identifier (FHIR ID or MRN); given name, family name, and birth date; or given name, family name, legal sex, and phone or email. Each Epic organization can change these requirements.
- **Patient search can return more than one patient.** Confirm the right patient before you use a result.
- **Date ranges.** **Created From** and **Created To** (document references), and **Date From** and **Date To** (encounters), are sent to Epic as `date=ge{value}` and `date=le{value}`. They accept `YYYY-MM-DD` or an ISO 8601 date-time.
- **Clinical notes.** **Category** defaults to `clinical-note`. To filter by note type, set **Type** to a LOINC code, such as `http://loinc.org|11506-3` (progress note) or `http://loinc.org|18842-5` (discharge summary).
- **Note content.** A note usually has two Binary references, one HTML and one RTF. Prefer HTML.
- **Encounters.** Epic returns inpatient encounters and outpatient encounters that have been checked in. Future appointments aren't returned.
- **Coverages.** Epic returns every coverage that could apply to the patient, not only the coverages billed for a specific visit.
- **Paging.** If a search has more results than fit on one page, the response includes a link with `relation` set to `next`. In Copilot Studio, the agent follows it with `get_next_page`, which only follows links on your configured Epic FHIR server.
- **Security.** Epic applies the signed-in user's security, break-the-glass rules, and audit logging to every request.

## Privacy and telemetry

The connector returns protected health information. Before you use it in production:

- Apply [data loss prevention policies](https://learn.microsoft.com/power-platform/admin/wp-data-loss-prevention) to control which connectors can be used together with Epic FHIR.
- Confirm your use complies with your organization's privacy policies and agreements, and with the Epic organization's app requirements.

Application Insights telemetry is off by default. To turn it on, set `APP_INSIGHTS_ENABLED` to `true` and `APP_INSIGHTS_KEY` to your instrumentation key in `script.csx`. Telemetry records only operation and tool names, status codes, and durations, never patient data. If you extend the logging, keep it that way.

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| Epic shows an error about the redirect URI when you sign in | The connector's redirect URI isn't registered on the Epic app | Complete [Step 4](#step-4-register-the-connectors-redirect-uri-with-epic) |
| Sign-in fails with `invalid_client` | Wrong client ID or secret, or a sandbox client ID used against production (or the reverse) | Use the client ID and secret for the environment you're connecting to |
| A new or edited Epic app fails to authorize | Epic changes may not take effect immediately | Wait, then try the connection again |
| Sign-in fails with an `aud` error | `aud` is missing from the authorization URL or doesn't match the FHIR base URL | URL-encode your FHIR R4 base URL in the authorization URL, as shown in [Step 2](#step-2-point-the-connector-at-your-epic-fhir-server) |
| Every action returns 401 | The access token expired and no refresh token was issued | Enable refresh tokens on the Epic app, keep `offline_access` in the scopes, and reconnect |
| An action returns 403 | The app isn't authorized for that API, or the user lacks security in Epic | Add the API to the Epic app's incoming APIs, or ask the Epic organization to review the user's security |
| Search patients returns no results or a warning | The search doesn't meet the organization's minimum criteria | Add an identifier, or use given name, family name, and birth date |
| Search document references returns other document types | Neither category nor type is set | Set **Category** to `clinical-note`, or set **Type** |
| `get_binary` says the content isn't text | The Binary is a PDF, image, or other binary file | Use **Get binary content** in Power Automate to get the Base64 data |
| Actions call the sandbox instead of your organization | `host` or `basePath` still has the sandbox value | Update both and redeploy |

## Files

| File | Purpose |
|------|---------|
| `apiDefinition.swagger.json` | OpenAPI 2.0 definition with the MCP endpoint and nine Epic FHIR actions |
| `apiProperties.json` | SMART on FHIR OAuth 2.0 settings and script operation list |
| `script.csx` | MCP JSON-RPC handler and request handling for the actions |
