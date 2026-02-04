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

import ImageCanvas from '../core/canvas/imageCanvas.js';
import { showToast } from '../shared/components/uiComponents.js';

export class ImageViewerComponent {
    constructor(containerId) {
        this.container = document.getElementById(containerId);
        this.canvas = null;
        this.imageCanvas = null;
        this.currentImage = null;
        this.defects = [];
        
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
        this.imageCanvas = new ImageCanvas('viewer-canvas');
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
                        <button id="btn-open-image" class="cv-btn cv-btn-primary" title="打开图像">
                            <span>📂</span> 打开
                        </button>
                        <input type="file" id="file-input" accept="image/*" style="display: none;">
                    </div>
                    <div class="toolbar-divider"></div>
                    <div class="toolbar-group">
                        <button id="btn-zoom-in" class="cv-btn cv-btn-icon" title="放大">🔍+</button>
                        <button id="btn-zoom-out" class="cv-btn cv-btn-icon" title="缩小">🔍-</button>
                        <button id="btn-fit-window" class="cv-btn cv-btn-icon" title="适应窗口">↔️</button>
                        <button id="btn-actual-size" class="cv-btn cv-btn-icon" title="实际大小">1:1</button>
                    </div>
                    <div class="toolbar-divider"></div>
                    <div class="toolbar-group">
                        <button id="btn-clear-annotations" class="cv-btn cv-btn-secondary" title="清除标注">🗑️ 清除标注</button>
                        <button id="btn-toggle-annotations" class="cv-btn cv-btn-secondary" title="显示/隐藏标注">👁️ 标注</button>
                    </div>
                    <div class="toolbar-info">
                        <span id="image-info">未加载图像</span>
                        <span id="zoom-info">100%</span>
                    </div>
                </div>
                
                <!-- 画布区域 -->
                <div class="viewer-canvas-container">
                    <canvas id="viewer-canvas"></canvas>
                    <div class="viewer-placeholder" id="viewer-placeholder">
                        <div class="placeholder-content">
                            <span class="placeholder-icon">🖼️</span>
                            <p>点击"打开图像"或拖拽图像到此处</p>
                            <p class="placeholder-hint">支持 JPG, PNG, BMP, TIFF 格式</p>
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
        
        this.canvas = document.getElementById('viewer-canvas');
    }

    /**
     * 绑定工具栏事件
     */
    bindToolbarEvents() {
        // 打开文件
        const openBtn = this.container.querySelector('#btn-open-image');
        const fileInput = this.container.querySelector('#file-input');
        
        openBtn.addEventListener('click', () => fileInput.click());
        
        fileInput.addEventListener('change', (e) => {
            if (e.target.files && e.target.files[0]) {
                this.loadFromFile(e.target.files[0]);
            }
        });

        // 缩放控制
        this.container.querySelector('#btn-zoom-in').addEventListener('click', () => {
            this.zoomIn();
        });
        
        this.container.querySelector('#btn-zoom-out').addEventListener('click', () => {
            this.zoomOut();
        });
        
        this.container.querySelector('#btn-fit-window').addEventListener('click', () => {
            this.fitToWindow();
        });
        
        this.container.querySelector('#btn-actual-size').addEventListener('click', () => {
            this.actualSize();
        });

        // 标注控制
        this.container.querySelector('#btn-clear-annotations').addEventListener('click', () => {
            this.clearAnnotations();
        });
        
        this.container.querySelector('#btn-toggle-annotations').addEventListener('click', () => {
            this.toggleAnnotations();
        });

        // 拖拽支持
        const canvasContainer = this.container.querySelector('.viewer-canvas-container');
        canvasContainer.addEventListener('dragover', (e) => {
            e.preventDefault();
            e.dataTransfer.dropEffect = 'copy';
            canvasContainer.classList.add('drag-over');
        });
        
        canvasContainer.addEventListener('dragleave', () => {
            canvasContainer.classList.remove('drag-over');
        });
        
        canvasContainer.addEventListener('drop', (e) => {
            e.preventDefault();
            canvasContainer.classList.remove('drag-over');
            
            if (e.dataTransfer.files && e.dataTransfer.files[0]) {
                this.loadFromFile(e.dataTransfer.files[0]);
            }
        });
    }

