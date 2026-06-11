
export const aiPanelChatMixin = {
    _typewriterEffect(el, text, chunkSize = 3) {
        if (!el) return;
        el.textContent = '';
        let idx = 0;
        const write = () => {
            if (idx < text.length) {
                el.textContent += text.slice(idx, idx + chunkSize);
                idx += chunkSize;
                requestAnimationFrame(write);
            }
        };
        write();
    },

    _addMessage(role, text, options = {}) {
        const container = this.container.querySelector('#ai-chat-container');
        const msg = document.createElement('div');
        msg.className = `ai-message ${role}`;

        const safeText = this._escapeHtml(text);
        
        if (role === 'ai') {
            msg.innerHTML = `<div class="ai-bubble">${safeText}</div>`;
        } else if (role === 'user') {
            msg.innerHTML = `<div class="user-bubble">${safeText}</div>`;
        } else {
            msg.innerHTML = `<div class="system-bubble">${safeText}</div>`;
        }
        
        container.appendChild(msg);
        this._scrollToBottom();
        return msg;
    },

    _startAssistantTurn({ activate = true, statusText = '生成中', statusTone = 'streaming', openReasoning = false, openReply = true } = {}) {
        const container = this.container.querySelector('#ai-chat-container');
        if (!container) return null;

        const msg = document.createElement('div');
        msg.className = 'ai-message ai ai-message-rich';
        msg.innerHTML = `
            <div class="ai-assistant-card" data-turn-tone="${this._escapeHtml(statusTone)}">
                <div class="ai-assistant-card-header">
                    <div class="ai-assistant-card-title">AI 工作流助手</div>
                    <div class="ai-assistant-status is-${this._escapeHtml(statusTone)}">${this._escapeHtml(statusText)}</div>
                </div>
                <details class="ai-assistant-section ai-assistant-reasoning-section" ${openReasoning ? 'open' : ''} hidden>
                    <summary>生成诊断</summary>
                    <div class="ai-stream-body-wrap">
                        <div class="ai-assistant-section-body ai-assistant-reasoning-body"></div>
                        <span class="ai-cursor reasoning-cursor" hidden></span>
                    </div>
                </details>
                <details class="ai-assistant-section ai-assistant-reply-section" ${openReply ? 'open' : ''} hidden>
                    <summary>回复</summary>
                    <div class="ai-stream-body-wrap">
                        <div class="ai-assistant-section-body ai-assistant-reply-body"></div>
                        <span class="ai-cursor reply-cursor" hidden></span>
                    </div>
                </details>
                <details class="ai-assistant-section ai-assistant-process-section" open hidden>
                    <summary>执行过程</summary>
                    <div class="ai-assistant-section-body ai-assistant-process-body"></div>
                </details>
                <details class="ai-assistant-section ai-assistant-tools-section" hidden>
                    <summary>Agent 工具调用</summary>
                    <div class="ai-assistant-section-body ai-assistant-tools-body"></div>
                </details>
                <details class="ai-assistant-section ai-assistant-artifacts-section" hidden>
                    <summary>报告结果</summary>
                    <div class="ai-assistant-section-body ai-assistant-artifacts-body"></div>
                </details>
                <details class="ai-assistant-section ai-assistant-clarification-section" hidden>
                    <summary>需求澄清</summary>
                    <div class="ai-assistant-section-body ai-assistant-clarification-body"></div>
                </details>
                <section class="ai-assistant-section ai-assistant-failure-section" hidden>
                    <div class="ai-assistant-panel-label">失败诊断</div>
                    <div class="ai-assistant-section-body ai-assistant-failure-body"></div>
                </section>
            </div>
        `;

        container.appendChild(msg);
        const turn = {
            root: msg,
            card: msg.querySelector('.ai-assistant-card'),
            statusEl: msg.querySelector('.ai-assistant-status'),
            reasoningSection: msg.querySelector('.ai-assistant-reasoning-section'),
            reasoningBody: msg.querySelector('.ai-assistant-reasoning-body'),
            replySection: msg.querySelector('.ai-assistant-reply-section'),
            replyBody: msg.querySelector('.ai-assistant-reply-body'),
            reasoningCursor: msg.querySelector('.reasoning-cursor'),
            replyCursor: msg.querySelector('.reply-cursor'),
            processSection: msg.querySelector('.ai-assistant-process-section'),
            processBody: msg.querySelector('.ai-assistant-process-body'),
            toolsSection: msg.querySelector('.ai-assistant-tools-section'),
            toolsBody: msg.querySelector('.ai-assistant-tools-body'),
            artifactsSection: msg.querySelector('.ai-assistant-artifacts-section'),
            artifactsBody: msg.querySelector('.ai-assistant-artifacts-body'),
            clarificationSection: msg.querySelector('.ai-assistant-clarification-section'),
            clarificationBody: msg.querySelector('.ai-assistant-clarification-body'),
            failureSection: msg.querySelector('.ai-assistant-failure-section'),
            failureBody: msg.querySelector('.ai-assistant-failure-body')
        };

        if (activate) {
            this.activeAssistantTurn = turn;
        }

        this._scrollToBottom();
        return turn;
    },

    _updateThinkingStep(chainId, stepId, text) {
        const turn = this.activeAssistantTurn;
        if (!turn?.processSection || !turn?.processBody) return null;

        const value = String(text || '').trim();
        if (!value) return null;

        const key = `${String(chainId || 'agent-run').trim()}:${String(stepId || 'step').trim()}`;
        this.agentRunStepMap = this.agentRunStepMap instanceof Map
            ? this.agentRunStepMap
            : new Map();

        turn.processSection.hidden = false;
        let item = this.agentRunStepMap.get(key);
        if (!item || !turn.processBody.contains?.(item)) {
            item = document.createElement('div');
            item.className = 'ai-agent-run-step is-running';
            item.dataset.stepKey = key;
            item.innerHTML = `
                <span class="ai-agent-run-step-dot"></span>
                <div class="ai-agent-run-step-copy"></div>
            `;
            turn.processBody.appendChild(item);
            this.agentRunStepMap.set(key, item);
        }

        const copy = item.querySelector('.ai-agent-run-step-copy');
        if (copy) {
            copy.textContent = value;
        } else {
            item.textContent = value;
        }

        this._scrollToBottom();
        return item;
    },

    _setAssistantTurnStatus(turn, statusText, tone = 'streaming') {
        if (!turn?.statusEl || !turn?.card) return;
        turn.statusEl.textContent = statusText;
        turn.statusEl.className = `ai-assistant-status is-${tone}`;
        turn.card.dataset.turnTone = tone;

        if (tone !== 'streaming') {
            [turn.reasoningCursor, turn.replyCursor].forEach(cursor => {
                if (!cursor) return;
                cursor.classList.add('fading');
                window.setTimeout(() => cursor.setAttribute('hidden', 'true'), 180);
            });
            this.unreadStreamCount = 0;
            this.userHasScrolledUp = false;
            this._updateScrollBottomBtn();
        }
    },

    _appendAssistantStreamText(field, text) {
        const turn = this.activeAssistantTurn;
        if (!turn || !text) return;

        const body = field === 'reasoning' ? turn.reasoningBody : turn.replyBody;
        const section = field === 'reasoning' ? turn.reasoningSection : turn.replySection;
        if (!body || !section) return;

        section.hidden = false;

        const cursor = field === 'reasoning' ? turn.reasoningCursor : turn.replyCursor;
        if (cursor) {
            cursor.removeAttribute('hidden');
            cursor.classList.remove('fading');
        }

        const shouldFollowBottom = this._isNearBottom(body);
        body.textContent += text;
        if (shouldFollowBottom) {
            body.scrollTop = body.scrollHeight;
        }

        if (this.userHasScrolledUp) {
            this.unreadStreamCount += text.length;
            this._updateScrollBottomBtn();
        } else {
            this._scrollToBottom();
        }
    },

    _setAssistantSectionText(turn, field, text, { keepExisting = false } = {}) {
        if (!turn) return;
        const body = field === 'reasoning' ? turn.reasoningBody : turn.replyBody;
        const section = field === 'reasoning' ? turn.reasoningSection : turn.replySection;
        if (!body || !section) return;

        const value = String(text || '').trim();
        if (!value) {
            section.hidden = true;
            body.textContent = '';
            return;
        }

        section.hidden = false;
        body.textContent = keepExisting && body.textContent ? `${body.textContent}${value}` : value;
    },

    _renderPublicDiagnosticsSection(turn, payload = {}) {
        const lines = this._collectPublicDiagnosticLines(payload);
        this._setAssistantSectionText(turn, 'reasoning', lines.join('\n'));
    },

    _collectPublicDiagnosticLines(payload = {}) {
        if (!payload || typeof payload !== 'object') return [];

        const allowedFields = [
            'publicDiagnostics',
            'executionTrace',
            'toolEvents',
            'stageEvents',
            'failureDiagnostics'
        ];
        const lines = [];
        allowedFields.forEach(field => {
            const value = payload[field] ?? payload[this._toPascalCase?.(field) ?? `${field[0].toUpperCase()}${field.slice(1)}`];
            this._appendPublicDiagnosticLines(lines, field, value, 0);
        });

        return [...new Set(lines.map(line => this._redactPublicDiagnosticText(line)).filter(Boolean))].slice(0, 24);
    },

    _appendPublicDiagnosticLines(lines, label, value, depth = 0) {
        if (value === null || value === undefined || depth > 2) return;

        if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
            const text = String(value).trim();
            if (text) lines.push(`${this._formatPublicDiagnosticLabel(label)}: ${text}`);
            return;
        }

        if (Array.isArray(value)) {
            value.slice(0, 8).forEach(item => this._appendPublicDiagnosticLines(lines, label, item, depth + 1));
            return;
        }

        if (typeof value !== 'object') return;

        const hiddenKeys = new Set([
            'reasoning',
            'thinking',
            'chainOfThought',
            'chain_of_thought',
            'rawPrompt',
            'systemPrompt',
            'userPrompt',
            'reasoningContent'
        ]);
        const publicParts = [
            value.stage ?? value.Stage,
            value.toolName ?? value.ToolName,
            value.title ?? value.Title,
            value.status ?? value.Status,
            value.summary ?? value.Summary,
            value.message ?? value.Message,
            value.reportId ?? value.ReportId,
            value.firstFixRecommendation ?? value.FirstFixRecommendation
        ]
            .map(item => String(item ?? '').trim())
            .filter(Boolean);

        if (publicParts.length > 0) {
            lines.push(`${this._formatPublicDiagnosticLabel(label)}: ${publicParts.join(' / ')}`);
        }

        Object.entries(value)
            .filter(([key]) => !hiddenKeys.has(key))
            .filter(([key]) => /^(blockedReasons|issues|warnings|errors|diagnostics)$/i.test(key))
            .forEach(([key, nested]) => this._appendPublicDiagnosticLines(lines, key, nested, depth + 1));
    },

    _formatPublicDiagnosticLabel(label) {
        switch (String(label || '').trim()) {
            case 'publicDiagnostics':
                return '公开诊断';
            case 'executionTrace':
                return '执行过程';
            case 'toolEvents':
                return 'Build 工具证据';
            case 'stageEvents':
                return '阶段事件';
            case 'failureDiagnostics':
                return '失败诊断';
            default:
                return String(label || '公开诊断');
        }
    },

    _redactPublicDiagnosticText(value) {
        return String(value || '')
            .replace(/\bBearer\s+[A-Za-z0-9._~+/=-]{8,}/gi, 'Bearer [redacted]')
            .replace(/\b(?:authorization|x-api-key|api[-_ ]?key)\b\s*[:=]\s*["']?[^"'\s,;}]+/gi, '$1: [redacted]')
            .replace(/\b(?:token|secret|baseUrl|base_url|headers?)\b\s*[:=]\s*["']?[^"'\s,;}]+/gi, '$1: [redacted]')
            .replace(/\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b/g, '[redacted:ip]')
            .replace(/\bDB\d+\.DB[XBWD]\d+(?:\.\d+)?\b/gi, '[redacted:plc]')
            .replace(/\bM\d+(?:\.\d+)?\b/gi, '[redacted:plc]')
            .replace(/\bD\d+\b/gi, '[redacted:plc]')
            .replace(/plc:\/\/[^\s"'<>|]+/gi, '[redacted:plc]')
            .replace(/(?:[a-z]:\\|\\\\)[^\s"'<>|]+/gi, '[redacted:path]')
            .replace(/(?:\/users\/|\/home\/|\/var\/|\/tmp\/|\/mnt\/|\/data\/|\/models\/|\/artifacts\/)[^\s"'<>|]+/gi, '[redacted:path]')
            .replace(/data:image\/[a-z0-9.+-]+;base64,[a-z0-9+/=\r\n]+/gi, '[redacted:image]')
            .trim();
    },

    _renderAssistantFailure(turn, payload = {}) {
        if (!turn?.failureSection || !turn?.failureBody) return;

        const failurePayload = payload.failure || payload.Failure || null;
        const failureSummary = failurePayload?.failureSummary
            || failurePayload?.FailureSummary
            || payload.failureSummary
            || payload.FailureSummary
            || null;
        const diagnostics = Array.isArray(failurePayload?.diagnostics)
            ? failurePayload.diagnostics
            : (Array.isArray(failurePayload?.Diagnostics)
                ? failurePayload.Diagnostics
                : (Array.isArray(payload.lastAttemptDiagnostics)
                    ? payload.lastAttemptDiagnostics
                    : (Array.isArray(payload.LastAttemptDiagnostics) ? payload.LastAttemptDiagnostics : [])));
        const manualRetry = payload.manualRetry || payload.ManualRetry || null;
        const summaryText = failurePayload?.summary
            || failurePayload?.Summary
            || failureSummary?.message
            || payload.failureSummary
            || payload.errorMessage
            || payload.message
            || '生成失败';
        const repairTarget = failureSummary?.repairTarget || manualRetry?.repairTarget || '';
        const lastOutputSummary = failureSummary?.lastOutputSummary || manualRetry?.lastOutputSummary || '';
        const issueLines = diagnostics
            .flatMap(item => Array.isArray(item?.issues) ? item.issues : (Array.isArray(item?.Issues) ? item.Issues : []))
            .slice(0, 6);

        turn.failureSection.hidden = false;
        turn.failureBody.innerHTML = `
            <div class="ai-assistant-failure-summary">${this._escapeHtml(String(summaryText))}</div>
            ${repairTarget ? `<div class="ai-assistant-failure-meta"><span>关键修复</span>${this._escapeHtml(String(repairTarget))}</div>` : ''}
            ${lastOutputSummary ? `<div class="ai-assistant-failure-meta"><span>上一轮输出摘要</span>${this._escapeHtml(String(lastOutputSummary))}</div>` : ''}
            ${issueLines.length > 0 ? `
                <div class="ai-assistant-failure-list">
                    ${issueLines.map(issue => `
                        <div class="ai-assistant-failure-item">
                            <div class="ai-assistant-failure-item-title">${this._escapeHtml(`[${issue?.category || issue?.Category || '--'}/${issue?.code || issue?.Code || '--'}] ${issue?.message || issue?.Message || ''}`)}</div>
                            ${(issue?.repairHint || issue?.RepairHint) ? `<div class="ai-assistant-failure-item-hint">${this._escapeHtml(String(issue?.repairHint || issue?.RepairHint || ''))}</div>` : ''}
                        </div>
                    `).join('')}
                </div>
            ` : ''}
        `;
        this._scrollToBottom();
    },

    _normalizeRequirementBrief(item) {
        if (!item || typeof item !== 'object') return null;

        const clarificationQuestions = Array.isArray(item.clarificationQuestions)
            ? item.clarificationQuestions
            : (Array.isArray(item.ClarificationQuestions) ? item.ClarificationQuestions : []);

        const normalizeStringList = (value) => Array.isArray(value)
            ? [...new Set(value.map(item => String(item || '').trim()).filter(Boolean))]
            : [];

        return {
            scenarioKey: String(item.scenarioKey ?? item.ScenarioKey ?? '').trim(),
            scenarioName: String(item.scenarioName ?? item.ScenarioName ?? '').trim(),
            intentType: String(item.intentType ?? item.IntentType ?? '').trim(),
            requirementMode: this._normalizeRequirementMode(item.requirementMode ?? item.RequirementMode ?? 'strict'),
            confidence: Number(item.confidence ?? item.Confidence ?? 0),
            hasOpenQuestions: Boolean(item.hasOpenQuestions ?? item.HasOpenQuestions),
            clarificationRequired: Boolean(item.clarificationRequired ?? item.ClarificationRequired),
            canGenerateDraftNow: Boolean(item.canGenerateDraftNow ?? item.CanGenerateDraftNow),
            draftRiskLevel: String(item.draftRiskLevel ?? item.DraftRiskLevel ?? 'medium').trim() || 'medium',
            requiredFields: normalizeStringList(item.requiredFields ?? item.RequiredFields),
            blockingClarificationFields: normalizeStringList(item.blockingClarificationFields ?? item.BlockingClarificationFields),
            nonBlockingMissingFields: normalizeStringList(item.nonBlockingMissingFields ?? item.NonBlockingMissingFields),
            knownFacts: normalizeStringList(item.knownFacts ?? item.KnownFacts),
            missingFacts: normalizeStringList(item.missingFacts ?? item.MissingFacts),
            attachmentFacts: normalizeStringList(item.attachmentFacts ?? item.AttachmentFacts),
            objectName: String(item.objectName ?? item.ObjectName ?? '').trim(),
            imageSource: String(item.imageSource ?? item.ImageSource ?? '').trim(),
            outputTarget: String(item.outputTarget ?? item.OutputTarget ?? '').trim(),
            decisionRule: String(item.decisionRule ?? item.DecisionRule ?? '').trim(),
            roiRequirement: String(item.roiRequirement ?? item.RoiRequirement ?? '').trim(),
            calibrationRequirement: String(item.calibrationRequirement ?? item.CalibrationRequirement ?? '').trim(),
            objectTypes: normalizeStringList(item.objectTypes ?? item.ObjectTypes),
            defectTypes: normalizeStringList(item.defectTypes ?? item.DefectTypes),
            measurementTargets: normalizeStringList(item.measurementTargets ?? item.MeasurementTargets),
            requiredResources: normalizeStringList(item.requiredResources ?? item.RequiredResources),
            clarificationQuestions: clarificationQuestions
                .map(question => ({
                    field: String(question?.field ?? question?.Field ?? '').trim(),
                    question: String(question?.question ?? question?.Question ?? '').trim(),
                    required: Boolean(question?.required ?? question?.Required),
                    reason: String(question?.reason ?? question?.Reason ?? '').trim(),
                    priority: String(question?.priority ?? question?.Priority ?? '').trim(),
                    options: normalizeStringList(question?.options ?? question?.Options)
                }))
                .filter(question => question.question || question.field || question.reason)
        };
    },

    _buildClarificationFollowupText(brief) {
        if (!brief) return '';

        const lines = ['请先补充以下阻断澄清项，再继续生成：'];
        if (brief.scenarioName) {
            lines.push(`场景：${brief.scenarioName}`);
        }
        if (brief.objectName) {
            lines.push(`对象：${brief.objectName}`);
        }
        if (brief.outputTarget) {
            lines.push(`输出目标：${brief.outputTarget}`);
        }

        if (brief.knownFacts.length > 0) {
            lines.push('已知事实：');
            brief.knownFacts.forEach(item => lines.push(`- ${item}`));
        }

        if (brief.missingFacts.length > 0) {
            lines.push('阻断待确认项：');
            brief.missingFacts.forEach(item => lines.push(`- ${item}`));
        }

        if (brief.blockingClarificationFields.length > 0) {
            lines.push(`阻断字段：${brief.blockingClarificationFields.map(field => this._getRequirementFieldLabel(field)).join('、')}`);
        }

        if (brief.clarificationQuestions.length > 0) {
            lines.push('澄清问题：');
            brief.clarificationQuestions.forEach((question, index) => {
                const suffix = question.reason ? `（${question.reason}）` : '';
                const options = question.options.length > 0 ? ` 可选：${question.options.join(' / ')}` : '';
                lines.push(`${index + 1}. ${question.question}${suffix}${options}`);
            });
        }

        if (brief.nonBlockingMissingFields.length > 0) {
            lines.push(`非阻断待补：${brief.nonBlockingMissingFields.map(field => this._getRequirementFieldLabel(field)).join('、')}`);
        }

        lines.push(brief.canGenerateDraftNow
            ? '规划模式可按推荐假设继续，并把未解决项保留为构建风险。'
            : '规划模式需要先补齐阻断性工程字段，才能安全开始构建。');
        return lines.join('\n');
    },

    _buildClarificationSafeHint(brief) {
        if (!brief) return '';

        const lines = ['需求澄清上下文：'];
        if (brief.scenarioName) lines.push(`场景：${brief.scenarioName}`);
        if (brief.intentType) lines.push(`意图：${brief.intentType}`);
        if (brief.objectName) lines.push(`对象：${brief.objectName}`);
        if (brief.outputTarget) lines.push(`输出目标：${brief.outputTarget}`);
        if (brief.knownFacts.length > 0) {
            lines.push(`已知事实：${brief.knownFacts.join('；')}`);
        }
        if (brief.missingFacts.length > 0) {
            lines.push(`仍缺字段：${brief.missingFacts.join('；')}`);
        }
        if (brief.blockingClarificationFields.length > 0) {
            lines.push(`阻断字段：${brief.blockingClarificationFields.map(field => this._getRequirementFieldLabel(field)).join('；')}`);
        }
        if (brief.nonBlockingMissingFields.length > 0) {
            lines.push(`非阻断待补：${brief.nonBlockingMissingFields.map(field => this._getRequirementFieldLabel(field)).join('；')}`);
        }
        lines.push('请只根据用户下一轮明确补充的信息更新需求，不要把上面的澄清问题或示例选项当作用户答案。');
        return lines.join('\n');
    },

    _getRequirementFieldLabel(field) {
        const key = String(field || '').trim();
        const fieldLabelMap = {
            scene: '场景类型',
            object_type: '检测对象',
            defect_type: '缺陷类别',
            measurement_target: '测量目标',
            measurement_unit: '测量单位',
            sequence_rule: '线序规则',
            image_source: '图像来源',
            image_source_roi: '图像来源/ROI',
            output_target: '输出目标',
            decision_rule: '判定标准',
            draft_first: '是否允许草稿优先',
            model_path: '模型资源',
            roi: 'ROI范围',
            plc_address: 'PLC地址',
            database_table: '数据库表',
            threshold: '阈值',
            calibration: '标定方式',
            calibration_file: '标定文件',
            ambiguous_negative_signal: '歧义信息'
        };
        return fieldLabelMap[key] || key || '未命名字段';
    },

    _renderRequirementBrief(data = null) {
        const card = this.container?.querySelector('#ai-result-requirement-brief-card');
        const container = this.container?.querySelector('#ai-result-requirement-brief');
        if (!card || !container) return null;

        const brief = this._normalizeRequirementBrief(data?.requirementBrief ?? data?.RequirementBrief ?? null);
        if (!brief) {
            this._resetClarificationSelectionDraft();
            card.hidden = true;
            container.classList.add('is-empty');
            container.innerHTML = '<div class="ai-followup-empty">当前尚未提炼出需求摘要。</div>';
            return null;
        }
        this._resetClarificationSelectionDraft();

        const confidence = Number.isFinite(brief.confidence) ? brief.confidence : 0;
        const confidenceText = `${Math.max(0, Math.min(100, Math.round(confidence * 100)))}%`;
        const requirementModeLabel = brief.requirementMode === 'draft' ? '构建草稿' : '规划确认';
        const riskLabel = String(brief.draftRiskLevel || 'medium').trim() || 'medium';
        const summary = this._buildClarificationFollowupText(brief);
        const safeHint = this._buildClarificationSafeHint(brief);
        const requiredQuestionCount = brief.clarificationQuestions.filter(question => question.required).length;
        const confidenceEl = this.container?.querySelector('#ai-requirement-confidence');
        if (confidenceEl) {
            confidenceEl.textContent = brief.clarificationRequired
                ? `${requiredQuestionCount || brief.missingFacts.length} 项待确认`
                : `置信度 ${confidenceText}`;
            confidenceEl.classList.toggle('is-warning', Boolean(brief.clarificationRequired));
        }
        const metaChips = [
            brief.scenarioName ? `场景：${brief.scenarioName}` : '',
            brief.intentType ? `意图：${brief.intentType}` : '',
            `模式：${requirementModeLabel}`,
            `置信度：${confidenceText}`,
            `风险：${riskLabel}`,
            brief.objectName ? `对象：${brief.objectName}` : '',
            brief.outputTarget ? `输出：${brief.outputTarget}` : '',
            brief.imageSource && brief.imageSource !== 'unknown' ? `图像源：${brief.imageSource}` : '',
            brief.decisionRule ? `判定：${brief.decisionRule}` : '',
            brief.roiRequirement && brief.roiRequirement !== 'none' ? `ROI：${brief.roiRequirement}` : '',
            brief.calibrationRequirement && brief.calibrationRequirement !== 'none' ? `标定：${brief.calibrationRequirement}` : ''
        ].filter(Boolean);

        const renderTagList = (items, emptyText, tone = '') => {
            if (!Array.isArray(items) || items.length === 0) {
                return `<div class="ai-requirement-brief-empty">${this._escapeHtml(emptyText)}</div>`;
            }

            const toneClass = tone ? ` is-${tone}` : '';
            return `<div class="ai-requirement-brief-tags">${items
                .map(item => `<span class="ai-requirement-brief-tag${toneClass}">${this._escapeHtml(String(item))}</span>`)
                .join('')}</div>`;
        };

        const renderFieldChips = (items, emptyText, tone = '') => {
            const normalized = this._normalizeRuntimeFieldList(items);
            if (normalized.length === 0) {
                return `<div class="ai-requirement-brief-empty">${this._escapeHtml(emptyText)}</div>`;
            }

            const toneClass = tone ? ` is-${tone}` : '';
            return `<div class="ai-requirement-brief-tags">${normalized
                .map(field => `
                    <span class="ai-requirement-brief-tag${toneClass}" title="${this._escapeHtml(field)}">
                        ${this._escapeHtml(this._getRequirementFieldLabel(field))}
                    </span>`)
                .join('')}</div>`;
        };

        const renderQuestionList = (questions) => {
            if (!Array.isArray(questions) || questions.length === 0) {
                return '<div class="ai-requirement-brief-empty">当前没有进一步澄清问题。</div>';
            }

            return `<div class="ai-requirement-question-list">${questions.map((question, index) => {
                const requiredLabel = question.required ? '必填' : '建议';
                const priority = question.priority ? ` · ${question.priority}` : '';
                const fieldLabel = this._getRequirementFieldLabel(question.field);
                const options = question.options.length > 0
                    ? `
                        <div class="ai-requirement-question-options-title">参考选项，点击后生成澄清回答草稿</div>
                        <div class="ai-requirement-question-options">${question.options
                            .map(option => `
                                <button class="ai-requirement-question-option" type="button"
                                    aria-pressed="false"
                                    data-clarification-field="${this._escapeHtml(question.field)}"
                                    data-clarification-value="${this._escapeHtml(option)}">
                                    ${this._escapeHtml(option)}
                                </button>`)
                            .join('')}</div>`
                    : '';
                return `
                    <article class="ai-requirement-question ${question.required ? 'is-required' : 'is-recommended'}">
                        <div class="ai-requirement-question-header">
                            <span class="ai-requirement-question-level">${requiredLabel}${this._escapeHtml(priority)}</span>
                            ${fieldLabel ? `<span class="ai-requirement-question-field">${this._escapeHtml(fieldLabel)}</span>` : ''}
                        </div>
                        <div class="ai-requirement-question-title">${index + 1}. ${this._escapeHtml(question.question)}</div>
                        ${question.reason ? `<div class="ai-requirement-question-reason">${this._escapeHtml(question.reason)}</div>` : ''}
                        ${options}
                    </article>
                `;
            }).join('')}</div>`;
        };

        card.hidden = false;
        container.classList.remove('is-empty');
        container.innerHTML = `
            <div class="ai-requirement-brief-summary">
                <div class="ai-requirement-brief-title">当前需求摘要</div>
                <div class="ai-requirement-brief-chip-row">
                    ${metaChips.map(item => `<span class="ai-requirement-brief-chip">${this._escapeHtml(item)}</span>`).join('')}
                </div>
            </div>
            <div class="ai-requirement-brief-grid">
                <section class="ai-requirement-brief-section">
                    <div class="ai-requirement-brief-section-label">已知事实</div>
                    ${renderTagList(brief.knownFacts, '当前没有提炼出已知事实。', 'known')}
                </section>
                <section class="ai-requirement-brief-section">
                    <div class="ai-requirement-brief-section-label">阻断待确认</div>
                    ${this._renderMissingFactsWithActions(brief.missingFacts)}
                </section>
                <section class="ai-requirement-brief-section">
                    <div class="ai-requirement-brief-section-label">阻断字段</div>
                    ${renderFieldChips(brief.blockingClarificationFields, '当前没有阻断字段。', 'blocking')}
                </section>
                <section class="ai-requirement-brief-section">
                    <div class="ai-requirement-brief-section-label">澄清问题</div>
                    ${renderQuestionList(brief.clarificationQuestions)}
                </section>
                <section class="ai-requirement-brief-section">
                    <div class="ai-requirement-brief-section-label">非阻断待补</div>
                    ${renderFieldChips(brief.nonBlockingMissingFields, '当前没有非阻断待补字段。', 'nonblocking')}
                </section>
                <section class="ai-requirement-brief-section">
                    <div class="ai-requirement-brief-section-label">附件信号</div>
                    ${renderTagList(brief.attachmentFacts, '当前没有附件信号。')}
                </section>
            </div>
            <div class="ai-requirement-brief-actions">
                <button class="ai-requirement-brief-action" type="button" data-brief-action="copy">复制澄清清单</button>
                <button class="ai-requirement-brief-action" type="button" data-brief-action="insert">插入输入框</button>
                <button class="ai-requirement-brief-action" type="button" data-brief-action="queue">挂到下一轮</button>
                <button class="ai-requirement-brief-action" type="button" data-brief-action="draft">切到草稿模式</button>
                <button class="ai-requirement-brief-action is-primary" type="button" id="ai-btn-send-clarification-brief" data-brief-action="send-clarification" disabled>发送澄清回答</button>
            </div>
        `;

        container.querySelectorAll('[data-brief-action]').forEach(button => {
            const action = button.dataset.briefAction;
            button.disabled = this.isGenerating || action === 'send-clarification';
            button.addEventListener('click', async () => {
                if (action === 'copy') {
                    const copied = await this._copyTextToClipboard(summary);
                    this._addMessage('system', copied ? '澄清清单已复制。' : '复制失败，请手动复制。');
                    return;
                }

                if (action === 'insert') {
                    this._appendFollowupTextToInput(summary);
                    this._addMessage('system', '澄清清单已插入输入框。');
                    return;
                }

                if (action === 'queue') {
                    this.nextHintDraft = safeHint || summary;
                    this._renderQueuedHintBanner();
                    this._addMessage('system', '已挂载安全澄清上下文，下一轮不会把示例选项误当作用户答案。');
                    return;
                }

                if (action === 'draft') {
                    this._setRequirementMode('draft');
                    return;
                }

                if (action === 'send-clarification') {
                    const draftText = this._buildClarificationAnswerDraft();
                    if (!draftText) {
                        this._addMessage('system', '请先选择澄清选项，或直接在输入框里补充答案。');
                        return;
                    }
                    this._mergeClarificationDraftIntoInput(draftText);
                    this._handleGenerate();
                }
            });
        });

        this._bindClarificationOptionButtons(container);

        return brief;
    },

    _renderAssistantClarification(turn, payload = {}) {
        if (!turn?.clarificationSection || !turn?.clarificationBody) return;

        const brief = this._normalizeRequirementBrief(payload.requirementBrief ?? payload.RequirementBrief ?? null);
        const summary = String(payload.aiExplanation ?? payload.AiExplanation ?? payload.errorMessage ?? payload.message ?? '').trim()
            || (brief ? this._buildClarificationFollowupText(brief).split('\n')[0] : '当前需求需要先补充信息。');
        const questionItems = brief?.clarificationQuestions || [];
        const missingFacts = brief?.missingFacts || [];
        const knownFacts = brief?.knownFacts || [];

        turn.clarificationSection.hidden = false;
        turn.clarificationSection.open = true;
        turn.clarificationBody.innerHTML = `
            <div class="ai-assistant-clarification-summary">${this._escapeHtml(summary)}</div>
            ${knownFacts.length > 0 ? `
                <div class="ai-assistant-clarification-block">
                    <div class="ai-assistant-clarification-label">已知事实</div>
                    <div class="ai-assistant-clarification-tags">
                        ${knownFacts.slice(0, 6).map(item => `<span class="ai-assistant-clarification-tag">${this._escapeHtml(item)}</span>`).join('')}
                    </div>
                </div>
            ` : ''}
            ${missingFacts.length > 0 ? `
                <div class="ai-assistant-clarification-block">
                    <div class="ai-assistant-clarification-label">待确认项</div>
                    <div class="ai-assistant-clarification-tags">
                        ${missingFacts.slice(0, 6).map(item => `<span class="ai-assistant-clarification-tag">${this._escapeHtml(item)}</span>`).join('')}
                    </div>
                </div>
            ` : ''}
            ${questionItems.length > 0 ? `
                <div class="ai-assistant-clarification-list">
                    ${questionItems.slice(0, 3).map((question, index) => `
                        <div class="ai-assistant-clarification-item">
                            <div class="ai-assistant-clarification-item-title">${this._escapeHtml(`${index + 1}. ${question.question}`)}</div>
                            ${question.reason ? `<div class="ai-assistant-clarification-item-hint">${this._escapeHtml(question.reason)}</div>` : ''}
                            ${question.options.length > 0 ? `<div class="ai-assistant-clarification-options">${question.options.map(option => `
                                <button class="ai-assistant-clarification-option" type="button"
                                    aria-pressed="false"
                                    data-clarification-field="${this._escapeHtml(question.field)}"
                                    data-clarification-value="${this._escapeHtml(option)}">
                                    ${this._escapeHtml(option)}
                                </button>`).join('')}</div>` : ''}
                        </div>
                    `).join('')}
                </div>
            ` : '<div class="ai-assistant-clarification-empty">当前没有更多澄清问题。</div>'}
        `;
        this._bindClarificationOptionButtons(turn.clarificationBody);
        this._scrollToBottom();
    },

    _resolveAssistantStatusPresentation(payload = {}) {
        const status = String(payload?.status ?? payload?.Status ?? '').trim().toLowerCase();
        const manualRetry = payload?.manualRetry ?? payload?.ManualRetry ?? payload?.manual_retry ?? null;
        const clarificationRequired = Boolean(payload?.clarificationRequired ?? payload?.ClarificationRequired);

        if (clarificationRequired || status === 'clarification_required') {
            return { text: '待澄清', tone: 'warning' };
        }
        if (manualRetry?.required || status === 'manual_retry_required') {
            return { text: '待手动确认', tone: 'warning' };
        }

        switch (status) {
            case 'completed':
            case 'success':
                return { text: '生成成功', tone: 'success' };
            case 'cancelled':
            case 'canceled':
            case 'user_cancelled':
            case 'user_canceled':
                return { text: '已取消', tone: 'cancelled' };
            case 'timed_out':
            case 'timeout':
                return { text: '请求超时', tone: 'failed' };
            case 'system_error':
                return { text: '系统错误', tone: 'failed' };
            case 'failed':
                return { text: '生成失败', tone: 'failed' };
            default:
                return { text: '已完成', tone: 'neutral' };
        }
    },

    _renderAssistantTurnFromPayload(turnData = {}) {
        const payload = turnData?.payload ?? turnData?.Payload ?? null;
        if (!payload || typeof payload !== 'object') {
            return null;
        }

        const presentation = this._resolveAssistantStatusPresentation(payload);
        const turn = this._startAssistantTurn({
            activate: false,
            statusText: presentation.text,
            statusTone: presentation.tone,
            openReasoning: false,
            openReply: false
        });
        if (!turn) return null;

        const reply = String(payload.reply ?? payload.Reply ?? turnData?.message ?? turnData?.Message ?? '').trim();
        this._setAssistantSectionText(turn, 'reply', reply);
        this._renderPublicDiagnosticsSection(turn, payload);

        if (this._isClarificationResult(payload)) {
            this._renderAssistantClarification(turn, payload);
        }

        if (payload.failure || payload.Failure || payload.manualRetry || payload.ManualRetry) {
            this._renderAssistantFailure(turn, payload);
        }

        return turn;
    }
};
