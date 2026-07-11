const clarificationRuntimeByPanel = new WeakMap();

const unsafeRecommendationCategories = new Set([
    'safety_blocker',
    'contract_warning',
    'resource_pending'
]);

function clean(value) {
    return String(value ?? '').trim();
}

function asArray(value) {
    return Array.isArray(value) ? value : [];
}

function escapeHtml(panel, value) {
    return panel?._escapeHtml?.(clean(value)) ?? clean(value);
}

function getRuntime(panel) {
    let runtime = clarificationRuntimeByPanel.get(panel);
    if (!runtime) {
        runtime = {
            editingFields: new Set(),
            pendingFields: new Set(),
            focusField: '',
            awaitingNextFocus: false,
            lastActiveField: ''
        };
        clarificationRuntimeByPanel.set(panel, runtime);
    }
    return runtime;
}

function readField(item) {
    return clean(item?.field || item?.id);
}

function readQuestionId(item) {
    return clean(item?.questionId || item?.id);
}

function readConfirmedAnswer(state, field) {
    return state?.answers?.confirmedByField?.[field] || null;
}

function readOptimisticAnswer(state, field) {
    return state?.answers?.optimisticByField?.[field] || null;
}

function isResourceItem(item) {
    return item?.kind === 'resource' || clean(item?.category).toLowerCase() === 'resource_pending';
}

function findPlanQuestion(panel, plan, item) {
    const questionId = readQuestionId(item);
    const field = readField(item);
    return asArray(plan?.questions).find(question =>
        clean(question?.id) === questionId ||
        panel?._inferPlanQuestionFieldForQuestion?.(question, plan) === field
    ) || null;
}

function mergeQuestion(item, question) {
    if (!question) return item;
    return {
        ...question,
        ...item,
        id: readQuestionId(item) || clean(question.id),
        questionId: readQuestionId(item) || clean(question.id),
        field: readField(item),
        title: clean(question.title) || clean(item?.title),
        why: clean(question.why) || clean(item?.why),
        impact: clean(question.impact) || clean(item?.impact),
        options: asArray(item?.options).length ? item.options : asArray(question.options)
    };
}

function formatQuestionTitle(panel, item) {
    const field = readField(item);
    const title = clean(item?.title);
    if (title && title !== field && !/^[_a-z0-9.:-]+$/i.test(title)) return title;
    return clean(panel?._formatRequirementFieldLabel?.(field) || title || '关键问题待确认');
}

function isSafeRecommendedItem(item) {
    if (!item || isResourceItem(item)) return false;
    if (unsafeRecommendationCategories.has(clean(item.category).toLowerCase())) return false;
    return asArray(item.options).some(option =>
        option?.recommended === true &&
        clean(option?.value) &&
        clean(option?.answerEffect || 'resolve_field').toLowerCase() === 'resolve_field'
    );
}

function formatAnswerValue(panel, item, value) {
    const raw = clean(value);
    const option = asArray(item?.options).find(candidate => clean(candidate?.value) === raw);
    const replacements = {
        industrial_camera: '工业相机',
        image_folder: '图片目录',
        ok_ng: 'OK/NG',
        structured_result: '结构化结果'
    };
    return clean(option?.label) || replacements[raw] || clean(panel?._localizeDisplayText?.(raw)) || raw;
}

