import webMessageBridge from '../../core/messaging/webMessageBridge.js';
import httpClient from '../../core/messaging/httpClient.js';
import serviceRegistry from '../../core/app/serviceRegistry.js';
import projectManager from '../project/projectManager.js';
import inspectionController, {
    getResultImageUrl,
    loadImageUrlAsBase64
} from '../inspection/inspectionController.js';
import PreviewPanel from './previewPanel.js';
import RoiEditorPanel from './roiEditorPanel.js';
import CalibrationDraftWorkbench from './calibrationDraftWorkbench.js';
import { resolvePreviewInputImageBase64 } from './previewCoordinator.js';
import { buildWireSequenceFollowupHint, createWireSequenceParameterPatch } from './wireSequenceAssist.js';
import {
    getOperatorRoiConfig,
    isCircleSearchV2ToolEnabled,
    isNPointCalibrationWorkbenchEnabled
} from './roiEditorSupport.mjs';
import { getOperatorTypeDisplayName } from '../../shared/operatorDisplayNames.js';
import {
    collectEffectiveRequiredParameterErrors,
    getParameterEffectiveState,
    normalizeAcquisitionSourceType
} from '../../shared/parameterDependencyRules.js';
import { isVariableCompatibleWithDataType } from '../global-variables/globalVariableStore.js';

function normalizeParameterName(name) {
    return String(name || '').trim().toLowerCase();
}

function isEmptyValue(value) {
    return value === null || value === undefined || (typeof value === 'string' && value.trim() === '');
}

function isParameterRequired(param) {
    return Boolean(param?.isRequired ?? param?.IsRequired);
}

function getParameterLabel(param, fallbackName = '') {
    return param?.displayName || param?.DisplayName || param?.name || param?.Name || fallbackName || '参数';
}

function getParameterDataType(param, fallbackType = '') {
    return String(param?.dataType || param?.DataType || param?.type || param?.Type || fallbackType || '').trim();
}

function getParameterEffectiveValue(param) {
    return param?.value ?? param?.Value ?? param?.defaultValue ?? param?.DefaultValue ?? null;
}

function getParameterRangeValue(param, ...keys) {
    for (const key of keys) {
        if (param?.[key] !== undefined && param?.[key] !== null && param?.[key] !== '') {
            const parsed = Number(param[key]);
            if (Number.isFinite(parsed)) {
                return parsed;
            }
        }
    }

    return null;
}

function parseNumericValue(rawValue) {
    if (isEmptyValue(rawValue)) {
        return { empty: true, valid: false, value: null };
    }

    const value = Number(String(rawValue).trim());
    return {
        empty: false,
        valid: Number.isFinite(value),
        value: Number.isFinite(value) ? value : null
    };
}

const CIRCLE_MEASUREMENT_HOUGH_PARAMS = new Set(['dp', 'mindist', 'param1', 'param2']);
const CIRCLE_MEASUREMENT_V2_PARAMS = new Set([
    'searchcentermode',
    'searchcenterx',
    'searchcentery',
    'nominalradius',
    'calipercount',
    'averagingthickness',
    'profilesamplecount',
    'gaussiansigma',
    'edgepolarity',
    'edgethreshold',
    'minedgestrength',
    'minvalidcalipers',
    'mincoverageratio',
    'minangularcoveragedegrees',
    'outliermode',
    'outlierthreshold',
    'maxoutlieriterations',
    'maxresidualrmse'
]);
const CIRCLE_MEASUREMENT_GROUPS = [
    ['检测方法', ['method']],
    ['搜索几何', ['searchcentermode', 'searchcenterx', 'searchcentery', 'minradius', 'nominalradius', 'maxradius']],
    ['卡尺采样', ['calipercount', 'averagingthickness', 'profilesamplecount', 'gaussiansigma']],
    ['边缘', ['edgepolarity', 'edgethreshold', 'minedgestrength']],
    ['稳健拟合', ['outliermode', 'outlierthreshold', 'maxoutlieriterations']],
    ['质量门禁', ['minvalidcalipers', 'mincoverageratio', 'minangularcoveragedegrees', 'maxresidualrmse']]
];

class PropertyPanel {
    constructor(containerId, options = {}) {
        this.container = document.getElementById(containerId);
        this.currentOperator = null;
        this.onChangeCallback = null;
        this.previewPanel = null;
        this.roiEditorPanel = null;
        this.calibrationDraftWorkbench = null;
        this.previewCoordinator = options.previewCoordinator ?? null;
        this.onOpenPreviewImage = options.onOpenPreviewImage ?? (() => {});
        this.previewResourcesEnabled = options.previewResourcesEnabled !== false;
        this.loadImageUrlAsBase64 = options.loadImageUrlAsBase64 ?? loadImageUrlAsBase64;
        this.circleSearchV2ToolEnabled = options.circleSearchV2ToolEnabled;
        this.nPointCalibrationWorkbenchEnabled = options.nPointCalibrationWorkbenchEnabled;
        this.pendingRecommendation = null;
        this.recommendedFieldNames = new Set();
        this.cameraBindingsCache = [];
        this.cameraBindingsLoadingPromise = null;
        this.inputImageBase64Load = null;
        this.circleSearchV2ImageBounds = null;
        this.recommendationSupportedOperators = new Set([
            'Thresholding',
            'Filtering',
            'GaussianBlur',
            'BlobAnalysis',
            'SharpnessEvaluation'
        ]);
        this.disposeGlobalEvents = this.bindGlobalEvents();
    }

    /**
     * 绑定全局事件
     */
    bindGlobalEvents() {
        return webMessageBridge.on('FilePickedEvent', (event) => {
            const payload = event?.payload || event?.data || event || {};
            const isCancelled = Boolean(payload.IsCancelled ?? payload.isCancelled);
            if (isCancelled) return;

            const parameterNameRaw = payload.ParameterName ?? payload.parameterName;
            const filePathRaw = payload.FilePath ?? payload.filePath;
            const parameterName = String(parameterNameRaw || '').trim();
            const filePath = filePathRaw == null ? '' : String(filePathRaw);

            if (!parameterName) {
                console.warn('[PropertyPanel] FilePickedEvent missing parameterName:', payload);
                return;
            }

            const escapedName = (typeof CSS !== 'undefined' && typeof CSS.escape === 'function')
                ? CSS.escape(parameterName)
                : parameterName;

            let input = this.container.querySelector(`#param-${escapedName}`);
            if (!input) {
                input = this.container.querySelector(`input[name="${escapedName}"], select[name="${escapedName}"]`);
            }
            if (!input) {
                const allNamedInputs = this.container.querySelectorAll('input[name], select[name]');
                input = Array.from(allNamedInputs).find(item =>
                    String(item.name || '').toLowerCase() === parameterName.toLowerCase()) || null;
            }
            if (!input) {
                console.warn('[PropertyPanel] File parameter input not found:', parameterName);
                return;
            }

            if (this.currentOperator?.type === 'ImageAcquisition' && parameterName.toLowerCase() === 'filepath') {
                const sourceTypeInput = this.container.querySelector('#param-SourceType, #param-sourceType, select[name="SourceType"], select[name="sourceType"]');
                if (sourceTypeInput && this.normalizeSourceTypeValue(sourceTypeInput.value) !== 'file') {
                    const fileOption = Array.from(sourceTypeInput.options || [])
                        .find(option => this.normalizeSourceTypeValue(option.value) === 'file');
                    sourceTypeInput.value = fileOption?.value || 'File';
                    sourceTypeInput.dispatchEvent(new Event('change', { bubbles: true }));
                }
            }

            input.value = filePath;
            input.dispatchEvent(new Event('change', { bubbles: true }));

            if (this.currentOperator?.type === 'ImageAcquisition' && parameterName.toLowerCase() === 'filepath') {
                this.syncImageAcquisitionSourceControls({ clearFilePathWhenCamera: false });
            }

            this.applyChanges();
        });
    }

    /**
     * 设置算子
     */
    setOperator(operator) {
        const previousKey = this.getOperatorIdentity(this.currentOperator);
        const nextKey = this.getOperatorIdentity(operator);
        if (!operator || previousKey !== nextKey) {
            this.pendingRecommendation = null;
            this.recommendedFieldNames.clear();
        }
        this.currentOperator = operator;
        this.render();
    }

    getOperatorIdentity(operator) {
        if (!operator) {
            return '';
        }

        return String(operator.id || operator.type || operator.Type || operator.displayName || operator.DisplayName || '');
    }

    /**
     * 清空面板
     */
    clear() {
        if (this.previewPanel) {
            this.previewPanel.destroy();
            this.previewPanel = null;
        }
        if (this.roiEditorPanel) {
            this.roiEditorPanel.destroy();
            this.roiEditorPanel = null;
        }
        if (this.calibrationDraftWorkbench) {
            this.calibrationDraftWorkbench.destroy();
            this.calibrationDraftWorkbench = null;
        }
        this.inputImageBase64Load = null;
        this.currentOperator = null;
        this.container.innerHTML = `
            <p class="empty-text">选择一个算子查看属性</p>
            ${this.previewResourcesEnabled ? '<div id="operator-preview-container"></div>' : ''}
        `;
        this.initPreviewPanel();
    }

    destroy() {
        this.disposeGlobalEvents?.();
        this.disposeGlobalEvents = null;
        if (this.previewPanel) {
            this.previewPanel.destroy();
            this.previewPanel = null;
        }
        if (this.roiEditorPanel) {
            this.roiEditorPanel.destroy();
            this.roiEditorPanel = null;
        }
        if (this.calibrationDraftWorkbench) {
            this.calibrationDraftWorkbench.destroy();
            this.calibrationDraftWorkbench = null;
        }
        this.currentOperator = null;
        this.onChangeCallback = null;
        this.inputImageBase64Load = null;
        if (this.container) {
            this.container.innerHTML = '';
        }
    }

