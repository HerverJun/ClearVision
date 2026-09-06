import { aiIcon } from './aiIcons.js';

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
            lastActiveField: '',
            selectedKey: '',
            manualDrafts: new Map(),
            manualOpen: new Set(),
            resourceDrafts: new Map(),
            planIdentity: ''
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
    const identity = `${panel?.sessionId || ''}:${plan?.planId || ''}`;
    if (runtime.planIdentity && runtime.planIdentity !== identity) {
        clarificationRuntimeByPanel.delete(panel);
        return deriveAiClarificationPresentation(panel, plan);
    }
    runtime.planIdentity = identity;
    const projectedQueue = asArray(projection?.clarificationQueue);
    const queue = projectedQueue.length ? projectedQueue : asArray(plan?.questions);
    const projectedBatch = asArray(projection?.clarificationBatch);
    const batch = projectedBatch.length ? projectedBatch : queue.slice(0, 3);
    const projectedResources = asArray(projection?.missingResources);
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
        const deferred = rawItem?.deferred === true;
        const confirmed = Boolean(confirmedAnswer?.resolved) && !hasPendingChange && !deferred;
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
            deferred,
            resource: isResourceItem(rawItem)
        };
    });

    for (const item of items) {
        if (item.confirmed && runtime.pendingFields.has(item.field) && !panel?.workspaceSnapshotDirty) {
            runtime.pendingFields.delete(item.field);
            runtime.editingFields.delete(item.field);
            runtime.focusField = '';
            runtime.manualDrafts.delete(item.field);
            runtime.manualOpen.delete(item.field);
            if (runtime.selectedKey === `question:${item.field}`) runtime.selectedKey = '';
        }
    }

    const selectedQuestion = items.find(item => !item.resource && runtime.selectedKey === `question:${item.field}`);
    const activeQuestion = selectedQuestion || items.find(item =>
        !item.resource && !item.deferred &&
        (item.confirming || item.failed || item.unconfirmed || item.editing ||
            (runtime.pendingFields.has(item.field) && panel?.workspaceSnapshotDirty))
    ) || items.find(item =>
        !item.resource && !item.deferred && !item.confirmed
    ) || null;
    const unresolved = items.filter(item => !item.confirmed && !item.deferred && !item.resource);
    const confirmedItems = items.filter(item => item.confirmed && !item.deferred && item !== activeQuestion);
    const deferredItems = items.filter(item => item.deferred && !item.resource);
    const resourceItems = projectedResources
        .filter(item => item?.answered !== true)
        .map(item => ({
            ...item,
            resource: true,
            title: clean(item?.title) || clean(item?.description) || '资源待补齐',
            blocksBuild: item?.blocksBuild === true
        }));
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

    const questionItems = items.filter(item => !item.resource).map(item => ({
        ...item, key: `question:${item.field}`, blocksBuild: !item.deferred && item.blocksBuild !== false
    }));
    const resourceTodos = resourceItems.map((item, index) => ({
        ...item, resourceIndex: index,
        key: `resource:${item.canonicalId || item.resourceKey || index}`
    }));
    const todoItems = [...questionItems, ...resourceTodos];
    if (!todoItems.some(item => item.key === runtime.selectedKey)) runtime.selectedKey = '';
    const defaultTodo = todoItems.find(item => !item.confirmed && !item.deferred && item.blocksBuild) ||
        todoItems.find(item => !item.confirmed && !item.deferred) || null;
    const activeKey = runtime.selectedKey || (activeQuestion ? `question:${activeQuestion.field}` : defaultTodo?.key) || '';
    const summary = panel?._buildPlanMissingSummary?.(plan);
    return {
        activeQuestion: activeKey.startsWith('resource:') ? null : activeQuestion,
        activeKey,
        todoItems,
        totalCount: summary?.totalCount ?? unresolved.length + resourceItems.length,
        confirmedItems,
        deferredItems,
        resourceItems,
        unresolvedCount: unresolved.length,
        unanswerableBlockerCount: asArray(
            asArray(projection?.readiness?.blockers).length
                ? projection.readiness.blockers
                : plan?.buildReadiness?.blockers
        ).filter(item =>
            item?.blocksBuild === true && !isResourceItem(item)
        ).length,
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
        ? '<div class="ai-clarification-v2-status is-confirming" role="status">已选择，正在校验构建条件…</div>'
        : item.failed
            ? `<div class="ai-clarification-v2-status is-error" role="alert">${escapeHtml(panel, presentation.readinessError || '确认失败，请重新选择或重试。')}</div>`
            : item.unconfirmed
                ? '<div class="ai-clarification-v2-status is-error" role="status">该选择尚未确认，请重新选择。</div>'
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
                <div class="ai-clarification-v2-manual" data-ai-hook="clarification-manual" ${getRuntime(panel).manualOpen.has(item.field) ? '' : 'hidden'}>
                <label>
                    <span>补充内容将用于「${escapeHtml(panel, item.title || item.field)}」</span>
                    <textarea rows="3" ${disabled ? 'disabled' : ''} data-ai-hook="clarification-manual-input" placeholder="输入你的补充说明">${escapeHtml(panel, getRuntime(panel).manualDrafts.get(item.field) || '')}</textarea>
                </label>
                <div>
                    <button type="button" data-ai-action="clarification-manual-submit" ${disabled ? 'disabled' : ''}>提交</button>
                    <button type="button" data-ai-action="clarification-manual-cancel">取消</button>
                </div>
                </div>
            ` : '<div class="ai-clarification-v2-status" role="status">该问题暂时没有可选答案，请重试规划。</div>'}
            ${status}
        </fieldset>
    `;
}


