/**
 * planarScaleOffsetCalibWizard.js
 * 二维平面比例偏移标定三步向导组件
 *
 * Step 1: 采集标定点
 * Step 2: 验证与求解
 * Step 3: 保存工程标定资产
 */
import webMessageBridge from '../messaging/webMessageBridge.js';

const MIN_ACCEPTED_POINT_COUNT = 3;

export class PlanarScaleOffsetCalibWizard {
    constructor(cameraManager = null, options = {}) {
        this.cameraManager = cameraManager;
        this.captureFrame = typeof options.captureFrame === 'function' ? options.captureFrame : null;
        this.getCameraBindingId = typeof options.getCameraBindingId === 'function' ? options.getCameraBindingId : null;
        this.getProjectId = typeof options.getProjectId === 'function' ? options.getProjectId : null;
        this.projectId = options.projectId ?? null;
        this.getExpectedPersistenceRevision = typeof options.getExpectedPersistenceRevision === 'function'
            ? options.getExpectedPersistenceRevision
            : null;
        this.expectedPersistenceRevision = options.expectedPersistenceRevision ?? null;
        this.getSessionId = typeof options.getSessionId === 'function' ? options.getSessionId : null;
        this.sessionId = options.sessionId ?? null;
        this.assetId = typeof options.assetId === 'string' && options.assetId.trim()
            ? options.assetId.trim()
            : 'planar-scale-offset';
        this.currentStep = 1;
        this.points = [];
        this.solveResult = null;
        this.solveArtifact = null;
        this.solveContext = null;
        this.overlay = null;
        this.els = null;
        this._boundRenderFrame = null;
        this._boundKeydown = null;
        this._cameraPreviewUrl = null;

        this.onWebMessageReceived = this.onWebMessageReceived.bind(this);

        this.createUI();
        this.attachEvents();
    }

