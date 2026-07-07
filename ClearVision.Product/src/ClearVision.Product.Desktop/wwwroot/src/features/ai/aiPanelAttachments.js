import webMessageBridge from '../../core/messaging/webMessageBridge.js';
export const aiPanelAttachmentsMixin = {
    _sanitizeAttachmentDisplayText(value, maxChars = 180) {
        const text = String(value ?? '').trim();
        if (!text) return '';
        return (this._sanitizeAssistantFailureText?.(text, maxChars) ||
            this._redactPublicDiagnosticText?.(text)?.slice(0, maxChars) ||
            text.slice(0, maxChars))
            .replace(/\b[\w.-]*\.(?:onnx|pt|pth|engine|caffemodel|weights|bin)\b/gi, '[redacted:model]');
    },

    _getAttachmentDisplayName(item, fallback = '未知文件') {
        return this._sanitizeAttachmentDisplayText(item?.name || item?.Name || this._getFileName(item?.path || item?.Path || ''), 160) || fallback;
    },

    _normalizeAttachmentStatus(status) {
        const value = String(status || 'ready').trim().toLowerCase();
        return ['ready', 'pending', 'sent', 'skipped'].includes(value) ? value : 'ready';
    },

    _handleAttachmentClick() {
        if (this.isGenerating) return;
        webMessageBridge.sendMessage('PickFileCommand', {
            parameterName: 'aiAttachment',
            filter: 'Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All Files|*.*'
        });
    },

    _handleFilePickedEvent(data) {
        const payload = data?.payload || data || {};
        if (payload.parameterName === 'aiPendingParameterFile') {
            const context = this.pendingParameterFilePickContext;
            this.pendingParameterFilePickContext = null;
            if (!context || payload.isCancelled || !payload.filePath) return;
            this._setPendingDraftConfirmedValue(
                context.operatorId,
                context.parameterName,
                String(payload.filePath || '').trim(),
                'file',
                'user_input'
            );
            if (this.currentResult?.flow) {
                this._renderFollowupChecklist(this.currentResult, this.currentResult.flow);
                this._renderParameterDraftEditor(this.currentResult, this.currentResult.flow);
            }
            return;
        }

        if (payload.parameterName !== 'aiAttachment') return;
        if (payload.isCancelled || !payload.filePath) return;

        const normalizedPath = payload.filePath.trim();
        if (!normalizedPath) return;

        const exists = this.attachments.some(item =>
            item.path.toLowerCase() === normalizedPath.toLowerCase());
        if (exists) {
            this._addMessage('system', '该附件已存在，无需重复添加。');
            return;
        }

        const attachment = {
            path: normalizedPath,
            name: this._getFileName(normalizedPath),
            status: 'ready',
            reason: ''
        };
        this.attachments.push(attachment);
        this._renderAttachments();
        this._addMessage('system', `已添加附件：${this._getAttachmentDisplayName(attachment)}`);
    },

    _handleAttachmentReport(data) {
        const payload = data?.payload || data || {};
        if (!this._shouldHandleGenerateRealtimePayload(payload)) return;

        // Cache for attachment panel
        this._lastAttachmentReport = payload;

        const sent = Array.isArray(payload.sent) ? payload.sent : [];
        const skipped = Array.isArray(payload.skipped) ? payload.skipped : [];

        if (sent.length === 0 && skipped.length === 0) return;

        const sentMap = new Map(sent
            .filter(item => item?.path)
            .map(item => [String(item.path).toLowerCase(), item]));
        const skippedMap = new Map(skipped
            .filter(item => item?.path)
            .map(item => [String(item.path).toLowerCase(), item]));

        this.attachments = this.attachments.map(item => {
            const key = item.path.toLowerCase();
            if (skippedMap.has(key)) {
                const skipInfo = skippedMap.get(key);
                return {
                    ...item,
                    status: 'skipped',
                    reason: this._formatSkipReason(skipInfo?.reason)
                };
            }
            if (sentMap.has(key)) {
                return {
                    ...item,
                    status: 'sent',
                    reason: ''
                };
            }
            return item;
        });

        this._renderAttachments();

        const sentNames = sent.map(item => this._getAttachmentDisplayName(item, '')).filter(Boolean);
        const skippedNames = skipped.map(item => {
            const name = this._getAttachmentDisplayName(item, '');
            const reason = this._sanitizeAttachmentDisplayText(this._formatSkipReason(item?.reason), 180);
            return reason ? `${name}(${reason})` : name;
        }).filter(Boolean);

        const sections = [];
        if (sentNames.length > 0) {
            sections.push(`已发送: ${sentNames.join('，')}`);
        }
        if (skippedNames.length > 0) {
            sections.push(`已跳过: ${skippedNames.join('，')}`);
        }
        if (sections.length > 0) {
            this._addMessage('system', `附件处理结果\n${sections.join('\n')}`);
        }
    },

    _removeAttachment(path) {
        this.attachments = this.attachments.filter(item => item.path !== path);
        this._renderAttachments();
    },

    _renderAttachments() {
        const container = this.container?.querySelector('#ai-attachments');
        if (!container) return;

        if (!this.attachments.length) {
            container.innerHTML = '';
            return;
        }

        const chips = this.attachments.map((item, index) => {
            const status = this._normalizeAttachmentStatus(item.status);
            const safeName = this._getAttachmentDisplayName(item);
            const safeReason = this._sanitizeAttachmentDisplayText(item.reason, 180);
            const title = safeReason ? `${safeName}\n${safeReason}` : safeName;
            const statusLabel = this._getAttachmentStatusLabel(status, safeReason);
            const statusClass = `status-${status}`;
            return `
                <div class="ai-attachment-chip" title="${this._escapeHtml(title)}">
                    <span class="ai-attachment-name">${this._escapeHtml(safeName)}</span>
                    <span class="ai-attachment-status ${statusClass}">${this._escapeHtml(statusLabel)}</span>
                    <button class="ai-attachment-remove" data-attachment-index="${this._escapeHtml(String(index))}" type="button" aria-label="remove attachment">×</button>
                </div>
            `;
        }).join('');

        container.innerHTML = `<div class="ai-attachment-list">${chips}</div>`;
        container.querySelectorAll('.ai-attachment-remove').forEach(btn => {
            btn.addEventListener('click', () => this._removeAttachment(this.attachments[Number(btn.dataset.attachmentIndex)]?.path || ''));
            btn.disabled = this.isGenerating;
        });
    },

    _getAttachmentStatusLabel(status, reason) {
        switch (status) {
            case 'pending': return '发送中';
            case 'sent': return '已发送';
            case 'skipped': return reason ? `已跳过(${reason})` : '已跳过';
            default: return '待发送';
        }
    },

    _formatSkipReason(reason) {
        switch (reason) {
            case 'file_missing': return '文件不存在';
            case 'unsupported_format': return '格式不支持';
            case 'file_too_large': return '文件过大';
            case 'read_failed': return '读取失败';
            case 'limit_exceeded': return '超出数量上限';
            case 'model_not_support_image': return '当前模型不支持图片';
            default: return reason || '';
        }
    },

    _getFileName(filePath) {
        const parts = String(filePath || '').split(/[/\\]/);
        return parts[parts.length - 1] || filePath;
    },

    _renderAttachmentPanel() {
        const card = this.container?.querySelector('#ai-result-attachment-card');
        const container = this.container?.querySelector('#ai-result-attachments');
        if (!card || !container) return;

        const report = this._lastAttachmentReport;
        const attachments = this.attachments || [];
        const supportsVision = this._lastModelSupportsVision;

        if (!report && attachments.length === 0) {
            card.hidden = true;
            container.innerHTML = '';
            return;
        }

        card.hidden = false;
        const sections = [];

        // Model vision capability
        if (supportsVision === false) {
            sections.push(`
                <div class="ai-attachment-vision-warning">
                    当前模型不支持视觉输入，附件仅用于元信息分析，不会发送图片给模型。
                </div>
            `);
        } else if (supportsVision === true) {
            sections.push(`
                <div class="ai-attachment-vision-ok">
                    当前模型支持视觉输入，图片已发送给模型分析。
                </div>
            `);
        }

        // Sent attachments
        const sent = report?.sent || report?.Sent || [];
        if (sent.length > 0) {
            sections.push(`
                <div class="ai-attachment-section">
                    <div class="ai-attachment-section-header">已发送 (${sent.length})</div>
                    ${sent.map(item => `
                        <div class="ai-attachment-item is-sent">
                            <span class="ai-attachment-icon">&#128206;</span>
                            <span class="ai-attachment-name">${this._escapeHtml(this._getAttachmentDisplayName(item))}</span>
                        </div>
                    `).join('')}
                </div>
            `);
        }

        // Skipped attachments
        const skipped = report?.skipped || report?.Skipped || [];
        if (skipped.length > 0) {
            sections.push(`
                <div class="ai-attachment-section">
                    <div class="ai-attachment-section-header">已跳过 (${skipped.length})</div>
                    ${skipped.map(item => `
                        <div class="ai-attachment-item is-skipped">
                            <span class="ai-attachment-icon">&#9888;</span>
                            <span class="ai-attachment-name">${this._escapeHtml(this._getAttachmentDisplayName(item))}</span>
                            <span class="ai-attachment-reason">${this._escapeHtml(this._sanitizeAttachmentDisplayText(item.reason || item.Reason || '未知原因', 180))}</span>
                        </div>
                    `).join('')}
                </div>
            `);
        }

        // Pending attachments (no report yet)
        if (!report && attachments.length > 0) {
            sections.push(`
                <div class="ai-attachment-section">
                    <div class="ai-attachment-section-header">附件 (${attachments.length})</div>
                    ${attachments.map(item => `
                        <div class="ai-attachment-item is-${this._escapeHtml(this._normalizeAttachmentStatus(item.status || 'pending'))}">
                            <span class="ai-attachment-icon">&#128206;</span>
                            <span class="ai-attachment-name">${this._escapeHtml(this._getAttachmentDisplayName(item))}</span>
                            ${item.reason ? `<span class="ai-attachment-reason">${this._escapeHtml(this._sanitizeAttachmentDisplayText(item.reason, 180))}</span>` : ''}
                        </div>
                    `).join('')}
                </div>
            `);
        }

        container.innerHTML = sections.join('');
    }

    // ── 应用预览与撤销 ────────────────────────────────────────
};
