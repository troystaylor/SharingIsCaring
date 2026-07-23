import { LightningElement, api, track } from 'lwc';
import { NavigationMixin } from 'lightning/navigation';
import startConversation from '@salesforce/apex/CopilotStudioController.startConversation';
import sendMessage from '@salesforce/apex/CopilotStudioController.sendMessage';
import endConversation from '@salesforce/apex/CopilotStudioController.endConversation';
import getAuthLoginUrl from '@salesforce/apex/CopilotStudioController.getAuthLoginUrl';
import saveAuthSession from '@salesforce/apex/CopilotStudioController.saveAuthSession';
import getAuthSession from '@salesforce/apex/CopilotStudioController.getAuthSession';
import clearAuthSession from '@salesforce/apex/CopilotStudioController.clearAuthSession';
import { parseAttachments } from './cardRenderer';

export default class CopilotChat extends NavigationMixin(LightningElement) {
    /** When placed on a Record Page, Salesforce injects the record ID. */
    @api recordId;

    /** When placed on a Record Page, Salesforce injects the object API name. */
    @api objectApiName;

    @track messages = [];
    @track suggestedActions = [];
    @track _darkMode = false;

    userInput = '';
    _conversationId = '';
    _userId = '';
    _msgIdCounter = 0;
    _connectionState = 'disconnected';
    _authSessionId = '';
    @track _agentName = 'Copilot';

    isWaitingForBot = false;
    _isSilentAuthInProgress = false;

    // ── Lifecycle ───────────────────────────────────────────────────

    connectedCallback() {
        if (window.matchMedia?.('(prefers-color-scheme: dark)').matches) {
            this._darkMode = true;
        }
        this._applyTheme();

        // Listen for postMessage from the auth popup
        this._boundAuthHandler = this._handleAuthMessage.bind(this);
        window.addEventListener('message', this._boundAuthHandler);

        // Restore session from server-side Platform Cache (immune to Locker/LWS)
        this._restoreSession();
    }

    renderedCallback() {
        // Intercept clicks on Salesforce record links (/lightning/r/...)
        // and navigate using NavigationMixin to stay within the Lightning session
        const container = this.refs.chatContainer;
        if (container && !container._linkHandlerAttached) {
            container.addEventListener('click', (e) => {
                const anchor = e.target.closest('a[href*="/lightning/r/"]');
                if (!anchor) return;
                e.preventDefault();
                e.stopPropagation();
                // Parse /lightning/r/{ObjectApiName}/{recordId}/view
                const match = anchor.getAttribute('href').match(/\/lightning\/r\/([^/]+)\/([^/]+)\/view/);
                if (match) {
                    this[NavigationMixin.Navigate]({
                        type: 'standard__recordPage',
                        attributes: {
                            objectApiName: match[1],
                            recordId: match[2],
                            actionName: 'view'
                        }
                    });
                }
            });
            container._linkHandlerAttached = true;
        }
    }

    disconnectedCallback() {
        window.removeEventListener('message', this._boundAuthHandler);
        if (this._conversationId) {
            endConversation({ conversationId: this._conversationId }).catch(() => {});
        }
    }

    // ── Auth Flow ───────────────────────────────────────────────────

    /**
     * Attempt to re-authenticate silently using the user's existing
     * Entra ID session cookie (prompt=none). Opens a tiny popup that
     * completes the OAuth flow without user interaction if possible.
     * Rejects if the user has no active Entra session.
     */
    async _trySilentAuth() {
        this._isSilentAuthInProgress = true;
        try {
            const loginUrl = await getAuthLoginUrl();
            const silentUrl = loginUrl + '?prompt=none';

            return await new Promise((resolve, reject) => {
                let settled = false;
                const popup = window.open(
                    silentUrl,
                    'CopilotSilentAuth',
                    'width=1,height=1,top=0,left=-1000'
                );
                if (!popup) {
                    reject(new Error('Popup blocked'));
                    return;
                }

                const cleanup = () => {
                    if (settled) return;
                    settled = true;
                    clearTimeout(timer);
                    window.removeEventListener('message', onMsg);
                    try { popup.close(); } catch (e) { /* ignore */ }
                };

                const timer = setTimeout(() => {
                    cleanup();
                    reject(new Error('Silent auth timed out'));
                }, 8000);

                const onMsg = (event) => {
                    const data = event.data;
                    if (!data || typeof data !== 'object') return;
                    if (data.type === 'copilot-auth-success' && data.sessionId) {
                        cleanup();
                        resolve(data.sessionId);
                    } else if (data.type === 'copilot-auth-error') {
                        cleanup();
                        reject(new Error(data.error || 'Silent auth failed'));
                    }
                };

                window.addEventListener('message', onMsg);
            });
        } finally {
            this._isSilentAuthInProgress = false;
        }
    }

