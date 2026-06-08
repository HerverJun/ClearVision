export const aiPanelRequirementBriefMixin = {
    _normalizeRequirementMode(mode) {
        return String(mode || '').trim().toLowerCase() === 'draft' ? 'draft' : 'strict';
    },

    _loadRequirementMode() {
        try {
            return this._normalizeRequirementMode(localStorage.getItem('cv_ai_requirement_mode'));
        } catch {
            return 'strict';
        }
    },

    _saveRequirementMode(mode) {
        try {
            localStorage.setItem('cv_ai_requirement_mode', this._normalizeRequirementMode(mode));
        } catch {
            // ignore localStorage failures
        }
    },

    _setRequirementMode(mode, { silent = false } = {}) {
        const normalized = this._normalizeRequirementMode(mode);
        if (normalized === this.requirementMode) {
            this._updateRequirementModeUI();
            return;
        }

        this.requirementMode = normalized;
        this._saveRequirementMode(normalized);
        this._updateRequirementModeUI();

        if (!silent) {
            const label = normalized === 'draft' ? 'Build-ready draft' : 'Plan confirmation';
            this._addMessage('system', `Plan state changed to ${label}.`);
        }
    },

    _updateRequirementModeUI() {
        const normalized = this._normalizeRequirementMode(this.requirementMode);
        const tip = this.container?.querySelector('#ai-requirement-mode-tip');
        const buttons = this.container?.querySelectorAll('[data-requirement-mode]');

        buttons?.forEach(button => {
            const buttonMode = this._normalizeRequirementMode(button.dataset.requirementMode);
            button.classList.toggle('is-active', buttonMode === normalized);
            button.setAttribute('aria-pressed', buttonMode === normalized ? 'true' : 'false');
        });

        if (tip) {
            tip.textContent = normalized === 'draft'
                ? 'Build can continue from current assumptions while unresolved items stay visible as risks.'
                : 'Plan Mode confirms blocking engineering fields before Build starts.';
        }
    }
};