    /**
     * 渲染面板 - 阶段四增强版，支持参数分组折叠
     */
    render() {
        if (!this.currentOperator) {
            this.clear();
            return;
        }

        if (this.currentOperator.isLibrarySelection) {
            this.renderLibraryOperatorSummary();
            return;
        }

        // 兼容 title (画布节点) 和 displayName (算子库)
        const title = this.currentOperator.title || this.currentOperator.displayName || this.currentOperator.type;
        const { type, parameters = [], iconPath, icon } = this.currentOperator;
        const typeDisplay = getOperatorTypeDisplayName(type, { includeType: true });
        const roiEditorConfig = getOperatorRoiConfig(this.currentOperator, {
            circleSearchV2ToolEnabled: this.isCircleSearchV2ToolFeatureEnabled(),
            nPointCalibrationWorkbenchEnabled: this.isNPointCalibrationWorkbenchFeatureEnabled()
        });
        const shouldMountCalibrationDraftWorkbench = this.shouldMountNPointCalibrationWorkbench();
        const shouldMountRoiEditor = this.previewResourcesEnabled && roiEditorConfig.supported && !shouldMountCalibrationDraftWorkbench;
        const parametersForRenderBase = type === 'ImageAcquisition'
            ? parameters.filter(param => !['exposuretime', 'gain', 'triggermode'].includes(String(param?.name || '').toLowerCase()))
            : parameters;
        const parametersForRender = this.getParametersForRender(type, parametersForRenderBase);
        const canRecommend = this.canRecommend(type);
        
        const iconHtml = iconPath 
            ? `<div class="property-icon"><svg viewBox="0 0 24 24" width="24" height="24" fill="currentColor"><path d="${iconPath}"/></svg></div>`
            : (icon ? `<div class="property-icon text-icon">${icon}</div>` : '');

        let html = `
            <div class="property-header">
                ${iconHtml}
                <div class="header-text">
                    <h4>${title}</h4>
                    <span class="property-type">${this.escapeHtml(typeDisplay || type)}</span>
                </div>
                <div class="property-header-actions">
                    ${canRecommend ? `<button type="button" class="btn btn-secondary btn-recommend" id="btn-recommend">智能推荐</button>` : ''}
                </div>
            </div>
            <div class="property-content">
        `;

        if (parametersForRender.length === 0) {
            html += '<p class="empty-text">该算子没有可配置参数</p>';
        } else {
            // 按分组组织参数
            const groupedParams = this.groupParameters(parametersForRender);
            
            html += '<form class="property-form" id="property-form">';
            html += this.renderCircleMeasurementWorkloadHint(type, parametersForRenderBase);
            
            // 渲染每个分组
            Object.entries(groupedParams).forEach(([groupName, params], index) => {
                const groupId = `param-group-${index}`;
                const isExpanded = index === 0; // 默认展开第一个分组
                
                html += `
                    <div class="param-group ${isExpanded ? 'expanded' : ''}" data-group="${groupId}">
                        <div class="param-group-header" onclick="this.closest('.param-group').classList.toggle('expanded')">
                            <span class="group-toggle-icon">${isExpanded ? '▼' : '▶'}</span>
                            <span class="group-title">${groupName}</span>
                            <span class="group-count">${params.length}</span>
                        </div>
                        <div class="param-group-content">
                `;
                
                params.forEach(param => {
                    html += this.renderParameterEnhanced(param);
                });
                
                html += '</div></div>';
            });
            
            html += `
                </div>
                <div class="recommendation-actions ${this.pendingRecommendation ? '' : 'hidden'}" id="recommendation-actions">
                    <span>已应用智能推荐参数</span>
                    <div class="recommendation-buttons">
                        <button type="button" class="btn btn-primary btn-sm" id="btn-accept-recommendation">接受</button>
                        <button type="button" class="btn btn-sm" id="btn-revert-recommendation">撤销</button>
                    </div>
                </div>
                <div class="property-actions">
                    <button type="button" class="btn btn-primary" id="btn-apply">应用</button>
                    <button type="button" class="btn" id="btn-reset">重置</button>
                </div>
            </form>
            `;
        }

        html += `
                ${shouldMountCalibrationDraftWorkbench ? '<div id="calibration-draft-workbench-container"></div>' : ''}
                ${shouldMountRoiEditor ? '<div id="roi-editor-container"></div>' : ''}
                ${this.previewResourcesEnabled ? '<div id="operator-preview-container"></div>' : ''}
            </div>
        `;
        this.container.innerHTML = html;

        // 绑定事件
        this.bindEvents();
        this.initSliders();
        this.initCalibrationDraftWorkbench();
        this.initRoiEditorPanel();
        this.initPreviewPanel();

        if (this.pendingRecommendation) {
            this._restoreRecommendationHighlights();
        }
    }

    renderLibraryOperatorSummary() {
        if (this.previewPanel) {
            this.previewPanel.destroy();
            this.previewPanel = null;
        }
        if (this.roiEditorPanel) {
            this.roiEditorPanel.destroy();
            this.roiEditorPanel = null;
        }
        if (this.calibrationDraftWorkbench) {
            this.calibrationDraftWorkbench.destroy();
            this.calibrationDraftWorkbench = null;
        }

        const operator = this.currentOperator || {};
        const title = this.getOperatorText(operator, ['title', 'displayName', 'DisplayName', 'name', 'Name', 'type', 'Type'], '未命名算子');
        const type = this.getOperatorText(operator, ['type', 'Type'], '');
        const typeDisplay = getOperatorTypeDisplayName(type, { includeType: true });
        const category = this.getOperatorText(operator, ['category', 'Category'], '其他');
        const description = this.getOperatorText(operator, ['description', 'Description'], '暂无说明');
        const usage = this.getOperatorText(operator, ['usage', 'Usage', 'purpose', 'Purpose'], '');
        const scenario = this.getOperatorText(operator, ['scenario', 'Scenario', 'typicalScenario', 'TypicalScenario'], '');
        const iconPath = operator.iconPath || operator.IconPath || null;
        const icon = operator.icon || operator.Icon || '';
        const inputPorts = Array.isArray(operator.inputPorts)
            ? operator.inputPorts
            : (Array.isArray(operator.InputPorts) ? operator.InputPorts : []);
        const outputPorts = Array.isArray(operator.outputPorts)
            ? operator.outputPorts
            : (Array.isArray(operator.OutputPorts) ? operator.OutputPorts : []);
        const inputType = this.getOperatorText(operator, ['inputType', 'InputType'], 'Any');
        const outputType = this.getOperatorText(operator, ['outputType', 'OutputType'], 'Any');
        const parameters = Array.isArray(operator.parameters)
            ? operator.parameters
            : (Array.isArray(operator.Parameters) ? operator.Parameters : []);

        const iconHtml = iconPath
            ? `<div class="property-icon"><svg viewBox="0 0 24 24" width="24" height="24" fill="currentColor"><path d="${this.escapeAttribute(iconPath)}"/></svg></div>`
            : (icon ? `<div class="property-icon text-icon">${this.escapeHtml(icon)}</div>` : '');

        this.container.innerHTML = `
            <div class="property-header property-header-library">
                ${iconHtml}
                <div class="header-text">
                    <h4>${this.escapeHtml(title)}</h4>
                    <span class="property-type">${this.escapeHtml(typeDisplay || type)}</span>
                </div>
            </div>
            <div class="property-content property-library-summary">
                <div class="property-summary-meta">
                    <span>${this.escapeHtml(category)}</span>
                    <span>${this.escapeHtml(parameters.length)} 个参数</span>
                </div>

                <section class="property-summary-section">
                    <h5>说明</h5>
                    <p>${this.escapeHtml(description)}</p>
                </section>

                ${usage && usage !== description ? `
                    <section class="property-summary-section">
                        <h5>用途</h5>
                        <p>${this.escapeHtml(usage)}</p>
                    </section>
                ` : ''}

                ${scenario ? `
                    <section class="property-summary-section">
                        <h5>典型场景</h5>
                        <p>${this.escapeHtml(scenario)}</p>
                    </section>
                ` : ''}

                <section class="property-summary-section">
                    <h5>输入 / 输出</h5>
                    <div class="property-port-list">
                        ${this.renderLibraryPorts(inputPorts, inputType, '输入')}
                        ${this.renderLibraryPorts(outputPorts, outputType, '输出')}
                    </div>
                </section>

                <section class="property-summary-section">
                    <h5>关键参数</h5>
                    ${this.renderLibraryParameterList(parameters)}
                </section>
            </div>
        `;
    }

    getOperatorText(operator, keys, fallback = '') {
        for (const key of keys) {
            const value = operator?.[key];
            if (value !== null && value !== undefined && String(value).trim() !== '') {
                return String(value);
            }
        }

        return fallback;
    }

    renderLibraryPorts(ports, fallbackType, label) {
        if (!Array.isArray(ports) || ports.length === 0) {
            return `
                <div class="property-port-row">
                    <span class="property-port-direction">${this.escapeHtml(label)}</span>
                    <span class="property-port-name">${this.escapeHtml(fallbackType || 'Any')}</span>
                </div>
            `;
        }

        return ports.slice(0, 3).map(port => {
            const name = port.displayName || port.DisplayName || port.name || port.Name || '未命名';
            const dataType = port.dataType || port.DataType || port.type || port.Type || 'Any';
            const required = label === '输入' && Boolean(port.isRequired ?? port.IsRequired);

            return `
                <div class="property-port-row">
                    <span class="property-port-direction">${this.escapeHtml(label)}</span>
                    <span class="property-port-name">${this.escapeHtml(name)}${required ? ' *' : ''}</span>
                    <span class="property-port-type">${this.escapeHtml(dataType)}</span>
                </div>
            `;
        }).join('');
    }

    renderLibraryParameterList(parameters) {
        if (!Array.isArray(parameters) || parameters.length === 0) {
            return '<p class="property-summary-empty">无可配置参数</p>';
        }

        const keyParameters = [...parameters]
            .sort((left, right) => Number(isParameterRequired(right)) - Number(isParameterRequired(left)))
            .slice(0, 6);

        return `
            <ul class="property-param-list">
                ${keyParameters.map(param => {
                    const name = getParameterLabel(param);
                    const type = getParameterDataType(param, 'Any') || 'Any';
                    const value = getParameterEffectiveValue(param);
                    const description = param.description || param.Description || '';
                    const valueText = isEmptyValue(value) ? '' : `默认: ${value}`;
                    const detail = valueText || description || type;

                    return `
                        <li class="property-param-row">
                            <span class="property-param-name">${this.escapeHtml(name)}${isParameterRequired(param) ? ' *' : ''}</span>
                            <span class="property-param-detail">${this.escapeHtml(detail)}</span>
                        </li>
                    `;
                }).join('')}
            </ul>
        `;
    }

