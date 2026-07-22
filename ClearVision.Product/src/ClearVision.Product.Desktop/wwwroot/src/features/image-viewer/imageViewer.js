/**
 * ImageViewerComponent - 图像查看器组件
 * Sprint 4: S4-001 实现
 * 
 * 功能：
 * - 图像加载（URL/Base64/File/Blob）
 * - 缩放/平移/适应窗口
 * - 缺陷标注渲染（矩形框+标签）
 * - 文件选择器集成
 * - ROI交互
 */

import ImageCanvas from '../../core/canvas/imageCanvas.js';
import { showToast } from '../../shared/components/uiComponents.js';

const MAX_IMAGE_VIEWER_DEFECTS = 300;
const DEFECT_TEXT_LIMIT = 120;
const DEFECT_ID_TEXT_LIMIT = 96;

function normalizeImageFormat(format) {
    const normalized = String(format || 'png').trim().toLowerCase();
    return /^[a-z0-9.+-]+$/.test(normalized) ? normalized : 'png';
}

function parseDataImageSource(source) {
    if (typeof source !== 'string') {
        return null;
    }

    const trimmed = source.trim();
    const match = /^data:image\/([^;,]+)((?:;[^,]*)?),(.*)$/i.exec(trimmed);
    if (!match || !match[2].toLowerCase().includes(';base64')) {
        return null;
    }

    return {
        format: normalizeImageFormat(match[1]),
        base64: match[3].replace(/\s+/g, '')
    };
}

function decodeBase64ToBytes(base64String) {
    const sanitized = String(base64String || '').replace(/\s+/g, '');
    if (!sanitized) {
        return new Uint8Array();
    }

    if (typeof atob === 'function') {
        const binary = atob(sanitized);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i += 1) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    }

    if (typeof Buffer !== 'undefined') {
        return new Uint8Array(Buffer.from(sanitized, 'base64'));
    }

    throw new Error('Base64 decoding is not supported in this environment');
}

function hashString(value) {
    let hash = 2166136261;
    for (let index = 0; index < value.length; index += 1) {
        hash ^= value.charCodeAt(index);
        hash = Math.imul(hash, 16777619);
    }

    return (hash >>> 0).toString(16);
}

function getBase64SourceKey(base64String, format = 'png') {
    const sanitized = String(base64String || '').replace(/\s+/g, '');
    return `base64:${normalizeImageFormat(format)}:${sanitized.length}:${hashString(sanitized)}`;
}