    /**
     * 绑定画布事件
     */
    bindCanvasEvents() {
        // 监听图像加载
        const originalLoadImage = this.imageCanvas.loadImage.bind(this.imageCanvas);
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
        this.imageCanvas.render = () => {
            originalRender();
            this.updateZoomInfo();
        };

        // 监听标注点击
        this.imageCanvas.canvas.addEventListener('click', (e) => {
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
    loadFromFile(file) {
        if (!file.type.startsWith('image/')) {
            showToast('请选择有效的图像文件', 'error');
            return Promise.reject(new Error('Invalid file type'));
        }

        showToast(`正在加载: ${file.name}`, 'info');
        
        return this.imageCanvas.loadImage(file).then(() => {
            showToast('图像加载成功', 'success');
        }).catch((err) => {
            showToast('图像加载失败: ' + err.message, 'error');
            throw err;
        });
    }

    /**
     * 从URL加载图像
     */
    loadFromUrl(url) {
        showToast('正在加载图像...', 'info');
        
        return this.imageCanvas.loadImage(url).then(() => {
            showToast('图像加载成功', 'success');
        }).catch((err) => {
            showToast('图像加载失败', 'error');
            throw err;
        });
    }

    /**
     * 从Base64加载图像
     */
    loadFromBase64(base64String, format = 'png') {
        const url = `data:image/${format};base64,${base64String}`;
        return this.loadFromUrl(url);
    }

    /**
     * 从字节数组加载
     */
    loadFromByteArray(byteArray, format = 'png') {
        return this.imageCanvas.loadImageData(byteArray, format);
    }

    /**
     * 显示缺陷标注
     */
    showDefects(defects) {
        this.clearAnnotations();
        this.defects = defects;
        
        defects.forEach((defect, index) => {
            const color = this.getDefectColor(defect.type);
            const overlay = this.imageCanvas.addOverlay(
                'rectangle',
                defect.x,
                defect.y,
                defect.width,
                defect.height,
                {
                    color: color,
                    lineWidth: 3,
                    text: `${index + 1}. ${defect.type}`,
                    fill: true,
                    fillColor: color + '33', // 20%透明度
                    data: defect
                }
            );
            overlay.defectId = defect.id || index;
        });
        
        this.renderDefectList();
    }

    /**
     * 获取缺陷类型对应的颜色
     */
    getDefectColor(type) {
        const colors = {
            '划痕': '#ff4d4f',
            '污渍': '#faad14',
            '异物': '#52c41a',
            '缺失': '#1890ff',
            '变形': '#722ed1',
            '尺寸偏差': '#eb2f96',
            '颜色异常': '#13c2c2',
            '其他': '#8c8c8c'
        };
        return colors[type] || '#ff4d4f';
    }

    /**
     * 渲染缺陷列表
     */
    renderDefectList() {
        const list = this.container.querySelector('#defect-list');
        
        if (this.defects.length === 0) {
            list.innerHTML = '<div class="defect-empty">暂无缺陷</div>';
            return;
        }
        
        list.innerHTML = this.defects.map((defect, index) => `
            <div class="defect-item" data-id="${defect.id || index}">
                <span class="defect-index" style="background: ${this.getDefectColor(defect.type)}">${index + 1}</span>
                <div class="defect-info">
                    <span class="defect-type">${defect.type}</span>
                    <span class="defect-position">位置: (${defect.x}, ${defect.y})</span>
                    <span class="defect-confidence">置信度: ${(defect.confidence * 100).toFixed(1)}%</span>
                </div>
            </div>
        `).join('');
        
        // 绑定点击事件
        list.querySelectorAll('.defect-item').forEach(item => {
            item.addEventListener('click', () => {
                const id = item.dataset.id;
                this.selectDefect(id);
            });
        });
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
            if (overlay.defectId === defectId) {
                overlay.lineWidth = 5;
                overlay.color = '#ffffff';
            } else {
                overlay.lineWidth = 3;
                overlay.color = this.getDefectColor(overlay.data?.type);
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
        this.imageCanvas.clearOverlays();
        this.defects = [];
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
        const placeholder = this.container.querySelector('#viewer-placeholder');
        if (placeholder) {
            placeholder.style.display = 'none';
        }
    }

    /**
     * 显示占位符
     */
    showPlaceholder() {
        const placeholder = this.container.querySelector('#viewer-placeholder');
        if (placeholder) {
            placeholder.style.display = 'flex';
        }
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
}

export default ImageViewerComponent;