function renderTodoRow(panel, item, presentation) {
    const active = item.key === presentation.activeKey;
    const status = item.confirming ? '校验中' : item.failed ? '确认失败' : item.unconfirmed ? '尚未确认'
        : item.confirmed ? '已确认' : item.deferred ? '已暂缓' : item.resource ? '待绑定' : '待确认';
    const content = !active ? '' : item.resource
        ? panel?._renderResourceAuditTaskCard?.(item, panel?._getMissingResourceActionModel?.(item) || {}, item.resourceIndex) || ''
        : renderActiveQuestion(panel, item, presentation);
    const hook = item.resource ? 'clarification-resources' : item.confirmed ? 'clarification-confirmed' : item.deferred ? 'clarification-deferred' : 'clarification-pending';
    return `<article class="ai-todo-row ${active ? 'is-active' : ''}" data-ai-hook="${hook}" data-ai-todo-key="${escapeHtml(panel, item.key)}">
        <button type="button" class="ai-todo-toggle" data-ai-action="todo-select" data-todo-key="${escapeHtml(panel, item.key)}" aria-expanded="${active}">
            ${aiIcon(item.confirmed ? 'check' : 'chevron-right')}
            <span><strong>${escapeHtml(panel, item.title)}</strong>${item.confirmedDisplayValue ? `<small>${escapeHtml(panel, item.confirmedDisplayValue)}</small>` : ''}</span>
            <small class="ai-todo-status">${status}</small>
        </button>
        ${active ? `<div class="ai-todo-content">${content}</div>` : ''}
    </article>`;
}

function renderTodoGroups(panel, presentation) {
    const groups = [
        ['构建前必需', presentation.todoItems.filter(item => !item.confirmed && item.blocksBuild)],
        ['运行前必需', presentation.todoItems.filter(item => !item.confirmed && !item.blocksBuild)],
        ['已确认', presentation.todoItems.filter(item => item.confirmed)]
    ];
    return groups.filter(([, items]) => items.length).map(([title, items]) => `<div class="ai-todo-group">
        <h3>${title}<span>${items.length}</span></h3>
        ${items.map(item => renderTodoRow(panel, item, presentation)).join('')}
    </div>`).join('');
}