function isLikelyImageUrl(source) {
    if (typeof source !== 'string') {
        return false;
    }

    const trimmed = source.trim();
    if (/^https?:\/\//i.test(trimmed) || /^blob:/i.test(trimmed) || /^file:/i.test(trimmed)) {
        return true;
    }

    if (/^\/api\/images(?:\/|\?|$)/i.test(trimmed)) {
        return true;
    }

    return /^(?:\/|\.\/|\.\.\/).+\.(?:png|jpe?g|gif|webp|bmp|svg)(?:[?#].*)?$/i.test(trimmed);
}

function getImageSourceKey(source) {
    if (typeof source !== 'string') {
        return null;
    }

    const dataImage = parseDataImageSource(source);
    if (dataImage) {
        return getBase64SourceKey(dataImage.base64, dataImage.format);
    }

    if (source.startsWith('data:') || isLikelyImageUrl(source)) {
        return `url:${source}`;
    }

    return getBase64SourceKey(source, 'png');
}

export class ImageViewerComponent {
    constructor(containerId) {
        this.container = document.getElementById(containerId);
        this.containerId = containerId;
        this.canvas = null;
        this.imageCanvas = null;
        this.currentImage = null;
        this.currentImageSource = null;
        this.currentImageSourceKey = null;
        this.defects = [];
        this.omittedDefectCount = 0;
        this._eventDisposers = [];
        this._originalImageCanvasLoadImage = null;
        this._originalImageCanvasRender = null;
        this._isDestroyed = false;
        
        // 生成唯一 ID，避免多个实例冲突
        this.canvasId = `viewer-canvas-${containerId}`;
        this.placeholderId = `viewer-placeholder-${containerId}`;
        
        // 事件回调
        this.onRegionSelected = null;
        this.onAnnotationClicked = null;
        this.onImageLoaded = null;
        
        this.initialize();
    }

    /**
     * 初始化组件
     */
    initialize() {
        this.renderUI();
        this.imageCanvas = new ImageCanvas(this.canvasId);
        this.bindToolbarEvents();
        this.bindCanvasEvents();
    }

    /**
     * 渲染UI结构
     */
    renderUI() {
        this.container.innerHTML = `
            <div class="image-viewer-wrapper">
                <!-- 工具栏 -->
                <div class="viewer-toolbar">

                    <div class="toolbar-group">
                        <button id="btn-zoom-in" class="cv-btn cv-btn-icon" title="放大">
                            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line><line x1="11" y1="8" x2="11" y2="14"></line><line x1="8" y1="11" x2="14" y2="11"></line></svg>
                        </button>
                        <button id="btn-zoom-out" class="cv-btn cv-btn-icon" title="缩小">
                            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line><line x1="8" y1="11" x2="14" y2="11"></line></svg>
                        </button>
                        <button id="btn-fit-window" class="cv-btn cv-btn-icon" title="适应窗口">
                            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 3 21 3 21 9"></polyline><polyline points="9 21 3 21 3 15"></polyline><line x1="21" y1="3" x2="14" y2="10"></line><line x1="3" y1="21" x2="10" y2="14"></line></svg>
                        </button>
                        <button id="btn-actual-size" class="cv-btn cv-btn-icon" title="实际大小" style="font-size: 13px; font-weight: 600;">1:1</button>
                    </div>
                    <div class="toolbar-divider"></div>
                    <div class="toolbar-info">
                        <span id="image-info"></span>
                        <span id="zoom-info">100%</span>
                    </div>
                </div>
                
                <!-- 画布区域 -->
                <div class="viewer-canvas-container">
                    <canvas id="${this.canvasId}"></canvas>
                    <div class="viewer-placeholder" id="${this.placeholderId}">
                        <div class="placeholder-content">
                            <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="var(--text-secondary)" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" style="margin-bottom: 8px;"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect><circle cx="8.5" cy="8.5" r="1.5"></circle><polyline points="21 15 16 10 5 21"></polyline></svg>
                            <p style="color: var(--text-secondary); margin: 0; font-size: 14px;">等待检测图像</p>
                        </div>
                    </div>
                </div>
                
                <!-- 缺陷列表侧边栏 -->
                <div class="defect-sidebar" id="defect-sidebar">
                    <h4>检测结果</h4>
                    <div class="defect-list" id="defect-list"></div>
                </div>
            </div>
        `;
        
        this.canvas = document.getElementById(this.canvasId);
        this.updateDefectSidebarState();
    }

    /**
     * 绑定工具栏事件
     */
    bindToolbarEvents() {
        // 缩放控制
        this.addDomEventListener(this.container.querySelector('#btn-zoom-in'), 'click', () => {
            this.zoomIn();
        });
        
        this.addDomEventListener(this.container.querySelector('#btn-zoom-out'), 'click', () => {
            this.zoomOut();
        });
        
        this.addDomEventListener(this.container.querySelector('#btn-fit-window'), 'click', () => {
            this.fitToWindow();
        });
        
        this.addDomEventListener(this.container.querySelector('#btn-actual-size'), 'click', () => {
            this.actualSize();
        });
    }

    addDomEventListener(target, type, handler, options) {
        if (!target || typeof target.addEventListener !== 'function') {
            return;
        }

        target.addEventListener(type, handler, options);
        this._eventDisposers.push(() => target.removeEventListener(type, handler, options));
    }

    /**
     * 绑定画布事件
     */
    bindCanvasEvents() {
        // 监听图像加载
        const originalLoadImage = this.imageCanvas.loadImage.bind(this.imageCanvas);
        this._originalImageCanvasLoadImage = originalLoadImage;
        this.imageCanvas.loadImage = (source) => {
            return originalLoadImage(source).then((img) => {
                this.currentImage = img;
                this.hidePlaceholder();
                this.updateImageInfo();
                if (this.onImageLoaded) {
                    this.onImageLoaded(img);
                }
                return img;
            });
        };

        // 监听缩放变化
        const originalRender = this.imageCanvas.render.bind(this.imageCanvas);
        this._originalImageCanvasRender = originalRender;
        this.imageCanvas.render = () => {
            originalRender();
            this.updateZoomInfo();
        };

        // 监听标注点击
        this.addDomEventListener(this.imageCanvas.canvas, 'click', (e) => {
            const overlay = this.getOverlayAt(e.offsetX, e.offsetY);
            if (overlay && this.onAnnotationClicked) {
                this.onAnnotationClicked(overlay);
                this.selectDefect(overlay.id);
            }
        });
    }

    /**
     * 从文件加载图像
     */
    loadFromFile(file, options = {}) {
        const silent = options.silent === true;
        if (!file.type.startsWith('image/')) {
            if (!silent) {
                showToast('请选择有效的图像文件', 'error');
            }
            return Promise.reject(new Error('Invalid file type'));
        }

        if (!silent) {
            showToast(`正在加载: ${file.name}`, 'info');
        }
        
        return this.imageCanvas.loadImage(file).then(() => {
            this.currentImageSource = null;
            this.currentImageSourceKey = null;
            if (!silent) {
                showToast('图像加载成功', 'success');
            }
        }).catch((err) => {
            if (!silent) {
                showToast('图像加载失败: ' + err.message, 'error');
            }
            throw err;
        });
    }

    /**
     * 从URL加载图像
     */
    loadFromUrl(url, options = {}) {
        const silent = options.silent === true;
        if (!silent) {
            showToast('正在加载图像...', 'info');
        }
        
        return this.imageCanvas.loadImage(url).then(() => {
            this.currentImageSource = url;
            this.currentImageSourceKey = getImageSourceKey(url);
            if (!silent) {
                showToast('图像加载成功', 'success');
            }
        }).catch((err) => {
            if (!silent) {
                showToast('图像加载失败', 'error');
            }
            throw err;
        });
    }

    /**
     * 从Base64加载图像
     */
    loadFromBase64(base64String, format = 'png', options = {}) {
        try {
            const bytes = decodeBase64ToBytes(base64String);
            return this.loadFromByteArray(bytes, normalizeImageFormat(format)).then((image) => {
                this.currentImageSource = null;
                this.currentImageSourceKey = options.sourceKey || getBase64SourceKey(base64String, format);
                return image;
            });
        } catch (error) {
            if (options.silent !== true) {
                showToast('Image load failed: ' + error.message, 'error');
            }
            return Promise.reject(error);
        }
    }

    /**
     * 从字节数组加载
     */
    loadFromByteArray(byteArray, format = 'png') {
        return this.imageCanvas.loadImageData(byteArray, format);
    }

    /**
     * 通用图像加载方法 - 支持 data URL 和 raw base64
     * @param {string} source - data URL (如 "data:image/png;base64,...") 或者 raw base64 字符串
     */
    loadImage(source, options = {}) {
        if (!source) {
            console.warn('[ImageViewer] loadImage: source is empty');
            return Promise.reject(new Error('Image source is empty'));
        }

        const sourceKey = getImageSourceKey(source);
        if (this.currentImage && sourceKey && this.currentImageSourceKey === sourceKey) {
            this.imageCanvas?.resize?.();
            this.imageCanvas?.render?.();
            return Promise.resolve(this.currentImage);
        }

        // Decode base64 data URLs to Blob URLs so old inspection frames can be revoked.
        if (typeof source === 'string' && source.startsWith('data:')) {
            const dataImage = parseDataImageSource(source);
            if (dataImage) {
                return this.loadFromBase64(dataImage.base64, dataImage.format, { ...options, sourceKey });
            }

            return this.loadFromUrl(source, options);
        }
        
        // Route real image URLs before treating any string as raw base64.
        if (isLikelyImageUrl(source)) {
            return this.loadFromUrl(source, options);
        }

        if (typeof source === 'string') {
            return this.loadFromBase64(source, 'png', { ...options, sourceKey });
        }

        if (source instanceof Blob || source instanceof ArrayBuffer || source instanceof Uint8Array) {
            return this.imageCanvas.loadImage(source).then(() => {
                this.currentImageSource = null;
                this.currentImageSourceKey = null;
            }).catch((err) => {
                if (options.silent !== true) {
                    showToast('图像加载失败: ' + err.message, 'error');
                }
                throw err;
            });
        }

        return this.loadFromFile(source, options);
    }

    /**
     * 显示缺陷标注
     */
    showDefects(defects) {
        const sourceDefects = Array.isArray(defects) ? defects : [];
        this.resetAnnotations();
        this.defects = sourceDefects
            .slice(0, MAX_IMAGE_VIEWER_DEFECTS)
            .map((defect, index) => this.createDisplayDefect(defect, index));
        this.omittedDefectCount = Math.max(0, sourceDefects.length - this.defects.length);
        
        this.defects.forEach((defect, index) => {
            const id = this.getDefectProp(defect, 'id') || index;
            const type = this.getDefectProp(defect, 'type');
            const description = this.getDefectProp(defect, 'description');
            const x = this.getDefectProp(defect, 'x');
            const y = this.getDefectProp(defect, 'y');
            const width = this.getDefectProp(defect, 'width');
            const height = this.getDefectProp(defect, 'height');

            const displayType = description || type || 'Unknown';
            const color = this.getDefectColor(type);
            
            const overlay = this.imageCanvas.addOverlay(
                'rectangle',
                x,
                y,
                width,
                height,
                {
                    color: color,
                    lineWidth: 3,
                    text: `${index + 1}. ${displayType}`,
                    fill: true,
                    fillColor: color + '33', // 20%透明度
                    data: defect
                }
            );
            overlay.defectId = id;
        });
        
        this.renderDefectList();
    }

    /**
     * 获取缺陷类型对应的颜色
     */
    resetAnnotations() {
        this.imageCanvas.clearOverlays();
        this.defects = [];
        this.omittedDefectCount = 0;
        this.updateDefectSidebarState();
    }

    createDisplayDefect(defect, index) {
        return {
            id: this.compactDefectText(this.getDefectProp(defect, 'id') ?? index, DEFECT_ID_TEXT_LIMIT),
            type: this.compactDefectText(this.getDefectProp(defect, 'type')),
            description: this.compactDefectText(this.getDefectProp(defect, 'description')),
            x: this.getDefectProp(defect, 'x'),
            y: this.getDefectProp(defect, 'y'),
            width: this.getDefectProp(defect, 'width'),
            height: this.getDefectProp(defect, 'height'),
            confidenceScore: this.getDefectProp(defect, 'confidenceScore')
        };
    }

    compactDefectText(value, limit = DEFECT_TEXT_LIMIT) {
        if (value === null || value === undefined || typeof value !== 'string') {
            return value;
        }

        const maxLength = Number.isFinite(limit) && limit > 0 ? limit : DEFECT_TEXT_LIMIT;
        return value.length > maxLength
            ? `${value.slice(0, maxLength)}...`
            : value;
    }

    getDefectColor(type) {
        const colors = {
            // 中文映射
            '划痕': '#ff4d4f',
            '污渍': '#faad14',
            '异物': '#52c41a',
            '缺失': '#1890ff',
            '变形': '#722ed1',
            '尺寸偏差': '#eb2f96',
            '颜色异常': '#13c2c2',
            '其他': '#8c8c8c',
            
            // 英文映射 (PascalCase)
            'Scratch': '#ff4d4f',
            'Stain': '#faad14',
            'ForeignObject': '#52c41a',
            'Missing': '#1890ff',
            'Deformation': '#722ed1',
            'DimensionalDeviation': '#eb2f96',
            'ColorAbnormality': '#13c2c2',
            'Other': '#8c8c8c',
            
            // 数字映射 (String)
            '0': '#ff4d4f',
            '1': '#faad14',
            '2': '#52c41a',
            '3': '#1890ff',
            '4': '#722ed1',
            '5': '#eb2f96',
            '6': '#13c2c2',
            '99': '#8c8c8c'
        };
        return colors[String(type)] || '#ff4d4f';
    }

    /**
     * 获取缺陷属性（兼容 camelCase 和 PascalCase）
     */
    getDefectProp(defect, propName) {
        if (!defect) return undefined;
        // 尝试 camelCase
        const camel = propName.charAt(0).toLowerCase() + propName.slice(1);
        if (defect[camel] !== undefined) return defect[camel];
        
        // 尝试 PascalCase
        const pascal = propName.charAt(0).toUpperCase() + propName.slice(1);
        if (defect[pascal] !== undefined) return defect[pascal];
        
        // 特殊处理
        if (propName === 'description' && defect.className) return defect.className;
        if (propName === 'confidenceScore' && defect.confidence) return defect.confidence;
        
        return undefined;
    }

    /**
     * 渲染缺陷列表
     */
    renderDefectList() {
        const list = this.container.querySelector('#defect-list');
        this.updateDefectSidebarState();
        if (!list) {
            return;
        }
        
        if (this.defects.length === 0) {
            list.innerHTML = '<div class="defect-empty">暂无缺陷</div>';
            return;
        }
        
        const rows = this.defects.map((defect, index) => {
            const id = this.getDefectProp(defect, 'id') || index;
            const type = this.getDefectProp(defect, 'type');
            const description = this.getDefectProp(defect, 'description');
            const x = this.getDefectProp(defect, 'x');
            const y = this.getDefectProp(defect, 'y');
            const confidenceScore = this.getDefectProp(defect, 'confidenceScore');
            
            const displayType = description || type || 'Unknown';
            const displayConf = confidenceScore !== undefined ? (confidenceScore * 100).toFixed(1) : '0.0';
            
            return `
            <div class="defect-item" data-id="${this.escapeHtml(id)}">
                <span class="defect-index" style="background: ${this.getDefectColor(type)}">${index + 1}</span>
                <div class="defect-info">
                    <span class="defect-type">${this.escapeHtml(displayType)}</span>
                    <span class="defect-position">位置: (${Math.round(x)}, ${Math.round(y)})</span>
                    <span class="defect-confidence">置信度: ${displayConf}%</span>
                </div>
            </div>
        `}).join('');

        const hiddenRow = this.omittedDefectCount > 0
            ? `<div class="defect-empty">Hidden ${this.omittedDefectCount} more defects</div>`
            : '';
        list.innerHTML = `${rows}${hiddenRow}`;
        
        // 绑定点击事件
        list.querySelectorAll('.defect-item').forEach(item => {
            item.addEventListener('click', () => {
                const id = item.dataset.id;
                this.selectDefect(id);
            });
        });
    }

    updateDefectSidebarState() {
        const wrapper = this.container.querySelector('.image-viewer-wrapper');
        if (!wrapper) {
            return;
        }

        wrapper.classList.toggle('has-defects', this.defects.length > 0);
    }

    /**
     * 选中缺陷
     */
    selectDefect(defectId) {
        // 高亮列表项
        this.container.querySelectorAll('.defect-item').forEach(item => {
            item.classList.toggle('selected', item.dataset.id === String(defectId));
        });
        
        // 高亮标注
        this.imageCanvas.overlays.forEach(overlay => {
            // overlay.defectId 可能也是 PascalCase 问题，但通常 overlay 是前端创建的对象
            // 但如果 overlay 是从 loadAnnotations 来的...
            // 假设 overlay 结构是前端控制的，暂不处理
            
            if (String(overlay.defectId) === String(defectId)) {
                overlay.lineWidth = 5;
                overlay.color = '#ffffff';
            } else {
                overlay.lineWidth = 3;
                // overlay.data 可能包含原始缺陷数据
                const type = overlay.data ? this.getDefectProp(overlay.data, 'type') : overlay.type; // fallback
                overlay.color = this.getDefectColor(type);
            }
        });
        
        this.imageCanvas.render();
    }

    /**
     * 获取点击位置的标注
     */
    getOverlayAt(x, y) {
        // 转换到图像坐标
        const imageX = (x - this.imageCanvas.offset.x) / this.imageCanvas.scale;
        const imageY = (y - this.imageCanvas.offset.y) / this.imageCanvas.scale;
        
        // 查找包含该点的标注
        for (let i = this.imageCanvas.overlays.length - 1; i >= 0; i--) {
            const o = this.imageCanvas.overlays[i];
            if (imageX >= o.x && imageX <= o.x + o.width &&
                imageY >= o.y && imageY <= o.y + o.height) {
                return o;
            }
        }
        return null;
    }

    /**
     * 缩放控制
     */
    zoomIn() {
        this.imageCanvas.scale *= 1.2;
        this.imageCanvas.render();
    }

    zoomOut() {
        this.imageCanvas.scale /= 1.2;
        this.imageCanvas.render();
    }

    zoomTo(scale) {
        this.imageCanvas.scale = scale;
        this.imageCanvas.render();
    }

    fitToWindow() {
        this.imageCanvas.fitToScreen();
    }

    actualSize() {
        this.imageCanvas.actualSize();
    }

    /**
     * 标注控制
     */
    clearAnnotations() {
        this.resetAnnotations();
        this.renderDefectList();
        showToast('已清除所有标注', 'info');
    }

    toggleAnnotations() {
        const visible = this.imageCanvas.overlays.some(o => !o.visible);
        this.imageCanvas.overlays.forEach(o => o.visible = visible);
        this.imageCanvas.render();
        showToast(visible ? '显示标注' : '隐藏标注', 'info');
    }

    /**
     * 隐藏占位符
     */
    hidePlaceholder() {
        const placeholder = this.container.querySelector(`#${this.placeholderId}`);
        if (placeholder) {
            placeholder.style.display = 'none';
        }
    }

    /**
     * 显示占位符
     */
    showPlaceholder() {
        const placeholder = this.container.querySelector(`#${this.placeholderId}`);
        if (placeholder) {
            placeholder.style.display = 'flex';
        }
    }

    setPlaceholderMessage(message, options = {}) {
        const placeholder = this.container.querySelector(`#${this.placeholderId}`);
        if (!placeholder) {
            return;
        }

        const content = placeholder.querySelector('.placeholder-content');
        const text = content?.querySelector('p');
        if (text) {
            text.textContent = message || '等待检测图像';
        }

        let retryButton = content?.querySelector('[data-image-retry]');
        if (typeof options.onRetry === 'function') {
            if (!retryButton && content) {
                retryButton = document.createElement('button');
                retryButton.type = 'button';
                retryButton.className = 'cv-btn cv-btn-secondary';
                retryButton.dataset.imageRetry = 'true';
                retryButton.style.marginTop = '10px';
                content.appendChild(retryButton);
            }

            if (retryButton) {
                retryButton.textContent = options.retryLabel || '重试加载';
                retryButton.onclick = () => options.onRetry();
            }
        } else if (retryButton) {
            retryButton.remove();
        }

        this.showPlaceholder();
    }

    clearImage(message = '等待检测图像', options = {}) {
        this.currentImage = null;
        this.currentImageSource = null;
        this.currentImageSourceKey = null;
        this.imageCanvas?.clear?.();
        this.resetAnnotations();
        this.setPlaceholderMessage(message, options);
        this.updateImageInfo();
    }

    /**
     * 更新图像信息
     */
    updateImageInfo() {
        const info = this.container.querySelector('#image-info');
        if (this.currentImage) {
            info.textContent = `${this.currentImage.width} × ${this.currentImage.height}`;
        }
    }

    /**
     * 更新缩放信息
     */
    updateZoomInfo() {
        const info = this.container.querySelector('#zoom-info');
        const percent = Math.round(this.imageCanvas.scale * 100);
        info.textContent = `${percent}%`;
    }

    /**
     * 获取当前图像数据
     */
    getCurrentImage() {
        return this.currentImage;
    }

    /**
     * 获取缺陷列表
     */
    getDefects() {
        return this.defects;
    }

    isAttachedTo(container = this.container) {
        return !!(
            container
            && this._isDestroyed !== true
            && this.container === container
            && this.canvas
            && (typeof container.contains !== 'function' || container.contains(this.canvas))
        );
    }

    destroy() {
        if (this._isDestroyed) {
            return;
        }

        this._isDestroyed = true;
        const disposers = Array.isArray(this._eventDisposers)
            ? this._eventDisposers.splice(0)
            : [];
        disposers.forEach(dispose => {
            try {
                dispose();
            } catch (error) {
                console.warn('[ImageViewer] Failed to remove event listener during destroy:', error);
            }
        });

        if (this.imageCanvas) {
            if (this._originalImageCanvasLoadImage) {
                this.imageCanvas.loadImage = this._originalImageCanvasLoadImage;
            }
            if (this._originalImageCanvasRender) {
                this.imageCanvas.render = this._originalImageCanvasRender;
            }
            this.imageCanvas.destroy?.();
        }

        this.currentImage = null;
        this.currentImageSource = null;
        this.currentImageSourceKey = null;
        this.defects = [];
        this.omittedDefectCount = 0;
        this.onRegionSelected = null;
        this.onAnnotationClicked = null;
        this.onImageLoaded = null;
        this._originalImageCanvasLoadImage = null;
        this._originalImageCanvasRender = null;
        this.imageCanvas = null;
        this.canvas = null;

        if (this.container) {
            this.container.innerHTML = '';
        }
    }

    dispose() {
        this.destroy();
    }

    escapeHtml(value) {
        return String(value ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }
}

export default ImageViewerComponent;