    createUI() {
        this.overlay = document.createElement('div');
        this.overlay.className = 'calib-wizard-overlay';

        const modal = document.createElement('div');
        modal.className = 'calib-wizard-modal';

        modal.innerHTML = `
            <div class="calib-wizard-header">
                <div class="calib-wizard-title">
                    <span>🔡</span> 二维平面比例偏移标定向导
                </div>
                <button class="calib-wizard-close" type="button" id="calib-btn-close" aria-label="关闭">×</button>
            </div>
            <div class="calib-stepper">
                <div class="calib-step active" id="calib-step-1-indic">
                    <div class="calib-step-circle">1</div>
                    <div class="calib-step-label">采集标定点</div>
                </div>
                <div class="calib-step" id="calib-step-2-indic">
                    <div class="calib-step-circle">2</div>
                    <div class="calib-step-label">验证与求解</div>
                </div>
                <div class="calib-step" id="calib-step-3-indic">
                    <div class="calib-step-circle">3</div>
                    <div class="calib-step-label">保存工程资产</div>
                </div>
            </div>
            <div class="calib-wizard-body">
                <div class="calib-step-content active" id="calib-step-1-content">
                    <div class="calib-layout-s1">
                        <div class="calib-camera-view" id="calib-camera-view">
                            <div class="calib-camera-placeholder">
                                <i class="fas fa-camera" style="font-size: 32px; margin-bottom: 8px;"></i>
                                <span id="calib-camera-placeholder-primary">请先在设置页相机管理中选择一台相机</span>
                                <span id="calib-camera-placeholder-secondary">点击“刷新预览”获取一帧图像后，再在画面上选点</span>
                            </div>
                            <img id="calib-camera-img" class="calib-camera-img" style="display:none;" />
                            <div id="calib-marker" class="calib-point-marker" style="display:none;"></div>
                        </div>
                        <div class="calib-data-panel">
                            <div class="calib-input-group">
                                <div class="calib-input-heading">当前点位录入（至少 ${MIN_ACCEPTED_POINT_COUNT} 个有效点）</div>
                                <div class="calib-input-row">
                                    <label>像素 X:</label>
                                    <input type="number" id="calib-px" step="0.1" placeholder="点击图像获取" readonly>
                                </div>
                                <div class="calib-input-row">
                                    <label>像素 Y:</label>
                                    <input type="number" id="calib-py" step="0.1" placeholder="点击图像获取" readonly>
                                </div>
                                <div class="calib-input-row" style="margin-top: 8px;">
                                    <label>物理 X:</label>
                                    <input type="number" id="calib-hx" step="0.001" placeholder="示教器 X 坐标 (mm)">
                                </div>
                                <div class="calib-input-row">
                                    <label>物理 Y:</label>
                                    <input type="number" id="calib-hy" step="0.001" placeholder="示教器 Y 坐标 (mm)">
                                </div>
                                <button class="calib-btn-add" id="calib-btn-add" type="button" disabled>添加标定点</button>
                                <button class="calib-btn calib-btn-prev" id="calib-btn-refresh-preview" type="button" style="margin-top: 8px;">刷新预览</button>
                            </div>

                            <div class="calib-table-container">
                                <table class="calib-table">
                                    <thead>
                                        <tr>
                                            <th width="40px">#</th>
                                            <th>像素 (X, Y)</th>
                                            <th>物理 (X, Y)</th>
                                            <th width="40px">操作</th>
                                        </tr>
                                    </thead>
                                    <tbody id="calib-table-body"></tbody>
                                </table>
                            </div>
                        </div>
                    </div>
                </div>

                <div class="calib-step-content" id="calib-step-2-content">
                    <div class="calib-layout-s2">
                        <div class="calib-solve-heading">
                            <h3>开始解算标定矩阵</h3>
                            <p id="calib-s2-desc">已采集 0 个有效点位，将使用最小二乘法进行线性回归解算。</p>
                        </div>

                        <button class="calib-solve-btn" id="calib-btn-solve" type="button">
                            <span style="font-size: 20px;">▶</span> 执行计算
                        </button>

                        <div class="calib-solve-result" id="calib-solve-result">
                            <h4 class="calib-result-heading">
                                <span>解算成功</span>
                                <span id="calib-status-badge" class="calib-status-badge">数据更新</span>
                            </h4>

                            <p id="calib-quality-hint" class="calib-quality-hint"></p>

                            <div class="calib-result-grid">
                                <div class="calib-metric">
                                    <div class="calib-metric-title">重投影根均方误差 (RMSE)</div>
                                    <div class="calib-metric-value" id="calib-res-rmse">0.000 <span class="calib-metric-unit">mm</span></div>
                                </div>
                                <div class="calib-metric">
                                    <div class="calib-metric-title">平均像素物理尺寸 (Scale)</div>
                                    <div class="calib-metric-value" id="calib-res-scale">0.000000 <span class="calib-metric-unit">mm/px</span></div>
                                </div>
                                <div class="calib-metric">
                                    <div class="calib-metric-title">坐标原点 X (Origin X)</div>
                                    <div class="calib-metric-value" id="calib-res-ox">0.000 <span class="calib-metric-unit">mm</span></div>
                                </div>
                                <div class="calib-metric">
                                    <div class="calib-metric-title">坐标原点 Y (Origin Y)</div>
                                    <div class="calib-metric-value" id="calib-res-oy">0.000 <span class="calib-metric-unit">mm</span></div>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>

                <div class="calib-step-content" id="calib-step-3-content">
                    <div class="calib-layout-s3">
                        <div class="calib-save-icon">✓</div>
                        <h3 style="margin: 0; font-size: 20px; color: var(--text-primary);">标定数据已就绪</h3>
                        <p style="color: var(--text-secondary); font-size: 14px; margin: 0 0 10px 0;">
                            正式保存会使用本次服务端解算凭据写入当前工程；没有工程上下文时仍可查看结果，但不能保存。
                        </p>

                        <div class="calib-save-input-group">
                            <label>工程标定资产 ID:</label>
                            <input type="text" id="calib-asset-id" value="planar-scale-offset" autocomplete="off">
                        </div>
                        <p id="calib-save-context-hint" class="calib-quality-hint"></p>
                    </div>
                </div>
            </div>
            <div class="calib-wizard-footer">
                <button class="calib-btn calib-btn-prev" id="calib-btn-prev" type="button" style="visibility: hidden;">上一步</button>
                <button class="calib-btn calib-btn-next" id="calib-btn-next" type="button" disabled>下一步</button>
            </div>
        `;

        this.overlay.appendChild(modal);

        this.els = {
            modal,
            step1: this.overlay.querySelector('#calib-step-1-content'),
            step2: this.overlay.querySelector('#calib-step-2-content'),
            step3: this.overlay.querySelector('#calib-step-3-content'),
            indic1: this.overlay.querySelector('#calib-step-1-indic'),
            indic2: this.overlay.querySelector('#calib-step-2-indic'),
            indic3: this.overlay.querySelector('#calib-step-3-indic'),
            btnPrev: this.overlay.querySelector('#calib-btn-prev'),
            btnNext: this.overlay.querySelector('#calib-btn-next'),
            btnClose: this.overlay.querySelector('#calib-btn-close'),
            btnRefreshPreview: this.overlay.querySelector('#calib-btn-refresh-preview'),
            camImg: this.overlay.querySelector('#calib-camera-img'),
            camArea: this.overlay.querySelector('#calib-camera-view'),
            marker: this.overlay.querySelector('#calib-marker'),
            placeholder: this.overlay.querySelector('.calib-camera-placeholder'),
            placeholderPrimary: this.overlay.querySelector('#calib-camera-placeholder-primary'),
            placeholderSecondary: this.overlay.querySelector('#calib-camera-placeholder-secondary'),
            inpPx: this.overlay.querySelector('#calib-px'),
            inpPy: this.overlay.querySelector('#calib-py'),
            inpHx: this.overlay.querySelector('#calib-hx'),
            inpHy: this.overlay.querySelector('#calib-hy'),
            btnAdd: this.overlay.querySelector('#calib-btn-add'),
            tbody: this.overlay.querySelector('#calib-table-body'),
            desc2: this.overlay.querySelector('#calib-s2-desc'),
            btnSolve: this.overlay.querySelector('#calib-btn-solve'),
            resPanel: this.overlay.querySelector('#calib-solve-result'),
            valRmse: this.overlay.querySelector('#calib-res-rmse'),
            valScale: this.overlay.querySelector('#calib-res-scale'),
            valOx: this.overlay.querySelector('#calib-res-ox'),
            valOy: this.overlay.querySelector('#calib-res-oy'),
            badge: this.overlay.querySelector('#calib-status-badge'),
            qualityHint: this.overlay.querySelector('#calib-quality-hint'),
            inpAssetId: this.overlay.querySelector('#calib-asset-id'),
            saveContextHint: this.overlay.querySelector('#calib-save-context-hint')
        };
        this.els.inpAssetId.value = this.assetId;
    }

