export const aiPanelLifecycleMixin = {
    dispose() {
        this._messageUnsubscribes.forEach(unsubscribe => {
            try {
                unsubscribe?.();
            } catch {
                // Best-effort cleanup.
            }
        });
        this._messageUnsubscribes = [];
        this.unsubscribeStructureState?.();
        this.unsubscribeStructureState = null;
        this._closeAllAgentTransports?.();
        this._resetPlanReadinessPreviewState?.({ abort: true });
        this.activePlanReadinessPreviewController?.abort?.();
        this.activePlanReadinessPreviewController = null;
        if (this.publicLiveStatusTimer) {
            window.clearTimeout?.(this.publicLiveStatusTimer);
            this.publicLiveStatusTimer = null;
        }
        if (this.pendingParameterHighlightTimer) {
            window.clearTimeout?.(this.pendingParameterHighlightTimer);
            this.pendingParameterHighlightTimer = null;
        }
        if (this._scrollStateRaf) {
            window.cancelAnimationFrame?.(this._scrollStateRaf);
            this._scrollStateRaf = 0;
        }
        if (this._chatContainer && this._chatScrollHandler) {
            this._chatContainer.removeEventListener?.('scroll', this._chatScrollHandler);
        }
        this._chatContainer = null;
        this._chatScrollHandler = null;
        if (this._composerResizeHandler) {
            window.removeEventListener?.('resize', this._composerResizeHandler);
            this._composerResizeHandler = null;
        }
        this._inputResizeObserver?.disconnect?.();
        this._inputResizeObserver = null;
        if (globalThis.__clearVisionFlushAiPanelWorkspace) {
            delete globalThis.__clearVisionFlushAiPanelWorkspace;
        }
        this.container.innerHTML = '';
    },

    destroy() {
        this.dispose();
    }
};
