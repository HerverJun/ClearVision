import settingsApi from '../settingsApi.js';
import { validateAiModelDraftPayload, assertValidation } from '../settingsValidators.js';
import { showToast } from '../../../shared/components/uiComponents.js';

export function installAiTab(SettingsView) {
    Object.assign(SettingsView.prototype, {
        async loadAiModels({ preserveEditingId = false } = {}) {
            const previousEditingId = this.editingAiModelId;
            try {
                const models = await settingsApi.listAiModels();
                this.aiModels = models || [];
            } catch (e) {
                console.warn('[SettingsView] Failed to load AI models from backend:', e);
                this.aiModels = [];
            }

            if (this.aiModels.length > 0) {
                const active = this.aiModels.find(m => m.isActive);
                this.activeAiModelId = active ? active.id : this.aiModels[0].id;
            } else {
                this.activeAiModelId = null;
            }
            if (preserveEditingId && previousEditingId && this.aiModels.some(m => m.id === previousEditingId)) {
                this.editingAiModelId = previousEditingId;
            } else {
                this.editingAiModelId = this.activeAiModelId;
            }
            this._pendingFormEdits = {};
            this.aiReasoningSupportPreview = null;
            this._aiReasoningSupportRequestId += 1;
        }
        ,
        isRuntimePreviewPilotDeveloperUiEnabled() {
            try {
                return String(localStorage.getItem('cv_ai_agent_dev_ui') || '').toLowerCase() === 'true';
            } catch {
                return false;
            }
        }
        ,
        sanitizeRuntimePreviewPilotValue(value) {
            const text = String(value ?? '');
            if (!text) return '';
            if (/base64|data:image|authorization|bearer|x-api-key|api[_-]?key|token=/i.test(text)) {
                return '<redacted>';
            }
            if (/https?:\/\/|(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?(?:\/\S*)?/i.test(text)) {
                return '<redacted>';
            }
            if (/[A-Za-z]:\\|\\\\|\/[^/\s]+\/|\.\./.test(text)) {
                return '<redacted>';
            }
            return this.escapeHtml(text);
        }
        ,
        normalizeRuntimePreviewPilotConfig(payload) {
            const source = payload?.config || payload || {};
            const list = value => Array.isArray(value) ? value.map(item => String(item || '').trim()).filter(Boolean) : [];
            return {
                enabled: source.enabled === true,
                mode: 'metadata_only',
                allowedCameraBindingIds: list(source.allowedCameraBindingIds),
                allowedModelIds: list(source.allowedModelIds),
                allowedTemplateIds: list(source.allowedTemplateIds),
                allowedFlowIds: list(source.allowedFlowIds),
                allowedResourceRoots: list(source.allowedResourceRoots),
                maxPreviewArtifacts: Number.isFinite(Number(source.maxPreviewArtifacts)) ? Number(source.maxPreviewArtifacts) : 8,
                maxMetadataBytes: Number.isFinite(Number(source.maxMetadataBytes)) ? Number(source.maxMetadataBytes) : 16384,
                fallbackToOffline: source.fallbackToOffline !== false,
                denyExternalPath: true,
                denyImageBytes: true
            };
        }
        ,
        async loadRuntimePreviewPilotState() {
            if (!this.isRuntimePreviewPilotDeveloperUiEnabled()) {
                return;
            }

            try {
                const [configResult, catalog, sessionsResult] = await Promise.all([
                    settingsApi.loadRuntimePreviewPilotConfig(),
                    settingsApi.loadRuntimePreviewPilotCatalog(),
                    settingsApi.listRuntimePreviewPilotSessions()
                ]);
                this.runtimePreviewPilotConfig = this.normalizeRuntimePreviewPilotConfig(configResult);
                this.runtimePreviewPilotCatalog = catalog || { items: [] };
                this.runtimePreviewPilotSessions = sessionsResult?.sessions || [];
            } catch {
                this.runtimePreviewPilotConfig = this.normalizeRuntimePreviewPilotConfig({});
                this.runtimePreviewPilotCatalog = { items: [] };
                this.runtimePreviewPilotSessions = [];
            }
        }
        ,
        getRuntimePreviewPilotSelectedSessionId(aiTab) {
            return (aiTab.querySelector('#cfg-rp-session-id')?.value || this.runtimePreviewPilotSelectedSessionId || '').trim();
        }
        ,
        getRuntimePreviewPilotCatalogSelections(aiTab) {
            const selected = {
                allowedCameraBindingIds: [],
                allowedModelIds: [],
                allowedTemplateIds: [],
                allowedFlowIds: [],
                allowedResourceRoots: []
            };
            const mapType = resourceType => {
                const type = String(resourceType || '').toLowerCase();
                if (type.includes('camera')) return 'allowedCameraBindingIds';
                if (type.includes('model')) return 'allowedModelIds';
                if (type.includes('template')) return 'allowedTemplateIds';
                if (type.includes('flow')) return 'allowedFlowIds';
                if (type.includes('root')) return 'allowedResourceRoots';
                return null;
            };

            aiTab.querySelectorAll('[data-rp-catalog-allowlist="true"]:checked').forEach(input => {
                const key = mapType(input.dataset.resourceType);
                const id = String(input.dataset.resourceId || '').trim();
                if (key && id && !selected[key].includes(id)) {
                    selected[key].push(id);
                }
            });
            return selected;
        }
        ,
        buildRuntimePreviewPilotConfigDiff(beforeConfig, afterConfig) {
            const fields = [
                'allowedCameraBindingIds',
                'allowedModelIds',
                'allowedTemplateIds',
                'allowedFlowIds',
                'allowedResourceRoots'
            ];
            const before = this.normalizeRuntimePreviewPilotConfig(beforeConfig || {});
            const after = this.normalizeRuntimePreviewPilotConfig(afterConfig || {});
            return fields
                .map(field => ({
                    field,
                    before: before[field] || [],
                    after: after[field] || []
                }))
                .filter(item => item.before.join('|') !== item.after.join('|'));
        }
        ,
        applyRuntimePreviewPilotCatalogAllowlist(aiTab) {
            const before = this.normalizeRuntimePreviewPilotConfig(this.runtimePreviewPilotConfig || {});
            const selected = this.getRuntimePreviewPilotCatalogSelections(aiTab);
            const draft = {
                ...this.readRuntimePreviewPilotConfigDraft(aiTab),
                ...selected
            };
            this.runtimePreviewPilotConfigDiff = this.buildRuntimePreviewPilotConfigDiff(before, draft);
            this.runtimePreviewPilotConfig = this.normalizeRuntimePreviewPilotConfig(draft);
        }
        ,
        getAiPerformanceModelLabel() {
            const active = this.aiModels.find(m => m.id === this.activeAiModelId)
                || this.aiModels.find(m => m.isActive)
                || this.aiModels[0]
                || null;
            return active?.model || active?.name || '当前激活模型';
        }
        ,
        refreshAiPerformanceOverview() {
            const modelNameEl = this.container?.querySelector('#ai-performance-model-name');
            if (!modelNameEl) return;
            modelNameEl.textContent = this.getAiPerformanceModelLabel();
        }
        ,
        getAiReasoningNote(support) {
            const modeText = support.allowedModes.map(mode => mode === 'auto' ? 'Auto' : mode === 'off' ? 'Off' : 'On').join(' / ');
            const effortText = support.allowedEfforts.map(effort => effort === 'low' ? 'Low' : effort === 'high' ? 'High' : effort === 'xhigh' ? 'XHigh' : 'Medium').join(' / ');

            if (support.allowedModes.length === 1 && support.allowedModes[0] === 'auto') {
                return '当前模型族仅支持 Auto。';
            }

            if (support.allowedEfforts.length === 1) {
                return `可选模式：${modeText}；强度固定为 ${effortText}。`;
            }

            return `可选模式：${modeText}；可选强度：${effortText}。`;
        }
        ,
        scheduleAiReasoningSupportPreview() {
            if (this._aiReasoningSupportDebounce) {
                this.clearTrackedTimeout(this._aiReasoningSupportDebounce);
            }

            this._aiReasoningSupportDebounce = this.setTrackedTimeout(() => {
                this._aiReasoningSupportDebounce = null;
                this.refreshAiReasoningSupportPreview();
            }, 120);
        }
        ,
        async refreshAiReasoningSupportPreview() {
            const aiTab = this.container?.querySelector('[data-section="ai"]');
            const model = this.aiModels.find(x => x.id === this.editingAiModelId);
            if (!aiTab || !model) {
                this.aiReasoningSupportPreview = null;
                this.syncAiReasoningUiState();
                return;
            }

            const requestId = ++this._aiReasoningSupportRequestId;
            try {
                const support = await settingsApi.resolveAiReasoningSupport({
                    provider: aiTab.querySelector('#cfg-ai-provider')?.value || model.provider || '',
                    model: aiTab.querySelector('#cfg-ai-model')?.value || model.model || '',
                    baseUrl: aiTab.querySelector('#cfg-ai-baseurl')?.value || model.baseUrl || '',
                    protocol: null
                });

                if (requestId !== this._aiReasoningSupportRequestId) return;
                this.aiReasoningSupportPreview = this.normalizeAiReasoningSupport(support);
            } catch (error) {
                if (requestId !== this._aiReasoningSupportRequestId) return;
                console.warn('[SettingsView] Failed to refresh AI reasoning support preview:', error);
                this.aiReasoningSupportPreview = null;
            }

            this.syncAiReasoningUiState();
        }
        ,
        getCurrentAiReasoningSupport() {
            const model = this.aiModels.find(x => x.id === this.editingAiModelId);
            if (this.aiReasoningSupportPreview) {
                return this.normalizeAiReasoningSupport(this.aiReasoningSupportPreview);
            }

            return this.normalizeAiReasoningSupport(model?.reasoningSupport);
        }
        ,
        syncAiReasoningUiState() {
            const aiTab = this.container?.querySelector('[data-section="ai"]');
            if (!aiTab) return;

            const modeEl = aiTab.querySelector('#cfg-ai-reasoning-mode');
            const effortEl = aiTab.querySelector('#cfg-ai-reasoning-effort');
            const familyEl = aiTab.querySelector('#ai-reasoning-family');
            const helpEl = aiTab.querySelector('#ai-reasoning-help');
            const noteEl = aiTab.querySelector('#ai-reasoning-note');
            if (!modeEl || !effortEl || !familyEl || !helpEl || !noteEl) return;

            const support = this.getCurrentAiReasoningSupport();
            const allowedModes = support.allowedModes || ['auto'];
            const allowedEfforts = support.allowedEfforts || ['medium'];
            const modeOptions = Array.from(modeEl.options || []);
            const effortOptions = Array.from(effortEl.options || []);

            modeOptions.forEach(option => {
                option.disabled = !allowedModes.includes(option.value);
            });
            effortOptions.forEach(option => {
                option.disabled = !allowedEfforts.includes(option.value);
            });

            if (!allowedModes.includes(modeEl.value)) {
                modeEl.value = allowedModes[0] || 'auto';
            }
            if (!allowedEfforts.includes(effortEl.value)) {
                effortEl.value = allowedEfforts[0] || 'medium';
            }

            modeEl.disabled = allowedModes.length <= 1;
            effortEl.disabled = modeEl.value === 'off' || allowedEfforts.length <= 1;
            familyEl.textContent = `${support.familyName} (${support.familyId})`;
            helpEl.textContent = support.helpText || '';
            noteEl.textContent = this.getAiReasoningNote(support);
        }
        ,
        bindAiSettingsEvents() {
            const aiTab = this.container.querySelector('[data-section="ai"]');
            if (!aiTab) return;

            aiTab.addEventListener('click', async (e) => {
                const btn = e.target.closest('button');
                if (!btn) return;

                if (btn.id === 'btn-toggle-apikey') {
                    const input = aiTab.querySelector('#cfg-ai-apikey');
                    if (input) {
                        input.type = input.type === 'password' ? 'text' : 'password';
                        btn.textContent = input.type === 'password' ? '👁' : '🔒';
                    }
                } else if (btn.id === 'btn-add-llm') {
                    try {
                        const result = await settingsApi.createAiModel({
                            name: '新建模型',
                            provider: 'OpenAI Compatible',
                            model: '',
                            baseUrl: '',
                            apiKey: '',
                            apiKeyOperation: 'new',
                            protocol: 'openai_compatible',
                            wireApi: 'chat_completions',
                            authMode: 'bearer',
                            roleBindings: ['generation'],
                            modelRole: 'generation',
                            priority: 100,
                            isEnabled: true,
                            remark: '',
                            timeoutMs: 120000
                        });
                        await this.loadAiModels();
                        this.editingAiModelId = result.id;
                        this._pendingFormEdits = {};
                        this.refreshAiTableAndForm();
                        showToast('模型已创建', 'success');
                    } catch (err) {
                        showToast('创建模型失败: ' + err.message, 'error');
                    }
                } else if (btn.dataset.action === 'edit') {
                    this.editingAiModelId = btn.dataset.id;
                    this.aiReasoningSupportPreview = null;
                    this._aiReasoningSupportRequestId += 1;
                    this._pendingFormEdits = {};
                    this.refreshAiTableAndForm();
                } else if (btn.dataset.action === 'delete') {
                    if (this.aiModels.length <= 1) {
                        showToast('至少需保留一个模型', 'warning');
                        return;
                    }
                    const id = btn.dataset.id;
                    const model = this.aiModels.find(item => item.id === id);
                    if (!confirm(`确定要删除 AI 模型“${model?.name || id}”吗？\n\n删除后该模型的 API Key 和路由配置会一并移除，正在使用该模型的流程需要重新选择模型。`)) {
                        return;
                    }
                    try {
                        await settingsApi.deleteAiModel(id);
                        await this.loadAiModels();
                        if (this.editingAiModelId === id) {
                            this.editingAiModelId = this.aiModels[0]?.id;
                            this._pendingFormEdits = {};
                        }
                        this.refreshAiTableAndForm();
                        showToast('模型已删除', 'success');
                    } catch (err) {
                        showToast('删除失败: ' + err.message, 'error');
                    }
                } else if (btn.dataset.action === 'activate') {
                    const id = btn.dataset.id;
                    try {
                        await settingsApi.activateAiModel(id);
                        await this.loadAiModels();
                        this.refreshAiTableAndForm();
                        showToast('激活模型已切换', 'success');
                    } catch (err) {
                        showToast('切换激活失败: ' + err.message, 'error');
                    }
                } else if (btn.dataset.action === 'default-planner') {
                    const id = btn.dataset.id;
                    try {
                        await settingsApi.setDefaultPlannerAiModel(id);
                        await this.loadAiModels({ preserveEditingId: true });
                        this.refreshAiTableAndForm();
                        showToast('Default planner model updated.', 'success');
                    } catch (err) {
                        showToast('Default planner update failed: ' + err.message, 'error');
                    }
                } else if (btn.dataset.action === 'default-shadow-eval') {
                    const id = btn.dataset.id;
                    try {
                        await settingsApi.setDefaultShadowEvalAiModel(id);
                        await this.loadAiModels({ preserveEditingId: true });
                        this.refreshAiTableAndForm();
                        showToast('Shadow eval model updated.', 'success');
                    } catch (err) {
                        showToast('Shadow eval model update failed: ' + err.message, 'error');
                    }
                } else if (btn.id === 'btn-ai-test') {
                    const modelId = this.editingAiModelId;
                    if (!modelId) return;
                    const resultEl = aiTab.querySelector('#ai-test-result');
                    btn.disabled = true;
                    btn.textContent = '⏳ 测试中...';
                    if (resultEl) resultEl.textContent = '';

                    try {
                        // 先保存当前表单到后端，再测试（确保用的是最新配置）
                        await this._saveCurrentForm();
                        const result = await settingsApi.testAiModel(modelId);
                        const ok = result.connectionOk ?? result.success;
                        const message = result.sanitizedMessage || result.message || result.errorCode || "";
                        const latency = Number.isFinite(Number(result.latencyMs)) ? ` (${result.latencyMs} ms)` : "";
                        if (ok) {
                            if (resultEl) { resultEl.textContent = "OK " + message + latency; resultEl.style.color = "#4caf50"; }
                            showToast("AI connection test succeeded", "success");
                        } else {
                            if (resultEl) { resultEl.textContent = `${result.errorCode || "failed"}: ${message}${latency}`; resultEl.style.color = "var(--cinnabar)"; }
                            showToast("AI connection test failed: " + message, "error");
                        }
                    } catch (err) {
                        if (resultEl) { resultEl.textContent = '❌ 请求失败: ' + err.message; resultEl.style.color = 'var(--cinnabar)'; }
                        showToast('AI 请求失败: ' + err.message, 'error');
                    } finally {
                        btn.disabled = false;
                        btn.textContent = '🔗 测试连接';
                    }
                } else if (btn.id === 'btn-ai-save') {
                    const modelId = this.editingAiModelId;
                    if (!modelId) return;
                    try {
                        await this._saveCurrentForm();
                        await settingsApi.activateAiModel(modelId);
                        await this.loadAiModels({ preserveEditingId: true });
                        this.refreshAiTableAndForm();
                        showToast('模型设置已保存', 'success');
                    } catch(err) {
                        showToast('保存失败: ' + err.message, 'error');
                    }
                } else if (btn.id === 'btn-runtime-preview-pilot-refresh') {
                    await this.loadRuntimePreviewPilotState();
                    this.refreshRuntimePreviewPilotPanel();
                } else if (btn.id === 'btn-runtime-preview-pilot-save') {
                    try {
                        const payload = this.readRuntimePreviewPilotConfigDraft(aiTab);
                        this.runtimePreviewPilotConfigDiff = this.buildRuntimePreviewPilotConfigDiff(this.runtimePreviewPilotConfig || {}, payload);
                        const confirmed = typeof window?.confirm === 'function'
                            ? window.confirm('Save metadata-only RuntimePreview Pilot allowlist changes?')
                            : true;
                        if (!confirmed) return;
                        const result = await settingsApi.saveRuntimePreviewPilotConfig(payload);
                        this.runtimePreviewPilotConfig = this.normalizeRuntimePreviewPilotConfig(result);
                        await this.loadRuntimePreviewPilotState();
                        this.refreshRuntimePreviewPilotPanel();
                        showToast('RuntimePreview Pilot config saved.', 'success');
                    } catch (err) {
                        showToast('RuntimePreview Pilot config failed: ' + err.message, 'error');
                    }
                } else if (btn.id === 'btn-runtime-preview-pilot-apply-catalog-allowlist') {
                    try {
                        this.applyRuntimePreviewPilotCatalogAllowlist(aiTab);
                        this.refreshRuntimePreviewPilotPanel();
                    } catch (err) {
                        showToast('RuntimePreview Pilot catalog allowlist failed: ' + err.message, 'error');
                    }
                } else if (btn.id === 'btn-runtime-preview-pilot-readiness') {
                    try {
                        const payload = this.buildRuntimePreviewPilotSessionPayload(aiTab);
                        const result = await settingsApi.checkRuntimePreviewPilotReadiness(payload);
                        this.runtimePreviewPilotReadiness = result?.readiness || result;
                        this.runtimePreviewPilotCatalog = result?.catalog || this.runtimePreviewPilotCatalog;
                        this.refreshRuntimePreviewPilotPanel();
                    } catch (err) {
                        showToast('RuntimePreview Pilot readiness failed: ' + err.message, 'error');
                    }
                } else if (btn.id === 'btn-runtime-preview-pilot-create-session') {
                    try {
                        const result = await settingsApi.createRuntimePreviewPilotSession(this.buildRuntimePreviewPilotSessionPayload(aiTab));
                        this.runtimePreviewPilotSelectedSessionId = result?.session?.sessionId || null;
                        await this.loadRuntimePreviewPilotState();
                        this.refreshRuntimePreviewPilotPanel();
                    } catch (err) {
                        showToast('RuntimePreview Pilot session create failed: ' + err.message, 'error');
                    }
                } else if (btn.id === 'btn-runtime-preview-pilot-simulate') {
                    try {
                        const result = await settingsApi.simulateRuntimePreviewPilotSession(this.buildRuntimePreviewPilotSessionPayload(aiTab));
                        this.runtimePreviewPilotSessionReport = result?.report || null;
                        this.runtimePreviewPilotSelectedSessionId = result?.session?.sessionId || result?.report?.sessionId || null;
                        await this.loadRuntimePreviewPilotState();
                        this.refreshRuntimePreviewPilotPanel();
                    } catch (err) {
                        showToast('RuntimePreview Pilot simulation failed: ' + err.message, 'error');
                    }
                } else if (btn.id === 'btn-runtime-preview-pilot-load-report') {
                    try {
                        const sessionId = aiTab.querySelector('#cfg-rp-session-id')?.value || this.runtimePreviewPilotSelectedSessionId;
                        if (!sessionId) return;
                        const result = await settingsApi.loadRuntimePreviewPilotSessionReport(sessionId);
                        this.runtimePreviewPilotSessionReport = result?.report || null;
                        this.runtimePreviewPilotSelectedSessionId = sessionId;
                        this.refreshRuntimePreviewPilotPanel();
                    } catch (err) {
                        showToast('RuntimePreview Pilot report failed: ' + err.message, 'error');
                    }
                } else if (btn.id === 'btn-runtime-preview-pilot-replay-session') {
                    try {
                        const sessionId = this.getRuntimePreviewPilotSelectedSessionId(aiTab);
                        if (!sessionId) return;
                        const result = await settingsApi.replayRuntimePreviewPilotSession(sessionId);
                        this.runtimePreviewPilotReplay = result?.replay || null;
                        this.runtimePreviewPilotSelectedSessionId = sessionId;
                        this.refreshRuntimePreviewPilotPanel();
                    } catch (err) {
                        showToast('RuntimePreview Pilot replay failed: ' + err.message, 'error');
                    }
                } else if (btn.id === 'btn-runtime-preview-pilot-export-report') {
                    try {
                        const sessionId = this.getRuntimePreviewPilotSelectedSessionId(aiTab);
                        if (!sessionId) return;
                        const result = await settingsApi.exportRuntimePreviewPilotSessionReport(sessionId);
                        this.runtimePreviewPilotExport = result?.export || null;
                        this.runtimePreviewPilotSelectedSessionId = sessionId;
                        this.refreshRuntimePreviewPilotPanel();
                    } catch (err) {
                        showToast('RuntimePreview Pilot export failed: ' + err.message, 'error');
                    }
                } else if (btn.id === 'btn-runtime-preview-pilot-deploy-readiness') {
                    try {
                        const result = await settingsApi.generateRuntimePreviewDeployReadiness(this.buildRuntimePreviewPilotSessionPayload(aiTab));
                        this.runtimePreviewPilotDeployReadinessReport = result?.deployReadinessReport || null;
                        this.runtimePreviewPilotSessionReport = result?.deployReadinessReport?.simulationReport || this.runtimePreviewPilotSessionReport;
                        this.runtimePreviewPilotSelectedSessionId = result?.deployReadinessReport?.sessionId || result?.session?.sessionId || this.runtimePreviewPilotSelectedSessionId;
                        await this.loadRuntimePreviewPilotState();
                        this.refreshRuntimePreviewPilotPanel();
                    } catch (err) {
                        showToast('RuntimePreview Pilot deploy readiness failed: ' + err.message, 'error');
                    }
                } else if (btn.id === 'btn-runtime-preview-pilot-scenario-evidence') {
                    try {
                        this.runtimePreviewPilotScenarioEvidence = await settingsApi.loadRuntimePreviewScenarioEvidence();
                        this.refreshRuntimePreviewPilotPanel();
                    } catch (err) {
                        showToast('RuntimePreview Pilot scenario evidence failed: ' + err.message, 'error');
                    }
                } else if (btn.id === 'btn-runtime-preview-pilot-cleanup') {
                    try {
                        const retentionDays = Number(aiTab.querySelector('#cfg-rp-retention-days')?.value || 30);
                        const maxSessions = Number(aiTab.querySelector('#cfg-rp-max-sessions')?.value || 200);
                        const result = await settingsApi.cleanupRuntimePreviewPilotRetention({ retentionDays, maxSessions });
                        this.runtimePreviewPilotRetentionCleanup = result?.cleanup || result;
                        await this.loadRuntimePreviewPilotState();
                        this.refreshRuntimePreviewPilotPanel();
                    } catch (err) {
                        showToast('RuntimePreview Pilot cleanup failed: ' + err.message, 'error');
                    }
                } else if (btn.id === 'btn-runtime-preview-pilot-cancel-session') {
                    try {
                        const sessionId = this.getRuntimePreviewPilotSelectedSessionId(aiTab);
                        if (!sessionId) return;
                        await settingsApi.cancelRuntimePreviewPilotSession(sessionId);
                        await this.loadRuntimePreviewPilotState();
                        this.refreshRuntimePreviewPilotPanel();
                    } catch (err) {
                        showToast('RuntimePreview Pilot cancel failed: ' + err.message, 'error');
                    }
                }
            });

            const handleAiFieldChange = (e) => {
                const el = e.target;
                if (!el || !el.id) return;

                const fieldMap = {
                    'cfg-ai-name': 'name',
                    'cfg-ai-display-name': 'displayName',
                    'cfg-ai-provider': 'provider',
                    'cfg-ai-protocol': 'protocol',
                    'cfg-ai-wireapi': 'wireApi',
                    'cfg-ai-authmode': 'authMode',
                    'cfg-ai-model': 'model',
                    'cfg-ai-baseurl': 'baseUrl',
                    'cfg-ai-apikey': 'apiKey',
                    'cfg-ai-apikey-clear': 'apiKey.clear',
                    'cfg-ai-timeout': 'timeoutMs',
                    'cfg-ai-priority': 'priority',
                    'cfg-ai-enabled': 'isEnabled',
                    'cfg-ai-remark': 'remark',
                    'cfg-ai-reasoning-mode': 'reasoning.mode',
                    'cfg-ai-reasoning-effort': 'reasoning.effort'
                };
                const field = fieldMap[el.id];
                if (!field) return;

                this._pendingFormEdits[field] = el.type === 'checkbox' ? el.checked : el.value;
                if (el.id === 'cfg-ai-name') {
                    const m = this.aiModels.find(x => x.id === this.editingAiModelId);
                    if (m) {
                        m.name = el.value;
                        this.refreshAiTableOnly();
                    }
                }

                if (['cfg-ai-provider', 'cfg-ai-protocol', 'cfg-ai-model', 'cfg-ai-baseurl'].includes(el.id)) {
                    this.scheduleAiReasoningSupportPreview();
                }

                if (['cfg-ai-reasoning-mode', 'cfg-ai-reasoning-effort'].includes(el.id)) {
                    this.syncAiReasoningUiState();
                }
            };

            aiTab.addEventListener('input', handleAiFieldChange);
            aiTab.addEventListener('change', handleAiFieldChange);

            this.setTrackedTimeout(() => {
                this.refreshAiTableAndForm();
                this.syncAiReasoningUiState();
                if (this.isRuntimePreviewPilotDeveloperUiEnabled()) {
                    this.loadRuntimePreviewPilotState().then(() => this.refreshRuntimePreviewPilotPanel()).catch(() => {});
                }
            }, 0);
        }
        ,
        hasPendingAiChanges() {
            const modelId = this.editingAiModelId;
            if (!modelId) {
                return false;
            }

            if (Object.keys(this._pendingFormEdits || {}).length > 0) {
                return true;
            }

            const aiTab = this.container?.querySelector('[data-section="ai"]');
            if (!aiTab) {
                return false;
            }

            const model = this.aiModels.find(x => x.id === modelId);
            if (!model) {
                return false;
            }

            const currentTimeout = parseInt(aiTab.querySelector('#cfg-ai-timeout')?.value || '120000', 10);
            const normalizedTimeout = Number.isFinite(currentTimeout) ? currentTimeout : 120000;
            const currentPriority = parseInt(aiTab.querySelector('#cfg-ai-priority')?.value || '100', 10);
            const normalizedPriority = Number.isFinite(currentPriority) ? currentPriority : 100;
            const pendingApiKey = aiTab.querySelector('#cfg-ai-apikey')?.value || '';
            const clearApiKey = aiTab.querySelector('#cfg-ai-apikey-clear')?.checked === true;
            const currentRoles = this.normalizeAiRoleBindings(model.roleBindings, model.modelRole).join(',');
            const draftRoles = Array.from(aiTab.querySelectorAll('[data-ai-role]:checked'))
                .map(item => item.dataset.aiRole)
                .join(',');
            const currentReasoning = this.normalizeAiReasoning(model.reasoning);
            const draftReasoning = this.normalizeAiReasoning({
                mode: aiTab.querySelector('#cfg-ai-reasoning-mode')?.value || currentReasoning.mode,
                effort: aiTab.querySelector('#cfg-ai-reasoning-effort')?.value || currentReasoning.effort
            });

            return (aiTab.querySelector('#cfg-ai-name')?.value || '') !== (model.name || '')
                || (aiTab.querySelector('#cfg-ai-display-name')?.value || '') !== (model.displayName || '')
                || (aiTab.querySelector('#cfg-ai-provider')?.value || 'OpenAI Compatible') !== (model.provider || 'OpenAI Compatible')
                || this.normalizeAiProtocol(aiTab.querySelector('#cfg-ai-protocol')?.value, model.provider) !== this.normalizeAiProtocol(model.protocol, model.provider)
                || this.normalizeAiWireApi(aiTab.querySelector('#cfg-ai-wireapi')?.value) !== this.normalizeAiWireApi(model.wireApi)
                || this.normalizeAiAuthMode(aiTab.querySelector('#cfg-ai-authmode')?.value, model.protocol) !== this.normalizeAiAuthMode(model.authMode, model.protocol)
                || (aiTab.querySelector('#cfg-ai-model')?.value || '') !== (model.model || '')
                || (aiTab.querySelector('#cfg-ai-baseurl')?.value || '') !== (model.baseUrl || '')
                || normalizedTimeout !== (model.timeoutMs ?? 120000)
                || normalizedPriority !== (model.priority ?? 100)
                || (aiTab.querySelector('#cfg-ai-enabled')?.checked !== (model.isEnabled !== false))
                || (aiTab.querySelector('#cfg-ai-remark')?.value || '') !== (model.remark || '')
                || draftRoles !== currentRoles
                || draftReasoning.mode !== currentReasoning.mode
                || draftReasoning.effort !== currentReasoning.effort
                || clearApiKey
                || pendingApiKey.trim().length > 0;
        }
        ,
        clearAiSecretInputs() {
            const apiKeyInput = this.container?.querySelector('#cfg-ai-apikey');
            const clearApiKeyInput = this.container?.querySelector('#cfg-ai-apikey-clear');
            const toggleButton = this.container?.querySelector('#btn-toggle-apikey');
            if (apiKeyInput) {
                apiKeyInput.value = '';
                apiKeyInput.type = 'password';
            }
            if (clearApiKeyInput) {
                clearApiKeyInput.checked = false;
            }
            if (toggleButton) {
                toggleButton.textContent = '👁';
            }
        }
        ,
        validateAiModelDraftPayload(payload) {
            return assertValidation(validateAiModelDraftPayload(payload));
        }
        ,
        async _saveCurrentForm() {
            const modelId = this.editingAiModelId;
            if (!modelId) return;
            const aiTab = this.container.querySelector('[data-section="ai"]');
            if (!aiTab) return;

            const model = this.aiModels.find(x => x.id === modelId);
            const apiKeyValue = aiTab.querySelector("#cfg-ai-apikey")?.value || "";
            const clearApiKey = aiTab.querySelector("#cfg-ai-apikey-clear")?.checked === true;
            const apiKeyOperation = clearApiKey
                ? "clear"
                : (apiKeyValue.trim().length > 0 ? (model?.hasApiKey ? "replace" : "new") : "keep");
            const provider = aiTab.querySelector("#cfg-ai-provider")?.value || "OpenAI Compatible";
            const protocol = this.normalizeAiProtocol(aiTab.querySelector("#cfg-ai-protocol")?.value, provider);
            const roleBindings = this.normalizeAiRoleBindings(
                Array.from(aiTab.querySelectorAll("[data-ai-role]:checked")).map(item => item.dataset.aiRole));

            const payload = {
                name: aiTab.querySelector("#cfg-ai-name")?.value || "",
                displayName: aiTab.querySelector("#cfg-ai-display-name")?.value || "",
                provider,
                protocol,
                wireApi: this.normalizeAiWireApi(aiTab.querySelector("#cfg-ai-wireapi")?.value),
                authMode: this.normalizeAiAuthMode(aiTab.querySelector("#cfg-ai-authmode")?.value, protocol),
                model: aiTab.querySelector("#cfg-ai-model")?.value || "",
                baseUrl: aiTab.querySelector("#cfg-ai-baseurl")?.value || "",
                apiKey: apiKeyOperation === "replace" || apiKeyOperation === "new" ? apiKeyValue : "",
                apiKeyOperation,
                timeoutMs: parseInt(aiTab.querySelector("#cfg-ai-timeout")?.value || "120000", 10),
                roleBindings,
                modelRole: roleBindings[0] || "generation",
                isEnabled: aiTab.querySelector("#cfg-ai-enabled")?.checked !== false,
                priority: parseInt(aiTab.querySelector("#cfg-ai-priority")?.value || "100", 10),
                remark: aiTab.querySelector("#cfg-ai-remark")?.value || "",
                reasoning: {
                    mode: aiTab.querySelector("#cfg-ai-reasoning-mode")?.value || "auto",
                    effort: aiTab.querySelector("#cfg-ai-reasoning-effort")?.value || "medium"
                }
            };

            this.validateAiModelDraftPayload(payload);
            await settingsApi.updateAiModel(modelId, payload);
            await this.loadAiModels({ preserveEditingId: true });
            this.aiReasoningSupportPreview = null;
            this._pendingFormEdits = {};
            this.refreshAiTableAndForm();
        }
        ,
        refreshAiTableOnly() {
            const tbody = this.container.querySelector('#ai-models-table tbody');
            if (!tbody) return;

            tbody.innerHTML = this.aiModels.map(m => {
                const isEditing = m.id === this.editingAiModelId;
                const id = this.escapeHtml(m.id || '');
                const name = this.escapeHtml(m.name || '-');
                const provider = String(m.provider || '');
                const providerHtml = this.escapeHtml(provider || '-');
                const model = this.escapeHtml(m.model || '-');
                const roles = this.normalizeAiRoleBindings(m.roleBindings, m.modelRole);
                const roleHtml = roles.map(role => `<span class="type-badge" style="margin-right:4px;">${this.escapeHtml(role)}</span>`).join('');
                const enabledHtml = m.isEnabled === false
                    ? '<span class="type-badge settings-ai-model-state is-disabled">Disabled</span>'
                    : '<span class="type-badge settings-ai-model-state is-enabled">Enabled</span>';

                return `
                    <tr class="${isEditing ? 'is-editing' : ''}">
                        <td class="font-bold">${name}</td>
                        <td><span class="type-badge settings-ai-provider-badge">${providerHtml}</span></td>
                        <td class="font-mono">${model}</td>
                        <td>${roleHtml}${enabledHtml}</td>
                        <td>
                            ${m.isActive
                                ? '<span class="settings-status-badge status-connected settings-ai-active-badge"><span class="status-dot"></span> 已启用</span>'
                                : `<button class="cv-btn settings-btn-light" style="padding:2px 8px; font-size:12px; height:24px;" data-action="activate" data-id="${id}">设为激活</button>`}
                        </td>
                        <td>
                            <button class="action-icon-btn" data-action="edit" data-id="${id}" title="编辑">
                                <svg viewBox="0 0 24 24"><path d="M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25z"/></svg>
                            </button>
                            <button class="cv-btn settings-btn-light" style="padding:2px 8px; font-size:12px; height:24px;" data-action="default-planner" data-id="${id}">Planner</button>
                            <button class="cv-btn settings-btn-light" style="padding:2px 8px; font-size:12px; height:24px;" data-action="default-shadow-eval" data-id="${id}">Shadow</button>
                            <button class="action-icon-btn" data-action="delete" data-id="${id}" title="删除" style="color:var(--cinnabar);">
                                <svg viewBox="0 0 24 24"><path d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z"/></svg>
                            </button>
                        </td>
                    </tr>
                `;
            }).join('');
            this.refreshAiPerformanceOverview();
        }
        ,
        refreshAiTableAndForm() {
            this.refreshAiTableOnly();

            const formContainer = this.container.querySelector('#ai-detail-form');
            if (!formContainer) return;

            const m = this.aiModels.find(x => x.id === this.editingAiModelId);
            if (!m) {
                formContainer.innerHTML = '<div style="padding:40px;text-align:center;color:var(--text-muted);">请选择一个模型进行编辑</div>';
                return;
            }

            // apiKey 不再从后端获取真实值，用 placeholder 提示
            const apiKeyPlaceholder = m.hasApiKey ? '●●●●●●（已配置，留空则不修改）' : '请输入 API Key';
            const reasoning = this.normalizeAiReasoning(m.reasoning);
            const wireApi = this.normalizeAiWireApi(m.wireApi);
            const support = this.normalizeAiReasoningSupport(this.aiReasoningSupportPreview || m.reasoningSupport);
            const nameValue = this.escapeHtml(m.name || '');
            const modelValue = this.escapeHtml(m.model || '');
            const baseUrlValue = this.escapeHtml(m.baseUrl || '');
            const apiKeyPlaceholderValue = this.escapeHtml(apiKeyPlaceholder);
            const timeoutValue = Number.isFinite(Number(m.timeoutMs)) ? Number(m.timeoutMs) : 120000;
            const displayNameValue = this.escapeHtml(m.displayName || '');
            const protocol = this.normalizeAiProtocol(m.protocol, m.provider);
            const authMode = this.normalizeAiAuthMode(m.authMode, protocol);
            const roles = this.normalizeAiRoleBindings(m.roleBindings, m.modelRole);
            const priorityValue = Number.isFinite(Number(m.priority)) ? Number(m.priority) : 100;
            const remarkValue = this.escapeHtml(m.remark || '');
            const lastTestStatus = this.escapeHtml(m.lastTestStatus || 'untested');
            const lastTestAt = this.escapeHtml(m.lastTestAt || '-');
            const lastTestLatency = Number.isFinite(Number(m.lastTestLatencyMs)) ? `${m.lastTestLatencyMs} ms` : '-';

            formContainer.innerHTML = `
                <div style="display:flex; gap:16px; margin-bottom:16px;">
                     <div class="settings-fieldset" style="flex:1;">
                         <label>Model Name</label>
                         <input type="text" class="cv-input" id="cfg-ai-name" value="${nameValue}" placeholder="Local alias">
                     </div>
                     <div class="settings-fieldset" style="flex:1;">
                         <label>Display Name</label>
                         <input type="text" class="cv-input" id="cfg-ai-display-name" value="${displayNameValue}" placeholder="Workbench label">
                     </div>
                     <div class="settings-fieldset" style="flex:1;">
                         <label>API 协议</label>
                         <select class="cv-input" id="cfg-ai-provider">
                             <option value="OpenAI API" ${m.provider==='OpenAI API'?'selected':''}>OpenAI API</option>
                             <option value="Anthropic Claude" ${m.provider==='Anthropic Claude'?'selected':''}>Anthropic Claude</option>
                             <option value="OpenAI Compatible" ${m.provider==='OpenAI Compatible'?'selected':''}>本地模型 / OpenAI 兼容 (Ollama, vLLM, GLM等)</option>
                         </select>
                     </div>
                     <div class="settings-fieldset" style="flex:1;">
                         <label>Protocol</label>
                         <select class="cv-input" id="cfg-ai-protocol">
                             <option value="openai_compatible" ${protocol === "openai_compatible" ? "selected" : ""}>OpenAI Compatible</option>
                             <option value="anthropic" ${protocol === "anthropic" ? "selected" : ""}>Anthropic</option>
                             <option value="azure_openai" ${protocol === "azure_openai" ? "selected" : ""}>Azure OpenAI</option>
                             <option value="ollama_native" ${protocol === "ollama_native" ? "selected" : ""}>Ollama Native</option>
                         </select>
                     </div>
                     <div class="settings-fieldset" style="flex:1;">
                         <label>API 接口</label>
                         <select class="cv-input" id="cfg-ai-wireapi">
                             <option value="chat_completions" ${wireApi === 'chat_completions' ? 'selected' : ''}>Chat Completions</option>
                             <option value="responses" ${wireApi === 'responses' ? 'selected' : ''}>Responses</option>
                         </select>
                     </div>
                     <div class="settings-fieldset" style="flex:1;">
                         <label>Auth Mode</label>
                         <select class="cv-input" id="cfg-ai-authmode">
                             <option value="bearer" ${authMode === "bearer" ? "selected" : ""}>Bearer</option>
                             <option value="header_key" ${authMode === "header_key" ? "selected" : ""}>Header Key</option>
                             <option value="none" ${authMode === "none" ? "selected" : ""}>None</option>
                         </select>
                     </div>
                     <div class="settings-fieldset" style="flex:1;">
                         <label>模型选择</label>
                         <input type="text" class="cv-input" id="cfg-ai-model" value="${modelValue}" placeholder="如 deepseek-chat">
                      </div>
                 </div>
                 <div class="settings-fieldset" style="margin-bottom:16px;">
                     <label>API Endpoint (Host & Path)</label>
                     <div style="display:flex;">
                          <span style="padding:10px 12px; background:#f8fafc; border:1px solid #cbd5e1; border-right:none; border-radius:6px 0 0 6px; color:#64748b;">URL:</span>
                          <input type="text" class="cv-input" id="cfg-ai-baseurl" value="${baseUrlValue}" placeholder="如 https://api.deepseek.com/v1" style="border-radius:0 6px 6px 0;">
                      </div>
                 </div>
                  <div style="display:flex; gap:16px;">
                      <div class="settings-fieldset" style="flex:2;">
                          <label>API Key</label>
                         <div class="input-with-suffix" style="position:relative;">
                              <input type="password" class="cv-input" id="cfg-ai-apikey" value="" placeholder="${apiKeyPlaceholderValue}" style="padding-right:36px; font-family:monospace;">
                             <button class="icon-action-btn" id="btn-toggle-apikey" style="position:absolute; right:10px; top:50%; transform:translateY(-50%);">👁</button>
                         </div>
                              <label style="display:flex; align-items:center; gap:8px; margin-top:8px; font-size:12px; color:#64748b;">
                                  <input type="checkbox" id="cfg-ai-apikey-clear"> Clear saved key
                              </label>
                     </div>
                      <div class="settings-fieldset" style="flex:1;">
                          <label>请求超时 (ms)</label>
                          <input type="number" class="cv-input" id="cfg-ai-timeout" value="${timeoutValue}">
                      </div>
                  </div>
                   <div style="display:flex; gap:16px; margin-top:16px; align-items:flex-start;">
                       <div class="settings-fieldset" style="flex:2;">
                           <label>Model Roles</label>
                           <div style="display:flex; flex-wrap:wrap; gap:8px; padding:8px 0;">
                               <label><input type="checkbox" data-ai-role="generation" ${roles.includes("generation") ? "checked" : ""}> generation</label>
                               <label><input type="checkbox" data-ai-role="planner" ${roles.includes("planner") ? "checked" : ""}> planner</label>
                               <label><input type="checkbox" data-ai-role="vision-agent-shadow-eval" ${roles.includes("vision-agent-shadow-eval") ? "checked" : ""}> shadow eval</label>
                           </div>
                           <div hidden data-ai-shadow-eval-entry="hidden">Shadow eval execution entry is developer-hidden by default.</div>
                       </div>
                       <div class="settings-fieldset" style="flex:1;">
                           <label>Priority</label>
                           <input type="number" class="cv-input" id="cfg-ai-priority" value="${priorityValue}" min="1" max="10000">
                       </div>
                       <div class="settings-fieldset" style="flex:1;">
                           <label>Enabled</label>
                           <label style="display:flex; align-items:center; gap:8px; height:38px;"><input type="checkbox" id="cfg-ai-enabled" ${m.isEnabled === false ? "" : "checked"}> Enabled</label>
                       </div>
                   </div>
                   <div class="settings-fieldset" style="margin-top:16px;">
                       <label>Remark</label>
                       <textarea class="cv-input" id="cfg-ai-remark" rows="2">${remarkValue}</textarea>
                   </div>
                   <div style="margin-top:12px; font-size:12px; color:#64748b; display:flex; gap:16px; flex-wrap:wrap;">
                       <span>Last test: <strong>${lastTestStatus}</strong></span>
                       <span>At: ${lastTestAt}</span>
                       <span>Latency: ${lastTestLatency}</span>
                   </div>
                  <details class="settings-fieldset" style="margin-top:16px; border:1px solid var(--border-color); border-radius:10px; padding:12px 14px; background:var(--bg-subtle);" open>
                      <summary style="cursor:pointer; font-weight:700; color:#1e293b;">推理 / Thinking</summary>
                      <div style="display:flex; gap:16px; margin-top:14px;">
                          <div class="settings-fieldset" style="flex:1;">
                              <label>推理模式</label>
                              <select class="cv-input" id="cfg-ai-reasoning-mode">
                                  <option value="auto" ${reasoning.mode === 'auto' ? 'selected' : ''}>Auto</option>
                                  <option value="off" ${reasoning.mode === 'off' ? 'selected' : ''}>Off</option>
                                  <option value="on" ${reasoning.mode === 'on' ? 'selected' : ''}>On</option>
                              </select>
                          </div>
                          <div class="settings-fieldset" style="flex:1;">
                              <label>思考强度</label>
                              <select class="cv-input" id="cfg-ai-reasoning-effort">
                                  <option value="low" ${reasoning.effort === 'low' ? 'selected' : ''}>Low</option>
                                  <option value="medium" ${reasoning.effort === 'medium' ? 'selected' : ''}>Medium</option>
                                  <option value="high" ${reasoning.effort === 'high' ? 'selected' : ''}>High</option>
                                  <option value="xhigh" ${reasoning.effort === 'xhigh' ? 'selected' : ''}>XHigh</option>
                              </select>
                          </div>
                      </div>
                      <div style="display:flex; gap:10px; align-items:center; flex-wrap:wrap; margin-top:12px;">
                           <span style="font-size:12px; font-weight:700; color:#475569;">识别模型族</span>
                           <span id="ai-reasoning-family" style="font-size:12px; padding:4px 8px; border-radius:999px; background:#eef2ff; color:#3730a3;">${this.escapeHtml(`${support.familyName} (${support.familyId})`)}</span>
                           <span id="ai-reasoning-note" style="font-size:12px; color:#64748b;">${this.escapeHtml(this.getAiReasoningNote(support))}</span>
                       </div>
                       <div id="ai-reasoning-help" style="margin-top:8px; font-size:12px; line-height:1.6; color:#475569;">${this.escapeHtml(support.helpText || '')}</div>
                   </details>
                  <div style="display:flex; justify-content:flex-end; gap:12px; margin-top:24px;">
                      <button class="cv-btn settings-btn-light" id="btn-ai-test">🔗 测试连接</button>
                      <button class="cv-btn settings-btn-danger" id="btn-ai-save">💾 保存并应用该模型集</button>
                 </div>
                  <div id="ai-test-result" style="margin-top:10px; text-align:right; font-size:13px; font-weight:500;"></div>
            `;
            this.syncAiReasoningUiState();
        }
        ,
        readRuntimePreviewPilotConfigDraft(aiTab) {
            const readList = selector => (aiTab.querySelector(selector)?.value || '')
                .split(',')
                .map(item => item.trim())
                .filter(Boolean);
            return {
                enabled: aiTab.querySelector('#cfg-rp-enabled')?.checked === true,
                mode: 'metadata_only',
                allowedCameraBindingIds: readList('#cfg-rp-allow-cameras'),
                allowedModelIds: readList('#cfg-rp-allow-models'),
                allowedTemplateIds: readList('#cfg-rp-allow-templates'),
                allowedFlowIds: readList('#cfg-rp-allow-flows'),
                allowedResourceRoots: readList('#cfg-rp-allow-roots'),
                maxPreviewArtifacts: Number(aiTab.querySelector('#cfg-rp-max-artifacts')?.value || 8),
                maxMetadataBytes: Number(aiTab.querySelector('#cfg-rp-max-metadata')?.value || 16384),
                fallbackToOffline: aiTab.querySelector('#cfg-rp-fallback')?.checked !== false,
                denyExternalPath: true,
                denyImageBytes: true
            };
        }
        ,
        buildRuntimePreviewPilotSessionPayload(aiTab) {
            const cameraBindingId = (aiTab.querySelector('#cfg-rp-allow-cameras')?.value || '')
                .split(',')[0]?.trim() || '<pending-camera-binding>';
            return {
                config: this.readRuntimePreviewPilotConfigDraft(aiTab),
                toolName: 'runtime_preview_metadata',
                runtimePreviewConsent: true,
                arguments: {
                    flow: {
                        operators: [
                            {
                                tempId: 'op_cam',
                                operatorType: 'ImageAcquisition',
                                parameters: {
                                    SourceType: 'Camera',
                                    CameraBindingId: cameraBindingId
                                }
                            }
                        ],
                        connections: []
                    }
                }
            };
        }
        ,
        renderRuntimePreviewPilotPanel() {
            const developerEnabled = this.isRuntimePreviewPilotDeveloperUiEnabled();
            const config = this.normalizeRuntimePreviewPilotConfig(this.runtimePreviewPilotConfig || {});
            const catalogItems = Array.isArray(this.runtimePreviewPilotCatalog?.items) ? this.runtimePreviewPilotCatalog.items : [];
            const readiness = this.runtimePreviewPilotReadiness || null;
            const sessions = Array.isArray(this.runtimePreviewPilotSessions) ? this.runtimePreviewPilotSessions : [];
            const report = this.runtimePreviewPilotSessionReport || null;
            const replay = this.runtimePreviewPilotReplay || null;
            const reportExport = this.runtimePreviewPilotExport || null;
            const deployReadiness = this.runtimePreviewPilotDeployReadinessReport || null;
            const scenarioEvidence = this.runtimePreviewPilotScenarioEvidence || null;
            const cleanup = this.runtimePreviewPilotRetentionCleanup || null;
            const configDiff = Array.isArray(this.runtimePreviewPilotConfigDiff) ? this.runtimePreviewPilotConfigDiff : [];
            const listValue = value => this.escapeHtml((value || []).join(', '));
            const isCatalogChecked = item => {
                const type = String(item.resourceType || '').toLowerCase();
                const id = String(item.id || '').trim();
                if (!id) return false;
                if (type.includes('camera')) return config.allowedCameraBindingIds.includes(id);
                if (type.includes('model')) return config.allowedModelIds.includes(id);
                if (type.includes('template')) return config.allowedTemplateIds.includes(id);
                if (type.includes('flow')) return config.allowedFlowIds.includes(id);
                if (type.includes('root')) return config.allowedResourceRoots.includes(id);
                return false;
            };
            const catalogHtml = catalogItems.length
                ? catalogItems.slice(0, 16).map(item => `
                    <tr>
                        <td><input type="checkbox" data-rp-catalog-allowlist="true" data-resource-type="${this.sanitizeRuntimePreviewPilotValue(item.resourceType)}" data-resource-id="${this.sanitizeRuntimePreviewPilotValue(item.id)}" ${isCatalogChecked(item) ? 'checked' : ''} ${item.safeForPilot ? '' : 'disabled'}></td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(item.resourceType)}</td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(item.id)}</td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(item.displayName)}</td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(item.source)}</td>
                        <td>${item.safeForPilot ? 'safe' : 'blocked'}</td>
                        <td>${item.redacted ? '&lt;redacted&gt;' : 'no'}</td>
                    </tr>
                `).join('')
                : '<tr><td colspan="7" style="color:#64748b;">No catalog loaded.</td></tr>';
            const readinessHtml = readiness
                ? `
                    <div data-rp-readiness-status="${this.sanitizeRuntimePreviewPilotValue(readiness.status)}" style="display:grid; grid-template-columns:repeat(3,minmax(0,1fr)); gap:8px; font-size:12px;">
                        <span>Status: <strong>${this.sanitizeRuntimePreviewPilotValue(readiness.status)}</strong></span>
                        <span>Can run: <strong>${readiness.canRunMetadataPilot ? 'true' : 'false'}</strong></span>
                        <span>Draft editable: <strong>${readiness.workflowDraftAllowed !== false ? 'true' : 'false'}</strong></span>
                    </div>
                    <pre style="margin-top:8px; white-space:pre-wrap; font-size:11px; max-height:180px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify({
                        blockingIssues: readiness.blockingIssues || [],
                        missingResources: readiness.missingResources || [],
                        pendingActions: readiness.pendingActions || [],
                        resourceTrace: readiness.resourceTrace || {},
                        fallback: readiness.fallback || {},
                        allowlistCoverage: readiness.allowlistCoverage || {}
                    }, null, 2))}</pre>
                `
                : '<div style="font-size:12px; color:#64748b;">Readiness has not been run.</div>';
            const sessionRows = sessions.length
                ? sessions.slice(0, 10).map(session => `
                    <tr>
                        <td>${this.sanitizeRuntimePreviewPilotValue(session.sessionId)}</td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(session.status)}</td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(session.readinessStatus)}</td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(session.permissionStatus)}</td>
                        <td>${this.sanitizeRuntimePreviewPilotValue(session.reportId || '')}</td>
                    </tr>
                `).join('')
                : '<tr><td colspan="5" style="color:#64748b;">No RuntimePreview sessions.</td></tr>';
            const diffHtml = configDiff.length
                ? `<pre data-rp-allowlist-diff="true" style="margin-top:8px; white-space:pre-wrap; font-size:11px; max-height:120px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify(configDiff, null, 2))}</pre>`
                : '<div data-rp-allowlist-diff="true" style="font-size:12px; color:#64748b;">No catalog allowlist diff.</div>';
            const reportHtml = report
                ? `
                    <div data-rp-report-preview="true" style="font-size:12px;">
                        <div>Report: <strong>${this.sanitizeRuntimePreviewPilotValue(report.reportId)}</strong></div>
                        <div>Preview ready: <strong>${report.previewReady ? 'true' : 'false'}</strong></div>
                        <div>Real resources touched: <strong>${report.realResourcesTouched ? 'true' : 'false'}</strong></div>
                    </div>
                    <pre data-rp-audit-timeline="true" style="margin-top:8px; white-space:pre-wrap; font-size:11px; max-height:160px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify({
                        simulatedTimeline: report.simulation?.timeline || [],
                        auditTimeline: report.auditEvents || [],
                        permissionDecision: report.permissionDecision || {},
                        resourceHandles: report.resourceHandles || []
                    }, null, 2))}</pre>
                `
                : '<div style="font-size:12px; color:#64748b;">No report selected.</div>';
            const replayHtml = replay
                ? `<pre data-rp-session-replay="true" style="white-space:pre-wrap; font-size:11px; max-height:140px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify({
                    sessionId: replay.sessionId,
                    reportId: replay.reportId,
                    previewReady: replay.previewReady,
                    timeline: replay.timeline || [],
                    auditEvents: replay.auditEvents || [],
                    realResourcesTouched: replay.realResourcesTouched
                }, null, 2))}</pre>`
                : '<div data-rp-session-replay="true" style="font-size:12px; color:#64748b;">No replay loaded.</div>';
            const exportHtml = reportExport
                ? `<pre data-rp-report-export-payload="true" style="white-space:pre-wrap; font-size:11px; max-height:120px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify({
                    fileName: reportExport.fileName,
                    exportedAtUtc: reportExport.exportedAtUtc,
                    metadataOnly: reportExport.metadataOnly,
                    realResourcesTouched: reportExport.realResourcesTouched
                }, null, 2))}</pre>`
                : '<div data-rp-report-export-payload="true" style="font-size:12px; color:#64748b;">No report export loaded.</div>';
            const deployReadinessHtml = deployReadiness
                ? `<pre data-rp-deploy-readiness-report="true" style="white-space:pre-wrap; font-size:11px; max-height:160px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify({
                    reportId: deployReadiness.reportId,
                    sessionId: deployReadiness.sessionId,
                    previewReady: deployReadiness.previewReady,
                    readyForDeployment: deployReadiness.readyForDeployment,
                    deploymentBlocked: deployReadiness.deploymentBlocked,
                    packageCreated: deployReadiness.packageCreated,
                    deploymentExecuted: deployReadiness.deploymentExecuted,
                    realResourcesTouched: deployReadiness.realResourcesTouched,
                    pendingActions: deployReadiness.pendingActions || []
                }, null, 2))}</pre>`
                : '<div data-rp-deploy-readiness-report="true" style="font-size:12px; color:#64748b;">No deploy readiness report generated.</div>';
            const scenarioEvidenceHtml = scenarioEvidence
                ? `<pre data-rp-scenario-evidence="true" style="white-space:pre-wrap; font-size:11px; max-height:180px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify({
                    caseCount: scenarioEvidence.caseCount,
                    passedCaseCount: scenarioEvidence.passedCaseCount,
                    accepted: scenarioEvidence.accepted,
                    realResourcesTouched: scenarioEvidence.realResourcesTouched,
                    cases: scenarioEvidence.cases || []
                }, null, 2))}</pre>`
                : '<div data-rp-scenario-evidence="true" style="font-size:12px; color:#64748b;">Scenario evidence has not been loaded.</div>';
            const cleanupHtml = cleanup
                ? `<pre data-rp-retention-cleanup="true" style="white-space:pre-wrap; font-size:11px; max-height:100px; overflow:auto;">${this.sanitizeRuntimePreviewPilotValue(JSON.stringify(cleanup, null, 2))}</pre>`
                : '<div data-rp-retention-cleanup="true" style="font-size:12px; color:#64748b;">No retention cleanup result.</div>';

            return `
                <details class="settings-modern-card" data-runtime-preview-pilot-admin="hidden" ${developerEnabled ? '' : 'hidden'}>
                    <summary class="settings-card-header" style="cursor:pointer;">
                        <span>RuntimePreview Pilot Console v1.1</span>
                        <span class="settings-status-badge ${config.enabled ? 'status-connected' : 'status-disconnected'}" style="margin-left:auto;">
                            <span class="status-dot"></span> ${config.enabled ? 'enabled' : 'disabled'}
                        </span>
                    </summary>
                    <div class="settings-card-body" id="runtime-preview-pilot-panel">
                        <div style="display:grid; grid-template-columns:repeat(3,minmax(0,1fr)); gap:12px;">
                            <label class="settings-fieldset"><span>Enabled</span><input type="checkbox" id="cfg-rp-enabled" ${config.enabled ? 'checked' : ''}></label>
                            <label class="settings-fieldset"><span>Mode</span><input class="cv-input" value="metadata_only" disabled></label>
                            <label class="settings-fieldset"><span>Fallback offline</span><input type="checkbox" id="cfg-rp-fallback" ${config.fallbackToOffline ? 'checked' : ''}></label>
                            <label class="settings-fieldset"><span>Max artifacts</span><input class="cv-input" type="number" id="cfg-rp-max-artifacts" value="${config.maxPreviewArtifacts}" min="1" max="50"></label>
                            <label class="settings-fieldset"><span>Max metadata bytes</span><input class="cv-input" type="number" id="cfg-rp-max-metadata" value="${config.maxMetadataBytes}" min="1" max="524288"></label>
                            <div class="settings-fieldset"><span>Safety</span><div style="font-size:12px;">metadata_only, denyExternalPath=true, denyImageBytes=true</div></div>
                        </div>
                        <div style="display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:12px; margin-top:12px;">
                            <label class="settings-fieldset"><span>Camera allowlist</span><input class="cv-input" id="cfg-rp-allow-cameras" value="${listValue(config.allowedCameraBindingIds)}"></label>
                            <label class="settings-fieldset"><span>Model allowlist</span><input class="cv-input" id="cfg-rp-allow-models" value="${listValue(config.allowedModelIds)}"></label>
                            <label class="settings-fieldset"><span>Template allowlist</span><input class="cv-input" id="cfg-rp-allow-templates" value="${listValue(config.allowedTemplateIds)}"></label>
                            <label class="settings-fieldset"><span>Flow allowlist</span><input class="cv-input" id="cfg-rp-allow-flows" value="${listValue(config.allowedFlowIds)}"></label>
                            <label class="settings-fieldset"><span>Resource root allowlist</span><input class="cv-input" id="cfg-rp-allow-roots" value="${listValue(config.allowedResourceRoots)}"></label>
                        </div>
                        <div style="display:flex; gap:8px; justify-content:flex-end; margin-top:12px;">
                            <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-refresh">Refresh catalog</button>
                            <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-apply-catalog-allowlist">Apply catalog allowlist</button>
                            <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-readiness">Run readiness</button>
                            <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-create-session">Create metadata session</button>
                            <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-simulate">Simulate metadata session</button>
                            <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-deploy-readiness">Deploy readiness report</button>
                            <button class="cv-btn settings-btn-danger" id="btn-runtime-preview-pilot-save">Save pilot config</button>
                        </div>
                        ${diffHtml}
                        <div style="margin-top:14px; border-top:1px solid var(--border-color); padding-top:12px;">
                            <h4 style="margin:0 0 8px;">Catalog</h4>
                            <table class="settings-modern-table" data-rp-catalog-table="true">
                                <thead><tr><th>Allow</th><th>Type</th><th>ID</th><th>Name</th><th>Source</th><th>Safe</th><th>Redacted</th></tr></thead>
                                <tbody>${catalogHtml}</tbody>
                            </table>
                        </div>
                        <div style="margin-top:14px; border-top:1px solid var(--border-color); padding-top:12px;">
                            <h4 style="margin:0 0 8px;">Readiness</h4>
                            ${readinessHtml}
                        </div>
                        <div style="margin-top:14px; border-top:1px solid var(--border-color); padding-top:12px;" data-rp-session-console="true">
                            <h4 style="margin:0 0 8px;">Session Console</h4>
                            <div style="display:flex; gap:8px; margin-bottom:8px;">
                                <input class="cv-input" id="cfg-rp-session-id" placeholder="sessionId for replay/report/export/cancel" value="${this.sanitizeRuntimePreviewPilotValue(this.runtimePreviewPilotSelectedSessionId || '')}">
                                <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-load-report">Load report</button>
                                <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-replay-session">Replay session</button>
                                <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-export-report">Export report</button>
                                <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-cancel-session">Cancel session</button>
                            </div>
                            <table class="settings-modern-table" data-rp-session-list="true">
                                <thead><tr><th>Session</th><th>Status</th><th>Readiness</th><th>Permission</th><th>Report</th></tr></thead>
                                <tbody>${sessionRows}</tbody>
                            </table>
                        </div>
                        <div style="margin-top:14px; border-top:1px solid var(--border-color); padding-top:12px;" data-rp-report-export="true">
                            <h4 style="margin:0 0 8px;">Audit timeline & report preview</h4>
                            ${reportHtml}
                            ${replayHtml}
                            ${exportHtml}
                        </div>
                        <div style="margin-top:14px; border-top:1px solid var(--border-color); padding-top:12px;" data-rp-deploy-readiness="true">
                            <h4 style="margin:0 0 8px;">Deploy readiness report</h4>
                            ${deployReadinessHtml}
                        </div>
                        <div style="margin-top:14px; border-top:1px solid var(--border-color); padding-top:12px;" data-rp-scenario-evidence-panel="true">
                            <div style="display:flex; align-items:center; justify-content:space-between; gap:8px;">
                                <h4 style="margin:0 0 8px;">Scenario evidence</h4>
                                <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-scenario-evidence">Load scenario evidence</button>
                            </div>
                            ${scenarioEvidenceHtml}
                        </div>
                        <div style="margin-top:14px; border-top:1px solid var(--border-color); padding-top:12px;" data-rp-retention-panel="true">
                            <h4 style="margin:0 0 8px;">Retention cleanup</h4>
                            <div style="display:flex; gap:8px; align-items:center; margin-bottom:8px;">
                                <input class="cv-input" type="number" id="cfg-rp-retention-days" value="30" min="1" max="365" style="max-width:120px;">
                                <input class="cv-input" type="number" id="cfg-rp-max-sessions" value="200" min="1" max="5000" style="max-width:120px;">
                                <button class="cv-btn settings-btn-light" id="btn-runtime-preview-pilot-cleanup">Run cleanup</button>
                            </div>
                            ${cleanupHtml}
                        </div>
                    </div>
                </details>
            `;
        }
        ,
        refreshRuntimePreviewPilotPanel() {
            const panel = this.container?.querySelector('[data-runtime-preview-pilot-admin]');
            if (!panel) return;
            const replacement = document.createElement('div');
            replacement.innerHTML = this.renderRuntimePreviewPilotPanel();
            panel.replaceWith(replacement.firstElementChild);
        }
        ,
        renderAiTab() {
            const aiInfo = this.getAiPerformanceModelLabel();
            return `
                <div class="settings-section-title">
                    <h2>AI & LLM 模型管理</h2>
                    <p>集成深度学习本地模型与云端大语言模型 API 配置。</p>
                </div>
                ${this.renderScopeNotice('ai')}
                <!-- Block 1: Model Tab & List -->
                <div class="settings-modern-card">
                    <div class="settings-card-header" style="background:var(--bg-surface); border-bottom:1px solid var(--border-color); padding:0; display:flex;">
                        <div style="display:flex; padding-top:16px;">
                            <div style="padding:0 24px 12px; color:#94a3b8; font-weight:600; font-size:14px; cursor:pointer;">
                                <svg viewBox="0 0 24 24" style="width:16px; height:16px; vertical-align:text-bottom; margin-right:4px; fill:currentColor;"><path d="M19.14,12.94c0.04-0.3,0.06-0.61,0.06-0.94c0-0.32-0.02-0.64-0.06-0.94l2.03-1.58c0.18-0.14,0.23-0.41,0.12-0.61 l-1.92-3.32c-0.12-0.22-0.37-0.29-0.59-0.22l-2.39,0.96c-0.5-0.38-1.03-0.7-1.62-0.94L14.4,2.81c-0.04-0.24-0.24-0.41-0.48-0.41 h-3.84c-0.24,0-0.43,0.17-0.47,0.41L9.25,5.35C8.66,5.59,8.12,5.92,7.63,6.29L5.24,5.33c-0.22-0.08-0.47,0-0.59,0.22L2.73,8.87 C2.62,9.08,2.66,9.34,2.86,9.48l2.03,1.58C4.84,11.36,4.8,11.69,4.8,12s0.02,0.64,0.06,0.94l-2.03,1.58 c-0.18,0.14-0.23,0.41-0.12,0.61l1.92,3.32c0.12,0.22,0.37,0.29,0.59,0.22l2.39-0.96c0.5,0.38,1.03,0.7,1.62,0.94l0.36,2.54 c0.05,0.24,0.24,0.41,0.48,0.41h3.84c0.24,0,0.43-0.17,0.47-0.41l0.36-2.54c0.59-0.24,1.13-0.56,1.62-0.94l2.39,0.96 c0.22,0.08,0.47,0,0.59-0.22l1.92-3.32c0.12-0.22,0.07-0.49-0.12-0.61L19.14,12.94z M12,15.6c-1.98,0-3.6-1.62-3.6-3.6 s1.62-3.6,3.6-3.6s3.6,1.62,3.6,3.6S13.98,15.6,12,15.6z"/></svg> 本地模型
                            </div>
                            <div style="padding:0 24px 12px; color:var(--cinnabar); font-weight:600; font-size:14px; border-bottom:2px solid var(--cinnabar); cursor:pointer;">
                                <svg viewBox="0 0 24 24" style="width:16px; height:16px; vertical-align:text-bottom; margin-right:4px; fill:currentColor;"><path d="M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96zM14 13v4h-4v-4H7l5-5 5 5h-3z"/></svg> 大语言模型 (LLM)
                            </div>
                        </div>
                        <div style="margin-left:auto; padding:12px 24px;">
                            <button class="cv-btn settings-btn-light" style="height:32px;" id="btn-add-llm">+ 添加 LLM</button>
                        </div>
                    </div>
                    <div class="settings-card-table-wrapper">
                        <table class="settings-modern-table" id="ai-models-table">
                            <thead>
                                <tr>
                                    <th>名称</th>
                                    <th>协议</th>
                                    <th>模型标识</th>
                                    <th>Roles</th>
                                    <th>状态</th>
                                    <th>操作</th>
                                </tr>
                            </thead>
                            <tbody>
                                <!-- Javascript Dynamically Inserted Here -->
                            </tbody>
                        </table>
                    </div>
                </div>

                <!-- Block 2: 详情配置 -->
                <div style="display:flex; gap:24px;">
                    <div class="settings-modern-card" style="flex:2;">
                        <div class="settings-card-header" style="background:var(--bg-surface);">
                            <div class="settings-header-left">
                                <svg viewBox="0 0 24 24" class="settings-header-icon"><path d="M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96zM14 13v4h-4v-4H7l5-5 5 5h-3z" fill="var(--text-muted)"/></svg>
                                <span>配置所选模型 (Editing)</span>
                            </div>
                        </div>
                        <div class="settings-card-body" id="ai-detail-form">
                            <!-- Loaded by JS -->
                        </div>
                    </div>

                    <div class="settings-modern-card" style="flex:1; border:1px solid var(--border-color); box-shadow:none;">
                        <div class="settings-card-body" style="text-align:center; padding:32px 24px;">
                            <h3 style="font-size:15px; color:#475569; margin:0 0 24px;">API 性能概览</h3>

                            <div style="position:relative; width:140px; height:140px; margin:0 auto 24px;">
                                <!-- Fake Donut Chart SVG -->
                                <svg viewBox="0 0 36 36" style="width:100%; height:100%;">
                                    <path d="M18 2.0845 a 15.9155 15.9155 0 0 1 0 31.831 a 15.9155 15.9155 0 0 1 0 -31.831" fill="none" stroke="#f1f5f9" stroke-width="4"></path>
                                    <path d="M18 2.0845 a 15.9155 15.9155 0 0 1 0 31.831 a 15.9155 15.9155 0 0 1 0 -31.831" fill="none" stroke="#10b981" stroke-width="4" stroke-dasharray="80, 100" stroke-dashoffset="0" style="transition: stroke-dasharray 1s ease-out;"></path>
                                </svg>
                                <div style="position:absolute; top:50%; left:50%; transform:translate(-50%, -50%);">
                                    <div style="font-size:32px; font-weight:700; color:var(--text-primary); line-height:1;">450</div>
                                    <div style="font-size:12px; color:#94a3b8; font-weight:600; text-transform:uppercase;">ms</div>
                                </div>
                            </div>

                            <div style="font-size:13px; font-weight:600; color:#10b981; margin-bottom:24px;">● 网络延迟 (Latency)</div>

                            <div style="text-align:left; font-size:13px; border-top:1px dashed var(--border-color); padding-top:16px;">
                                <div style="display:flex; justify-content:space-between; margin-bottom:8px;">
                                    <span class="text-muted">Token 消耗 (Daily)</span>
                                    <span class="font-bold">14.2K / 50K</span>
                                </div>
                                <div style="background:#e2e8f0; height:6px; border-radius:3px; overflow:hidden; margin-bottom:16px;">
                                    <div style="background:#3b82f6; width:28%; height:100%;"></div>
                                </div>

                                <div style="display:flex; justify-content:space-between; margin-bottom:8px;">
                                    <span class="text-muted">RPM (Requests/Min)</span>
                                    <span class="font-bold">45 / 100</span>
                                </div>
                                <div style="background:#e2e8f0; height:6px; border-radius:3px; overflow:hidden;">
                                    <div style="background:#ec4899; width:45%; height:100%;"></div>
                                </div>
                            </div>

                            <div style="background:#ecfdf5; border:1px solid #d1fae5; border-radius:8px; padding:12px; margin-top:24px; text-align:left; font-size:12px; color:#065f46; display:flex; gap:8px;">
                                <svg viewBox="0 0 24 24" style="width:16px; height:16px; fill:#10b981; flex-shrink:0;"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z"/></svg>
                                当前激活模型：<span id="ai-performance-model-name">${this.escapeHtml(aiInfo)}</span>。连接健康状态以测试连接与最近一次生成诊断为准。
                            </div>
                        </div>
                    </div>
                </div>
            `;
        }
        ,
        async saveAiDraftFromTop() {
            if (!this.editingAiModelId) {
                showToast('请先选择一个 AI 模型再保存。', 'warning');
                return;
            }

            try {
                await this._saveCurrentForm();
                showToast('AI 模型草稿已保存，激活模型未切换。', 'success');
            } catch (error) {
                showToast('保存 AI 模型失败: ' + error.message, 'error');
            }
        }

    });
}