    attachEvents() {
        this.els.btnClose.addEventListener('click', () => this.hide());
        this.overlay.addEventListener('click', (event) => {
            if (event.target === this.overlay) {
                this.hide();
            }
        });

        this.els.btnNext.addEventListener('click', () => {
            if (this.currentStep === 3) {
                this.saveCalibration();
            } else {
                this.goToStep(this.currentStep + 1);
            }
        });

        this.els.btnPrev.addEventListener('click', () => {
            this.goToStep(this.currentStep - 1);
        });

        this.els.btnRefreshPreview?.addEventListener('click', () => {
            this.refreshPreviewFrame();
        });

        this.els.camImg.addEventListener('click', (event) => {
            const rect = this.els.camImg.getBoundingClientRect();
            const normalizedX = (event.clientX - rect.left) / rect.width;
            const normalizedY = (event.clientY - rect.top) / rect.height;
            const actualPx = normalizedX * this.els.camImg.naturalWidth;
            const actualPy = normalizedY * this.els.camImg.naturalHeight;

            this.els.inpPx.value = actualPx.toFixed(1);
            this.els.inpPy.value = actualPy.toFixed(1);

            this.els.marker.style.display = 'block';
            this.els.marker.style.left = normalizedX * 100 + '%';
            this.els.marker.style.top = normalizedY * 100 + '%';

            this.checkAddButtonState();
            this.els.inpHx.focus();
        });

        [this.els.inpPx, this.els.inpPy, this.els.inpHx, this.els.inpHy].forEach((el) => {
            el.addEventListener('input', () => this.checkAddButtonState());
        });
        this.els.inpAssetId.addEventListener('input', () => {
            if (this.currentStep === 3) this.refreshFormalSaveState();
        });

        this.els.btnAdd.addEventListener('click', () => {
            const point = {
                pixelX: parseFloat(this.els.inpPx.value),
                pixelY: parseFloat(this.els.inpPy.value),
                physicalX: parseFloat(this.els.inpHx.value),
                physicalY: parseFloat(this.els.inpHy.value)
            };

            this.points.push(point);
            this.invalidateSolveResult();
            this.renderTable();
            this.clearCurrentPointInputs();
        });

        this.els.btnSolve.addEventListener('click', () => {
            this.solveCalibration();
        });
    }