    escapeHtml(value) {
        return String(value ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    escapeAttribute(value) {
        return this.escapeHtml(value);
    }
    
    /**
     * 按分组组织参数
     */
    groupParameters(parameters) {
        if (this.isCircleMeasurementOperator() && this.isCircleSearchV2ToolFeatureEnabled()) {
            return this.groupCircleMeasurementParameters(parameters);
        }

        const groups = { '基本参数': [] };
        
        parameters.forEach(param => {
            const group = param.group || param.category || '基本参数';
            if (!groups[group]) {
                groups[group] = [];
            }
            groups[group].push(param);
        });
        
        // 如果只有基本参数且有多个，保持原样
        // 否则返回所有分组
        return groups;
    }

    getParametersForRender(type, parameters) {
        if (String(type || '').trim() !== 'CircleMeasurement') {
            return parameters;
        }

        if (!this.isCircleSearchV2ToolFeatureEnabled()) {
            return parameters;
        }

        const method = this.getCircleMeasurementMethod(parameters);
        return parameters.filter(param => {
            const name = normalizeParameterName(param?.name || param?.Name);
            if (method === 'caliperfitv2') {
                return !CIRCLE_MEASUREMENT_HOUGH_PARAMS.has(name);
            }

            return !CIRCLE_MEASUREMENT_V2_PARAMS.has(name);
        });
    }

    isCircleMeasurementOperator() {
        return String(this.currentOperator?.type || this.currentOperator?.operatorType || '').trim() === 'CircleMeasurement';
    }

    isCircleSearchV2ToolFeatureEnabled() {
        return isCircleSearchV2ToolEnabled({ circleSearchV2ToolEnabled: this.circleSearchV2ToolEnabled });
    }

    isNPointCalibrationOperator() {
        return String(this.currentOperator?.type || this.currentOperator?.operatorType || '').trim() === 'NPointCalibration';
    }

    isNPointCalibrationWorkbenchFeatureEnabled() {
        return isNPointCalibrationWorkbenchEnabled({ nPointCalibrationWorkbenchEnabled: this.nPointCalibrationWorkbenchEnabled });
    }

    shouldMountNPointCalibrationWorkbench() {
        return this.isNPointCalibrationOperator() && this.isNPointCalibrationWorkbenchFeatureEnabled();
    }

    getCircleMeasurementMethod(parameters = this.currentOperator?.parameters || []) {
        const methodParam = (parameters || []).find(param => normalizeParameterName(param?.name || param?.Name) === 'method');
        return String(getParameterEffectiveValue(methodParam) || 'HoughCircle').trim().toLowerCase();
    }

    groupCircleMeasurementParameters(parameters) {
        const remaining = [...parameters];
        const groups = {};

        CIRCLE_MEASUREMENT_GROUPS.forEach(([groupName, names]) => {
            const picked = [];
            names.forEach(name => {
                const index = remaining.findIndex(param => normalizeParameterName(param?.name || param?.Name) === name);
                if (index >= 0) {
                    picked.push(remaining[index]);
                    remaining.splice(index, 1);
                }
            });

            if (picked.length > 0) {
                groups[groupName] = picked;
            }
        });

        if (remaining.length > 0) {
            groups['其他参数'] = remaining;
        }

        return Object.keys(groups).length > 0 ? groups : { '基本参数': parameters };
    }

    readParameterNumber(parameters, name, fallback = 0) {
        const param = (parameters || []).find(item => normalizeParameterName(item?.name || item?.Name) === normalizeParameterName(name));
        const value = Number(getParameterEffectiveValue(param));
        return Number.isFinite(value) ? value : fallback;
    }

    renderCircleMeasurementWorkloadHint(type, parameters) {
        if (String(type || '').trim() !== 'CircleMeasurement' ||
            !this.isCircleSearchV2ToolFeatureEnabled() ||
            this.getCircleMeasurementMethod(parameters) !== 'caliperfitv2') {
            return '';
        }

        const caliperCount = this.readParameterNumber(parameters, 'CaliperCount', 96);
        const profileSampleCount = this.readParameterNumber(parameters, 'ProfileSampleCount', 129);
        const averagingThickness = this.readParameterNumber(parameters, 'AveragingThickness', 5);
        const workUnits = Math.max(0, Math.round(caliperCount * profileSampleCount * Math.ceil(Math.max(1, averagingThickness))));
        const nearBudget = workUnits >= 500000;

        return `
            <div class="circle-search-v2-workload ${nearBudget ? 'warning' : ''}" data-circle-search-v2-workload="true">
                <span>Sampling work: ${this.escapeHtml(workUnits.toLocaleString())}</span>
                <span>${nearBudget ? '采样工作量接近预算，后端校验仍为最终权威。' : '采样工作量在常规范围内。'}</span>
            </div>
        `;
    }

    /**
     * 渲染参数控件
     */
    renderParameter(param) {
        const { name, displayName, description, dataType, value, defaultValue, min, max, isRequired } = param;
        
        let inputHtml = '';
        const effectiveState = this.getParameterRuleState(param);
        const requiredMark = effectiveState.effectiveRequired ? '<span class="required">*</span>' : '';
        const disabledHint = effectiveState.effectiveDisabled && effectiveState.disabledReason
            ? `<p class="form-description parameter-rule-hint">${this.escapeHtml(effectiveState.disabledReason)}</p>`
            : '';
        const currentValue = value !== undefined ? value : defaultValue;
        
        switch (dataType) {
            case 'int':
            case 'double':
            case 'float':
                inputHtml = `
                    <input type="number" 
                           id="param-${name}" 
                           name="${name}" 
                           value="${value !== undefined ? value : defaultValue}"
                           ${min !== undefined ? `min="${min}"` : ''}
                           ${max !== undefined ? `max="${max}"` : ''}
                           step="${dataType === 'int' ? 1 : 0.1}"
                           class="form-input"
                           data-type="${dataType}">
                `;
                break;
                
            case 'string':
                inputHtml = `
                    <input type="text" 
                           id="param-${name}" 
                           name="${name}" 
                           value="${this.escapeAttribute(value !== undefined ? value : defaultValue || '')}"
                           class="form-input"
                           data-type="string">
                `;
                break;
                
            case 'bool':
            case 'boolean':
                const checked = (value !== undefined ? value : defaultValue) ? 'checked' : '';
                inputHtml = `
                    <label class="switch">
                        <input type="checkbox" 
                               id="param-${name}" 
                               name="${name}" 
                               ${checked}
                               data-type="boolean">
                        <span class="slider"></span>
                    </label>
                `;
                break;
                
            case 'enum':
            case 'select':
                const options = param.options || [];
                inputHtml = `
                    <select id="param-${name}" 
                            name="${name}" 
                            class="form-select"
                            data-type="enum">
                        ${options.map(opt => {
                            const label = typeof opt === 'string' ? opt : (opt.label || opt.Label || 'undefined');
                            const val = typeof opt === 'string' ? opt : (opt.value ?? opt.Value);
                            const currentVal = value !== undefined ? value : defaultValue;
                            return `
                                <option value="${val}" ${val === currentVal ? 'selected' : ''}>
                                    ${label}
                                </option>
                            `;
                        }).join('')}
                    </select>
                `;
                break;
                
            case 'color':
                inputHtml = `
                    <input type="color" 
                           id="param-${name}" 
                           name="${name}" 
                           value="${value !== undefined ? value : defaultValue || '#000000'}"
                           class="form-color"
                           data-type="color">
                `;
                break;
                
            case 'file':
                inputHtml = `
                    <div class="file-picker-wrapper">
                        <input type="text" id="param-${name}" name="${name}" value="${this.escapeAttribute(value !== undefined ? value : defaultValue || '')}" class="form-input" readonly data-type="file">
                        <button type="button" class="btn btn-sm btn-secondary btn-pick-file" data-param="${name}">...</button>
                    </div>
                `;
                break;
                
            case 'cameraBinding': {
                const bindings = this.cameraBindingsCache || [];
                const hasBindings = bindings.length > 0;
                const selectedCameraId = currentValue || '';

                inputHtml = `
                    <select id="param-${name}"
                            name="${name}"
                            class="form-select"
                            data-type="string"
                            data-camera-binding-select="true"
                            data-current-value="${selectedCameraId}">
                        <option value="">-- 请选择相机 --</option>
                        ${hasBindings
                            ? bindings.map(b => `
                                <option value="${b.id}" ${b.id === selectedCameraId ? 'selected' : ''}>
                                    ${b.displayName} (${b.serialNumber})
                                </option>
                            `).join('')
                            : '<option value="" disabled>加载中...</option>'}
                    </select>
                    ${hasBindings ? '' : '<p class="form-description error" data-camera-binding-hint>正在加载相机绑定列表...</p>'}
                `;
                break;
            }
                
            default:
                inputHtml = `
                    <input type="text" 
                           id="param-${name}" 
                           name="${name}" 
                           value="${this.escapeAttribute(value !== undefined ? value : defaultValue || '')}"
                           class="form-input"
                           data-type="${dataType}">
                `;
        }

        return `
            <div class="form-group ${effectiveState.effectiveDisabled ? 'is-rule-disabled' : ''}" data-effective-required="${effectiveState.effectiveRequired ? 'true' : 'false'}" data-effective-disabled="${effectiveState.effectiveDisabled ? 'true' : 'false'}">
                <label for="param-${name}" class="form-label">
                    ${displayName || name} ${requiredMark}
                </label>
                ${this.renderGlobalVariableBindingControl(param)}
                ${inputHtml}
                ${description ? `<p class="form-description">${description}</p>` : ''}
                ${disabledHint}
            </div>
        `;
    }

    /**
     * 渲染增强版参数控件 - 带滑块和颜色选择器
     */
    renderParameterEnhanced(param) {
        const { name, displayName, description, dataType, value, defaultValue, min, max, isRequired, step } = param;
        
        const effectiveState = this.getParameterRuleState(param);
        const requiredMark = effectiveState.effectiveRequired ? '<span class="required">*</span>' : '';
        const disabledHint = effectiveState.effectiveDisabled && effectiveState.disabledReason
            ? `<p class="form-description parameter-rule-hint">${this.escapeHtml(effectiveState.disabledReason)}</p>`
            : '';
        const readonly = this.isReadonlyCircleMeasurementParameter(name);
        const currentValue = this.resolveCircleMeasurementDisplayValue(name, value !== undefined ? value : defaultValue);
        let inputHtml = '';
        
        switch (dataType) {
            case 'int':
            case 'double':
            case 'float':
                // 数值类型：输入框 + 滑块
                const hasRange = min !== undefined && max !== undefined;
                const stepValue = step || (dataType === 'int' ? 1 : 0.1);
                
                inputHtml = `
                    <div class="number-input-wrapper">
                        <input type="number" 
                               id="param-${name}" 
                               name="${name}" 
                               value="${currentValue}"
                               ${min !== undefined ? `min="${min}"` : ''}
                               ${max !== undefined ? `max="${max}"` : ''}
                               step="${stepValue}"
                               class="form-input number-input"
                               data-type="${dataType}"
                               ${readonly ? 'readonly aria-readonly="true"' : ''}>
                        ${hasRange ? `
                            <input type="range" 
                                   class="param-slider"
                                   min="${min}" 
                                   max="${max}" 
                                   step="${stepValue}"
                                   value="${currentValue}"
                                   ${readonly ? 'disabled aria-disabled="true"' : ''}
                                   oninput="document.getElementById('param-${name}').value = this.value; document.getElementById('param-${name}').dispatchEvent(new Event('change'));">
                        ` : ''}
                    </div>
                `;
                break;
                
            case 'color':
                // 增强的颜色选择器
                inputHtml = `
                    <div class="color-picker-wrapper">
                        <input type="color" 
                               id="param-${name}" 
                               name="${name}" 
                               value="${currentValue || '#000000'}"
                               class="form-color-hidden"
                               data-type="color">
                        <div class="color-preview-box" onclick="document.getElementById('param-${name}').click()" style="background-color: ${currentValue || '#000000'}">
                            <span class="color-value">${currentValue || '#000000'}</span>
                        </div>
                    </div>
                `;
                break;
                
            default:
                // 其他类型使用默认渲染
                return this.renderParameter(param);
        }
        
        return `
            <div class="form-group param-enhanced ${effectiveState.effectiveDisabled ? 'is-rule-disabled' : ''} ${readonly ? 'is-readonly' : ''}" data-effective-required="${effectiveState.effectiveRequired ? 'true' : 'false'}" data-effective-disabled="${effectiveState.effectiveDisabled ? 'true' : 'false'}">
                <label for="param-${name}" class="form-label">
                    ${displayName || name} ${requiredMark}
                </label>
                ${this.renderGlobalVariableBindingControl(param)}
                ${inputHtml}
                ${description ? `<p class="form-description">${description}</p>` : ''}
                ${readonly ? '<p class="form-description">ImageCenter 模式下由当前预览图像中心提供。</p>' : ''}
                ${disabledHint}
            </div>
        `;
    }

    isReadonlyCircleMeasurementParameter(name) {
        if (!this.isCircleMeasurementOperator() || !this.isCircleSearchV2ToolFeatureEnabled()) {
            return false;
        }

        const normalized = normalizeParameterName(name);
        if (normalized !== 'searchcenterx' && normalized !== 'searchcentery') {
            return false;
        }

        const modeParam = this.getParameterByName('SearchCenterMode');
        const mode = String(getParameterEffectiveValue(modeParam) || 'ImageCenter').trim().toLowerCase();
        return mode === 'imagecenter';
    }

    resolveCircleMeasurementDisplayValue(name, fallbackValue) {
        if (!this.isReadonlyCircleMeasurementParameter(name)) {
            return fallbackValue;
        }

        const bounds = this.circleSearchV2ImageBounds;
        const normalized = normalizeParameterName(name);
        const size = normalized === 'searchcenterx'
            ? Number(bounds?.width)
            : Number(bounds?.height);
        if (!Number.isFinite(size) || size <= 0) {
            return fallbackValue;
        }

        return Math.round(((size - 1) * 0.5) * 1000) / 1000;
    }

    handleRoiImageBoundsChanged(bounds) {
        this.circleSearchV2ImageBounds = bounds &&
            Number.isFinite(Number(bounds.width)) &&
            Number.isFinite(Number(bounds.height)) &&
            Number(bounds.width) > 0 &&
            Number(bounds.height) > 0
            ? { width: Number(bounds.width), height: Number(bounds.height) }
            : null;
        this.updateCircleSearchV2CenterInputsFromImageBounds();
    }

    updateCircleSearchV2CenterInputsFromImageBounds() {
        if (!this.isCircleMeasurementOperator() || !this.isCircleSearchV2ToolFeatureEnabled()) {
            return;
        }

        ['SearchCenterX', 'SearchCenterY'].forEach(name => {
            const input = this.findParamInput(name);
            if (!input || !this.isReadonlyCircleMeasurementParameter(name)) {
                return;
            }

            const displayValue = this.resolveCircleMeasurementDisplayValue(name, input.value);
            input.value = displayValue ?? '';
            const range = input.parentElement?.querySelector(`input[type="range"][value]`);
            if (range && !range.disabled) {
                range.value = input.value;
            }
        });
    }

    applyGlobalVariableInputState() {
        const form = document.getElementById('property-form');
        if (!form) {
            return;
        }

        form.querySelectorAll('.gv-binding-select[data-parameter-name]').forEach(select => {
            const parameterName = select.dataset.parameterName || '';
            const escapedName = (typeof CSS !== 'undefined' && typeof CSS.escape === 'function')
                ? CSS.escape(parameterName)
                : parameterName;
            const input = form.querySelector(`[name="${escapedName}"]`);
            if (input) {
                input.disabled = Boolean(select.value);
                input.title = select.value ? '由全局变量提供' : '';
                input.setAttribute?.('aria-disabled', select.value ? 'true' : 'false');
            }
        });
    }

    renderGlobalVariableBindingControl(param) {
        const project = projectManager.getCurrentProject?.();
        const schema = this.normalizeGlobalVariableSchema(project?.globalVariables || project?.GlobalVariables);
        if (!schema.variables.length || !this.currentOperator?.id || !param?.id) {
            return '';
        }

        const binding = schema.targetBindings.find(item =>
            String(item.operatorId || '').toLowerCase() === String(this.currentOperator.id).toLowerCase() &&
            String(item.parameterId || '').toLowerCase() === String(param.id).toLowerCase());
        const compatibleVariables = schema.variables.filter(variable => this.isVariableCompatibleWithParameter(variable, param));
        const boundVariable = binding
            ? schema.variables.find(variable => String(variable.id || '').toLowerCase() === String(binding.variableId || '').toLowerCase())
            : null;

        return `
            <div class="global-variable-source-control">
                <label class="form-label compact">参数来源</label>
                <select class="form-input gv-binding-select" data-parameter-id="${this.escapeAttribute(param.id)}" data-parameter-name="${this.escapeAttribute(param.name)}">
                    <option value="">固定值</option>
                    ${compatibleVariables.map(variable => `
                        <option value="${this.escapeAttribute(variable.id)}" ${binding && String(binding.variableId).toLowerCase() === String(variable.id).toLowerCase() ? 'selected' : ''}>
                            ${this.escapeHtml(variable.displayName || variable.name)} (${this.escapeHtml(variable.name)})
                        </option>
                    `).join('')}
                </select>
                ${binding && !boundVariable ? '<p class="form-description error">\u5df2\u7ed1\u5b9a\u7684\u5168\u5c40\u53d8\u91cf\u4e0d\u5b58\u5728\uff0c\u8bf7\u91cd\u65b0\u9009\u62e9\u3002</p>' : ''}
                ${binding && boundVariable ? '<p class="form-description">\u5df2\u7ed1\u5b9a\u5168\u5c40\u53d8\u91cf\uff0c\u56fa\u5b9a\u503c\u63a7\u4ef6\u7531\u5168\u5c40\u53d8\u91cf\u63d0\u4f9b\u3002</p>' : ''}
            </div>
        `;
    }

    normalizeGlobalVariableSchema(schema) {
        return {
            schemaVersion: schema?.schemaVersion || schema?.SchemaVersion || '1.0',
            variables: Array.isArray(schema?.variables) ? schema.variables : (schema?.Variables || []),
            sourceBindings: Array.isArray(schema?.sourceBindings) ? schema.sourceBindings : (schema?.SourceBindings || []),
            targetBindings: Array.isArray(schema?.targetBindings) ? schema.targetBindings : (schema?.TargetBindings || [])
        };
    }

    isVariableCompatibleWithParameter(variable, param) {
        return isVariableCompatibleWithDataType(
            variable?.valueType || variable?.ValueType,
            param?.dataType || param?.DataType || param?.type || param?.Type
        );
    }

    /**
     * 初始化滑块同步
     */
    initSliders() {
        const sliders = this.container.querySelectorAll('.param-slider');
        sliders.forEach(slider => {
            const targetInput = slider.parentElement.querySelector('input[type="number"]');
            if (targetInput) {
                // 输入框改变时更新滑块
                targetInput.addEventListener('input', () => {
                    slider.value = targetInput.value;
                });
            }
        });
        
        // 颜色选择器预览更新
        const colorInputs = this.container.querySelectorAll('input[type="color"]');
        colorInputs.forEach(input => {
            input.addEventListener('input', (e) => {
                const preview = input.parentElement.querySelector('.color-preview-box');
                const valueText = input.parentElement.querySelector('.color-value');
                if (preview) preview.style.backgroundColor = e.target.value;
                if (valueText) valueText.textContent = e.target.value;
            });
        });
    }

    /**
     * 绑定事件
     */
    bindEvents() {
        const recommendBtn = document.getElementById('btn-recommend');
        if (recommendBtn) {
            recommendBtn.addEventListener('click', () => this.recommendParameters());
        }

        const acceptBtn = document.getElementById('btn-accept-recommendation');
        if (acceptBtn) {
            acceptBtn.addEventListener('click', () => this.acceptRecommendation());
        }

        const revertBtn = document.getElementById('btn-revert-recommendation');
        if (revertBtn) {
            revertBtn.addEventListener('click', () => this.revertRecommendation());
        }

        const form = document.getElementById('property-form');
        if (!form) return;

        // 应用按钮
        const applyBtn = document.getElementById('btn-apply');
        if (applyBtn) {
            applyBtn.addEventListener('click', () => this.applyChanges());
        }

        // 重置按钮
        const resetBtn = document.getElementById('btn-reset');
        if (resetBtn) {
            resetBtn.addEventListener('click', () => this.resetChanges());
        }

        // 实时更新
        const inputs = form.querySelectorAll('input, select');
        const sourceTypeInput = form.querySelector('#param-SourceType, #param-sourceType, select[name="SourceType"], select[name="sourceType"]');
        sourceTypeInput?.addEventListener('change', () => {
            this.syncImageAcquisitionSourceControls({ clearFilePathWhenCamera: true });
        });

        inputs.forEach(input => {
            input.addEventListener('change', () => {
                if (input.classList.contains('gv-binding-select')) {
                    this.applyGlobalVariableInputState();
                    this.syncGlobalVariableTargetBindings();
                    return;
                }

                if (this.shouldRerenderForCircleMeasurementParameter(input.name)) {
                    this._notifyValueChanged({ schedulePreview: false, syncRoiEditor: false });
                    this.render();
                    return;
                }

                this._notifyValueChanged();
            });
        });

        // 文件选择按钮
        const fileBtns = form.querySelectorAll('.btn-pick-file');
        fileBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                if (btn.disabled) {
                    return;
                }

                const paramName = btn.dataset.param;
                webMessageBridge.sendMessage('PickFileCommand', {
                    parameterName: paramName,
                    filter: 'Image Files|*.bmp;*.jpg;*.png;*.jpeg|All Files|*.*'
                });
            });
        });

        this.syncImageAcquisitionSourceControls();
        this.applyGlobalVariableInputState();
        this.loadCameraBindingsForSelects(true);
    }

    shouldRerenderForCircleMeasurementParameter(name) {
        if (!this.isCircleMeasurementOperator() || !this.isCircleSearchV2ToolFeatureEnabled()) {
            return false;
        }

        const normalized = normalizeParameterName(name);
        return normalized === 'method' || normalized === 'searchcentermode';
    }

    async loadCameraBindingsForSelects(forceRefresh = false) {
        const form = document.getElementById('property-form');
        if (!form) return;

        const cameraSelects = form.querySelectorAll('select[data-camera-binding-select="true"]');
        if (cameraSelects.length === 0) return;

        try {
            const bindings = await this.fetchCameraBindings(forceRefresh);
            this.populateCameraBindingSelects(cameraSelects, bindings);
        } catch (error) {
            console.error('[PropertyPanel] Failed to load camera bindings:', error);
            const message = error?.message || 'Unknown error';
            this.populateCameraBindingSelects(cameraSelects, [], message);
        }
    }

    async fetchCameraBindings(forceRefresh = false) {
        if (!forceRefresh && this.cameraBindingsCache.length > 0) {
            return this.cameraBindingsCache;
        }

        if (!forceRefresh && this.cameraBindingsLoadingPromise) {
            return this.cameraBindingsLoadingPromise;
        }

        this.cameraBindingsLoadingPromise = (async () => {
            const bindings = await httpClient.get('/cameras/bindings');
            this.cameraBindingsCache = Array.isArray(bindings) ? bindings : [];
            return this.cameraBindingsCache;
        })();

        try {
            return await this.cameraBindingsLoadingPromise;
        } finally {
            this.cameraBindingsLoadingPromise = null;
        }
    }

    populateCameraBindingSelects(selects, bindings, errorMessage = '') {
        const hasBindings = Array.isArray(bindings) && bindings.length > 0;

        selects.forEach(select => {
            const selectedCameraId = select.dataset.currentValue || select.value || '';
            let optionsHtml = '<option value="">-- 请选择相机 --</option>';

            if (hasBindings) {
                optionsHtml += bindings.map(b => `
                    <option value="${b.id}">
                        ${b.displayName} (${b.serialNumber})
                    </option>
                `).join('');
            } else if (errorMessage) {
                optionsHtml += '<option value="" disabled>加载失败</option>';
            } else {
                optionsHtml += '<option value="" disabled>暂无可用相机绑定</option>';
            }

            select.innerHTML = optionsHtml;

            if (hasBindings && bindings.some(b => b.id === selectedCameraId)) {
                select.value = selectedCameraId;
            } else {
                select.value = '';
            }

            const hint = select.closest('.form-group')?.querySelector('[data-camera-binding-hint]');
            if (!hint) return;

            if (hasBindings) {
                hint.remove();
                return;
            }

            hint.textContent = errorMessage
                ? `加载相机绑定失败: ${errorMessage}`
                : '未检测到相机绑定，请在“系统设置”中添加。';
            hint.classList.add('error');
        });
    }

    /**
     * 【修复】同步 UI 值到算子参数对象
     * @param {Object} values - 从 getValues() 获取的键值对
     */
    normalizeSourceTypeValue(value) {
        return normalizeAcquisitionSourceType(value);
    }

    getParameterRuleState(paramOrName, operator = this.currentOperator, values = null) {
        return getParameterEffectiveState(operator, paramOrName, { values });
    }

    collectFormRuleValues(form = document.getElementById('property-form')) {
        if (!form) {
            return {};
        }

        const values = {};
        Array.from(form.querySelectorAll('input[name], select[name]')).forEach(input => {
            if (!input.name || input.type === 'range') {
                return;
            }
            values[input.name] = input.type === 'checkbox' ? input.checked : input.value;
        });
        return values;
    }

    getParameterByName(name, operator = this.currentOperator) {
        const normalizedName = normalizeParameterName(name);
        return (operator?.parameters || []).find(param =>
            normalizeParameterName(param?.name || param?.Name) === normalizedName) || null;
    }

    findParamInput(...names) {
        const form = document.getElementById('property-form');
        if (!form) return null;

        const normalizedNames = names.map(name => String(name).toLowerCase());
        return Array.from(form.querySelectorAll('input[name], select[name]'))
            .find(input => normalizedNames.includes(String(input.name || '').toLowerCase())) || null;
    }

    syncImageAcquisitionSourceControls(options = {}) {
        if (this.currentOperator?.type !== 'ImageAcquisition') {
            return;
        }

        const sourceTypeInput = this.findParamInput('SourceType', 'sourceType');
        if (!sourceTypeInput) {
            return;
        }

        const values = this.collectFormRuleValues();
        const isCameraMode = this.normalizeSourceTypeValue(sourceTypeInput.value) === 'camera';
        const controlledInputs = ['FilePath', 'CameraId', 'CameraBindingId']
            .map(name => this.findParamInput(name))
            .filter(Boolean)
            .filter((input, index, all) => all.indexOf(input) === index);

        controlledInputs.forEach(input => {
            const param = this.getParameterByName(input.name);
            const state = this.getParameterRuleState(param || input.name, this.currentOperator, values);
            const group = input.closest('.form-group');
            const pickerButton = group?.querySelector('.btn-pick-file');
            const label = group?.querySelector('.form-label');
            const requiredMark = label?.querySelector('.required');

            if (
                isCameraMode &&
                state.effectiveDisabled &&
                input.name.toLowerCase() === 'filepath' &&
                options.clearFilePathWhenCamera !== false &&
                input.value
            ) {
                input.value = '';
            }

            input.disabled = state.effectiveDisabled;
            input.setAttribute('aria-disabled', state.effectiveDisabled ? 'true' : 'false');
            if (pickerButton) {
                pickerButton.disabled = state.effectiveDisabled;
            }

            group?.classList.toggle('hidden', state.effectiveDisabled);
            group?.classList.toggle('is-rule-disabled', state.effectiveDisabled);
            group?.setAttribute('data-effective-disabled', state.effectiveDisabled ? 'true' : 'false');
            group?.setAttribute('data-effective-required', state.effectiveRequired ? 'true' : 'false');

            if (label && state.effectiveRequired && !requiredMark) {
                label.insertAdjacentHTML('beforeend', ' <span class="required">*</span>');
            } else if (!state.effectiveRequired && requiredMark) {
                requiredMark.remove();
            }
        });
    }

    normalizeImageAcquisitionValues(values) {
        if (this.currentOperator?.type !== 'ImageAcquisition' || !values) {
            return values;
        }

        const sourceKey = Object.keys(values).find(key => key.toLowerCase() === 'sourcetype');
        const filePathKey = Object.keys(values).find(key => key.toLowerCase() === 'filepath');
        if (sourceKey && filePathKey && this.normalizeSourceTypeValue(values[sourceKey]) === 'camera') {
            values[filePathKey] = '';
        }

        return values;
    }

    updateCurrentOperatorParams(values) {
        if (!this.currentOperator || !this.currentOperator.parameters) return;
        
        this.currentOperator.parameters.forEach(param => {
            if (values[param.name] !== undefined) {
                // 同时更新 value 和 defaultValue 以适应不同的语义层
                param.value = values[param.name];
                // 如果是新创建的算子可能没有 value 只有 defaultValue，所以也同步一份
                if (param.defaultValue !== undefined) {
                    // 仅当原始定义就支持该字段时同步，避免污染
                    // 注意：这里保持 defaultValue 逻辑是为了兼容 app.js 及 serialize 的旧逻辑
                }
            }
        });
    }

    /**
     * 获取当前值
     */
    getValues() {
        const form = document.getElementById('property-form');
        if (!form) return {};

        const values = {};
        const inputs = form.querySelectorAll('input, select');
        
        inputs.forEach(input => {
            const name = input.name;
            if (!name || input.type === 'range') {
                return;
            }

            if (this.isReadonlyCircleMeasurementParameter(name)) {
                values[name] = getParameterEffectiveValue(this.getParameterByName(name));
                return;
            }

            const type = input.dataset.type;
            
            switch (type) {
                case 'int':
                    // 【修复】处理空或非数字情况
                    {
                        const raw = String(input.value || '').trim();
                        const parsed = parseNumericValue(raw);
                        values[name] = parsed.empty ? null : (parsed.valid && Number.isInteger(parsed.value) ? parsed.value : raw);
                    }
                    break;
                case 'double':
                case 'float':
                    {
                        const raw = String(input.value || '').trim();
                        const parsed = parseNumericValue(raw);
                        values[name] = parsed.empty ? null : (parsed.valid ? parsed.value : raw);
                    }
                    break;
                case 'boolean':
                case 'bool':
                    values[name] = input.checked;
                    break;
                default:
                    values[name] = input.value;
            }
        });

        return values;
    }

    /**
     * 应用更改
     */
    clearValidationErrors() {
        this.container?.querySelectorAll('.form-group.invalid').forEach(group => {
            group.classList.remove('invalid');
        });
        this.container?.querySelectorAll('[data-validation-error]').forEach(element => {
            element.remove();
        });
    }

    validateCurrentOperator(options = {}) {
        const {
            showToast = true,
            markFields = showToast,
            scrollToFirst = showToast
        } = options;

        const errors = this.collectCurrentOperatorValidationErrors();
        if (markFields) {
            this.renderValidationErrors(errors, { scrollToFirst });
        }

        if (errors.length > 0) {
            if (showToast) {
                this.showToast(errors[0].message, 'error');
            }
            return false;
        }

        if (markFields) {
            this.clearValidationErrors();
        }
        return true;
    }

    collectCurrentOperatorValidationErrors() {
        const form = document.getElementById('property-form');
        if (!form || !this.currentOperator) {
            return [];
        }

        const errors = [];
        const ruleValues = this.collectFormRuleValues(form);
        const requiredErrors = collectEffectiveRequiredParameterErrors(this.currentOperator, this.currentOperator.parameters || [], {
            values: ruleValues,
            getLabel: (param, fallbackName) => getParameterLabel(param, fallbackName)
        });
        requiredErrors.forEach(error => {
            errors.push({ name: error.name, message: error.message });
        });

        const inputs = Array.from(form.querySelectorAll('input[name], select[name]'))
            .filter(input => input.type !== 'range');

        inputs.forEach(input => {
            const param = this.getParameterByName(input.name);
            const state = this.getParameterRuleState(param || input.name, this.currentOperator, ruleValues);
            if (state.effectiveDisabled) {
                return;
            }

            const dataType = String(input.dataset.type || getParameterDataType(param)).toLowerCase();
            const label = getParameterLabel(param, input.name);
            const rawValue = input.type === 'checkbox' ? input.checked : input.value;

            if (['int', 'integer', 'double', 'float', 'number'].includes(dataType) && !isEmptyValue(rawValue)) {
                const parsed = parseNumericValue(rawValue);
                if (!parsed.valid) {
                    errors.push({ name: input.name, message: `${label} 必须是有效数字` });
                    return;
                }

                if ((dataType === 'int' || dataType === 'integer') && !Number.isInteger(parsed.value)) {
                    errors.push({ name: input.name, message: `${label} 必须是整数` });
                    return;
                }

                const min = getParameterRangeValue(param, 'min', 'Min', 'minValue', 'MinValue');
                const max = getParameterRangeValue(param, 'max', 'Max', 'maxValue', 'MaxValue');
                if (min !== null && parsed.value < min) {
                    errors.push({ name: input.name, message: `${label} 不能小于 ${min}` });
                    return;
                }
                if (max !== null && parsed.value > max) {
                    errors.push({ name: input.name, message: `${label} 不能大于 ${max}` });
                }
            }
        });

        return errors;
    }

    collectImageAcquisitionValidationErrors(operator, errors) {
        if (operator?.type !== 'ImageAcquisition') {
            return;
        }

        const values = this.currentOperator?.id === operator.id
            ? this.collectFormRuleValues()
            : null;
        collectEffectiveRequiredParameterErrors(operator, operator.parameters || [], {
            values,
            getLabel: (param, fallbackName) => getParameterLabel(param, fallbackName)
        }).forEach(error => {
            errors.push({ name: error.name, message: error.message });
        });
    }

    renderValidationErrors(errors, options = {}) {
        this.clearValidationErrors();
        if (!Array.isArray(errors) || errors.length === 0) {
            return;
        }

        errors.forEach(error => {
            const input = this.findParamInput(error.name);
            const group = input?.closest('.form-group');
            if (!group) {
                return;
            }

            group.classList.add('invalid');
            const message = document.createElement('p');
            message.className = 'form-description validation-error';
            message.dataset.validationError = 'true';
            message.textContent = error.message;
            group.appendChild(message);
        });

        if (options.scrollToFirst !== false) {
            const firstInvalid = this.container?.querySelector('.form-group.invalid');
            firstInvalid?.scrollIntoView?.({ block: 'nearest', behavior: 'smooth' });
        }
    }

    validateOperatorModel(operator) {
        const errors = [];
        if (!operator) {
            return errors;
        }

        const nodeTitle = operator.title || operator.name || operator.Name || operator.type || '算子';
        const requiredErrors = collectEffectiveRequiredParameterErrors(operator, operator.parameters || [], {
            getLabel: (param, fallbackName) => getParameterLabel(param, fallbackName)
        });
        requiredErrors.forEach(error => {
            errors.push({ nodeId: operator.id, name: error.name, message: `${nodeTitle}：${error.message}` });
        });

        (operator.parameters || []).forEach(param => {
            const name = param?.name || param?.Name;
            const label = getParameterLabel(param, name);
            const dataType = getParameterDataType(param).toLowerCase();
            const value = getParameterEffectiveValue(param);
            const state = this.getParameterRuleState(param, operator);

            if (state.effectiveDisabled) {
                return;
            }

            if (['int', 'integer', 'double', 'float', 'number'].includes(dataType) && !isEmptyValue(value)) {
                const parsed = parseNumericValue(value);
                if (!parsed.valid) {
                    errors.push({ nodeId: operator.id, name, message: `${nodeTitle}：${label} 必须是有效数字` });
                    return;
                }

                if ((dataType === 'int' || dataType === 'integer') && !Number.isInteger(parsed.value)) {
                    errors.push({ nodeId: operator.id, name, message: `${nodeTitle}：${label} 必须是整数` });
                    return;
                }

                const min = getParameterRangeValue(param, 'min', 'Min', 'minValue', 'MinValue');
                const max = getParameterRangeValue(param, 'max', 'Max', 'maxValue', 'MaxValue');
                if (min !== null && parsed.value < min) {
                    errors.push({ nodeId: operator.id, name, message: `${nodeTitle}：${label} 不能小于 ${min}` });
                    return;
                }
                if (max !== null && parsed.value > max) {
                    errors.push({ nodeId: operator.id, name, message: `${nodeTitle}：${label} 不能大于 ${max}` });
                }
            }
        });

        return errors;
    }

    validateFlowForAction(flowCanvas, options = {}) {
        const {
            showToast = true,
            action = '执行'
        } = options;

        if (this.currentOperator && this.validateCurrentOperator({
            showToast,
            markFields: showToast,
            scrollToFirst: showToast
        }) === false) {
            return false;
        }

        const nodes = flowCanvas?.nodes instanceof Map
            ? Array.from(flowCanvas.nodes.values())
            : [];
        const errors = [];
        nodes.forEach(node => {
            if (node?.id === this.currentOperator?.id) {
                return;
            }
            errors.push(...this.validateOperatorModel(node));
        });

        if (errors.length > 0) {
            const first = errors[0];
            if (first.nodeId && flowCanvas?.nodes?.has?.(first.nodeId)) {
                flowCanvas.selectedNode = first.nodeId;
                flowCanvas.onNodeSelected?.(flowCanvas.nodes.get(first.nodeId));
                flowCanvas.invalidate?.();
            }
            if (showToast) {
                this.showToast(`${action}前请先修正参数：${first.message}`, 'error');
            }
            return false;
        }

        return true;
    }

    applyChanges(options = {}) {
        const { showToast = true } = options;
        if (this.validateCurrentOperator({ showToast, markFields: showToast }) === false) {
            return false;
        }

        this.syncGlobalVariableTargetBindings();
        this._notifyValueChanged();

        // 显示成功提示
        if (showToast) {
            this.showToast('参数已应用', 'success');
        }
        return true;
    }

    syncDraftChanges(options = {}) {
        if (!this.currentOperator) {
            return true;
        }

        this.syncGlobalVariableTargetBindings();
        this._notifyValueChanged({
            schedulePreview: options.schedulePreview === true,
            forcePreview: false,
            syncRoiEditor: options.syncRoiEditor !== false
        });

        return true;
    }

    syncGlobalVariableTargetBindings() {
        const project = projectManager.getCurrentProject?.();
        if (!project || !this.currentOperator?.id) {
            return;
        }

        const form = document.getElementById('property-form');
        if (!form) {
            return;
        }

        const schema = this.normalizeGlobalVariableSchema(project.globalVariables || project.GlobalVariables);
        const operatorId = this.currentOperator.id;
        const selects = Array.from(form.querySelectorAll('.gv-binding-select[data-parameter-id]'));
        let changed = false;

        selects.forEach(select => {
            const parameterId = select.dataset.parameterId;
            const parameterName = select.dataset.parameterName || '';
            const selectedVariableId = select.value || '';
            const previousCount = schema.targetBindings.length;
            schema.targetBindings = schema.targetBindings.filter(binding =>
                !(String(binding.operatorId || '').toLowerCase() === String(operatorId).toLowerCase() &&
                  String(binding.parameterId || '').toLowerCase() === String(parameterId).toLowerCase()));

            if (schema.targetBindings.length !== previousCount) {
                changed = true;
            }

            if (selectedVariableId) {
                schema.targetBindings.push({
                    id: crypto.randomUUID(),
                    variableId: selectedVariableId,
                    operatorId,
                    parameterId,
                    operatorName: this.currentOperator.name || this.currentOperator.title || this.currentOperator.type || '',
                    parameterName,
                    conversionMode: 'Exact',
                    expression: ''
                });
                changed = true;
            }
        });

        if (changed) {
            projectManager.updateGlobalVariables(schema);
            serviceRegistry.get('globalVariablePanel')?.setSchemaFromExternal?.(schema);
        }
    }

    initPreviewPanel() {
        if (!this.previewResourcesEnabled) {
            if (this.previewPanel) {
                this.previewPanel.destroy();
                this.previewPanel = null;
            }
            return;
        }

        const container = this.container.querySelector('#operator-preview-container');
        if (!container) {
            if (this.previewPanel) {
                this.previewPanel.destroy();
                this.previewPanel = null;
            }
            return;
        }

        if (this.previewPanel) {
            this.previewPanel.destroy();
        }

        this.previewPanel = new PreviewPanel(container, {
            getOperator: () => this.currentOperator,
            previewCoordinator: this.previewCoordinator,
            onOpenImage: this.onOpenPreviewImage,
            onAnalyzePreview: async payload => this.handleWireSequenceAnalyze(payload),
            onAutoTune: async payload => this.handleWireSequenceAutoTune(payload),
            getFlowRevision: () => {
                const flowCanvas = serviceRegistry.get('flowCanvasAdapter') || serviceRegistry.get('flowCanvas');
                return flowCanvas?.getFlowRevision?.() ?? flowCanvas?.getRevision?.() ?? 0;
            },
            getNodes: () => {
                const flowCanvas = serviceRegistry.get('flowCanvasAdapter') || serviceRegistry.get('flowCanvas');
                const nodes = flowCanvas?.nodes;
                if (nodes instanceof Map) {
                    return Array.from(nodes.values());
                }
                if (Array.isArray(nodes)) {
                    return nodes;
                }
                return [];
            },
            getLiveNode: nodeId => {
                const flowCanvas = serviceRegistry.get('flowCanvasAdapter') || serviceRegistry.get('flowCanvas');
                return flowCanvas?.nodes?.get?.(nodeId) || null;
            },
            onSelectNode: nodeId => {
                const flowCanvas = serviceRegistry.get('flowCanvasAdapter') || serviceRegistry.get('flowCanvas');
                if (flowCanvas?.selectNode?.(nodeId)) {
                    return;
                }

                const rawCanvas = serviceRegistry.get('flowCanvas');
                const node = rawCanvas?.nodes?.get?.(nodeId);
                if (!node) {
                    return;
                }

                rawCanvas.selectedNode = nodeId;
                rawCanvas.selectedConnection = null;
                rawCanvas.markSelectionChanged?.('operator-result-panel');
                rawCanvas.onNodeSelected?.(node);
                rawCanvas.render?.();
            },
            subscribeStructureState: listener => {
                const flowCanvas = serviceRegistry.get('flowCanvasAdapter') || serviceRegistry.get('flowCanvas');
                return flowCanvas?.subscribeStructureState?.(listener) || (() => {});
            },
            validateBeforePreview: options => this.validateCurrentOperator({
                showToast: options?.showToast === true,
                markFields: options?.showToast === true,
                scrollToFirst: options?.showToast === true
            }),
            debounceMs: 500
        });

        this.previewPanel.scheduleAutoPreview();
    }

    initRoiEditorPanel() {
        if (!this.previewResourcesEnabled) {
            if (this.roiEditorPanel) {
                this.roiEditorPanel.destroy();
                this.roiEditorPanel = null;
            }
            return;
        }

        const container = this.container.querySelector('#roi-editor-container');
        if (!container) {
            if (this.roiEditorPanel) {
                this.roiEditorPanel.destroy();
                this.roiEditorPanel = null;
            }
            return;
        }

        if (this.roiEditorPanel) {
            this.roiEditorPanel.destroy();
        }

        this.roiEditorPanel = new RoiEditorPanel(container, {
            getOperator: () => this.currentOperator,
            getRoiConfig: operator => getOperatorRoiConfig(operator, {
                circleSearchV2ToolEnabled: this.isCircleSearchV2ToolFeatureEnabled(),
                nPointCalibrationWorkbenchEnabled: this.isNPointCalibrationWorkbenchFeatureEnabled()
            }),
            previewCoordinator: this.previewCoordinator,
            onRectChanged: (values, phase) => this.handleRoiRectChanged(values, phase),
            onImageBoundsChanged: bounds => this.handleRoiImageBoundsChanged(bounds),
            onRequestSyncFromParams: () => this.syncRoiEditorFromParams()
        });
    }

    initCalibrationDraftWorkbench() {
        const container = this.container.querySelector('#calibration-draft-workbench-container');
        if (!container) {
            if (this.calibrationDraftWorkbench) {
                this.calibrationDraftWorkbench.destroy();
                this.calibrationDraftWorkbench = null;
            }
            return;
        }

        if (this.calibrationDraftWorkbench) {
            this.calibrationDraftWorkbench.destroy();
        }

        this.calibrationDraftWorkbench = new CalibrationDraftWorkbench(container, {
            getOperator: () => this.currentOperator,
            previewCoordinator: this.previewCoordinator,
            getProject: () => projectManager.getCurrentProject?.() || null,
            getProjectId: () => projectManager.getCurrentProject?.()?.id || null,
            getFlowRevision: () => this.previewCoordinator?.getState?.()?.request?.flowRevision ?? 0,
            getDebugSessionId: (projectId, nodeId) => this.previewCoordinator?.getDebugSessionId?.(projectId, nodeId) ?? null,
            onFormalSaveSuccess: response => {
                const currentProject = projectManager.getCurrentProject?.();
                const responseProjectId = response?.projectId || response?.ProjectId;
                if (!currentProject?.id || String(currentProject.id).toLowerCase() !== String(responseProjectId || '').toLowerCase()) {
                    return;
                }

                currentProject.persistenceRevision = response?.persistenceRevision ?? response?.PersistenceRevision ?? currentProject.persistenceRevision;
                currentProject.assets = response?.assets || response?.Assets || currentProject.assets || { calibrationAssets: [], spatialAssets: [] };
            }
        });
    }

    /**
     * 当前算子是否支持智能推荐
     */
    canRecommend(type) {
        return this.recommendationSupportedOperators.has(type);
    }

    /**
     * 参数变更后的统一通知
     */
    _notifyValueChanged(options = {}) {
        const {
            schedulePreview = true,
            previewDebounceMs = 500,
            forcePreview = false,
            syncRoiEditor = true
        } = options;
        const values = this.normalizeImageAcquisitionValues(this.getValues());
        this.updateCurrentOperatorParams(values);
        this.syncImageAcquisitionSourceControls();

        if (this.onChangeCallback) {
            this.onChangeCallback(values);
        }

        if (syncRoiEditor) {
            this.syncRoiEditorFromParams();
        }

        if (schedulePreview && this.previewPanel) {
            this.previewPanel.scheduleAutoPreview({
                debounceMs: previewDebounceMs,
                force: forcePreview
            });
        }

        return values;
    }

    handleRoiRectChanged(values, phase) {
        this._applyValuesToForm(values);

        if (phase !== 'commit') {
            return;
        }

        this.updateCurrentOperatorParams(values);

        if (this.onChangeCallback) {
            this.onChangeCallback(values);
        }

        if (this.previewPanel) {
            this.previewPanel.scheduleAutoPreview({
                debounceMs: 250,
                force: true
            });
        }
    }

    syncRoiEditorFromParams() {
        this.roiEditorPanel?.refreshFromOperator?.({ forceSyncOverlay: true });
    }

    async recommendParameters() {
        if (!this.currentOperator || !this.canRecommend(this.currentOperator.type)) {
            return;
        }

        const recommendBtn = document.getElementById('btn-recommend');
        if (recommendBtn) {
            recommendBtn.disabled = true;
            recommendBtn.textContent = '推荐中...';
        }

        try {
            const imageBase64 = await this.resolveInputImageBase64();
            if (!imageBase64) {
                this.showToast('未找到可用输入图像，请先执行一次检测流程', 'warning');
                return;
            }

            const response = await httpClient.post(
                `/operators/${encodeURIComponent(this.currentOperator.type)}/recommend-parameters`,
                { imageBase64 }
            );

            const recommended = response?.parameters || response?.Parameters || {};
            if (Object.keys(recommended).length === 0) {
                this.showToast('该算子暂无可推荐参数', 'info');
                return;
            }

            const changedCount = this.applyRecommendedValues(recommended);
            if (changedCount === 0) {
                this.showToast('推荐结果与当前参数一致', 'info');
                return;
            }

            this.showToast(`已应用 ${changedCount} 个推荐参数`, 'success');
        } catch (error) {
            console.error('[PropertyPanel] 参数推荐失败:', error);
            this.showToast(`参数推荐失败: ${error.message}`, 'error');
        } finally {
            if (recommendBtn) {
                recommendBtn.disabled = false;
                recommendBtn.textContent = '智能推荐';
            }
        }
    }

    applyRecommendedValues(recommendedValues) {
        const form = document.getElementById('property-form');
        if (!form || !recommendedValues) return 0;

        const previousValues = this.getValues();
        const allInputs = Array.from(form.querySelectorAll('input[name], select[name]'));

        this._clearRecommendationHighlights();
        this.recommendedFieldNames.clear();

        Object.entries(recommendedValues).forEach(([name, value]) => {
            const input = allInputs.find(item =>
                item.name && item.name.toLowerCase() === String(name).toLowerCase());
            if (!input) return;

            const oldValue = this._readInputValue(input);
            this._writeInputValue(input, value);
            const newValue = this._readInputValue(input);

            if (JSON.stringify(oldValue) === JSON.stringify(newValue)) {
                return;
            }

            this.recommendedFieldNames.add(input.name);
            const group = input.closest('.form-group');
            if (group) {
                group.classList.add('param-recommended');
            }
        });

        if (this.recommendedFieldNames.size === 0) {
            return 0;
        }

        this.pendingRecommendation = {
            previousValues,
            fields: Array.from(this.recommendedFieldNames)
        };
        this._toggleRecommendationActions(true);
        this._notifyValueChanged();

        return this.recommendedFieldNames.size;
    }

    acceptRecommendation() {
        this.pendingRecommendation = null;
        this._clearRecommendationHighlights();
        this._toggleRecommendationActions(false);
        this.showToast('已接受推荐参数', 'success');
    }

    revertRecommendation() {
        if (!this.pendingRecommendation?.previousValues) {
            return;
        }

        this._applyValuesToForm(this.pendingRecommendation.previousValues);
        this.pendingRecommendation = null;
        this._clearRecommendationHighlights();
        this._toggleRecommendationActions(false);
        this._notifyValueChanged();
        this.showToast('已撤销推荐参数', 'info');
    }

    _toggleRecommendationActions(visible) {
        const actions = document.getElementById('recommendation-actions');
        if (!actions) return;

        actions.classList.toggle('hidden', !visible);
    }

    _clearRecommendationHighlights() {
        this.container.querySelectorAll('.param-recommended').forEach(element => {
            element.classList.remove('param-recommended');
        });
    }

    _restoreRecommendationHighlights() {
        const form = document.getElementById('property-form');
        if (!form || this.recommendedFieldNames.size === 0) {
            this._toggleRecommendationActions(false);
            return;
        }

        const inputs = form.querySelectorAll('input[name], select[name]');
        inputs.forEach(input => {
            if (this.recommendedFieldNames.has(input.name)) {
                const group = input.closest('.form-group');
                if (group) {
                    group.classList.add('param-recommended');
                }
            }
        });

        this._toggleRecommendationActions(true);
    }

    _readInputValue(input) {
        const type = input.dataset.type;
        if (type === 'boolean' || type === 'bool') {
            return Boolean(input.checked);
        }

        if (type === 'int') {
            const raw = String(input.value || '').trim();
            const parsed = parseNumericValue(raw);
            return parsed.empty ? null : (parsed.valid && Number.isInteger(parsed.value) ? parsed.value : raw);
        }

        if (type === 'double' || type === 'float') {
            const raw = String(input.value || '').trim();
            const parsed = parseNumericValue(raw);
            return parsed.empty ? null : (parsed.valid ? parsed.value : raw);
        }

        return input.value;
    }

    _writeInputValue(input, rawValue) {
        const type = input.dataset.type;
        if (type === 'boolean' || type === 'bool') {
            input.checked = this._toBoolean(rawValue);
            return;
        }

        if (type === 'int') {
            const parsed = parseNumericValue(rawValue);
            input.value = parsed.empty ? '' : (parsed.valid && Number.isInteger(parsed.value) ? `${parsed.value}` : `${rawValue}`);
        } else if (type === 'double' || type === 'float') {
            const parsed = parseNumericValue(rawValue);
            input.value = parsed.empty ? '' : (parsed.valid ? `${parsed.value}` : `${rawValue}`);
        } else {
            input.value = rawValue ?? '';
        }

        // 数值输入框存在滑块时，同步滑块值
        const slider = input.parentElement?.querySelector('.param-slider');
        if (slider) {
            slider.value = input.value;
        }

        // 颜色选择器预览同步
        if (input.type === 'color') {
            const preview = input.parentElement?.querySelector('.color-preview-box');
            const valueText = input.parentElement?.querySelector('.color-value');
            if (preview) preview.style.backgroundColor = input.value;
            if (valueText) valueText.textContent = input.value;
        }
    }

    _applyValuesToForm(values) {
        const form = document.getElementById('property-form');
        if (!form) return;

        const inputs = form.querySelectorAll('input[name], select[name]');
        inputs.forEach(input => {
            if (!Object.prototype.hasOwnProperty.call(values, input.name)) {
                return;
            }

            this._writeInputValue(input, values[input.name]);
        });
    }

    _toBoolean(value) {
        if (typeof value === 'boolean') {
            return value;
        }

        return String(value).toLowerCase() === 'true';
    }

    async resolveInputImageBase64() {
        const latestImage = serviceRegistry.get('lastInspectionImageBase64')
            || inspectionController.getLastResultImageBase64?.();
        if (latestImage) {
            this.inputImageBase64Load = null;
            return latestImage;
        }

        const inspectionResult = serviceRegistry.get('lastInspectionResult') || inspectionController.getLastResult?.();
        const inlineImage = resolvePreviewInputImageBase64(inspectionResult);
        if (inlineImage) {
            this.inputImageBase64Load = null;
            return inlineImage;
        }

        const latestImageUrl = serviceRegistry.get('lastInspectionImageUrl')
            || inspectionController.getLastResultImageUrl?.()
            || getResultImageUrl(inspectionResult);
        return this.loadInputImageUrlAsBase64(latestImageUrl);
    }

    loadInputImageUrlAsBase64(imageUrl) {
        if (!imageUrl) {
            this.inputImageBase64Load = null;
            return null;
        }

        const sourceKey = String(imageUrl);
        if (this.inputImageBase64Load?.sourceKey === sourceKey) {
            return this.inputImageBase64Load.promise;
        }

        const promise = Promise.resolve()
            .then(() => this.loadImageUrlAsBase64(imageUrl))
            .finally(() => {
                if (this.inputImageBase64Load?.promise === promise) {
                    this.inputImageBase64Load = null;
                }
            });
        this.inputImageBase64Load = { sourceKey, promise };
        return promise;
    }

    async handleWireSequenceAnalyze({ operator, previewState } = {}) {
        if (!operator?.id) {
            throw new Error('当前没有可分析的线序节点');
        }

        const inputImageBase64 = previewState?.inputImageBase64 || await this.resolveInputImageBase64();
        const result = await inspectionController.previewFlowNodeWithMetrics(operator.id, {
            inputImageBase64
        });

        if (result?.missingResources?.length > 0) {
            this.showToast('线序分析发现缺失资源，请先补齐模型与标签。', 'warning');
        } else {
            this.showToast(result?.success ? '线序分析已完成' : '线序分析未完成，请查看诊断。', result?.success ? 'success' : 'warning');
        }

        const hint = buildWireSequenceFollowupHint({
            scenarioKey: 'wire-sequence-terminal',
            diagnosticCodes: result?.diagnosticCodes || [],
            suggestions: result?.suggestions || [],
            missingResources: result?.missingResources || []
        });
        if (hint) {
            serviceRegistry.get('aiPanel')?.queueParameterOnlyFollowupHint?.({
                scenarioKey: 'wire-sequence-terminal',
                diagnosticCodes: result?.diagnosticCodes || [],
                suggestions: result?.suggestions || [],
                missingResources: result?.missingResources || []
            });
        }

        return result;
    }

    async handleWireSequenceAutoTune({ operator, previewState } = {}) {
        if (!operator?.id) {
            throw new Error('当前没有可调参的线序节点');
        }

        const inputImageBase64 = previewState?.inputImageBase64 || await this.resolveInputImageBase64();
        const result = await inspectionController.autoTuneWireSequenceScenario({
            scenarioKey: 'wire-sequence-terminal',
            inputImageBase64
        });

        const patch = createWireSequenceParameterPatch(
            serviceRegistry.get('flowCanvasAdapter')?.serialize?.() || serviceRegistry.get('flowCanvas')?.serialize?.() || null,
            operator.id,
            result?.finalParameters || {}
        );

        if (patch) {
            this.applyWireSequenceParameterPatch(patch);
        }

        serviceRegistry.get('aiPanel')?.queueParameterOnlyFollowupHint?.({
            scenarioKey: 'wire-sequence-terminal',
            diagnosticCodes: result?.diagnosticCodes || [],
            finalParameters: result?.finalParameters || {},
            suggestions: result?.finalPreview?.suggestions || [],
            missingResources: result?.missingResources || []
        });

        if (result?.missingResources?.length > 0) {
            this.showToast('自动调参已停止：缺少模型或标签资源。', 'warning');
        } else if (result?.success) {
            this.showToast('线序自动调参已完成，并已回写检测参数。', 'success');
        } else {
            this.showToast(result?.errorMessage || '线序自动调参未收敛，请查看诊断。', 'warning');
        }

        this.previewCoordinator?.requestActivePreview?.({
            immediate: true,
            force: true
        });

        return result?.finalPreview || result;
    }

    applyWireSequenceParameterPatch(patch) {
        if (!patch?.operatorId || !patch?.parameters) {
            return false;
        }

        const node = serviceRegistry.get('flowCanvas')?.nodes?.get?.(patch.operatorId);
        if (!node) {
            return false;
        }

        const parameters = Array.isArray(node.parameters) ? node.parameters : [];
        Object.entries(patch.parameters).forEach(([name, value]) => {
            let parameter = parameters.find(item => String(item?.name || item?.Name || '').toLowerCase() === name.toLowerCase());
            if (!parameter) {
                parameter = {
                    id: typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
                        ? crypto.randomUUID()
                        : `${patch.operatorId}-${name}`,
                    name,
                    displayName: name,
                    dataType: typeof value === 'number' ? 'double' : 'string',
                    value,
                    defaultValue: value
                };
                parameters.push(parameter);
                return;
            }

            parameter.value = value;
            if (Object.prototype.hasOwnProperty.call(parameter, 'Value')) {
                parameter.Value = value;
            }
        });

        node.parameters = parameters;
        const flowCanvasAdapter = serviceRegistry.get('flowCanvasAdapter');
        if (flowCanvasAdapter?.markFlowStructureChanged) {
            flowCanvasAdapter.markFlowStructureChanged('wire-sequence-autotune');
        } else {
            serviceRegistry.get('flowCanvas')?.markFlowStructureChanged?.('wire-sequence-autotune');
        }
        return true;
    }

    /**
     * 重置更改
     */
    resetChanges() {
        if (this.currentOperator) {
            this.currentOperator.parameters.forEach(param => {
                param.value = param.defaultValue;
            });
        }
        this.pendingRecommendation = null;
        this.recommendedFieldNames.clear();
        this.render();
        this._notifyValueChanged();

        this.showToast('参数已重置', 'info');
    }

    /**
     * 设置变更回调
     */
    onChange(callback) {
        this.onChangeCallback = callback;
    }

    /**
     * 显示提示
     */
    showToast(message, type = 'info') {
        // 创建提示元素
        const toast = document.createElement('div');
        toast.className = `toast toast-${type}`;
        toast.textContent = message;
        
        document.body.appendChild(toast);
        
        // 动画显示
        setTimeout(() => toast.classList.add('show'), 10);
        
        // 自动隐藏
        setTimeout(() => {
            toast.classList.remove('show');
            setTimeout(() => toast.remove(), 300);
        }, 2000);
    }
}

export default PropertyPanel;
export { PropertyPanel };