    async _startAuthFlow() {
        this._connectionState = 'connecting';
        this._addMessage('system', 'Signing in to assistant...');

        try {
            const loginUrl = await getAuthLoginUrl();
            this._authPopup = window.open(
                loginUrl,
                'CopilotAuth',
                'width=500,height=700,menubar=no,toolbar=no'
            );
            if (!this._authPopup) {
                this._addMessage('system', 'Popup blocked. Please allow popups for this site and try again.');
                this._connectionState = 'disconnected';
            }
        } catch (error) {
            this._connectionState = 'disconnected';
            this._addMessage('system', 'Failed to start sign-in. Please try again.');
            console.error('Auth flow error', error);
        }
    }

    async _restoreSession() {
        try {
            const savedSession = await getAuthSession();
            if (savedSession) {
                this._authSessionId = savedSession;
                this._initConversation();
            } else {
                this._startAuthFlow();
            }
        } catch (err) {
            console.warn('Failed to restore session from server', err);
            this._startAuthFlow();
        }
    }

    _handleAuthMessage(event) {
        // During silent re-auth, a dedicated handler processes the response
        if (this._isSilentAuthInProgress) return;

        const data = event.data;
        if (!data || typeof data !== 'object') return;

        if (data.type === 'copilot-auth-success' && data.sessionId) {
            this._authSessionId = data.sessionId;
            saveAuthSession({ sessionId: data.sessionId }).catch((e) =>
                console.warn('Failed to save auth session', e)
            );
            // Remove 'signing in' message
            this.messages = this.messages.filter(m => m.sender !== 'system');
            this._initConversation();
        } else if (data.type === 'copilot-auth-error') {
            this._connectionState = 'disconnected';
            clearAuthSession().catch(() => {});
            this._addMessage('system', 'Sign-in failed: ' + (data.error || 'Unknown error'));
        }
    }

    // ── Getters ─────────────────────────────────────────────────────

    get cardTitle() {
        return this._agentName || 'Copilot';
    }

    get isConnected() {
        return this._connectionState === 'connected';
    }

    get isStartingConversation() {
        return this._connectionState === 'connecting';
    }

    get isDisconnected() {
        return this._connectionState === 'disconnected';
    }

    get isInputDisabled() {
        return !this.isConnected || this.isWaitingForBot;
    }

    get isSendDisabled() {
        return !this.userInput || this.isInputDisabled;
    }

    get hasSuggestedActions() {
        return this.suggestedActions.length > 0;
    }

    get themeTooltip() {
        return this._darkMode ? 'Switch to light mode' : 'Switch to dark mode';
    }

    get themeSvgHref() {
        return this._darkMode ? '#ic-sun' : '#ic-moon';
    }

    get statusDot() {
        const state = this._connectionState;
        return `status-dot status-dot--${state}`;
    }

    // ── Event Handlers ──────────────────────────────────────────────

    handleInputChange(event) {
        this.userInput = event.target.value;
    }


    handleKeyUp(event) {
        if (event.key === 'Enter') {
            this.handleSend();
        }
    }

    async handleSend() {
        const text = this.userInput?.trim();
        if (!text || this.isWaitingForBot) return;

        this.userInput = '';
        this.suggestedActions = [];
        this._addMessage('user', text);
        await this._sendAndReceive(text);
    }

    handleSuggestedAction(event) {
        const value = event.currentTarget.dataset.value;
        if (!value || this.isWaitingForBot) return;

        this.suggestedActions = [];
        this._addMessage('user', value);
        this._sendAndReceive(value);
    }

    async handleNewConversation() {
        if (this._conversationId) {
            endConversation({ conversationId: this._conversationId }).catch(() => {});
        }
        this.messages = [];
        this.suggestedActions = [];
        this._msgIdCounter = 0;
        this.isWaitingForBot = false;

        if (this._authSessionId) {
            await this._initConversation();
        } else {
            this._startAuthFlow();
        }
    }

    handleToggleTheme() {
        this._darkMode = !this._darkMode;
        this._applyTheme();
    }

    handleCardAction(event) {
        const actionType = event.currentTarget.dataset.actionType;
        const actionValue = event.currentTarget.dataset.actionValue;
        if (!actionValue) return;

        if (actionType === 'openUrl') {
            window.open(actionValue, '_blank', 'noopener,noreferrer');
        } else {
            this.suggestedActions = [];
            this._addMessage('user', actionValue);
            this._sendAndReceive(actionValue);
        }
    }