export function deriveAiClarificationPresentation(panel, plan) {
    const state = panel?.agentWorkspaceState || null;
    const projection = state?.projection || null;
    const runtime = getRuntime(panel);
    const queue = asArray(projection?.clarificationQueue);
    const batch = asArray(projection?.clarificationBatch);
    const batchKeys = new Set(batch.map(readField).filter(Boolean));
    const ordered = [
        ...batch,
        ...queue.filter(item => !batchKeys.has(readField(item)))
    ];

    const items = ordered.map(rawItem => {
        const field = readField(rawItem);
        const question = mergeQuestion(rawItem, findPlanQuestion(panel, plan, rawItem));
        question.title = formatQuestionTitle(panel, question);
        const confirmedAnswer = readConfirmedAnswer(state, field);
        const optimisticAnswer = readOptimisticAnswer(state, field);
        const hasPendingChange = Boolean(optimisticAnswer) && clean(optimisticAnswer.value) !== clean(confirmedAnswer?.value);
        const confirming = hasPendingChange && state?.readinessStatus === 'validating';
        const failed = hasPendingChange && state?.readinessStatus === 'failed';
        const unconfirmed = hasPendingChange && !confirming && !failed;
        const confirmed = Boolean(confirmedAnswer?.resolved) && !hasPendingChange;
        const selectedValue = clean(optimisticAnswer?.value || confirmedAnswer?.value || rawItem?.selectedValue);
        const editing = runtime.editingFields.has(field);
        return {
            ...question,
            field,
            questionId: readQuestionId(question),
            confirmedAnswer,
            confirmedDisplayValue: formatAnswerValue(panel, question, confirmedAnswer?.value),
            optimisticAnswer,
            selectedValue,
            confirming,
            failed,
            unconfirmed,
            confirmed,
            editing,
            deferred: rawItem?.deferred === true,
            resource: isResourceItem(rawItem)
        };
    });

    for (const item of items) {
        if (item.confirmed && runtime.pendingFields.has(item.field)) {
            runtime.pendingFields.delete(item.field);
            runtime.editingFields.delete(item.field);
            runtime.focusField = '';
        }
    }

    const activeQuestion = items.find(item =>
        !item.resource && !item.deferred &&
        (item.confirming || item.failed || item.unconfirmed || item.editing)
    ) || items.find(item =>
        !item.resource && !item.deferred && !item.confirmed
    ) || null;
    const unresolved = items.filter(item => !item.confirmed && !item.deferred);
    const confirmedItems = items.filter(item => item.confirmed && item !== activeQuestion);
    const resourceItems = items.filter(item => item.resource && !item.confirmed);
    const recommendable = unresolved.filter(isSafeRecommendedItem);
    const unsafeRecommended = unresolved.some(item =>
        asArray(item.options).some(option => option?.recommended === true) && !isSafeRecommendedItem(item)
    );
    const planRecommendedQuestions = asArray(plan?.questions).filter(question =>
        asArray(question?.options).some(option => option?.recommended === true && clean(option?.answerEffect || 'resolve_field') === 'resolve_field')
    );
    const safeQuestionIds = new Set(recommendable.map(readQuestionId).filter(Boolean));
    const canAcceptAllRecommended = recommendable.length > 0 && !unsafeRecommended &&
        planRecommendedQuestions.every(question => safeQuestionIds.has(clean(question.id)) ||
            Boolean(readConfirmedAnswer(state, panel?._inferPlanQuestionFieldForQuestion?.(question, plan))));

    return {
        activeQuestion,
        confirmedItems,
        resourceItems,
        unresolvedCount: unresolved.length,
        currentPosition: activeQuestion ? 1 : 0,
        canAcceptAllRecommended,
        readinessStatus: clean(state?.readinessStatus).toLowerCase(),
        readinessError: clean(state?.readinessError || panel?.lastPlanReadinessPreviewError)
    };
}

function renderOption(panel, item, option, disabled) {
    const value = clean(option?.value);
    const selected = value === item.selectedValue;
    const recommended = option?.recommended === true;
    const recommendationText = clean(option?.answerEffect).toLowerCase() === 'defer' ? '建议暂缓' : '推荐';
    const impact = clean(option?.impact || option?.description || option?.recommendationReason);
    const id = `ai-clarification-${escapeHtml(panel, item.questionId)}-${escapeHtml(panel, value)}`.replace(/[^a-zA-Z0-9_-]/g, '-');
    return `
        <label class="ai-clarification-v2-option ${selected ? 'is-selected' : ''} ${recommended ? 'is-recommended' : ''}" for="${id}">
            <input
                id="${id}"
                type="radio"
                name="ai-clarification-${escapeHtml(panel, item.questionId)}"
                value="${escapeHtml(panel, value)}"
                data-ai-plan-option="true"
                data-question-id="${escapeHtml(panel, item.questionId)}"
                data-field="${escapeHtml(panel, item.field)}"
                ${selected ? 'checked' : ''}
                ${disabled ? 'disabled' : ''} />
            <span class="ai-clarification-v2-option-copy">
                <strong>${escapeHtml(panel, option?.label || value)}</strong>
                ${recommended ? `<small class="ai-clarification-v2-recommended">${recommendationText}</small>` : ''}
                ${impact ? `<span>${escapeHtml(panel, impact)}</span>` : ''}
            </span>
        </label>
    `;
}

