import inspectionController from './inspectionController.js';

export const INSPECTION_CAPABILITY_OWNER_ID = 'inspection-capability-v2';

function resolveElement(target) {
    if (!target) {
        return null;
    }

    if (typeof target === 'string') {
        return typeof document !== 'undefined' ? document.getElementById(target) : null;
    }

    return target;
}

function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function normalizeStatus(state = {}) {
    if (state.isRealtime || state.IsRealtime) {
        return 'realtime';
    }

    if (state.isRunning || state.IsRunning || String(state.status || state.Status || '').toLowerCase() === 'running') {
        return 'running';
    }

    return String(state.status || state.Status || 'idle').toLowerCase();
}

export class InspectionCapabilityAdapter {
    constructor({ inspectionControllerRef = inspectionController } = {}) {
        this.inspectionController = inspectionControllerRef;
    }

    getState() {
        return this.inspectionController.getState?.() || {};
    }

    getLastResult() {
        return this.inspectionController.getLastResult?.() || null;
    }

    setProject(projectId) {
        this.inspectionController.setProject?.(projectId);
    }

    subscribeState(listener) {
        return this.inspectionController.subscribeState?.(listener) || (() => {});
    }

    onCompleted(listener) {
        return this.inspectionController.onInspectionCompleted?.(listener) || (() => {});
    }

    onError(listener) {
        return this.inspectionController.onInspectionError?.(listener) || (() => {});
    }

    async executeSingle() {
        return await this.inspectionController.executeSingle();
    }

    async startRealtime() {
        if (typeof this.inspectionController.startRealtimeFlowMode === 'function') {
            return await this.inspectionController.startRealtimeFlowMode();
        }

        return await this.inspectionController.startRealtime();
    }

    async stopRealtime() {
        return await this.inspectionController.stopRealtime();
    }
}

export function createInspectionCapabilityAdapter(options = {}) {
    return new InspectionCapabilityAdapter(options);
}

export class InspectionCapabilityOwner {
    constructor(container, {
        adapter,
        showToast = () => {},
        imageSink = null
    } = {}) {
        this.container = resolveElement(container);
        if (!this.container) {
            throw new Error('InspectionCapabilityOwner requires a container.');
        }
        if (!adapter) {
            throw new Error('InspectionCapabilityOwner requires an adapter.');
        }

        this.adapter = adapter;
        this.showToast = typeof showToast === 'function' ? showToast : () => {};
        this.imageSink = imageSink;
        this.projectId = null;
        this.state = this.adapter.getState();
        this.lastResult = this.adapter.getLastResult();
        this.errorMessage = '';
        this.disposed = false;
        this.unsubscribes = [];
        this.handleClick = this.handleClick.bind(this);

        this.container.dataset.inspectionOwner = INSPECTION_CAPABILITY_OWNER_ID;
        this.container.addEventListener('click', this.handleClick);
        this.unsubscribes.push(
            this.adapter.subscribeState(state => this.applyState(state)),
            this.adapter.onCompleted(result => this.handleInspectionResult(result)),
            this.adapter.onError(error => this.handleInspectionError(error))
        );
        this.render();
    }

    setProjectContext(projectId) {
        this.projectId = projectId || null;
        this.adapter.setProject(this.projectId);
        if (!this.projectId) {
            this.lastResult = null;
        }
        this.render();
    }

    applyState(state) {
        if (this.disposed) {
            return;
        }

        this.state = state || {};
        this.render();
    }

    handleInspectionResult(result) {
        if (this.disposed) {
            return;
        }

        this.lastResult = result || null;
        this.errorMessage = '';
        this.imageSink?.(result);
        this.render();
    }

    handleInspectionError(error) {
        if (this.disposed) {
            return;
        }

        this.errorMessage = error?.message || String(error || '检测失败');
        this.render();
    }

    async handleClick(event) {
        const action = event.target?.closest?.('[data-inspection-action]')?.dataset?.inspectionAction;
        if (!action || this.disposed) {
            return;
        }

        event.preventDefault();
        try {
            if (action === 'single') {
                await this.adapter.executeSingle();
            } else if (action === 'start-realtime') {
                await this.adapter.startRealtime();
            } else if (action === 'stop-realtime') {
                await this.adapter.stopRealtime();
            } else if (action === 'clear') {
                this.lastResult = null;
                this.errorMessage = '';
                this.render();
            }
        } catch (error) {
            this.handleInspectionError(error);
            this.showToast(error?.message || '检测命令失败', 'error');
        }
    }

    refresh() {
        this.state = this.adapter.getState();
        this.lastResult = this.adapter.getLastResult() || this.lastResult;
        this.render();
    }

    render() {
        if (this.disposed || !this.container) {
            return;
        }

        const status = normalizeStatus(this.state);
        const running = status === 'running' || status === 'realtime';
        const resultStatus = this.lastResult?.status || this.lastResult?.Status || '--';
        const resultId = this.lastResult?.id || this.lastResult?.resultId || this.lastResult?.Id || this.lastResult?.ResultId || '--';
        this.container.innerHTML = `
            <section class="inspection-panel inspection-capability-owner" data-owner="${INSPECTION_CAPABILITY_OWNER_ID}">
                <h3 class="panel-title">检测控制</h3>
                <div class="inspection-status-card">
                    <span>状态</span>
                    <strong>${escapeHtml(status)}</strong>
                    <small>${this.projectId ? `工程 ${escapeHtml(this.projectId)}` : '未打开工程'}</small>
                </div>
                <div class="inspection-actions">
                    <button type="button" class="btn btn-primary" data-inspection-action="single" ${!this.projectId || running ? 'disabled' : ''}>单次检测</button>
                    <button type="button" class="btn btn-secondary" data-inspection-action="start-realtime" ${!this.projectId || running ? 'disabled' : ''}>开始实时</button>
                    <button type="button" class="btn btn-danger" data-inspection-action="stop-realtime" ${running ? '' : 'disabled'}>停止实时</button>
                    <button type="button" class="btn btn-secondary" data-inspection-action="clear">清空结果</button>
                </div>
                ${this.errorMessage ? `<div class="inspection-error" role="alert">${escapeHtml(this.errorMessage)}</div>` : ''}
                <div class="inspection-progress">
                    <span>进度</span>
                    <progress value="${Number(this.state.progress || this.state.Progress || 0)}" max="100"></progress>
                </div>
                <div class="inspection-result-summary">
                    <h4>最近结果</h4>
                    <dl>
                        <div><dt>结果 ID</dt><dd>${escapeHtml(resultId)}</dd></div>
                        <div><dt>状态</dt><dd>${escapeHtml(resultStatus)}</dd></div>
                    </dl>
                </div>
            </section>
        `;
    }

    destroy() {
        this.dispose();
    }

    dispose() {
        if (this.disposed) {
            return;
        }

        this.disposed = true;
        this.unsubscribes.forEach(unsubscribe => {
            try {
                unsubscribe?.();
            } catch {
                // Best-effort cleanup.
            }
        });
        this.unsubscribes = [];
        this.container.removeEventListener('click', this.handleClick);
        delete this.container.dataset.inspectionOwner;
        this.container.innerHTML = '';
    }
}

export default InspectionCapabilityOwner;