    checkAddButtonState() {
        const hasValidInputs = this.els.inpPx.value !== ''
            && this.els.inpPy.value !== ''
            && this.els.inpHx.value !== ''
            && this.els.inpHy.value !== '';
        this.els.btnAdd.disabled = !hasValidInputs;
    }

    clearCurrentPointInputs() {
        this.els.inpPx.value = '';
        this.els.inpPy.value = '';
        this.els.inpHx.value = '';
        this.els.inpHy.value = '';
        this.els.marker.style.display = 'none';
        this.checkAddButtonState();
    }

    renderTable() {
        this.els.tbody.innerHTML = '';
        this.points.forEach((point, index) => {
            const row = document.createElement('tr');
            row.innerHTML = `
                <td>${index + 1}</td>
                <td>(${point.pixelX}, ${point.pixelY})</td>
                <td>(${point.physicalX}, ${point.physicalY})</td>
                <td><button class="calib-btn-del" type="button" data-index="${index}">×</button></td>
            `;
            this.els.tbody.appendChild(row);
        });

        this.els.tbody.querySelectorAll('.calib-btn-del').forEach((button) => {
            button.addEventListener('click', (event) => {
                const index = parseInt(event.currentTarget.dataset.index, 10);
                this.points.splice(index, 1);
                this.invalidateSolveResult();
                this.renderTable();
            });
        });

        if (this.currentStep === 1) {
            this.els.btnNext.disabled = this.points.length < MIN_ACCEPTED_POINT_COUNT;
        }
    }

    invalidateSolveResult() {
        this.solveResult = null;
        this.solveArtifact = null;
        this.solveContext = null;
        this.els.resPanel.classList.remove('visible');
        this.els.qualityHint.textContent = '';
        if (this.currentStep === 2) {
            this.els.btnNext.disabled = true;
        }
    }

    resolveProjectId() {
        const value = this.getProjectId?.() ?? this.projectId;
        return value == null ? '' : String(value).trim();
    }

    resolveSessionId() {
        const value = this.getSessionId?.() ?? this.sessionId;
        return value == null ? '' : String(value).trim();
    }

    resolveExpectedPersistenceRevision() {
        const value = this.getExpectedPersistenceRevision?.() ?? this.expectedPersistenceRevision;
        if (value == null || value === '') return null;
        const revision = Number(value);
        return Number.isSafeInteger(revision) && revision >= 0 ? revision : null;
    }

