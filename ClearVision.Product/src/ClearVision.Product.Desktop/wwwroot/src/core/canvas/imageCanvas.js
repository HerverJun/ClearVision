import {
    cancelRectangleDraft,
    clampRectToBounds,
    commitRectangleDraft,
    createRectangleDraftSession,
    getRectHandlePoints,
    hitTestRectHandle,
    hitTestRectangle,
    normalizeRectFromPoints,
    nudgeRect,
    redoRectangleDraft,
    resizeRectByHandle,
    roundRect,
    screenToImagePoint,
    setRectangleDraftCurrent,
    translateRect,
    undoRectangleDraft
} from '../../features/flow-editor/roiGeometry.mjs';

function createLegacyOverlayId() {
    return `overlay_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
}

function normalizeOverlayNumber(value, fallback = 0) {
    const numberValue = Number(value);
    return Number.isFinite(numberValue) ? numberValue : fallback;
}

function compareOverlayOrder(left, right) {
    const layerCompare = String(left.layer || '').localeCompare(String(right.layer || ''), 'en', { sensitivity: 'case' });
    if (layerCompare !== 0) {
        return layerCompare;
    }

    const zCompare = normalizeOverlayNumber(left.zOrder) - normalizeOverlayNumber(right.zOrder);
    if (zCompare !== 0) {
        return zCompare;
    }

    return String(left.id || '').localeCompare(String(right.id || ''), 'en', { sensitivity: 'case' });
}

export function buildOverlayRenderCommands(overlays = []) {
    return overlays
        .filter(overlay => overlay?.visible !== false)
        .slice()
        .sort(compareOverlayOrder)
        .map(overlay => ({
            id: overlay.id,
            type: overlay.type,
            layer: overlay.layer || 'default',
            zOrder: normalizeOverlayNumber(overlay.zOrder),
            x: normalizeOverlayNumber(overlay.x),
            y: normalizeOverlayNumber(overlay.y),
            width: normalizeOverlayNumber(overlay.width),
            height: normalizeOverlayNumber(overlay.height),
            radius: normalizeOverlayNumber(overlay.radius, 0),
            points: Array.isArray(overlay.points)
                ? overlay.points
                    .map(point => ({
                        x: normalizeOverlayNumber(point?.x),
                        y: normalizeOverlayNumber(point?.y)
                    }))
                    .filter(point => Number.isFinite(point.x) && Number.isFinite(point.y))
                : [],
            color: overlay.color || '#ff0000',
            lineWidth: normalizeOverlayNumber(overlay.lineWidth, 2),
            fill: overlay.fill === true,
            fillColor: overlay.fillColor || 'rgba(255, 0, 0, 0.2)',
            text: overlay.text || '',
            fontSize: normalizeOverlayNumber(overlay.fontSize, 14),
            overlay
        }));
}

/**
 * 图像画布渲染器
 * 支持图像显示、缩放、平移、标注
 */

class ImageCanvas {
    constructor(canvasId, options = {}) {
        this.canvas = document.getElementById(canvasId);
        this.ctx = this.canvas.getContext('2d');

        // 图像数据
        this.image = null;
        this.imageData = null;

        // 视图状态
        this.scale = 1;
        this.offset = { x: 0, y: 0 };
        this.minScale = 0.1;
        this.maxScale = 10;

        // 交互状态
        this.isDragging = false;
        this.dragStart = { x: 0, y: 0 };
        this.lastMouse = { x: 0, y: 0 };

        // 标注层
        this.overlays = [];
        this.selectedOverlay = null;
        this.activeOverlayId = null;

        // 交互模式
        this.interactionMode = options.interactionMode || 'legacy';
        this.onOverlayChanged = options.onOverlayChanged || null;
        this.enableRightButtonPan = options.enableRightButtonPan ?? this.interactionMode === 'roi-rect';
        this.handleSize = options.handleSize || 10;
        this.minimumOverlaySize = options.minimumOverlaySize || 1;
        this.activeHandle = null;
        this.interactionState = null;
        this.roiDraftState = null;

        // 【关键修复】记录是否有待处理的重置视图（当画布尺寸为0时）
        this._pendingResetView = false;

        // 逻辑尺寸与 DPR
        this._dpr = 1;
        this._logicalWidth = 0;
        this._logicalHeight = 0;

        // 渲染调度（脏标记 + 单一 RAF）
        this._animationFrameId = null;
        this._dirty = true;
        this._drawFrameBound = this._drawFrame.bind(this);

        // ResizeObserver 节流
        this._resizeObserver = null;
        this._resizeRafId = null;

        // Blob URL 待清理（避免内存泄漏）
        this._imageUrlToRevoke = null;

        // 事件处理器引用（用于销毁时移除）
        this._resizeHandler = this.resize.bind(this);
        this._mouseDownHandler = this.handleMouseDown.bind(this);
        this._mouseMoveHandler = this.handleMouseMove.bind(this);
        this._mouseUpHandler = this.handleMouseUp.bind(this);
        this._wheelHandler = this.handleWheel.bind(this);
        this._dblClickHandler = this.handleDoubleClick.bind(this);
        this._contextMenuHandler = this.handleContextMenu.bind(this);
        this._keyDownHandler = this.handleKeyDown.bind(this);

        this.initialize();
    }

    /**
     * 初始化画布
     */
    initialize() {
        this.resize();

        // ResizeObserver：容器尺寸变化时自动 resize，避免 window.resize 精度不足
        if (typeof window !== 'undefined' && typeof window.ResizeObserver !== 'undefined' && this.canvas.parentElement) {
            this._resizeObserver = new window.ResizeObserver(() => {
                if (this._resizeRafId) cancelAnimationFrame(this._resizeRafId);
                this._resizeRafId = requestAnimationFrame(() => this.resize());
            });
            this._resizeObserver.observe(this.canvas.parentElement);
        } else {
            window.addEventListener('resize', this._resizeHandler);
        }

        // 绑定事件
        this.canvas.addEventListener('mousedown', this._mouseDownHandler);
        this.canvas.addEventListener('mousemove', this._mouseMoveHandler);
        this.canvas.addEventListener('mouseup', this._mouseUpHandler);
        this.canvas.addEventListener('wheel', this._wheelHandler);
        this.canvas.addEventListener('dblclick', this._dblClickHandler);
        this.canvas.addEventListener('contextmenu', this._contextMenuHandler);
        if (this.interactionMode === 'roi-rect' && !this.canvas.hasAttribute?.('tabindex')) {
            this.canvas.tabIndex = 0;
        }
        this.canvas.addEventListener('keydown', this._keyDownHandler);

        // 启动渲染循环（由脏标记驱动）
        this.invalidate();
    }

    /**
     * 销毁画布，清理所有事件监听和动画循环
     */
    destroy() {
        // 停止渲染循环
        if (this._animationFrameId) {
            cancelAnimationFrame(this._animationFrameId);
            this._animationFrameId = null;
        }

        // 移除 ResizeObserver
        if (this._resizeObserver) {
            this._resizeObserver.disconnect();
            this._resizeObserver = null;
        }
        if (this._resizeRafId) {
            cancelAnimationFrame(this._resizeRafId);
            this._resizeRafId = null;
        }

        // 移除窗口事件监听
        window.removeEventListener('resize', this._resizeHandler);

        // 移除画布事件监听
        this.canvas.removeEventListener('mousedown', this._mouseDownHandler);
        this.canvas.removeEventListener('mousemove', this._mouseMoveHandler);
        this.canvas.removeEventListener('mouseup', this._mouseUpHandler);
        this.canvas.removeEventListener('wheel', this._wheelHandler);
        this.canvas.removeEventListener('dblclick', this._dblClickHandler);
        this.canvas.removeEventListener('contextmenu', this._contextMenuHandler);
        this.canvas.removeEventListener('keydown', this._keyDownHandler);

        // 释放旧的 Blob URL
        this._revokeImageUrl();

        // 释放 ImageBitmap
        if (typeof ImageBitmap !== 'undefined' && this.image instanceof ImageBitmap) {
            this.image.close();
        }

        // 清理资源
        this.image = null;
        this.imageData = null;
        this.overlays = [];
        this.selectedOverlay = null;
        this.activeOverlayId = null;
        this.interactionState = null;
        this.activeHandle = null;
        this.roiDraftState = null;
    }

    /**
     * 调整画布大小
     */
    resize() {
        const container = this.canvas.parentElement;
        const cssWidth = container ? container.clientWidth : 0;
        const cssHeight = container ? container.clientHeight : 0;

        // 【关键修复】如果尺寸从0变为非0且有待处理的重置视图，执行重置
        const wasZero = this._logicalWidth === 0 || this._logicalHeight === 0;
        const isNowNonZero = cssWidth > 0 && cssHeight > 0;

        const dpr = window.devicePixelRatio || 1;
        this._dpr = dpr;
        this._logicalWidth = cssWidth;
        this._logicalHeight = cssHeight;

        this.canvas.width = Math.round(cssWidth * dpr);
        this.canvas.height = Math.round(cssHeight * dpr);
        this.canvas.style.width = cssWidth ? `${cssWidth}px` : '';
        this.canvas.style.height = cssHeight ? `${cssHeight}px` : '';

        this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

        // 如果之前因为尺寸为0而延迟了resetView，现在重新尝试
        if (wasZero && isNowNonZero && this._pendingResetView && this.image) {
            this._pendingResetView = false;
            this.resetView();
        } else {
            this.invalidate();
        }
    }

    /**
     * 加载图像
     */
    loadImage(imageSource) {
        return new Promise((resolve, reject) => {
            const img = new Image();
            let urlToRevoke = null;

            img.onload = () => {
                this._releaseCurrentImage();
                this._imageUrlToRevoke = urlToRevoke;
                this.image = img;
                this.resetView();
                this.invalidate();
                resolve(img);
            };

            img.onerror = () => {
                if (urlToRevoke) {
                    URL.revokeObjectURL(urlToRevoke);
                }
                reject(new Error('图像加载失败'));
            };

            if (typeof imageSource === 'string') {
                img.src = imageSource;
            } else if (imageSource instanceof Blob) {
                urlToRevoke = URL.createObjectURL(imageSource);
                img.src = urlToRevoke;
            } else if (imageSource instanceof ArrayBuffer) {
                const blob = new Blob([imageSource]);
                urlToRevoke = URL.createObjectURL(blob);
                img.src = urlToRevoke;
            } else if (imageSource instanceof Uint8Array) {
                const blob = new Blob([imageSource]);
                urlToRevoke = URL.createObjectURL(blob);
                img.src = urlToRevoke;
            }
        });
    }

    /**
     * 释放当前图像资源（ImageBitmap 或 Blob URL）
     * @private
     */
    _releaseCurrentImage() {
        if (typeof ImageBitmap !== 'undefined' && this.image instanceof ImageBitmap) {
            this.image.close();
        }
        this._revokeImageUrl();
        this.image = null;
    }

    /**
     * 清理已登记的 Blob URL
     * @private
     */
    _revokeImageUrl() {
        if (this._imageUrlToRevoke) {
            URL.revokeObjectURL(this._imageUrlToRevoke);
            this._imageUrlToRevoke = null;
        }
    }

    /**
     * 加载图像数据（字节数组）
     */
    loadImageData(byteArray, format = 'png') {
        const blob = new Blob([byteArray], { type: `image/${format}` });
        return this.loadImage(blob);
    }

    /**
     * 从共享缓冲区加载图像 (RGBA格式)
     */
    loadImageFromBuffer(buffer, width, height) {
        try {
            const pixelData = new Uint8ClampedArray(buffer);
            const imageData = new ImageData(pixelData, width, height);

            createImageBitmap(imageData).then(bitmap => {
                this._releaseCurrentImage();
                this.image = bitmap;
                // 如果是第一帧，重置视图；否则保持视图状态以支持视频流
                if (this.scale === 1 && this.offset.x === 0 && this.offset.y === 0) {
                    this.resetView();
                }
                this.invalidate();
            }).catch(err => {
                console.error('CreateImageBitmap failed:', err);
            });
        } catch (e) {
            console.error('loadImageFromBuffer failed:', e);
        }
    }

    /**
     * 重置视图
     */
    resetView() {
        if (!this.image) return;

        const canvasWidth = this._logicalWidth;
        const canvasHeight = this._logicalHeight;
        const imageWidth = this.image.width;
        const imageHeight = this.image.height;

        // 【关键修复】如果画布尺寸为0（容器隐藏时），不计算缩放，等到可见时再处理
        if (canvasWidth === 0 || canvasHeight === 0) {
            this._pendingResetView = true;
            return;
        }
        
        // 成功重置视图，清除待处理标志
        this._pendingResetView = false;
        
        // 计算适应画布的缩放比例
        const scaleX = canvasWidth / imageWidth;
        const scaleY = canvasHeight / imageHeight;
        this.scale = Math.min(scaleX, scaleY) * 0.9; // 留一些边距
        
        // 居中显示
        this.offset.x = (canvasWidth - imageWidth * this.scale) / 2;
        this.offset.y = (canvasHeight - imageHeight * this.scale) / 2;
        
        this.invalidate();
    }

    /**
     * 缩放到适应屏幕
     */
    fitToScreen() {
        this.resetView();
    }

    /**
     * 缩放到实际大小
     */
    actualSize() {
        if (!this.image) return;
        this.scale = 1;
        this.offset.x = (this._logicalWidth - this.image.width) / 2;
        this.offset.y = (this._logicalHeight - this.image.height) / 2;
        this.invalidate();
    }

    /**
     * 添加标注
     */
    addOverlay(type, x, y, width, height, options = {}) {
        const stableId = typeof options.id === 'string' && options.id.trim()
            ? options.id.trim()
            : null;
        const overlay = {
            id: stableId || createLegacyOverlayId(),
            type, // 'rectangle', 'circle', 'polygon', 'text'
            x, y, width, height,
            color: options.color || '#ff0000',
            lineWidth: options.lineWidth || 2,
            fill: options.fill || false,
            fillColor: options.fillColor || 'rgba(255, 0, 0, 0.2)',
            text: options.text || '',
            visible: options.visible ?? true,
            selectable: options.selectable ?? true,
            readOnly: options.readOnly ?? false,
            groupId: options.groupId || null,
            layer: options.layer || 'default',
            zOrder: normalizeOverlayNumber(options.zOrder),
            ...options
        };
        overlay.id = stableId || overlay.id || createLegacyOverlayId();
        overlay.visible = options.visible ?? overlay.visible ?? true;
        overlay.selectable = options.selectable ?? overlay.selectable ?? true;
        overlay.readOnly = options.readOnly ?? overlay.readOnly ?? false;
        overlay.groupId = options.groupId || overlay.groupId || null;
        overlay.layer = options.layer || overlay.layer || 'default';
        overlay.zOrder = normalizeOverlayNumber(options.zOrder ?? overlay.zOrder);
        
        this.overlays.push(overlay);
        this.invalidate();
        return overlay;
    }

    setOverlayGroup(groupId, overlays = []) {
        if (!groupId) {
            return [];
        }

        const selectedOverlay = this.selectedOverlay;
        this.overlays = this.overlays.filter(overlay => overlay.groupId !== groupId);
        const added = overlays.map(overlay => this.addOverlay(
            overlay.type,
            overlay.x,
            overlay.y,
            overlay.width,
            overlay.height,
            {
                ...overlay,
                groupId
            }
        ));

        if (selectedOverlay && !this.overlays.some(overlay => overlay.id === selectedOverlay)) {
            this.selectedOverlay = null;
        }

        this.invalidate();
        return added;
    }

    clearOverlayGroup(groupId) {
        if (!groupId) {
            return;
        }

        const selectedOverlay = this.selectedOverlay;
        this.overlays = this.overlays.filter(overlay => overlay.groupId !== groupId);
        if (selectedOverlay && !this.overlays.some(overlay => overlay.id === selectedOverlay)) {
            this.selectedOverlay = null;
        }
        this.invalidate();
    }

    setInteractionMode(mode) {
        this.interactionMode = mode || 'legacy';
        this.enableRightButtonPan = this.interactionMode === 'roi-rect';
        this.interactionState = null;
        this.activeHandle = null;
        if (this.interactionMode === 'roi-rect' && !this.canvas.hasAttribute?.('tabindex')) {
            this.canvas.tabIndex = 0;
        }
    }

    setOverlayChangedCallback(callback) {
        this.onOverlayChanged = callback;
    }

    setEditableRectangle(rect, options = {}) {
        const normalized = this.clampRectToImage(roundRect(rect));
        const existing = this.activeOverlayId
            ? this.overlays.find(overlay => overlay.id === this.activeOverlayId)
            : null;
        const overlayStyle = {
            color: options.color || '#1890ff',
            lineWidth: options.lineWidth || 2,
            fill: options.fill ?? true,
            fillColor: options.fillColor || 'rgba(24, 144, 255, 0.14)',
            visible: true,
            editable: true
        };

        if (existing) {
            Object.assign(existing, normalized, overlayStyle);
            this.selectedOverlay = existing.id;
            if (options.resetDraft !== false) {
                this.resetRectangleDraft(normalized);
            }
            this.invalidate();
            return existing;
        }

        const overlay = this.addOverlay('rectangle', normalized.x, normalized.y, normalized.width, normalized.height, overlayStyle);
        overlay.editable = true;
        this.activeOverlayId = overlay.id;
        this.selectedOverlay = overlay.id;
        if (options.resetDraft !== false) {
            this.resetRectangleDraft(normalized);
        }
        this.invalidate();
        return overlay;
    }

    clearEditableRectangle() {
        if (!this.activeOverlayId) {
            return;
        }

        this.removeOverlay(this.activeOverlayId);
        this.activeOverlayId = null;
        this.activeHandle = null;
        this.roiDraftState = null;
    }

    fitToWindow() {
        this.fitToScreen();
    }

    /**
     * 删除标注
     */
    removeOverlay(overlayId) {
        this.overlays = this.overlays.filter(o => o.id !== overlayId);
        if (this.selectedOverlay === overlayId) {
            this.selectedOverlay = null;
        }
        this.invalidate();
    }

    /**
     * 清空标注
     */
    clearOverlays() {
        this.overlays = [];
        this.selectedOverlay = null;
        this.activeOverlayId = null;
        this.invalidate();
    }

    /**
     * 绘制图像
     */
    drawImage() {
        if (!this.image) {
            return;
        }
        
        // 绘制图像
        this.ctx.save();
        this.ctx.translate(this.offset.x, this.offset.y);
        this.ctx.scale(this.scale, this.scale);
        this.ctx.drawImage(this.image, 0, 0);
        this.ctx.restore();
    }

    /**
     * 绘制标注
     */
    drawOverlays() {
        if (!this.image) return;
        
        this.ctx.save();
        this.ctx.translate(this.offset.x, this.offset.y);
        this.ctx.scale(this.scale, this.scale);
        
        buildOverlayRenderCommands(this.overlays).forEach(command => {
            try {
                this.drawOverlayCommand(command);
            } catch (error) {
                command.overlay.visible = false;
            }
        });
        
        this.ctx.restore();
    }

    drawOverlayCommand(command) {
        const overlay = command.overlay;
        this.ctx.strokeStyle = command.color;
        this.ctx.lineWidth = command.lineWidth / this.scale; // 保持线宽恒定
        this.ctx.fillStyle = command.fillColor;

        switch (command.type) {
            case 'rectangle':
                this.ctx.beginPath();
                this.ctx.rect(command.x, command.y, command.width, command.height);
                if (command.fill) {
                    this.ctx.fill();
                }
                this.ctx.stroke();

                if (command.text) {
                    this.ctx.fillStyle = command.color;
                    this.ctx.font = '14px sans-serif';
                    this.ctx.textBaseline = 'bottom';
                    this.ctx.fillText(command.text, command.x, command.y - 2);
                }
                break;

            case 'circle': {
                const radius = command.radius > 0
                    ? command.radius
                    : Math.min(command.width, command.height) / 2;
                const centerX = command.radius > 0 ? command.x : command.x + command.width / 2;
                const centerY = command.radius > 0 ? command.y : command.y + command.height / 2;
                this.ctx.beginPath();
                this.ctx.arc(centerX, centerY, radius, 0, Math.PI * 2);
                if (command.fill) {
                    this.ctx.fill();
                }
                this.ctx.stroke();
                break;
            }

            case 'point': {
                const radius = command.radius > 0 ? command.radius : 4;
                this.ctx.beginPath();
                this.ctx.arc(command.x, command.y, radius, 0, Math.PI * 2);
                if (command.fill) {
                    this.ctx.fill();
                }
                this.ctx.stroke();
                break;
            }

            case 'polyline':
            case 'polygon':
                if (command.points.length < 2) {
                    break;
                }
                this.ctx.beginPath();
                this.ctx.moveTo(command.points[0].x, command.points[0].y);
                command.points.slice(1).forEach(point => this.ctx.lineTo(point.x, point.y));
                if (command.type === 'polygon') {
                    this.ctx.closePath();
                    if (command.fill) {
                        this.ctx.fill();
                    }
                }
                this.ctx.stroke();
                break;

            case 'text':
                this.ctx.fillStyle = command.color;
                this.ctx.font = `${command.fontSize}px sans-serif`;
                this.ctx.fillText(command.text, command.x, command.y);
                break;

            default:
                break;
        }

        if (overlay.id === this.selectedOverlay) {
            this.drawOverlaySelection(overlay, command);
        }
    }

    drawOverlaySelection(overlay, command) {
        const bounds = this.getOverlayBounds(overlay, command);
        if (!bounds) {
            return;
        }

        this.ctx.strokeStyle = '#1890ff';
        this.ctx.lineWidth = 3 / this.scale;
        this.ctx.setLineDash([5 / this.scale, 5 / this.scale]);
        this.ctx.strokeRect(bounds.x - 5, bounds.y - 5, bounds.width + 10, bounds.height + 10);
        this.ctx.setLineDash([]);

        if (this.interactionMode === 'roi-rect' && overlay.type === 'rectangle' && overlay.editable) {
            this.drawResizeHandles(overlay);
        }
    }

    /**
     * 请求重绘：标记画布为脏，并调度一次 RAF。
     * 替代原来的无限 render 循环，避免无用帧。
     */
    invalidate() {
        this._dirty = true;
        if (this._animationFrameId === null) {
            this._animationFrameId = requestAnimationFrame(this._drawFrameBound);
        }
    }

    /**
     * 兼容入口：等价于 invalidate()。
     */
    render() {
        this.invalidate();
    }

    _drawFrame() {
        this._animationFrameId = null;
        if (!this._dirty) return;
        this._dirty = false;

        const w = this._logicalWidth;
        const h = this._logicalHeight;

        // 清空画布
        this.ctx.clearRect(0, 0, w, h);

        // 绘制背景 - 浅色主题
        this.ctx.fillStyle = '#f5f5f5';
        this.ctx.fillRect(0, 0, w, h);

        // 绘制图像
        this.drawImage();

        // 绘制标注
        this.drawOverlays();

        // 显示信息
        this.drawInfo();
    }

    /**
     * 绘制信息
     */
    drawInfo() {
        if (!this.image) return;
        
        this.ctx.fillStyle = 'rgba(0, 0, 0, 0.7)';
        this.ctx.fillRect(10, 10, 200, 60);
        
        this.ctx.fillStyle = '#fff';
        this.ctx.font = '12px sans-serif';
        this.ctx.textAlign = 'left';
        this.ctx.textBaseline = 'top';
        
        this.ctx.fillText(`尺寸: ${this.image.width} x ${this.image.height}`, 15, 15);
        this.ctx.fillText(`缩放: ${(this.scale * 100).toFixed(1)}%`, 15, 35);
        this.ctx.fillText(`标注: ${this.overlays.length}`, 15, 55);
    }

    /**
     * 处理鼠标按下
     */
    handleMouseDown(e) {
        if (this.interactionMode === 'roi-rect') {
            this.handleRoiMouseDown(e);
            return;
        }

        const rect = this.canvas.getBoundingClientRect();
        const x = (e.clientX - rect.left - this.offset.x) / this.scale;
        const y = (e.clientY - rect.top - this.offset.y) / this.scale;
        
        // 检查是否点击了标注
        const ordered = buildOverlayRenderCommands(this.overlays);
        for (let i = ordered.length - 1; i >= 0; i--) {
            const overlay = ordered[i].overlay;
            if (!this.isOverlayInteractive(overlay)) {
                continue;
            }
            if (this.hitTestOverlay({ x, y }, overlay)) {
                this.selectedOverlay = overlay.id;
                this.isDragging = !overlay.readOnly;
                this.dragStart = { x: x - overlay.x, y: y - overlay.y };
                this.invalidate();
                return;
            }
        }
        
        this.selectedOverlay = null;
        this.isDragging = true;
        this.dragStart = { x: e.clientX, y: e.clientY };
        this.lastMouse = { x: e.clientX, y: e.clientY };
        this.invalidate();
    }

    /**
     * 处理鼠标移动
     */
    handleMouseMove(e) {
        if (this.interactionMode === 'roi-rect') {
            this.handleRoiMouseMove(e);
            return;
        }

        const rect = this.canvas.getBoundingClientRect();
        const x = (e.clientX - rect.left - this.offset.x) / this.scale;
        const y = (e.clientY - rect.top - this.offset.y) / this.scale;
        
        if (this.isDragging) {
            if (this.selectedOverlay) {
                // 拖拽标注
                const overlay = this.overlays.find(o => o.id === this.selectedOverlay);
                if (overlay && !overlay.readOnly) {
                    overlay.x = x - this.dragStart.x;
                    overlay.y = y - this.dragStart.y;
                }
            } else {
                // 平移画布
                const dx = e.clientX - this.lastMouse.x;
                const dy = e.clientY - this.lastMouse.y;
                this.offset.x += dx;
                this.offset.y += dy;
                this.lastMouse = { x: e.clientX, y: e.clientY };
            }
            this.invalidate();
        }
    }

    /**
     * 处理鼠标释放
     */
    handleMouseUp(e) {
        if (this.interactionMode === 'roi-rect') {
            this.handleRoiMouseUp(e);
            return;
        }

        this.isDragging = false;
    }

    handleKeyDown(e) {
        if (this.interactionMode !== 'roi-rect') {
            return;
        }

        this.handleRoiKeyDown(e);
    }

    /**
     * 处理滚轮缩放
     */
    handleWheel(e) {
        e.preventDefault();
        
        const rect = this.canvas.getBoundingClientRect();
        const mouseX = e.clientX - rect.left;
        const mouseY = e.clientY - rect.top;
        
        const delta = e.deltaY > 0 ? 0.9 : 1.1;
        const newScale = Math.max(this.minScale, Math.min(this.maxScale, this.scale * delta));
        
        if (newScale !== this.scale) {
            // 以鼠标位置为中心缩放
            this.offset.x = mouseX - (mouseX - this.offset.x) * (newScale / this.scale);
            this.offset.y = mouseY - (mouseY - this.offset.y) * (newScale / this.scale);
            this.scale = newScale;
            this.invalidate();
        }
    }

    /**
     * 处理双击
     */
    handleDoubleClick() {
        this.fitToScreen();
    }

    handleContextMenu(e) {
        if (this.enableRightButtonPan) {
            e.preventDefault();
        }
    }

    /**
     * 获取当前视图状态
     */
    getViewState() {
        return {
            scale: this.scale,
            offset: { ...this.offset }
        };
    }

    /**
     * 设置视图状态
     */
    setViewState(state) {
        this.scale = state.scale;
        this.offset = { ...state.offset };
        this.invalidate();
    }

    /**
     * 清空画布
     */
    clear() {
        this._releaseCurrentImage();
        this.overlays = [];
        this.selectedOverlay = null;
        this.activeOverlayId = null;
        this.interactionState = null;
        this.activeHandle = null;
        this._pendingResetView = false;
        this.invalidate();
    }

    getPrimaryEditableOverlay() {
        if (this.activeOverlayId) {
            const active = this.overlays.find(overlay => overlay.id === this.activeOverlayId);
            if (active) {
                return active;
            }
        }

        return this.overlays.find(overlay => overlay.editable) || null;
    }

    getCanvasPoint(e) {
        const rect = this.canvas.getBoundingClientRect();
        return {
            x: e.clientX - rect.left,
            y: e.clientY - rect.top
        };
    }

    getImagePointFromEvent(e) {
        return screenToImagePoint(this.getCanvasPoint(e), {
            scale: this.scale,
            offset: this.offset
        });
    }

    clampRectToImage(rect) {
        return clampRectToBounds(rect, {
            width: this.image?.width || 1,
            height: this.image?.height || 1
        }, this.minimumOverlaySize);
    }

    getImageBounds() {
        return {
            width: this.image?.width || 1,
            height: this.image?.height || 1
        };
    }

    resetRectangleDraft(rect) {
        this.roiDraftState = createRectangleDraftSession(rect, this.getImageBounds(), {
            minSize: this.minimumOverlaySize
        });
    }

    updateEditableOverlayRect(overlay, rect, phase = 'dragging') {
        const nextRect = this.clampRectToImage(rect);
        Object.assign(overlay, nextRect);
        if (this.roiDraftState) {
            this.roiDraftState = setRectangleDraftCurrent(this.roiDraftState, nextRect);
        }
        this.invalidate();
        this.emitOverlayChanged(overlay, phase);
        return nextRect;
    }

    commitEditableOverlayRect(overlay, previousRect = null) {
        const nextRect = this.clampRectToImage(overlay);
        Object.assign(overlay, nextRect);
        if (!this.roiDraftState) {
            this.resetRectangleDraft(nextRect);
        }
        this.roiDraftState = commitRectangleDraft(this.roiDraftState, nextRect, { previousRect });
        this.invalidate();
        this.emitOverlayChanged(overlay, 'commit');
        return nextRect;
    }

    drawResizeHandles(overlay) {
        const handles = getRectHandlePoints(overlay);
        const radius = this.handleSize / this.scale / 2;
        Object.values(handles).forEach(point => {
            this.ctx.beginPath();
            this.ctx.fillStyle = '#ffffff';
            this.ctx.strokeStyle = '#1890ff';
            this.ctx.lineWidth = 2 / this.scale;
            this.ctx.arc(point.x, point.y, radius, 0, Math.PI * 2);
            this.ctx.fill();
            this.ctx.stroke();
        });
    }

    hitTestResizeHandle(imagePoint, overlay) {
        if (!overlay) {
            return null;
        }

        return hitTestRectHandle(imagePoint, overlay, { scale: this.scale, offset: this.offset }, this.handleSize);
    }

    hitTestOverlay(imagePoint, overlay) {
        if (!overlay) {
            return false;
        }

        if (overlay.type === 'rectangle') {
            return hitTestRectangle(imagePoint, this.getOverlayBounds(overlay));
        }

        const bounds = this.getOverlayBounds(overlay);
        return Boolean(bounds) &&
            imagePoint.x >= bounds.x &&
            imagePoint.x <= bounds.x + bounds.width &&
            imagePoint.y >= bounds.y &&
            imagePoint.y <= bounds.y + bounds.height;
    }

    isOverlayInteractive(overlay) {
        return overlay?.visible !== false &&
            overlay?.selectable !== false;
    }

    getOverlayBounds(overlay, command = null) {
        if (!overlay) {
            return null;
        }

        const type = command?.type || overlay.type;
        if (type === 'point') {
            const radius = normalizeOverlayNumber(command?.radius ?? overlay.radius, 4);
            return {
                x: normalizeOverlayNumber(command?.x ?? overlay.x) - radius,
                y: normalizeOverlayNumber(command?.y ?? overlay.y) - radius,
                width: radius * 2,
                height: radius * 2
            };
        }

        if (type === 'circle' && normalizeOverlayNumber(command?.radius ?? overlay.radius, 0) > 0) {
            const radius = normalizeOverlayNumber(command?.radius ?? overlay.radius, 0);
            return {
                x: normalizeOverlayNumber(command?.x ?? overlay.x) - radius,
                y: normalizeOverlayNumber(command?.y ?? overlay.y) - radius,
                width: radius * 2,
                height: radius * 2
            };
        }

        if ((type === 'polyline' || type === 'polygon') && Array.isArray(overlay.points) && overlay.points.length > 0) {
            const xs = overlay.points.map(point => normalizeOverlayNumber(point?.x));
            const ys = overlay.points.map(point => normalizeOverlayNumber(point?.y));
            const minX = Math.min(...xs);
            const maxX = Math.max(...xs);
            const minY = Math.min(...ys);
            const maxY = Math.max(...ys);
            return {
                x: minX,
                y: minY,
                width: Math.max(1, maxX - minX),
                height: Math.max(1, maxY - minY)
            };
        }

        return {
            x: normalizeOverlayNumber(command?.x ?? overlay.x),
            y: normalizeOverlayNumber(command?.y ?? overlay.y),
            width: Math.max(1, normalizeOverlayNumber(command?.width ?? overlay.width)),
            height: Math.max(1, normalizeOverlayNumber(command?.height ?? overlay.height))
        };
    }

    emitOverlayChanged(overlay, phase) {
        if (!overlay || typeof this.onOverlayChanged !== 'function') {
            return;
        }

        this.onOverlayChanged(roundRect({
            x: overlay.x,
            y: overlay.y,
            width: overlay.width,
            height: overlay.height
        }), phase);
    }

    cancelActiveRoiInteraction() {
        const interaction = this.interactionState;
        if (!interaction) {
            return false;
        }

        this.interactionState = null;
        if (interaction.type === 'pan') {
            this.invalidate();
            return true;
        }

        const overlay = this.overlays.find(item => item.id === interaction.overlayId);
        if (!overlay) {
            this.invalidate();
            return true;
        }

        if (interaction.createdOverlay && !interaction.originalRect) {
            this.removeOverlay(overlay.id);
            this.activeOverlayId = null;
            this.selectedOverlay = null;
            this.roiDraftState = cancelRectangleDraft(this.roiDraftState);
            return true;
        }

        const fallbackRect = interaction.originalRect || this.roiDraftState?.initial;
        if (fallbackRect) {
            Object.assign(overlay, this.clampRectToImage(fallbackRect));
            this.roiDraftState = setRectangleDraftCurrent(
                this.roiDraftState || createRectangleDraftSession(overlay, this.getImageBounds(), {
                    minSize: this.minimumOverlaySize
                }),
                overlay
            );
            this.invalidate();
            this.emitOverlayChanged(overlay, 'cancel');
        }

        return true;
    }

    applyRoiDraftHistory(nextDraftState) {
        if (!nextDraftState || !nextDraftState.current) {
            return false;
        }

        const overlay = this.getPrimaryEditableOverlay();
        if (!overlay) {
            return false;
        }

        this.roiDraftState = nextDraftState;
        Object.assign(overlay, this.roiDraftState.current);
        this.invalidate();
        this.emitOverlayChanged(overlay, 'commit');
        return true;
    }

    handleRoiKeyDown(e) {
        const overlay = this.getPrimaryEditableOverlay();
        if (!overlay) {
            return;
        }

        if (e.key === 'Escape') {
            if (this.cancelActiveRoiInteraction()) {
                e.preventDefault?.();
            }
            return;
        }

        const isUndo = (e.ctrlKey || e.metaKey) && String(e.key).toLowerCase() === 'z' && !e.shiftKey;
        const isRedo = ((e.ctrlKey || e.metaKey) && String(e.key).toLowerCase() === 'y') ||
            ((e.ctrlKey || e.metaKey) && e.shiftKey && String(e.key).toLowerCase() === 'z');

        if (isUndo) {
            if (this.applyRoiDraftHistory(undoRectangleDraft(this.roiDraftState))) {
                e.preventDefault?.();
            }
            return;
        }

        if (isRedo) {
            if (this.applyRoiDraftHistory(redoRectangleDraft(this.roiDraftState))) {
                e.preventDefault?.();
            }
            return;
        }

        const deltas = {
            ArrowLeft: { x: -1, y: 0 },
            ArrowRight: { x: 1, y: 0 },
            ArrowUp: { x: 0, y: -1 },
            ArrowDown: { x: 0, y: 1 }
        };
        const delta = deltas[e.key];
        if (!delta) {
            return;
        }

        const step = e.shiftKey ? 10 : 1;
        const previousRect = { x: overlay.x, y: overlay.y, width: overlay.width, height: overlay.height };
        const nextRect = nudgeRect(overlay, {
            x: delta.x * step,
            y: delta.y * step
        }, this.getImageBounds(), this.minimumOverlaySize);
        Object.assign(overlay, nextRect);
        if (!this.roiDraftState) {
            this.resetRectangleDraft(previousRect);
        }
        this.roiDraftState = commitRectangleDraft(this.roiDraftState, nextRect, { previousRect });
        this.invalidate();
        this.emitOverlayChanged(overlay, 'commit');
        e.preventDefault?.();
    }

    handleRoiMouseDown(e) {
        if (!this.image) {
            return;
        }

        try {
            this.canvas.focus?.({ preventScroll: true });
        } catch {
            this.canvas.focus?.();
        }

        if (e.button === 2) {
            this.interactionState = {
                type: 'pan',
                startCanvasPoint: this.getCanvasPoint(e),
                startOffset: { ...this.offset }
            };
            this.invalidate();
            return;
        }

        if (e.button !== 0) {
            return;
        }

        const imagePoint = this.getImagePointFromEvent(e);
        const overlay = this.getPrimaryEditableOverlay();
        const handle = this.hitTestResizeHandle(imagePoint, overlay);

        if (overlay && handle) {
            this.selectedOverlay = overlay.id;
            this.activeOverlayId = overlay.id;
            this.activeHandle = handle;
            this.interactionState = {
                type: 'resize',
                handle,
                overlayId: overlay.id,
                originalRect: { x: overlay.x, y: overlay.y, width: overlay.width, height: overlay.height }
            };
            this.invalidate();
            return;
        }

        if (overlay && this.hitTestOverlay(imagePoint, overlay)) {
            this.selectedOverlay = overlay.id;
            this.activeOverlayId = overlay.id;
            this.activeHandle = null;
            this.interactionState = {
                type: 'move',
                overlayId: overlay.id,
                originalRect: { x: overlay.x, y: overlay.y, width: overlay.width, height: overlay.height },
                dragAnchor: imagePoint
            };
            this.invalidate();
            return;
        }

        const originalRect = overlay
            ? { x: overlay.x, y: overlay.y, width: overlay.width, height: overlay.height }
            : null;
        const nextOverlay = this.setEditableRectangle({
            x: imagePoint.x,
            y: imagePoint.y,
            width: this.minimumOverlaySize,
            height: this.minimumOverlaySize
        }, { resetDraft: !overlay });
        this.activeHandle = null;
        this.interactionState = {
            type: 'draw',
            overlayId: nextOverlay.id,
            startPoint: imagePoint,
            originalRect,
            createdOverlay: !overlay
        };
    }

    handleRoiMouseMove(e) {
        if (!this.image || !this.interactionState) {
            return;
        }

        if (this.interactionState.type === 'pan') {
            const canvasPoint = this.getCanvasPoint(e);
            this.offset.x = this.interactionState.startOffset.x + (canvasPoint.x - this.interactionState.startCanvasPoint.x);
            this.offset.y = this.interactionState.startOffset.y + (canvasPoint.y - this.interactionState.startCanvasPoint.y);
            this.invalidate();
            return;
        }

        const overlay = this.overlays.find(item => item.id === this.interactionState.overlayId);
        if (!overlay) {
            return;
        }

        const imagePoint = this.getImagePointFromEvent(e);
        let nextRect = null;

        if (this.interactionState.type === 'draw') {
            nextRect = this.clampRectToImage(normalizeRectFromPoints(this.interactionState.startPoint, imagePoint));
        } else if (this.interactionState.type === 'move') {
            nextRect = translateRect(
                this.interactionState.originalRect,
                {
                    x: imagePoint.x - this.interactionState.dragAnchor.x,
                    y: imagePoint.y - this.interactionState.dragAnchor.y
                },
                { width: this.image.width, height: this.image.height },
                this.minimumOverlaySize
            );
        } else if (this.interactionState.type === 'resize') {
            nextRect = resizeRectByHandle(
                this.interactionState.originalRect,
                this.interactionState.handle,
                imagePoint,
                { width: this.image.width, height: this.image.height },
                this.minimumOverlaySize
            );
        }

        if (!nextRect) {
            return;
        }

        this.updateEditableOverlayRect(overlay, nextRect, 'dragging');
    }

    handleRoiMouseUp() {
        if (!this.interactionState) {
            return;
        }

        const interaction = this.interactionState;
        this.interactionState = null;
        this.invalidate();

        if (interaction.type === 'pan') {
            return;
        }

        const overlay = this.overlays.find(item => item.id === interaction.overlayId);
        if (overlay) {
            this.commitEditableOverlayRect(overlay, interaction.originalRect);
        }
    }
}

export default ImageCanvas;
export { ImageCanvas };