function renderActiveQuestion(panel, item, presentation) {
    if (!item) return '';
    const readOnly = panel?._isPlanSnapshotReadOnly?.() === true;
    const disabled = readOnly || item.confirming;
    const hasAnswerOptions = asArray(item.options).filter(option => clean(option?.value)).length >= 2;
    const status = item.confirming
        ? '<div class="ai-clarification-v2-status is-confirming" role="status">已选择，正在等待权威 Readiness 确认…</div>'
        : item.failed
            ? `<div class="ai-clarification-v2-status is-error" role="alert">${escapeHtml(panel, presentation.readinessError || '确认失败，请重新选择或重试。')}</div>`
            : item.unconfirmed
                ? '<div class="ai-clarification-v2-status is-error" role="status">该选择尚未被后端确认，请重新选择。</div>'
                : '';
    return `
        <fieldset class="ai-clarification-v2-question" data-ai-hook="clarification-question" data-field="${escapeHtml(panel, item.field)}">
            <legend tabindex="-1" data-ai-hook="clarification-title">${escapeHtml(panel, item.title || '请确认关键问题')}</legend>
            ${item.why ? `<p class="ai-clarification-v2-why">${escapeHtml(panel, item.why)}</p>` : ''}
            ${hasAnswerOptions ? `
                <div class="ai-clarification-v2-options">
                    ${asArray(item.options).slice(0, 4).map(option => renderOption(panel, item, option, disabled)).join('')}
                </div>
                <button type="button" class="ai-clarification-v2-other" data-ai-action="clarification-other" ${disabled ? 'disabled' : ''}>其他 / 补充说明</button>
                <div class="ai-clarification-v2-manual" data-ai-hook="clarification-manual" hidden>
                <label>
                    <span>补充内容将用于「${escapeHtml(panel, item.title || item.field)}」</span>
                    <textarea rows="3" data-ai-hook="clarification-manual-input" placeholder="输入你的补充说明"></textarea>
                </label>
                <div>
                    <button type="button" data-ai-action="clarification-manual-submit">提交</button>
                    <button type="button" data-ai-action="clarification-manual-cancel">取消</button>
                </div>
                </div>
            ` : '<div class="ai-clarification-v2-status" role="status">该项等待后端提供可回答选项，前端不会创建替代答案入口。</div>'}
            ${status}
        </fieldset>
    `;
}

function renderConfirmedSummary(panel, items) {
    if (!items.length) return '';
    return `
        <div class="ai-clarification-v2-confirmed" data-ai-hook="clarification-confirmed">
            <div class="ai-clarification-v2-subtitle">已确认</div>
            ${items.map(item => `
                <div class="ai-clarification-v2-confirmed-row">
                    <span><strong>${escapeHtml(panel, item.title || item.field)}：</strong>${escapeHtml(panel, item.confirmedDisplayValue)}</span>
                    <button type="button" data-ai-action="clarification-edit" data-field="${escapeHtml(panel, item.field)}">修改</button>
                </div>
            `).join('')}
        </div>
    `;
}

function renderResources(panel, items) {
    if (!items.length) return '';
    return `
        <div class="ai-clarification-v2-resources" data-ai-hook="clarification-resources">
            ${items.map(item => `
                <div>
                    <strong>${escapeHtml(panel, item.title || '资源待补齐')}</strong>
                    <span>${item.blocksBuild === true
                        ? '当前权威 Readiness 将其标记为构建阻断；完整资源补齐将在后续阶段处理。'
                        : '该资源将在构建后补齐，本阶段不提前展示绑定控件。'}</span>
                </div>
            `).join('')}
        </div>
    `;
}

export function renderAiClarification(panel, plan) {
    const presentation = deriveAiClarificationPresentation(panel, plan);
    if (!presentation.activeQuestion && !presentation.confirmedItems.length && !presentation.resourceItems.length) {
        return '<div class="ai-plan-v2-ready" data-ai-hook="clarification-ready"><strong>方案已就绪</strong><span>关键问题已确认，可复核后开始构建。</span></div>';
    }
    return `
        <section class="ai-clarification-v2" data-ai-hook="clarification-workspace">
            <div class="ai-clarification-v2-header">
                <div>
                    <span>关键问题</span>
                    <strong>${presentation.unresolvedCount > 0
                        ? `还需确认 ${presentation.unresolvedCount} 项${presentation.activeQuestion ? ' · 当前第 1 项' : ''}`
                        : '关键问题已确认'}</strong>
                </div>
                ${presentation.canAcceptAllRecommended
                    ? '<button type="button" data-ai-action="clarification-accept-recommended">采用全部推荐项</button>'
                    : ''}
            </div>
            ${renderActiveQuestion(panel, presentation.activeQuestion, presentation)}
            ${renderConfirmedSummary(panel, presentation.confirmedItems)}
            ${renderResources(panel, presentation.resourceItems)}
        </section>
    `;
}

