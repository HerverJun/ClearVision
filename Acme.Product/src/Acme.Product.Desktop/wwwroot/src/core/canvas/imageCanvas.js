import {
    clampRectToBounds,
    getRectHandlePoints,
    normalizeRectFromPoints,
    resizeRectByHandle,
    roundRect,
    screenToImagePoint,
    translateRect
} from '../../features/flow-editor/roiGeometry.mjs';

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
        const overlay = {
            id: `overlay_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`,
            type, // 'rectangle', 'circle', 'polygon', 'text'
            x, y, width, height,
            color: options.color || '#ff0000',
            lineWidth: options.lineWidth || 2,
            fill: options.fill || false,
            fillColor: options.fillColor || 'rgba(255, 0, 0, 0.2)',
            text: options.text || '',
            visible: true,
            ...options
        };
        
        this.overlays.push(overlay);
        this.invalidate();
        return overlay;
    }

    setInteractionMode(mode) {
        this.interactionMode = mode || 'legacy';
        this.enableRightButtonPan = this.interactionMode === 'roi-rect';
        this.interactionState = null;
        this.activeHandle = null;
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
            this.invalidate();
            return existing;
        }

        const overlay = this.addOverlay('rectangle', normalized.x, normalized.y, normalized.width, normalized.height, overlayStyle);
        overlay.editable = true;
        this.activeOverlayId = overlay.id;
        this.selectedOverlay = overlay.id;
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
        
        this.overlays.forEach(overlay => {
            if (!overlay.visible) return;
            
            this.ctx.strokeStyle = overlay.color;
            this.ctx.lineWidth = overlay.lineWidth / this.scale; // 保持线宽恒定
            this.ctx.fillStyle = overlay.fillColor;
            
            switch (overlay.type) {
                case 'rectangle':
                    this.ctx.beginPath();
                    this.ctx.rect(overlay.x, overlay.y, overlay.width, overlay.height);
                    if (overlay.fill) {
                        this.ctx.fill();
                    }
                    this.ctx.stroke();
                    
                    // 绘制标签文本
                    if (overlay.text) {
                        this.ctx.fillStyle = overlay.color;
                        this.ctx.font = '14px sans-serif';
                        this.ctx.textBaseline = 'bottom';
                        this.ctx.fillText(overlay.text, overlay.x, overlay.y - 2);
                    }
                    break;
                    
                case 'circle':
                    this.ctx.beginPath();
                    this.ctx.arc(
                        overlay.x + overlay.width / 2,
                        overlay.y + overlay.height / 2,
                        Math.min(overlay.width, overlay.height) / 2,
                        0,
                        Math.PI * 2
                    );
                    if (overlay.fill) {
                        this.ctx.fill();
                    }
                    this.ctx.stroke();
                    break;
                    
                case 'text':
                    this.ctx.fillStyle = overlay.color;
                    this.ctx.font = `${overlay.fontSize || 14}px sans-serif`;
                    this.ctx.fillText(overlay.text, overlay.x, overlay.y);
                    break;
            }
            
            // 绘制选中高亮
            if (overlay.id === this.selectedOverlay) {
                this.ctx.strokeStyle = '#1890ff';
                this.ctx.lineWidth = 3 / this.scale;
                this.ctx.setLineDash([5 / this.scale, 5 / this.scale]);
                this.ctx.strokeRect(overlay.x - 5, overlay.y - 5, overlay.width + 10, overlay.height + 10);
                this.ctx.setLineDash([]);

                if (this.interactionMode === 'roi-rect' && overlay.type === 'rectangle' && overlay.editable) {
                    this.drawResizeHandles(overlay);
                }
            }
        });
        
        this.ctx.restore();
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
        for (let i = this.overlays.length - 1; i >= 0; i--) {
            const overlay = this.overlays[i];
            if (x >= overlay.x && x <= overlay.x + overlay.width &&
                y >= overlay.y && y <= overlay.y + overlay.height) {
                this.selectedOverlay = overlay.id;
                this.isDragging = true;
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
                if (overlay) {
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

        const handles = getRectHandlePoints(overlay);
        const tolerance = this.handleSize / this.scale;

        return Object.entries(handles).find(([, point]) =>
            Math.abs(imagePoint.x - point.x) <= tolerance &&
            Math.abs(imagePoint.y - point.y) <= tolerance)?.[0] || null;
    }

    hitTestOverlay(imagePoint, overlay) {
        if (!overlay) {
            return false;
        }

        return imagePoint.x >= overlay.x &&
            imagePoint.x <= overlay.x + overlay.width &&
            imagePoint.y >= overlay.y &&
            imagePoint.y <= overlay.y + overlay.height;
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

    handleRoiMouseDown(e) {
        if (!this.image) {
            return;
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

        const nextOverlay = this.setEditableRectangle({
            x: imagePoint.x,
            y: imagePoint.y,
            width: this.minimumOverlaySize,
            height: this.minimumOverlaySize
        });
        this.activeHandle = null;
        this.interactionState = {
            type: 'draw',
            overlayId: nextOverlay.id,
            startPoint: imagePoint
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

        Object.assign(overlay, nextRect);
        this.invalidate();
        this.emitOverlayChanged(overlay, 'dragging');
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
            this.emitOverlayChanged(overlay, 'commit');
        }
    }
}

export default ImageCanvas;
export { ImageCanvas };