export function renderAiClarification(panel, plan) {
    const presentation = deriveAiClarificationPresentation(panel, plan);
    if (!presentation.activeQuestion && !presentation.confirmedItems.length && !presentation.deferredItems.length && !presentation.resourceItems.length) {
        if (presentation.unanswerableBlockerCount > 0) {
            return `
                <div class="ai-plan-v2-clarification-gap" data-ai-hook="clarification-contract-gap" role="alert">
                    <strong>暂无可回答的关键问题</strong>
                    <span>构建条件尚未满足，当前方案没有提供可回答的问题，请重试规划。</span>
                    <button type="button" data-ai-action="planning-retry">重试规划</button>
                </div>
            `;
        }
        return '<div class="ai-plan-v2-ready" data-ai-hook="clarification-ready"><strong>方案已就绪</strong><span>关键问题已确认，可复核后开始构建。</span></div>';
    }
    return `
        <section class="ai-clarification-v2" data-ai-hook="clarification-workspace" tabindex="-1">
            <div class="ai-clarification-v2-header">
                <div>
                    <strong>待处理事项</strong>
                    <span>${presentation.totalCount > 0 ? `待补齐 ${presentation.totalCount} 项` : '当前事项已确认'}</span>
                </div>
                ${presentation.canAcceptAllRecommended
                    ? '<button type="button" data-ai-action="clarification-accept-recommended">采用全部推荐项</button>'
                    : ''}
            </div>
            ${renderTodoGroups(panel, presentation)}
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
    const presentation = deriveAiClarificationPresentation(panel, plan);
    root.querySelectorAll('[data-ai-action="todo-select"]').forEach(button => {
        button.addEventListener('click', () => {
            const runtime = getRuntime(panel);
            runtime.selectedKey = button.dataset.todoKey;
            const selected = presentation.todoItems.find(item => item.key === runtime.selectedKey);
            if (selected && !selected.resource) {
                runtime.editingFields.clear();
                if (selected.confirmed || selected.deferred) runtime.editingFields.add(selected.field);
            }
            panel._renderPlanWorkspace?.(plan);
            const selectedButton = Array.from(root.querySelectorAll('[data-ai-action="todo-select"]')).find(item => item.dataset.todoKey === runtime.selectedKey);
            selectedButton?.focus?.({ preventScroll: true });
        });
    });
    root.querySelectorAll('[data-resource-input]').forEach(input => {
        const key = input.closest('[data-ai-todo-key]')?.dataset.aiTodoKey;
        const runtime = getRuntime(panel);
        if (runtime.resourceDrafts.has(key)) input.value = runtime.resourceDrafts.get(key);
        input.addEventListener('input', () => runtime.resourceDrafts.set(key, input.value));
        input.addEventListener('change', () => runtime.resourceDrafts.set(key, input.value));
    });
    root.querySelectorAll('[data-resource-action]').forEach(button => {
        button.addEventListener('click', () => {
            const index = Number.parseInt(button.dataset.resourceIndex || '-1', 10);
            const resource = Number.isInteger(index) && index >= 0 ? presentation.resourceItems[index] : null;
            if (!resource) return;
            const card = button.closest?.('.ai-followup-resource-task');
            const input = card?.querySelector?.('[data-resource-input="true"]');
            panel?._handleMissingResourceAction?.(resource, button.dataset.resourceAction || '', {
                value: input?.value ?? ''
            });
        });
    });
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
        getRuntime(panel).manualOpen.add(questionRoot.dataset.field);
        manual.querySelector('[data-ai-hook="clarification-manual-input"]')?.focus?.();
    });
    root.querySelector('[data-ai-action="clarification-manual-cancel"]')?.addEventListener('click', () => {
        const manual = questionRoot?.querySelector('[data-ai-hook="clarification-manual"]');
        if (!manual) return;
        manual.hidden = true;
        getRuntime(panel).manualOpen.delete(questionRoot.dataset.field);
        root.querySelector('[data-ai-action="clarification-other"]')?.focus?.();
    });
    questionRoot?.querySelector('[data-ai-hook="clarification-manual-input"]')?.addEventListener('input', event => {
        getRuntime(panel).manualDrafts.set(questionRoot.dataset.field, event.target.value);
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
        if ((event.ctrlKey || event.metaKey) && event.key === 'Enter' && !event.isComposing) {
            event.preventDefault();
            root.querySelector('[data-ai-action="clarification-manual-submit"]')?.click?.();
        }
    });

    root.querySelectorAll('[data-ai-action="clarification-edit"]').forEach(button => {
        button.addEventListener('click', () => {
            const runtime = getRuntime(panel);
            runtime.editingFields.add(button.dataset.field || '');
            runtime.selectedKey = `question:${button.dataset.field || ''}`;
            runtime.focusField = button.dataset.field || '';
            panel._renderPlanWorkspace?.(plan);
        });
    });
    root.querySelector('[data-ai-action="clarification-accept-recommended"]')?.addEventListener('click', () => {
        panel._handlePlanUseRecommendedDefaultsClick?.(plan);
    });
    root.querySelector('[data-ai-action="planning-retry"]')?.addEventListener('click', () => {
        panel._retryPlanningLifecycle?.();
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
        schedule(() => {
            const current = typeof document !== 'undefined' ? document.activeElement : null;
            if (!current || current === document.body || root.contains?.(current)) {
                activeTitle.focus?.({ preventScroll: true });
            }
        });
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
