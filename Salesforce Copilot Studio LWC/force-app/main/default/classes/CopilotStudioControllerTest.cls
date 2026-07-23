/**
 * Tests for CopilotStudioController.
 * Uses HttpCalloutMock to simulate agent middleware responses.
 */
@isTest
private class CopilotStudioControllerTest {

    // ── Mock: returns a successful start-conversation response ──────

    private class StartConversationMock implements HttpCalloutMock {
        public HttpResponse respond(HttpRequest req) {
            HttpResponse res = new HttpResponse();
            res.setStatusCode(200);
            res.setHeader('Content-Type', 'application/json');
            res.setBody(
                '{"conversationId":"conv_123_abc",' +
                '"activities":[{"type":"message","text":"Hello!","from":{"id":"bot","name":"TestAgent"}}],' +
                '"agentName":"Test Agent"}'
            );
            return res;
        }
    }

    // ── Mock: returns a successful send-message response ────────────

    private class SendMessageMock implements HttpCalloutMock {
        public HttpResponse respond(HttpRequest req) {
            HttpResponse res = new HttpResponse();
            res.setStatusCode(200);
            res.setHeader('Content-Type', 'application/json');
            res.setBody(
                '{"activities":[{"type":"message","text":"Here is your answer.","from":{"id":"bot","name":"TestAgent"}}]}'
            );
            return res;
        }
    }

    // ── Mock: returns a successful end-conversation response ────────

    private class EndConversationMock implements HttpCalloutMock {
        public HttpResponse respond(HttpRequest req) {
            HttpResponse res = new HttpResponse();
            res.setStatusCode(200);
            res.setHeader('Content-Type', 'application/json');
            res.setBody('{"ended":true,"conversationId":"conv_123_abc"}');
            return res;
        }
    }

    // ── Mock: simulates an HTTP error ───────────────────────────────

    private class ErrorMock implements HttpCalloutMock {
        public HttpResponse respond(HttpRequest req) {
            HttpResponse res = new HttpResponse();
            res.setStatusCode(500);
            res.setBody('Internal Server Error');
            return res;
        }
    }

    // ── Tests ───────────────────────────────────────────────────────

    @isTest
    static void testGetAuthLoginUrl() {
        Test.startTest();
        String url = CopilotStudioController.getAuthLoginUrl();
        Test.stopTest();

        System.assertNotEquals(null, url, 'Login URL should not be null');
        System.assert(url.contains('/api/auth/login'), 'URL should point to auth login endpoint');
    }

    @isTest
    static void testStartConversation() {
        Test.setMock(HttpCalloutMock.class, new StartConversationMock());

        Test.startTest();
        Map<String, Object> result = CopilotStudioController.startConversation('test-session-id');
        Test.stopTest();

        System.assertEquals('conv_123_abc', (String) result.get('conversationId'), 'conversationId should match mock');
        System.assertEquals('Test Agent', (String) result.get('agentName'), 'agentName should match mock');
        System.assertEquals(UserInfo.getUserId(), (String) result.get('userId'), 'userId should be current user');
        System.assertNotEquals(null, result.get('activities'), 'activities should be present');
    }

    @isTest
    static void testStartConversationError() {
        Test.setMock(HttpCalloutMock.class, new ErrorMock());

        Test.startTest();
        try {
            CopilotStudioController.startConversation('test-session-id');
            System.assert(false, 'Should have thrown AuraHandledException');
        } catch (AuraHandledException e) {
            System.assert(e.getMessage().contains('500'), 'Error should contain status code');
        }
        Test.stopTest();
    }

    @isTest
    static void testSendMessage() {
        Test.setMock(HttpCalloutMock.class, new SendMessageMock());

        Test.startTest();
        Map<String, Object> result = CopilotStudioController.sendMessage(
            'conv_123_abc',
            'What is my order status?',
            null,
            'test-session-id'
        );
        Test.stopTest();

        System.assertNotEquals(null, result.get('activities'), 'activities should be present');
    }

    @isTest
    static void testSendMessageWithChannelData() {
        Test.setMock(HttpCalloutMock.class, new SendMessageMock());

        String channelData = '{"salesforceRecordId":"001000000000001","salesforceObjectType":"Account"}';

        Test.startTest();
        Map<String, Object> result = CopilotStudioController.sendMessage(
            'conv_123_abc',
            'Help with this account',
            channelData,
            'test-session-id'
        );
        Test.stopTest();

        System.assertNotEquals(null, result.get('activities'), 'activities should be present');
    }

    @isTest
    static void testSendMessageError() {
        Test.setMock(HttpCalloutMock.class, new ErrorMock());

        Test.startTest();
        try {
            CopilotStudioController.sendMessage('conv_123_abc', 'Hello', null, 'test-session-id');
            System.assert(false, 'Should have thrown AuraHandledException');
        } catch (AuraHandledException e) {
            System.assert(e.getMessage().contains('Failed'), 'Error should indicate failure');
        }
        Test.stopTest();
    }

    @isTest
    static void testEndConversation() {
        Test.setMock(HttpCalloutMock.class, new EndConversationMock());

        Test.startTest();
        Map<String, Object> result = CopilotStudioController.endConversation('conv_123_abc');
        Test.stopTest();

        System.assertEquals(true, result.get('ended'), 'ended should be true');
    }

    @isTest
    static void testEndConversationNullId() {
        // Should not make a callout when conversationId is blank
        Test.startTest();
        Map<String, Object> result = CopilotStudioController.endConversation('');
        Test.stopTest();

        System.assertEquals(true, result.get('ended'), 'ended should be true even with blank ID');
    }
}
