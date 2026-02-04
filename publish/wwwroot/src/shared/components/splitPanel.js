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

            // 添加拖动样式
            this.splitter.classList.add('dragging');
            document.body.style.cursor = this.options.direction === 'horizontal' ? 'col-resize' : 'row-resize';
            document.body.style.userSelect = 'none';

            e.preventDefault();
        });

        // 鼠标移动
        document.addEventListener('mousemove', (e) => {
            if (!this.isDragging) return;

            const currentPos = this.options.direction === 'horizontal' ? e.clientX : e.clientY;
            const delta = currentPos - this.startPos;
            const containerSize = this.options.direction === 'horizontal'
                ? this.container.clientWidth
                : this.container.clientHeight;

            // 计算新比例
            const deltaRatio = delta / containerSize;
            let newRatio = this.startRatio + deltaRatio;

            // 限制最小尺寸
            const minRatio = this.options.minSize / containerSize;
            const maxRatio = 1 - minRatio;
            newRatio = Math.max(minRatio, Math.min(maxRatio, newRatio));

            this.currentRatio = newRatio;
            this.applyRatio(newRatio);

            if (this.options.onResize) {
                this.options.onResize(this.currentRatio, this.firstPanel, this.secondPanel);
            }
        });

        // 鼠标释放
        document.addEventListener('mouseup', () => {
            if (this.isDragging) {
                this.isDragging = false;
                this.splitter.classList.remove('dragging');
                document.body.style.cursor = '';
                document.body.style.userSelect = '';
            }
        });

        // 窗口大小变化
        window.addEventListener('resize', () => {
            this.applyRatio(this.currentRatio);
        });
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
        this.applyRatio(0.05);
        this.firstPanel.classList.add('collapsed');
    }

    /**
     * 折叠第二个面板
     */
    collapseSecond() {
        this.applyRatio(0.95);
        this.secondPanel.classList.add('collapsed');
    }

    /**
     * 展开面板
     */
    expand() {
        this.currentRatio = this.options.initialRatio;
        this.applyRatio(this.currentRatio);
        this.firstPanel.classList.remove('collapsed');
        this.secondPanel.classList.remove('collapsed');
    }
}

export default SplitPanel;
