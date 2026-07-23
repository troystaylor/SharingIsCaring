/**
 * Server-side proxy for the Copilot Studio Agent Middleware.
 * All communication with the M365 Agents SDK goes through the Azure Function
 * middleware — Apex simply relays requests and responses.
 *
 * Delegated auth flow: the LWC opens a browser popup to the middleware's
 * /api/auth/login endpoint. After the user signs in with Entra ID, the
 * middleware returns a session token. That session token is passed in all
 * subsequent API calls via the X-Auth-Session header.
 *
 * Configuration: Custom Metadata Type CopilotStudio_Settings__mdt → Default
 *   Token_Endpoint__c = middleware base URL (e.g. https://YOUR-FUNCTION-APP.azurewebsites.net)
 *   Function_Key__c   = Azure Function host key for /api/conversations endpoints
 */
public with sharing class CopilotStudioController {

    private static final String AGENT_ENDPOINT;
    private static final String FUNCTION_KEY;

    static {
        CopilotStudio_Settings__mdt settings = CopilotStudio_Settings__mdt.getInstance('Default');
        if (settings != null && String.isNotBlank(settings.Token_Endpoint__c)) {
            String raw = settings.Token_Endpoint__c;
            AGENT_ENDPOINT = raw.endsWith('/') ? raw.removeEnd('/') : raw;
        } else {
            AGENT_ENDPOINT = '';
        }
        if (settings != null && String.isNotBlank(settings.Function_Key__c)) {
            FUNCTION_KEY = settings.Function_Key__c;
        } else {
            FUNCTION_KEY = '';
        }
    }

    // ── Get Auth Login URL ──────────────────────────────────────────────

    /**
     * Returns the middleware OAuth login URL. The LWC opens this in a
     * popup so the user can sign in with their Entra ID account.
     */
    @AuraEnabled
    public static String getAuthLoginUrl() {
        validateEndpoint();
        // The /api/auth/login endpoint is anonymous (no function key needed)
        return AGENT_ENDPOINT + '/api/auth/login';
    }

    // ── Session Storage ─────────────────────────────────────────────────

    /**
     * Store the middleware auth session ID server-side.
     * Uses Platform Cache session partition "lwc" — tied to the current
     * user's Salesforce session, survives page reloads, immune to Locker/LWS.
     */
    private static final String CACHE_KEY = 'local.lwc.CopilotAuthSession';

    @AuraEnabled
    public static void saveAuthSession(String sessionId) {
        Cache.Session.put(CACHE_KEY, sessionId, 28800); // 8 hours TTL
    }

    @AuraEnabled
    public static String getAuthSession() {
        return (String) Cache.Session.get(CACHE_KEY);
    }

    @AuraEnabled
    public static void clearAuthSession() {
        Cache.Session.remove(CACHE_KEY);
    }

    // ── Start Conversation ──────────────────────────────────────────────

    @AuraEnabled
    public static Map<String, Object> startConversation(String authSessionId) {
        validateEndpoint();

        HttpRequest req = new HttpRequest();
        req.setEndpoint(AGENT_ENDPOINT + '/api/conversations?code=' + EncodingUtil.urlEncode(FUNCTION_KEY, 'UTF-8'));
        req.setMethod('POST');
        req.setHeader('Content-Type', 'application/json');
        req.setHeader('X-Auth-Session', authSessionId);
        req.setBody('{}');
        req.setTimeout(30000);

        HttpResponse res = new Http().send(req);

        if (res.getStatusCode() < 200 || res.getStatusCode() >= 300) {
            throw new AuraHandledException(
                'Failed to start conversation. HTTP ' + res.getStatusCode()
            );
        }

        Map<String, Object> result = (Map<String, Object>) JSON.deserializeUntyped(res.getBody());
        result.put('userId', UserInfo.getUserId());
        return result;
    }

    // ── Send Message ────────────────────────────────────────────────────

    @AuraEnabled
    public static Map<String, Object> sendMessage(
        String conversationId,
        String userMessage,
        String channelData,
        String authSessionId
    ) {
        validateEndpoint();

        String endpoint = AGENT_ENDPOINT + '/api/conversations/'
            + EncodingUtil.urlEncode(conversationId, 'UTF-8')
            + '/activities?code=' + EncodingUtil.urlEncode(FUNCTION_KEY, 'UTF-8');

        Map<String, Object> body = new Map<String, Object>{
            'text' => userMessage
        };

        if (String.isNotBlank(channelData)) {
            body.put('channelData', JSON.deserializeUntyped(channelData));
        }

        HttpRequest req = new HttpRequest();
        req.setEndpoint(endpoint);
        req.setMethod('POST');
        req.setHeader('Content-Type', 'application/json');
        req.setHeader('X-Auth-Session', authSessionId);
        req.setBody(JSON.serialize(body));
        req.setTimeout(120000);

        HttpResponse res = new Http().send(req);

        if (res.getStatusCode() < 200 || res.getStatusCode() >= 300) {
            throw new AuraHandledException(
                'Failed to send message. HTTP ' + res.getStatusCode()
            );
        }

        return (Map<String, Object>) JSON.deserializeUntyped(res.getBody());
    }

    // ── End Conversation ────────────────────────────────────────────────

    @AuraEnabled
    public static Map<String, Object> endConversation(String conversationId) {
        if (String.isBlank(AGENT_ENDPOINT) || String.isBlank(conversationId)) {
            return new Map<String, Object>{ 'ended' => true };
        }

        String endpoint = AGENT_ENDPOINT + '/api/conversations/'
            + EncodingUtil.urlEncode(conversationId, 'UTF-8')
            + '?code=' + EncodingUtil.urlEncode(FUNCTION_KEY, 'UTF-8');

        HttpRequest req = new HttpRequest();
        req.setEndpoint(endpoint);
        req.setMethod('DELETE');
        req.setTimeout(15000);

        try {
            new Http().send(req);
        } catch (Exception e) {
            System.debug(LoggingLevel.WARN, 'endConversation failed: ' + e.getMessage());
        }

        return new Map<String, Object>{ 'ended' => true };
    }

    // ── Private Helpers ─────────────────────────────────────────────────

    private static void validateEndpoint() {
        if (String.isBlank(AGENT_ENDPOINT)) {
            throw new AuraHandledException(
                'Agent middleware endpoint is not configured. ' +
                'Set it in Custom Metadata Type: CopilotStudio_Settings__mdt → Default.'
            );
        }
    }
}
