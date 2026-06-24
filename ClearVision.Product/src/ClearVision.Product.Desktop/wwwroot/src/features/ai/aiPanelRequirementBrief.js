export const aiPanelRequirementBriefMixin = {
    _normalizeRequirementMode(mode) {
        return String(mode || '').trim().toLowerCase() === 'draft' ? 'draft' : 'strict';
    },

    _loadRequirementMode() {
        return 'strict';
    },

    _saveRequirementMode(mode) {
        return this._normalizeRequirementMode(mode);
    },

    _setRequirementMode(mode, { silent = false } = {}) {
        const normalized = this._normalizeRequirementMode(mode);
        if (normalized === this.requirementMode) {
            this._updateRequirementModeUI();
            return;
        }

        this.requirementMode = normalized;
        if (this.pendingVisionPlan) {
            this.pendingVisionPlan.requirementMode = normalized;
            this._rememberRequirementModeForPlan?.(this.pendingVisionPlan, normalized);
        }
        this.planAnswerRevision = (Number(this.planAnswerRevision) || 0) + 1;
        this._requestPlanReadinessPreview?.(this.pendingVisionPlan, { reason: 'requirement_mode' });
        this._updateRequirementModeUI();
        this._renderPlanWorkspace?.(this.pendingVisionPlan);
        this._renderAgentWorkspaceOverview?.();

        if (!silent) {
            const label = normalized === 'draft' ? '先生成可编辑草稿' : '严格确认后构建';
            this._addMessage?.('system', `规划模式已切换为：${label}。`);
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
                ? 'Draft：允许后端判定为可后补的决策或资源暂缓，先生成可编辑草稿，不代表可部署。'
                : 'Strict：关键决策及当前模式要求的资源确认后才可构建。';
        }
    }
};