    getFormalSaveBlockReason() {
        const projectId = this.resolveProjectId();
        if (!projectId) return '当前页面没有工程上下文，本次结果仅供查看。';
        if (!this.solveArtifact?.artifactId) return '缺少服务端解算凭据，请在当前工程上下文中重新执行计算。';
        if (this.solveContext?.projectId !== projectId) return '工程上下文已变化，请重新执行计算。';
        if (this.resolveExpectedPersistenceRevision() == null) return '缺少工程持久化 revision，不能正式保存。';
        if (!this.els.inpAssetId.value.trim()) return '请输入工程标定资产 ID。';
        return '';
    }

    refreshFormalSaveState() {
        const reason = this.getFormalSaveBlockReason();
        this.els.btnNext.disabled = Boolean(reason);
        this.els.saveContextHint.textContent = reason || '服务端解算凭据有效，可以按当前工程 revision 保存。';
        this.els.inpAssetId.disabled = !this.resolveProjectId();
    }

    goToStep(step) {
        if (step < 1 || step > 3) return;
        this.currentStep = step;

        this.els.step1.classList.toggle('active', step === 1);
        this.els.step2.classList.toggle('active', step === 2);
        this.els.step3.classList.toggle('active', step === 3);

        this.els.indic1.className = `calib-step ${step >= 1 ? 'active ' : ''}${step > 1 ? 'completed' : ''}`.trim();
        this.els.indic2.className = `calib-step ${step >= 2 ? 'active ' : ''}${step > 2 ? 'completed' : ''}`.trim();
        this.els.indic3.className = `calib-step ${step >= 3 ? 'active' : ''}`.trim();

        this.els.btnPrev.style.visibility = step === 1 ? 'hidden' : 'visible';

        if (step === 1) {
            this.els.btnNext.textContent = '下一步';
            this.els.btnNext.disabled = this.points.length < MIN_ACCEPTED_POINT_COUNT;
        } else if (step === 2) {
            this.els.btnNext.textContent = '下一步';
            this.els.btnNext.disabled = !this.solveResult?.accepted;
            this.els.desc2.textContent = `已采集 ${this.points.length} 个点位，点击执行计算进行最小二乘法解算。`;
        } else {
            this.els.btnNext.textContent = '保存并完成';
            this.refreshFormalSaveState();
        }
    }

    show() {
        if (!this.overlay.isConnected) {
            document.body.appendChild(this.overlay);
        }

        this.overlay.classList.add('visible');
        this.goToStep(1);

        if (this.cameraManager) {
            this._boundRenderFrame = this.renderFrame.bind(this);
            this.cameraManager.addEventListener('frame', this._boundRenderFrame);
        }

        if (!this._boundKeydown) {
            this._boundKeydown = (event) => {
                if (event.key === 'Escape') {
                    this.hide();
                }
            };
        }
        window.addEventListener('keydown', this._boundKeydown);
        window.chrome?.webview?.addEventListener('message', this.onWebMessageReceived);

        this.refreshPreviewFrame();
    }

    hide() {
        this.overlay.classList.remove('visible');

        if (this.cameraManager && this._boundRenderFrame) {
            this.cameraManager.removeEventListener('frame', this._boundRenderFrame);
            this._boundRenderFrame = null;
        }

        if (this._boundKeydown) {
            window.removeEventListener('keydown', this._boundKeydown);
        }

        window.chrome?.webview?.removeEventListener('message', this.onWebMessageReceived);
        this.destroyPreviewUrl();
        this.overlay.remove();
    }

    destroyPreviewUrl() {
        if (this._cameraPreviewUrl) {
            URL.revokeObjectURL(this._cameraPreviewUrl);
            this._cameraPreviewUrl = null;
        }
    }

    togglePlaceholder(visible, primaryText = null, secondaryText = null) {
        this.els.placeholder.style.display = visible ? 'flex' : 'none';
        this.els.camImg.style.display = visible ? 'none' : 'block';
        if (primaryText) {
            this.els.placeholderPrimary.textContent = primaryText;
        }
        if (secondaryText) {
            this.els.placeholderSecondary.textContent = secondaryText;
        }
    }

