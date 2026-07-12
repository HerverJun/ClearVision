export const aiPanelLifecycleMixin = {
    _setOwnedTimeout(callback, delay = 0) {
        this._ownedTimeouts = this._ownedTimeouts instanceof Set ? this._ownedTimeouts : new Set();
        let id = null;
        let completed = false;
        id = window.setTimeout?.(() => {
            completed = true;
            this._ownedTimeouts?.delete?.(id);
            if (!this._disposed) callback?.();
        }, delay);
        if (!completed && id !== undefined && id !== null) this._ownedTimeouts.add(id);
        return id;
    },

    _requestOwnedAnimationFrame(callback) {
        this._ownedAnimationFrames = this._ownedAnimationFrames instanceof Set ? this._ownedAnimationFrames : new Set();
        const schedule = window.requestAnimationFrame || (fn => window.setTimeout(fn, 0));
        let id = null;
        let completed = false;
        id = schedule(() => {
            completed = true;
            this._ownedAnimationFrames?.delete?.(id);
            if (!this._disposed) callback?.();
        });
        if (!completed && id !== undefined && id !== null) this._ownedAnimationFrames.add(id);
        return id;
    },

    dispose() {
        if (this._disposed) return;
        this._disposed = true;
        this._lifecycleEpoch = Number(this._lifecycleEpoch || 0) + 1;
        this.sessionNavigationEpoch = Number(this.sessionNavigationEpoch || 0) + 1;
        if (this.pendingSessionLoad?.timeoutId) {
            window.clearTimeout?.(this.pendingSessionLoad.timeoutId);
        }
        this.pendingSessionLoad = null;
        this._ownedTimeouts?.forEach?.(id => window.clearTimeout?.(id));
        this._ownedTimeouts?.clear?.();
        this._ownedAnimationFrames?.forEach?.(id => {
            window.cancelAnimationFrame?.(id);
            window.clearTimeout?.(id);
        });
        this._ownedAnimationFrames?.clear?.();
        this._closeApplyPreview?.({ restoreFocus: false, setReady: false });
        this._disposeAccessibility?.();
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
        this.operatorMetadataLoading?.clear?.();
        this.cameraBindingsLoadingPromise = null;
        if (globalThis.__clearVisionFlushAiPanelWorkspace === this._workspaceFlushHandler) {
            delete globalThis.__clearVisionFlushAiPanelWorkspace;
        }
        this._workspaceFlushHandler = null;
        this._initialized = false;
        if (this.container) this.container.innerHTML = '';
    },

    destroy() {
        this.dispose();
    }
};
