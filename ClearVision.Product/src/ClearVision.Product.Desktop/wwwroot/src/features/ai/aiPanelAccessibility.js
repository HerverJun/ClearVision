const FOCUSABLE_SELECTOR = [
    'button:not([disabled])',
    '[href]',
    'input:not([disabled])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    '[tabindex]:not([tabindex="-1"])'
].join(',');

function focusKey(element) {
    if (!element) return '';
    if (element.id) return `#${typeof CSS !== 'undefined' && CSS.escape ? CSS.escape(element.id) : element.id}`;
    const hook = element.getAttribute?.('data-ai-focus-key') || element.getAttribute?.('data-ai-action');
    return hook ? `[data-ai-focus-key="${hook}"], [data-ai-action="${hook}"]` : '';
}

function activateTab(panel, tab) {
    if (!tab) return;
    tab.click?.();
    tab.focus?.({ preventScroll: true });
    panel._lastAiFocusSelector = focusKey(tab);
}

export const aiPanelAccessibilityMixin = {
    _setupAccessibility() {
        if (!this.container || this._accessibilityInitialized) return;
        this._accessibilityInitialized = true;

        const input = this.container.querySelector?.('#ai-input');
        input?.setAttribute?.('aria-label', '视觉任务需求');
        input?.setAttribute?.('aria-describedby', 'ai-input-help');
        const historySearch = this.container.querySelector?.('#ai-history-search');
        historySearch?.setAttribute?.('aria-label', '搜索历史会话');

        this._accessibilityFocusHandler = event => {
            const selector = focusKey(event.target);
            if (selector) this._lastAiFocusSelector = selector;
        };
        this._accessibilityKeyHandler = event => {
            const tab = event.target?.closest?.('[role="tab"]');
            if (!tab || !this.container.contains?.(tab)) return;
            const tablist = tab.closest?.('[role="tablist"]');
            if (!tablist) return;
            const tabs = Array.from(tablist.querySelectorAll?.('[role="tab"]:not([disabled])') || []);
            const index = tabs.indexOf(tab);
            if (index < 0) return;
            let next = -1;
            if (event.key === 'ArrowRight' || event.key === 'ArrowDown') next = (index + 1) % tabs.length;
            if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') next = (index - 1 + tabs.length) % tabs.length;
            if (event.key === 'Home') next = 0;
            if (event.key === 'End') next = tabs.length - 1;
            if (next < 0) return;
            event.preventDefault?.();
            activateTab(this, tabs[next]);
        };
        this.container.addEventListener?.('focusin', this._accessibilityFocusHandler);
        this.container.addEventListener?.('keydown', this._accessibilityKeyHandler);

        if (typeof MutationObserver === 'function') {
            this._accessibilityObserver = new MutationObserver(() => this._restoreAccessibilityFocus());
            this._accessibilityObserver.observe(this.container, { childList: true, subtree: true });
        }
        this._syncAccessibilitySemantics();
    },

    _syncAccessibilitySemantics() {
        const shellTabs = this.container?.querySelector?.('[data-ai-hook="compact-tabs"]');
        shellTabs?.setAttribute?.('role', 'tablist');
        const paneIds = { workbench: 'ai-result-pane', conversation: 'ai-conversation-pane' };
        this.container?.querySelector?.('[data-ai-hook="conversation-pane"]')?.setAttribute?.('id', paneIds.conversation);
        this.container?.querySelectorAll?.('[data-ai-shell-pane]')?.forEach(tab => {
            const pane = tab.dataset.aiShellPane === 'conversation' ? 'conversation' : 'workbench';
            tab.setAttribute('role', 'tab');
            tab.setAttribute('aria-controls', paneIds[pane]);
            tab.tabIndex = tab.getAttribute('aria-selected') === 'true' ? 0 : -1;
        });
        this.container?.querySelectorAll?.('[data-ai-chat-pane], [data-ai-workbench-pane]')?.forEach(pane => {
            pane.setAttribute('role', 'tabpanel');
            pane.tabIndex = 0;
        });
    },

    _restoreAccessibilityFocus() {
        if (this._disposed || !this._lastAiFocusSelector || !this.container?.isConnected) return;
        const active = document?.activeElement;
        if (active && active !== document.body && active !== this.container && active.isConnected) return;
        const target = this.container.querySelector?.(this._lastAiFocusSelector);
        target?.focus?.({ preventScroll: true });
    },

    _announceAccessibilityStatus(message, tone = 'polite') {
        const region = this.container?.querySelector?.('#ai-accessibility-status');
        const text = String(message || '').trim();
        if (!region || !text || text === this._lastAccessibilityAnnouncement) return;
        this._lastAccessibilityAnnouncement = text;
        region.setAttribute('aria-live', tone === 'assertive' ? 'assertive' : 'polite');
        region.textContent = text;
    },

    _disposeAccessibility() {
        this._accessibilityObserver?.disconnect?.();
        this._accessibilityObserver = null;
        if (this._accessibilityFocusHandler) {
            this.container?.removeEventListener?.('focusin', this._accessibilityFocusHandler);
            this._accessibilityFocusHandler = null;
        }
        if (this._accessibilityKeyHandler) {
            this.container?.removeEventListener?.('keydown', this._accessibilityKeyHandler);
            this._accessibilityKeyHandler = null;
        }
        this._accessibilityInitialized = false;
    }
};

export { FOCUSABLE_SELECTOR };