    async refreshPreviewFrame() {
        if (!this.captureFrame) {
            this.togglePlaceholder(
                true,
                '当前环境未接入相机预览能力',
                '请返回设置页相机管理确认相机预览是否可用'
            );
            return;
        }

        const cameraBindingId = this.getCameraBindingId?.() || null;
        if (!cameraBindingId) {
            this.togglePlaceholder(
                true,
                '请先在设置页相机管理中选择一台相机',
                '然后点击“刷新预览”获取一帧图像'
            );
            return;
        }

        try {
            this.els.btnRefreshPreview.disabled = true;
            this.els.btnRefreshPreview.textContent = '预览加载中...';
            const preview = await this.captureFrame(cameraBindingId);

            this.destroyPreviewUrl();
            this._cameraPreviewUrl = preview.imageUrl;
            this.els.camImg.src = preview.imageUrl;
            this.togglePlaceholder(false);
        } catch (error) {
            this.togglePlaceholder(
                true,
                '相机预览加载失败',
                error.message || '请检查相机连接、曝光参数和软触发链路'
            );
        } finally {
            this.els.btnRefreshPreview.disabled = false;
            this.els.btnRefreshPreview.textContent = '刷新预览';
        }
    }

    renderFrame(event) {
        if (!event.detail?.data) return;
        this.togglePlaceholder(false);
        this.els.camImg.src = `data:image/jpeg;base64,${event.detail.data}`;
    }

    solveCalibration() {
        if (this.points.length < MIN_ACCEPTED_POINT_COUNT) {
            alert(`至少需要 ${MIN_ACCEPTED_POINT_COUNT} 个空间分布有效的标定点才能进行生产标定。`);
            this.goToStep(1);
            return;
        }

        this.els.btnSolve.innerHTML = '<span style="font-size: 20px;">◌</span> 解算中...';
        this.els.btnSolve.disabled = true;
        this.els.btnNext.disabled = true;

        const solveContext = {
            projectId: this.resolveProjectId(),
            sessionId: this.resolveSessionId(),
            assetId: this.els.inpAssetId.value.trim(),
            cameraBindingId: this.getCameraBindingId?.() || ''
        };
        this.solveArtifact = null;
        this.solveContext = solveContext;

        if (window.chrome?.webview) {
            webMessageBridge.sendMessage('planar2d:solve', {
                payload: {
                    points: this.points,
                    ...solveContext
                }
            });
            return;
        }

        setTimeout(() => {
            this.handleSolveResult({
                success: true,
                accepted: true,
                message: 'Solve succeeded and passed planar scale-offset acceptance.',
                rmse: 0.043,
                scaleX: 0.051,
                scaleY: 0.051,
                originX: Math.random() * 10,
                originY: -Math.random() * 10,
                meanErrorX: 0.03,
                meanErrorY: 0.03,
                pointCount: this.points.length
            });
        }, 500);
    }

