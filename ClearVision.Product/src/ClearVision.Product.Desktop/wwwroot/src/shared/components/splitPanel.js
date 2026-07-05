/**
 * 分割面板组件
 * 可拖拽调整左右或上下面板大小
 */

export class SplitPanel {
    constructor(containerId, options = {}) {
        this.container = typeof containerId === 'string'
            ? document.getElementById(containerId)
            : containerId;

        this.options = {
            direction: 'horizontal', // 'horizontal' | 'vertical'
            initialRatio: 0.5,      // 初始分割比例 (0-1)
            minSize: 100,           // 最小尺寸
            collapsible: false,     // 是否可折叠
            onResize: null,         // 尺寸变化回调
            ...options
        };

        this.isDragging = false;
        this.startPos = 0;
        this.startRatio = 0;
        this.currentRatio = this.options.initialRatio;
        this.dragContainerSize = 1;
        this.pendingRatio = null;
        this.resizeFrameId = null;
        this.resizeFrameIsTimeout = false;

        // AbortController 用于统一管理事件监听
        this._abortController = new AbortController();
        this._signal = this._abortController.signal;

        this.initialize();
    }

    initialize() {
        this.container.className = `cv-split-panel cv-split-${this.options.direction}`;
        this.container.innerHTML = '';

        // 第一个面板
        this.firstPanel = document.createElement('div');
        this.firstPanel.className = 'cv-split-first';

        // 分割条
        this.splitter = document.createElement('div');
        this.splitter.className = 'cv-splitter';

        // 分割条手柄
        const handle = document.createElement('div');
        handle.className = 'cv-splitter-handle';
        this.splitter.appendChild(handle);

        // 第二个面板
        this.secondPanel = document.createElement('div');
        this.secondPanel.className = 'cv-split-second';

        this.container.appendChild(this.firstPanel);
        this.container.appendChild(this.splitter);
        this.container.appendChild(this.secondPanel);

        // 绑定事件
        this.bindEvents();

        // 应用初始比例
        this.applyRatio(this.currentRatio);
    }

    bindEvents() {
        // 鼠标按下
        this.splitter.addEventListener('mousedown', (e) => {
            this.isDragging = true;
            this.startPos = this.options.direction === 'horizontal' ? e.clientX : e.clientY;
            this.startRatio = this.currentRatio;
            this.dragContainerSize = Math.max(
                1,
                this.options.direction === 'horizontal'
                    ? this.container.clientWidth
                    : this.container.clientHeight);

            // 添加拖动样式
            this.splitter.classList.add('dragging');
            document.body.style.cursor = this.options.direction === 'horizontal' ? 'col-resize' : 'row-resize';
            document.body.style.userSelect = 'none';

            e.preventDefault();
        }, { signal: this._signal });

        // 鼠标移动 - 使用AbortController管理
        document.addEventListener('mousemove', (e) => {
            if (!this.isDragging) return;

            const currentPos = this.options.direction === 'horizontal' ? e.clientX : e.clientY;
            const delta = currentPos - this.startPos;

            // 计算新比例
            const deltaRatio = delta / this.dragContainerSize;
            let newRatio = this.startRatio + deltaRatio;

            // 限制最小尺寸
            const minRatio = this.options.minSize / this.dragContainerSize;
            const maxRatio = 1 - minRatio;
            newRatio = Math.max(minRatio, Math.min(maxRatio, newRatio));

            this.scheduleRatioUpdate(newRatio);
        }, { signal: this._signal });

        // 鼠标释放 - 使用AbortController管理
        document.addEventListener('mouseup', () => {
            if (this.isDragging) {
                this.isDragging = false;
                this.splitter.classList.remove('dragging');
                document.body.style.cursor = '';
                document.body.style.userSelect = '';
                this.flushPendingRatio();
            }
        }, { signal: this._signal });

        // 窗口大小变化 - 使用AbortController管理
        window.addEventListener('resize', () => {
            this.applyRatio(this.currentRatio);
        }, { signal: this._signal });
    }

    /**
     * 销毁面板，清理所有事件监听
     */
    destroy() {
        // 中止所有通过AbortController注册的事件监听
        this._abortController.abort();
        this.cancelPendingFrame();

        // 清理DOM引用
        this.firstPanel = null;
        this.secondPanel = null;
        this.splitter = null;
        this.container = null;
    }