    // ── Core Send + Receive (synchronous via middleware) ─────────────

    async _sendAndReceive(text) {
        this.isWaitingForBot = true;

        try {
            let channelData = null;
            if (this.recordId) {
                channelData = JSON.stringify({
                    salesforceRecordId: this.recordId,
                    salesforceObjectType: this.objectApiName || ''
                });
            }

            const result = await sendMessage({
                conversationId: this._conversationId,
                userMessage: text,
                channelData: channelData,
                authSessionId: this._authSessionId
            });

            this._processActivities(result.activities || []);
        } catch (error) {
            this._addMessage('system', 'Error sending message. Please try again.');
            console.error('sendMessage error', error);
        } finally {
            this.isWaitingForBot = false;
        }
    }

    // ── Conversation Initialization ─────────────────────────────────

    async _initConversation() {
        this._connectionState = 'connecting';

        try {
            const result = await startConversation({
                authSessionId: this._authSessionId
            });
            this._conversationId = result.conversationId;
            this._userId = result.userId || '';
            this._agentName = result.agentName || 'Copilot';
            this._connectionState = 'connected';

            // Process greeting activities
            this._processActivities(result.activities || []);
        } catch (error) {
            // Session may have expired server-side; try silent re-auth first
            console.warn('startConversation failed, attempting silent re-auth', error);
            try {
                const newSessionId = await this._trySilentAuth();
                this._authSessionId = newSessionId;
                saveAuthSession({ sessionId: newSessionId }).catch(() => {});

                const result = await startConversation({
                    authSessionId: this._authSessionId
                });
                this._conversationId = result.conversationId;
                this._userId = result.userId || '';
                this._agentName = result.agentName || 'Copilot';
                this._connectionState = 'connected';
                this._processActivities(result.activities || []);
            } catch (silentErr) {
                // Silent re-auth failed; fall back to interactive popup
                this._authSessionId = '';
                clearAuthSession().catch(() => {});
                this._connectionState = 'disconnected';
                this._addMessage('system', 'Session expired. Signing in again...');
                console.error('Silent re-auth failed, starting interactive login', silentErr);
                this._startAuthFlow();
            }
        }
    }

    // ── Activity Processing ─────────────────────────────────────────

    _processActivities(activities) {
        const botMessages = activities.filter(
            (a) => a.type === 'message' && a.from && a.from.id !== this._userId
        );

        botMessages.forEach((a) => {
            let text = (a.text || '').trim();
            const isRich = /<[a-z][\s\S]*>/i.test(text);
            if (isRich) {
                // Strip <br/> adjacent to block elements — they double-space the output
                text = text
                    .replace(/(<br\s*\/?>\s*)+(<(?:p|h[1-6]|ul|ol|li|hr|div)[ >/])/gi, '$2')
                    .replace(/(<\/(?:p|h[1-6]|ul|ol|li|div)>)\s*(<br\s*\/?>\s*)+/gi, '$1')
                    .replace(/^(<br\s*\/?>\s*)+|(<br\s*\/?>\s*)+$/gi, '');
            }
            const cards = parseAttachments(a.attachments);
            this._addMessage('bot', text, isRich, cards);

            if (a.suggestedActions?.actions) {
                this.suggestedActions = a.suggestedActions.actions.map((sa) => ({
                    title: sa.title || sa.value,
                    value: sa.value || sa.title
                }));
            }
        });
    }

    // ── Dark Mode ───────────────────────────────────────────────────

    _applyTheme() {
        // eslint-disable-next-line @lwc/lwc/no-async-operation
        setTimeout(() => {
            const host = this.template.host;
            if (this._darkMode) {
                host?.setAttribute('data-theme', 'dark');
            } else {
                host?.removeAttribute('data-theme');
            }
        }, 0);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    _addMessage(sender, text, isRichText = false, cards = []) {
        this._msgIdCounter++;
        const now = new Date();
        const timestamp = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

        this.messages = [
            ...this.messages,
            {
                id: String(this._msgIdCounter),
                text,
                hasText: !!text,
                sender,
                isRichText,
                cards,
                hasCards: cards.length > 0,
                timestamp,
                cssClass: `message message-${sender}`,
                bubbleClass: `message-bubble bubble-${sender}`
            }
        ];

        this._scrollToBottom();
    }

    _scrollToBottom() {
        // eslint-disable-next-line @lwc/lwc/no-async-operation
        setTimeout(() => {
            const container = this.refs.chatContainer;
            if (container) {
                container.scrollTop = container.scrollHeight;
            }
        }, 50);
    }
}