    handleSolveResult(result) {
        this.els.btnSolve.innerHTML = '<span style="font-size: 20px;">▶</span> 重新执行计算';
        this.els.btnSolve.disabled = false;

        if (!result.success) {
            this.solveResult = null;
            this.els.resPanel.classList.remove('visible');
            this.els.btnNext.disabled = true;
            alert(`标定解算失败: ${result.message || '数据异常或共线'}`);
            return;
        }

        this.solveResult = result;
        this.solveArtifact = result.solveArtifact || null;
        this.els.resPanel.classList.add('visible');

        this.els.valRmse.innerHTML = `${parseFloat(result.rmse).toFixed(3)} <span class="calib-metric-unit">mm</span>`;
        if (result.rmse < 1.0) {
            this.els.valRmse.parentElement.classList.remove('calib-rmse-warn');
            this.els.valRmse.parentElement.classList.add('calib-rmse-ok');
        } else {
            this.els.valRmse.parentElement.classList.remove('calib-rmse-ok');
            this.els.valRmse.parentElement.classList.add('calib-rmse-warn');
        }

        const scaleAvg = ((Math.abs(result.scaleX) + Math.abs(result.scaleY)) / 2).toFixed(6);
        this.els.valScale.innerHTML = `${scaleAvg} <span class="calib-metric-unit">mm/px</span>`;
        this.els.valOx.innerHTML = `${parseFloat(result.originX).toFixed(3)} <span class="calib-metric-unit">mm</span>`;
        this.els.valOy.innerHTML = `${parseFloat(result.originY).toFixed(3)} <span class="calib-metric-unit">mm</span>`;

        this.els.btnNext.disabled = !result.accepted;
        if (!result.accepted) {
            this.els.badge.textContent = '需要复核';
            this.els.badge.classList.remove('accepted');
            this.els.badge.classList.add('review');
            const reasons = [];
            if ((result.pointCount ?? this.points.length) < MIN_ACCEPTED_POINT_COUNT) {
                reasons.push(`有效点位不足 ${MIN_ACCEPTED_POINT_COUNT} 个`);
            }
            if (Number(result.rmse) > 0.15) {
                reasons.push('RMSE 超过 0.15 mm');
            }
            if (Number(result.meanErrorX) > 0.10 || Number(result.meanErrorY) > 0.10) {
                reasons.push('单轴平均误差超过 0.10 mm');
            }
            this.els.qualityHint.textContent = `${reasons.join('；') || result.message || '当前结果未通过生产验收门槛'}。请返回上一步补充或调整标定点后重新计算。`;
        } else {
            this.els.badge.textContent = '已通过';
            this.els.badge.classList.remove('review');
            this.els.badge.classList.add('accepted');
            this.els.qualityHint.textContent = this.solveArtifact?.artifactId
                ? '结果已通过生产验收门槛，并已生成当前工程可用的服务端解算凭据。'
                : '结果已通过生产验收门槛；当前无工程上下文，仅可查看，不能正式保存。';
        }
    }

    saveCalibration() {
        if (!this.solveResult?.accepted) {
            alert('当前二维平面标定结果未通过验收门槛，不能保存为生产可用标定文件。');
            return;
        }

        const blockReason = this.getFormalSaveBlockReason();
        if (blockReason) {
            alert(blockReason);
            this.refreshFormalSaveState();
            return;
        }

        const projectId = this.resolveProjectId();
        const expectedPersistenceRevision = this.resolveExpectedPersistenceRevision();
        const assetId = this.els.inpAssetId.value.trim();
        this.els.btnNext.disabled = true;
        this.els.btnNext.textContent = '保存中...';

        if (window.chrome?.webview) {
            webMessageBridge.sendMessage('planar2d:save', {
                payload: {
                    solveArtifactId: this.solveArtifact.artifactId,
                    projectId,
                    expectedPersistenceRevision,
                    assetId,
                    sessionId: this.solveContext.sessionId,
                    cameraBindingId: this.solveContext.cameraBindingId
                }
            });
            return;
        }

        setTimeout(() => this.handleSaveResult({ success: true }), 500);
    }

    handleSaveResult(result) {
        this.els.btnNext.textContent = '保存并完成';

        if (!result.success) {
            this.refreshFormalSaveState();
            alert(`保存失败: ${result.message || '未知错误'}`);
            return;
        }

        const toast = document.createElement('div');
        toast.style.cssText = 'position: fixed; top: 20px; right: 20px; background: #10b981; color: white; padding: 12px 24px; border-radius: 8px; font-weight: 500; font-size: 14px; box-shadow: 0 4px 12px rgba(16,185,129,0.3); z-index: 10000;';
        toast.textContent = '✓ 工程标定资产保存成功';
        document.body.appendChild(toast);

        setTimeout(() => {
            toast.remove();
            this.hide();
        }, 1200);
    }

    onWebMessageReceived(event) {
        try {
            const data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
            if (data.messageType === 'planar2d:solve:result') {
                this.handleSolveResult(data.payload);
            } else if (data.messageType === 'planar2d:save:result') {
                this.handleSaveResult(data.payload);
            }
        } catch (error) {
            console.error('[HandEye] Message parse error', error);
        }
    }
}