function requestConfirmation(panel, plan, field, reason) {
    const runtime = getRuntime(panel);
    runtime.pendingFields.add(field);
    runtime.awaitingNextFocus = true;
    panel._requestPlanReadinessPreview?.(plan, { reason });
    panel._renderPlanWorkspace?.(plan);
}

export function bindAiClarificationInteractions(panel, root, plan) {
    if (!root) return;
    root.querySelectorAll('[data-ai-plan-option="true"]').forEach(input => {
        input.addEventListener('change', () => {
            if (!input.checked) return;
            const questionId = input.dataset.questionId || '';
            const field = input.dataset.field || '';
            panel._selectPlanQuestionOption?.(questionId, input.value || '');
            requestConfirmation(panel, plan, field, 'clarification_answer');
        });
        input.addEventListener('keydown', event => {
            if (event.key === 'Enter') {
                event.preventDefault();
                input.click?.();
            }
        });
    });

    const questionRoot = root.querySelector('[data-ai-hook="clarification-question"]');
    root.querySelector('[data-ai-action="clarification-other"]')?.addEventListener('click', () => {
        const manual = questionRoot?.querySelector('[data-ai-hook="clarification-manual"]');
        if (!manual) return;
        manual.hidden = false;
        manual.querySelector('[data-ai-hook="clarification-manual-input"]')?.focus?.();
    });
    root.querySelector('[data-ai-action="clarification-manual-cancel"]')?.addEventListener('click', () => {
        const manual = questionRoot?.querySelector('[data-ai-hook="clarification-manual"]');
        if (!manual) return;
        manual.hidden = true;
        root.querySelector('[data-ai-action="clarification-other"]')?.focus?.();
    });
    root.querySelector('[data-ai-action="clarification-manual-submit"]')?.addEventListener('click', () => {
        const input = questionRoot?.querySelector('[data-ai-hook="clarification-manual-input"]');
        const item = deriveAiClarificationPresentation(panel, plan).activeQuestion;
        const value = clean(input?.value);
        if (!item || !value) return;
        panel._customInputPlanQuestion?.(item.questionId, value);
        requestConfirmation(panel, plan, item.field, 'clarification_text');
    });
    questionRoot?.querySelector('[data-ai-hook="clarification-manual-input"]')?.addEventListener('keydown', event => {
        if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
            event.preventDefault();
            root.querySelector('[data-ai-action="clarification-manual-submit"]')?.click?.();
        }
    });

    root.querySelectorAll('[data-ai-action="clarification-edit"]').forEach(button => {
        button.addEventListener('click', () => {
            const runtime = getRuntime(panel);
            runtime.editingFields.add(button.dataset.field || '');
            runtime.focusField = button.dataset.field || '';
            panel._renderPlanWorkspace?.(plan);
        });
    });
    root.querySelector('[data-ai-action="clarification-accept-recommended"]')?.addEventListener('click', () => {
        panel._handlePlanUseRecommendedDefaultsClick?.(plan);
    });

    const runtime = getRuntime(panel);
    const focusField = runtime.focusField;
    const activeTitle = root.querySelector('[data-ai-hook="clarification-title"]');
    const activeField = questionRoot?.dataset.field || '';
    const shouldFocus = Boolean(activeTitle) && (
        (focusField && activeField === focusField) ||
        (runtime.awaitingNextFocus && runtime.lastActiveField && activeField !== runtime.lastActiveField)
    );
    runtime.lastActiveField = activeField;
    if (shouldFocus) {
        const schedule = typeof requestAnimationFrame === 'function' ? requestAnimationFrame : callback => callback();
        schedule(() => activeTitle.focus?.({ preventScroll: true }));
        runtime.focusField = '';
        runtime.awaitingNextFocus = false;
    } else if (!activeField) {
        runtime.awaitingNextFocus = false;
    }
}

export const aiPanelClarificationPresentationTestApi = {
    isSafeRecommendedItem,
    readField
};