    scheduleRatioUpdate(ratio) {
        this.currentRatio = ratio;
        this.pendingRatio = ratio;

        if (this.resizeFrameId !== null) {
            return;
        }

        if (typeof window.requestAnimationFrame === 'function') {
            this.resizeFrameIsTimeout = false;
            this.resizeFrameId = window.requestAnimationFrame(() => {
                this.resizeFrameId = null;
                this.applyPendingRatio();
            });
            return;
        }

        const scheduleTimeout = typeof window.setTimeout === 'function'
            ? window.setTimeout.bind(window)
            : setTimeout;
        this.resizeFrameIsTimeout = true;
        this.resizeFrameId = scheduleTimeout(() => {
            this.resizeFrameId = null;
            this.applyPendingRatio();
        }, 16);
    }

    cancelPendingFrame() {
        if (this.resizeFrameId === null) {
            return;
        }

        if (this.resizeFrameIsTimeout) {
            const clearScheduledTimeout = typeof window.clearTimeout === 'function'
                ? window.clearTimeout.bind(window)
                : clearTimeout;
            clearScheduledTimeout(this.resizeFrameId);
        } else if (typeof window.cancelAnimationFrame === 'function') {
            window.cancelAnimationFrame(this.resizeFrameId);
        } else {
            const clearScheduledTimeout = typeof window.clearTimeout === 'function'
                ? window.clearTimeout.bind(window)
                : clearTimeout;
            clearScheduledTimeout(this.resizeFrameId);
        }

        this.resizeFrameId = null;
        this.resizeFrameIsTimeout = false;
    }

    clearPendingRatio() {
        this.cancelPendingFrame();
        this.pendingRatio = null;
    }

    flushPendingRatio() {
        if (this.pendingRatio === null) {
            return;
        }

        this.cancelPendingFrame();
        this.applyPendingRatio();
    }

    applyPendingRatio() {
        if (this.pendingRatio === null) {
            return;
        }

        const ratio = this.pendingRatio;
        this.pendingRatio = null;
        this.applyRatio(ratio);

        if (this.options.onResize) {
            this.options.onResize(this.currentRatio, this.firstPanel, this.secondPanel);
        }
    }

    applyRatio(ratio) {
        const percentage = ratio * 100;
        this.firstPanel.style.flex = `0 0 ${percentage}%`;
        this.secondPanel.style.flex = `1 1 auto`;
    }

    /**
     * 获取第一个面板容器
     */
    getFirstPanel() {
        return this.firstPanel;
    }

    /**
     * 获取第二个面板容器
     */
    getSecondPanel() {
        return this.secondPanel;
    }

    /**
     * 设置分割比例
     */
    setRatio(ratio) {
        this.clearPendingRatio();
        this.currentRatio = Math.max(0.1, Math.min(0.9, ratio));
        this.applyRatio(this.currentRatio);
    }

    /**
     * 获取当前比例
     */
    getRatio() {
        return this.currentRatio;
    }

    /**
     * 设置第一个面板内容
     */
    setFirstContent(element) {
        this.firstPanel.innerHTML = '';
        if (typeof element === 'string') {
            this.firstPanel.innerHTML = element;
        } else {
            this.firstPanel.appendChild(element);
        }
    }

    /**
     * 设置第二个面板内容
     */
    setSecondContent(element) {
        this.secondPanel.innerHTML = '';
        if (typeof element === 'string') {
            this.secondPanel.innerHTML = element;
        } else {
            this.secondPanel.appendChild(element);
        }
    }

    /**
     * 折叠第一个面板
     */
    collapseFirst() {
        this.clearPendingRatio();
        this.currentRatio = 0.05;
        this.applyRatio(this.currentRatio);
        this.firstPanel.classList.add('collapsed');
    }

    /**
     * 折叠第二个面板
     */
    collapseSecond() {
        this.clearPendingRatio();
        this.currentRatio = 0.95;
        this.applyRatio(this.currentRatio);
        this.secondPanel.classList.add('collapsed');
    }

    /**
     * 展开面板
     */
    expand() {
        this.clearPendingRatio();
        this.currentRatio = this.options.initialRatio;
        this.applyRatio(this.currentRatio);
        this.firstPanel.classList.remove('collapsed');
        this.secondPanel.classList.remove('collapsed');
    }
}

export default SplitPanel;
