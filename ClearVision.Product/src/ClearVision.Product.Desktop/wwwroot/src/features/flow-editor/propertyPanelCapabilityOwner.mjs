import { getOperatorTypeDisplayName } from '../../shared/operatorDisplayNames.js';
import webMessageBridge from '../../core/messaging/webMessageBridge.js';
import httpClient from '../../core/messaging/httpClient.js';
import {
    collectEffectiveRequiredParameterErrors,
    getParameterEffectiveState,
    normalizeAcquisitionSourceType
} from '../../shared/parameterDependencyRules.js';
import RoiEditorPanel from './roiEditorPanel.js';
import {
    getOperatorRoiConfig,
    isCircleSearchV2ToolEnabled,
    isNPointCalibrationWorkbenchEnabled
} from './roiEditorSupport.mjs';

export const PROPERTY_PANEL_CAPABILITY_OWNER_ID = 'property-panel-capability-v2';

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
    const div = document.createElement('div');
    div.textContent = value == null ? '' : String(value);
    return div.innerHTML;
}

function escapeAttribute(value) {
    return escapeHtml(value).replace(/"/g, '&quot;');
}

function getParameterName(parameter) {
    return String(parameter?.name ?? parameter?.Name ?? '').trim();
}

function normalizeParameterName(name) {
    return String(name || '').trim().toLowerCase();
}

function getParameterLabel(parameter, fallbackName = '') {
    return String(
        parameter?.displayName ??
        parameter?.DisplayName ??
        parameter?.label ??
        parameter?.Label ??
        parameter?.name ??
        parameter?.Name ??
        fallbackName ??
        '参数'
    );
}

function getParameterDataType(parameter) {
    return String(
        parameter?.dataType ??
        parameter?.DataType ??
        parameter?.type ??
        parameter?.Type ??
        'string'
    ).trim().toLowerCase();
}

function isPathLikeParameter(parameter) {
    const normalizedName = normalizeParameterName(getParameterName(parameter));
    if (!normalizedName || normalizedName === 'ipaddress') {
        return false;
    }

    return normalizedName === 'filepath' ||
        normalizedName === 'outputpath' ||
        normalizedName === 'savepath' ||
        normalizedName.endsWith('filepath') ||
        normalizedName.endsWith('templatepath') ||
        normalizedName.endsWith('modelpath') ||
        normalizedName.endsWith('catalogpath') ||
        normalizedName.endsWith('labelspath') ||
        normalizedName.endsWith('bankpath') ||
        normalizedName.endsWith('path');
}

function resolveParameterControlType(parameter) {
    const dataType = getParameterDataType(parameter);
    if (dataType === 'file') {
        return 'file';
    }

    if (dataType === 'camerabinding') {
        return 'cameraBinding';
    }

    if (dataType === 'string' && isPathLikeParameter(parameter)) {
        return 'file';
    }

    return dataType;
}

function getParameterDescription(parameter) {
    return String(parameter?.description ?? parameter?.Description ?? '').trim();
}

function getParameterValue(parameter) {
    return parameter?.value ?? parameter?.Value ?? parameter?.defaultValue ?? parameter?.DefaultValue ?? null;
}

function getParameterRangeValue(parameter, ...keys) {
    for (const key of keys) {
        if (parameter?.[key] !== undefined && parameter?.[key] !== null && parameter?.[key] !== '') {
            const parsed = Number(parameter[key]);
            if (Number.isFinite(parsed)) {
                return parsed;
            }
        }
    }

    return null;
}

function isEmptyValue(value) {
    return value === null || value === undefined || (typeof value === 'string' && value.trim() === '');
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

function toBoolean(value) {
    if (typeof value === 'boolean') {
        return value;
    }

    return String(value).toLowerCase() === 'true';
}

function getParameterOptions(parameter) {
    const options = parameter?.options ?? parameter?.Options ?? [];
    return Array.isArray(options) ? options : [];
}

function getFormValue(input) {
    const dataType = String(input.dataset.type || '').toLowerCase();
    if (dataType === 'boolean' || dataType === 'bool') {
        return Boolean(input.checked);
    }

    if (dataType === 'int' || dataType === 'integer') {
        const parsed = parseNumericValue(input.value);
        return parsed.empty ? null : (parsed.valid && Number.isInteger(parsed.value) ? parsed.value : input.value);
    }

    if (dataType === 'double' || dataType === 'float' || dataType === 'number') {
        const parsed = parseNumericValue(input.value);
        return parsed.empty ? null : (parsed.valid ? parsed.value : input.value);
    }

    return input.value;
}

export class PropertyPanelCapabilityOwner {
    constructor(container, {
        propertyAdapter,
        showToast = () => {},
        previewCoordinator = null,
        previewResourcesEnabled = true,
        onOpenPreviewImage = () => {},
        circleSearchV2ToolEnabled = undefined,
        nPointCalibrationWorkbenchEnabled = undefined
    } = {}) {
        this.container = resolveElement(container);
        if (!this.container) {
            throw new Error('PropertyPanelCapabilityOwner requires a container.');
        }
        if (!propertyAdapter) {
            throw new Error('PropertyPanelCapabilityOwner requires a property adapter.');
        }

        this.propertyAdapter = propertyAdapter;
        this.showToast = typeof showToast === 'function' ? showToast : () => {};
        this.previewCoordinator = previewCoordinator;
        this.previewResourcesEnabled = previewResourcesEnabled !== false;
        this.onOpenPreviewImage = typeof onOpenPreviewImage === 'function'
            ? onOpenPreviewImage
            : () => {};
        this.circleSearchV2ToolEnabled = circleSearchV2ToolEnabled;
        this.nPointCalibrationWorkbenchEnabled = nPointCalibrationWorkbenchEnabled;
        this.currentOperator = null;
        this.currentNodeId = null;
        this.currentConnection = null;
        this.validationErrors = [];
        this.statusMessage = '';
        this.lastChangedParameterName = null;
        this.dirty = false;
        this.disposed = false;
        this.unsubscribes = [];
        this.cameraBindingsCache = [];
        this.cameraBindingsLoadingPromise = null;
        this.roiEditorPanel = null;
        this.geometryImageBounds = null;

        this.handleContainerChange = this.handleContainerChange.bind(this);
        this.handleContainerClick = this.handleContainerClick.bind(this);
        this.handleContainerInput = this.handleContainerInput.bind(this);
        this.handleContainerSubmit = this.handleContainerSubmit.bind(this);
        this.handleFilePickedEvent = this.handleFilePickedEvent.bind(this);

        this.container.dataset.propertyPanelOwner = PROPERTY_PANEL_CAPABILITY_OWNER_ID;
        this.container.addEventListener('change', this.handleContainerChange);
        this.container.addEventListener('click', this.handleContainerClick);
        this.container.addEventListener('input', this.handleContainerInput);
        this.container.addEventListener('submit', this.handleContainerSubmit);

        this.unsubscribes.push(
            webMessageBridge.on('FilePickedEvent', this.handleFilePickedEvent),
            this.propertyAdapter.subscribeSelectedNode((operator, state) => {
                this.handleSelectedNodeChanged(operator, state);
            }),
            this.propertyAdapter.subscribeFlowChanges((state) => {
                this.handleFlowChanged(state);
            })
        );

        this.render();
    }

    handleSelectedNodeChanged(operator, state = {}) {
        if (this.disposed) {
            return;
        }

        this.currentOperator = operator || null;
        this.currentNodeId = operator?.id || null;
        this.currentConnection = this.currentNodeId
            ? null
            : this.propertyAdapter.getSelectedConnectionSnapshot?.(state?.selectedConnectionId) || null;
        this.validationErrors = [];
        this.statusMessage = '';
        this.lastChangedParameterName = null;
        this.dirty = false;
        this.render();
    }

    handleFlowChanged() {
        if (this.disposed || !this.currentNodeId) {
            if (!this.currentNodeId && this.currentConnection) {
                const refreshedConnection = this.propertyAdapter.getSelectedConnectionSnapshot?.(this.currentConnection.id);
                if (!refreshedConnection) {
                    this.currentConnection = null;
                    this.render();
                } else {
                    this.currentConnection = refreshedConnection;
                    this.render();
                }
            }
            return;
        }

        const operator = this.propertyAdapter.getSelectedOperatorSnapshot(this.currentNodeId);
        if (!operator) {
            this.currentOperator = null;
            this.currentNodeId = null;
            this.currentConnection = null;
            this.validationErrors = [];
            this.statusMessage = '';
            this.lastChangedParameterName = null;
            this.dirty = false;
            this.render();
            return;
        }

        this.currentOperator = operator;
        this.currentConnection = null;
        this.render();
    }

    handleContainerSubmit(event) {
        event.preventDefault();
        this.applyChanges();
    }

    handleContainerClick(event) {
        if (this.disposed) {
            return;
        }

        const clearRoiButton = event.target?.closest?.('[data-property-geometry-clear-roi]');
        if (clearRoiButton) {
            event.preventDefault();
            if (!clearRoiButton.disabled) {
                this.handleGeometryClearRoi();
            }
            return;
        }

        const button = event.target?.closest?.('.btn-pick-file');
        if (!button) {
            return;
        }

        event.preventDefault();
        if (button.disabled) {
            return;
        }

        const parameterName = button.dataset.param || '';
        webMessageBridge.sendMessage('PickFileCommand', {
            parameterName,
            filter: this.getFilePickerFilter(parameterName)
        });
    }

    handleContainerInput(event) {
        const slider = event.target?.closest?.('.param-slider');
        if (slider) {
            const numberInput = slider.parentElement?.querySelector('input[type="number"][data-property-parameter="true"]');
            if (numberInput && !numberInput.disabled && !numberInput.readOnly) {
                numberInput.value = slider.value;
                numberInput.dispatchEvent(new Event('change', { bubbles: true }));
            }
            return;
        }

        const colorInput = event.target?.closest?.('input[type="color"][data-property-parameter="true"]');
        if (colorInput) {
            const wrapper = colorInput.closest('.color-picker-wrapper');
            const preview = wrapper?.querySelector('.color-preview-box');
            const valueText = wrapper?.querySelector('.color-value');
            if (preview) {
                preview.style.backgroundColor = colorInput.value;
            }
            if (valueText) {
                valueText.textContent = colorInput.value;
            }
        }
    }

    handleFilePickedEvent(event) {
        if (this.disposed || !this.currentNodeId) {
            return;
        }

        const payload = event?.payload || event?.data || event || {};
        const isCancelled = Boolean(payload.IsCancelled ?? payload.isCancelled);
        if (isCancelled) {
            return;
        }

        const parameterName = String(payload.ParameterName ?? payload.parameterName ?? '').trim();
        const filePathRaw = payload.FilePath ?? payload.filePath;
        const filePath = filePathRaw == null ? '' : String(filePathRaw);
        if (!parameterName) {
            console.warn('[PropertyPanelCapabilityOwner] FilePickedEvent missing parameterName:', payload);
            return;
        }

        const input = this.findParamInput(parameterName);
        if (!input) {
            console.warn('[PropertyPanelCapabilityOwner] File parameter input not found:', parameterName);
            return;
        }

        if (this.isImageAcquisitionOperator() && normalizeParameterName(parameterName) === 'filepath') {
            const sourceTypeInput = this.findParamInput('SourceType', 'sourceType');
            if (sourceTypeInput && normalizeAcquisitionSourceType(sourceTypeInput.value) !== 'file') {
                const fileOption = Array.from(sourceTypeInput.options || [])
                    .find(option => normalizeAcquisitionSourceType(option.value) === 'file');
                sourceTypeInput.value = fileOption?.value || 'File';
                this.updateCurrentOperatorParameterValue(sourceTypeInput.name, sourceTypeInput.value);
            }
        }

        input.value = filePath;
        this.updateCurrentOperatorParameterValue(input.name, filePath);
        input.dispatchEvent(new Event('change', { bubbles: true }));

        if (this.isImageAcquisitionOperator() && normalizeParameterName(parameterName) === 'filepath') {
            this.syncImageAcquisitionSourceControls({ clearFilePathWhenCamera: false });
        }

        this.applyChanges({ showToast: false });
    }

    handleContainerChange(event) {
        const input = event.target?.closest?.('[data-property-parameter="true"]');
        if (!input || !this.currentNodeId || this.disposed) {
            return;
        }

        let values = this.collectFormValues();
        values = this.normalizeImageAcquisitionValues(values, input.name);
        const errors = this.validateValues(values);
        if (errors.length > 0) {
            this.validationErrors = errors;
            this.statusMessage = '参数校验失败';
            this.renderValidationErrors();
            this.updateStatus();
            return;
        }

        const parameterName = input.name;
        const writeValues = this.buildWriteValues(parameterName, values);
        const result = this.propertyAdapter.writeParameters(this.currentNodeId, writeValues);

        if (result?.reason === 'node_not_found') {
            this.currentOperator = null;
            this.currentNodeId = null;
            this.render();
            return;
        }

        this.validationErrors = [];
        this.statusMessage = '参数已更新';
        this.lastChangedParameterName = parameterName;
        this.dirty = result?.updated === true;
        Object.entries(writeValues).forEach(([name, value]) => this.updateCurrentOperatorParameterValue(name, value));
        this.syncImageAcquisitionSourceControls({
            clearFilePathWhenCamera: normalizeParameterName(parameterName) === 'sourcetype'
        });
        this.renderValidationErrors();
        this.updateStatus();
    }

    isImageAcquisitionOperator() {
        return String(this.currentOperator?.type || this.currentOperator?.operatorType || '').trim() === 'ImageAcquisition';
    }

    findParamInput(...names) {
        const normalizedNames = names.map(normalizeParameterName).filter(Boolean);
        if (normalizedNames.length === 0) {
            return null;
        }

        return Array.from(this.container.querySelectorAll('[data-property-parameter="true"]'))
            .find(input => normalizedNames.includes(normalizeParameterName(input.name))) || null;
    }

    getCurrentParameterByName(name) {
        const normalizedName = normalizeParameterName(name);
        return (this.currentOperator?.parameters || [])
            .find(parameter => normalizeParameterName(getParameterName(parameter)) === normalizedName) || null;
    }

    updateCurrentOperatorParameterValue(name, value) {
        const parameter = this.getCurrentParameterByName(name);
        if (!parameter) {
            return;
        }

        parameter.value = value;
        if (Object.prototype.hasOwnProperty.call(parameter, 'Value')) {
            parameter.Value = value;
        }
    }

    normalizeImageAcquisitionValues(values, changedName = '') {
        if (!this.isImageAcquisitionOperator() || !values) {
            return values;
        }

        const nextValues = { ...values };
        const sourceKey = Object.keys(nextValues).find(key => normalizeParameterName(key) === 'sourcetype');
        const filePathKey = Object.keys(nextValues).find(key => normalizeParameterName(key) === 'filepath');
        if (!sourceKey) {
            return nextValues;
        }

        const normalizedSource = normalizeAcquisitionSourceType(nextValues[sourceKey]);
        if (normalizedSource === 'camera' && filePathKey) {
            nextValues[filePathKey] = '';
            const filePathInput = this.findParamInput(filePathKey);
            if (filePathInput) {
                filePathInput.value = '';
            }
        }

        if (normalizeParameterName(changedName) === 'filepath' && filePathKey && nextValues[filePathKey]) {
            nextValues[sourceKey] = this.findFileSourceValue(sourceKey);
        }

        return nextValues;
    }

    findFileSourceValue(sourceKey = 'SourceType') {
        const sourceInput = this.findParamInput(sourceKey, 'SourceType', 'sourceType');
        if (!sourceInput) {
            return 'File';
        }

        const fileOption = Array.from(sourceInput.options || [])
            .find(option => normalizeAcquisitionSourceType(option.value) === 'file');
        return fileOption?.value || sourceInput.value || 'File';
    }

    buildWriteValues(parameterName, values) {
        const writeValues = {
            [parameterName]: values[parameterName]
        };

        if (!this.isImageAcquisitionOperator()) {
            return writeValues;
        }

        const normalizedParameterName = normalizeParameterName(parameterName);
        const sourceKey = Object.keys(values).find(key => normalizeParameterName(key) === 'sourcetype');
        const filePathKey = Object.keys(values).find(key => normalizeParameterName(key) === 'filepath');

        if (sourceKey && normalizedParameterName === 'filepath') {
            writeValues[sourceKey] = values[sourceKey];
        }

        if (sourceKey && filePathKey && normalizedParameterName === 'sourcetype' && normalizeAcquisitionSourceType(values[sourceKey]) === 'camera') {
            writeValues[filePathKey] = '';
        }

        return writeValues;
    }

    syncImageAcquisitionSourceControls(options = {}) {
        if (!this.isImageAcquisitionOperator()) {
            return;
        }

        const sourceTypeInput = this.findParamInput('SourceType', 'sourceType');
        if (!sourceTypeInput) {
            return;
        }

        let values = this.collectFormValues();
        values = this.normalizeImageAcquisitionValues(values);
        const isCameraMode = normalizeAcquisitionSourceType(sourceTypeInput.value) === 'camera';
        ['FilePath', 'CameraId', 'CameraBindingId']
            .map(name => this.findParamInput(name))
            .filter(Boolean)
            .filter((input, index, all) => all.indexOf(input) === index)
            .forEach(input => {
                const parameter = this.getCurrentParameterByName(input.name);
                const state = getParameterEffectiveState(this.currentOperator, parameter || input.name, { values });
                const group = input.closest('.form-group');
                const pickerButton = group?.querySelector('.btn-pick-file');
                const label = group?.querySelector('.form-label');
                const requiredMark = label?.querySelector('.required');

                if (
                    isCameraMode &&
                    state.effectiveDisabled &&
                    normalizeParameterName(input.name) === 'filepath' &&
                    options.clearFilePathWhenCamera !== false &&
                    input.value
                ) {
                    input.value = '';
                    this.updateCurrentOperatorParameterValue(input.name, '');
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

    getFilePickerFilter(parameterName = '') {
        const normalizedName = normalizeParameterName(parameterName);
        if (
            normalizedName.includes('model') ||
            normalizedName.includes('embedding') ||
            normalizedName.includes('onnx')
        ) {
            return 'Model Files|*.onnx;*.pt;*.pth;*.engine;*.xml;*.bin|All Files|*.*';
        }

        if (
            normalizedName.includes('label') ||
            normalizedName.includes('catalog') ||
            normalizedName.includes('bank') ||
            normalizedName.includes('json')
        ) {
            return 'Data Files|*.json;*.txt;*.yaml;*.yml;*.csv;*.bin|All Files|*.*';
        }

        if (
            normalizedName === 'filepath' ||
            normalizedName.includes('image') ||
            normalizedName.includes('template')
        ) {
            return 'Image Files|*.bmp;*.jpg;*.png;*.jpeg;*.tif;*.tiff|All Files|*.*';
        }

        return 'All Files|*.*';
    }

    async loadCameraBindingsForSelects(forceRefresh = false) {
        const cameraSelects = this.container.querySelectorAll('select[data-camera-binding-select="true"]');
        if (cameraSelects.length === 0) {
            return;
        }

        try {
            const bindings = await this.fetchCameraBindings(forceRefresh);
            this.populateCameraBindingSelects(cameraSelects, bindings);
        } catch (error) {
            console.error('[PropertyPanelCapabilityOwner] Failed to load camera bindings:', error);
            this.populateCameraBindingSelects(cameraSelects, [], error?.message || 'Unknown error');
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
            let optionsHtml = '<option value="">-- Select camera --</option>';

            if (hasBindings) {
                optionsHtml += bindings.map(binding => `
                    <option value="${escapeAttribute(binding.id)}">
                        ${escapeHtml(binding.displayName || binding.name || binding.id)} (${escapeHtml(binding.serialNumber || '-')})
                    </option>
                `).join('');
            } else if (errorMessage) {
                optionsHtml += '<option value="" disabled>Load failed</option>';
            } else {
                optionsHtml += '<option value="" disabled>No camera bindings</option>';
            }

            select.innerHTML = optionsHtml;
            if (hasBindings && bindings.some(binding => String(binding.id) === String(selectedCameraId))) {
                select.value = selectedCameraId;
            } else {
                select.value = '';
            }

            const hint = select.closest('.form-group')?.querySelector('[data-camera-binding-hint]');
            if (!hint) {
                return;
            }

            if (hasBindings) {
                hint.remove();
                return;
            }

            hint.textContent = errorMessage
                ? `Failed to load camera bindings: ${errorMessage}`
                : 'No camera bindings are available.';
            hint.classList.add('error');
        });
    }

    setOperator(operator) {
        this.handleSelectedNodeChanged(operator);
    }

    clear() {
        this.teardownGeometryEditor();
        this.currentOperator = null;
        this.currentNodeId = null;
        this.currentConnection = null;
        this.validationErrors = [];
        this.statusMessage = '';
        this.lastChangedParameterName = null;
        this.dirty = false;
        this.render();
    }

    collectFormValues() {
        const form = this.container.querySelector('[data-property-capability-form="true"]');
        if (!form) {
            return {};
        }

        const values = {};
        form.querySelectorAll('[data-property-parameter="true"]').forEach(input => {
            if (!input.name) {
                return;
            }

            values[input.name] = getFormValue(input);
        });

        return values;
    }

    isCircleSearchV2ToolFeatureEnabled() {
        return isCircleSearchV2ToolEnabled({
            circleSearchV2ToolEnabled: this.circleSearchV2ToolEnabled
        });
    }

    isNPointCalibrationWorkbenchFeatureEnabled() {
        return isNPointCalibrationWorkbenchEnabled({
            nPointCalibrationWorkbenchEnabled: this.nPointCalibrationWorkbenchEnabled
        });
    }

    getGeometryEditorOptions() {
        return {
            circleSearchV2ToolEnabled: this.isCircleSearchV2ToolFeatureEnabled(),
            nPointCalibrationWorkbenchEnabled: this.isNPointCalibrationWorkbenchFeatureEnabled()
        };
    }

    getGeometryConfig(operator = this.currentOperator) {
        return getOperatorRoiConfig(operator, this.getGeometryEditorOptions());
    }

    getCurrentOperatorType() {
        return String(this.currentOperator?.type || this.currentOperator?.operatorType || '').trim();
    }

    isCaliperToolOperator() {
        return this.getCurrentOperatorType() === 'CaliperTool';
    }

    getGeometryEditorOperator() {
        if (!this.isCaliperToolOperator() || !this.currentNodeId) {
            return this.currentOperator;
        }

        return this.propertyAdapter.getCaliperSearchRegionOperatorSnapshot?.(this.currentNodeId) ||
            this.currentOperator;
    }

    buildGeometryWriteValues(values = {}) {
        const config = this.getGeometryConfig();
        return {
            ...(config?.commitValues || {}),
            ...values
        };
    }

    getCaliperSearchRegionStatus(result) {
        if (result?.updated !== false) {
            return '卡尺搜索区域已通过 RectangleRegion 连接更新';
        }

        switch (result?.reason) {
            case 'search_region_connected_to_non_rectangle_region':
                return 'CaliperTool.SearchRegion 已连接到非 RectangleRegion 节点，请先断开该连接或改用 RectangleRegion。';
            case 'search_region_connection_failed':
                return 'RectangleRegion 创建后连接 CaliperTool.SearchRegion 失败，已回滚新节点。';
            case 'rectangle_region_output_not_found':
                return 'RectangleRegion 缺少 Rectangle 输出端口，已回滚新节点。';
            case 'search_region_port_not_found':
                return 'CaliperTool.SearchRegion 输入端口不存在，无法写入搜索区域。';
            case 'flow_canvas_mutation_unavailable':
                return '当前画布不支持自动创建 SearchRegion 连接。';
            case 'rectangle_region_create_failed':
                return 'RectangleRegion 创建失败，搜索区域未写入。';
            default:
                return '卡尺搜索区域未变更。';
        }
    }

    writeInputValue(input, rawValue) {
        const type = String(input.dataset.type || '').toLowerCase();
        if (type === 'boolean' || type === 'bool') {
            input.checked = toBoolean(rawValue);
            return;
        }

        input.value = rawValue ?? '';

        const slider = input.parentElement?.querySelector('.param-slider');
        if (slider) {
            slider.value = input.value;
        }

        if (input.type === 'color') {
            const wrapper = input.closest('.color-picker-wrapper');
            const preview = wrapper?.querySelector('.color-preview-box');
            const valueText = wrapper?.querySelector('.color-value');
            if (preview) {
                preview.style.backgroundColor = input.value;
            }
            if (valueText) {
                valueText.textContent = input.value;
            }
        }
    }

    applyValuesToForm(values = {}) {
        Object.entries(values).forEach(([name, value]) => {
            const input = this.findParamInput(name);
            if (input) {
                this.writeInputValue(input, value);
            }
        });
    }

    handleGeometryChanged(values = {}, phase = 'dragging') {
        if (!this.currentNodeId || !this.currentOperator || this.disposed) {
            return;
        }

        const writeValues = this.buildGeometryWriteValues(values);
        this.applyValuesToForm(writeValues);

        if (phase !== 'commit') {
            this.statusMessage = '图上几何拖动中，松开后写回参数';
            this.updateStatus();
            return;
        }

        if (this.isCaliperToolOperator()) {
            const result = this.propertyAdapter.upsertCaliperSearchRegion?.(this.currentNodeId, writeValues);
            this.statusMessage = this.getCaliperSearchRegionStatus(result);
            this.dirty = result?.updated !== false;
            this.updateStatus();
            if (result?.updated !== false && result?.operator) {
                this.roiEditorPanel?.refreshFromOperator?.({ forceSyncOverlay: true });
            }
            return;
        }

        const result = this.propertyAdapter.writeParameters(this.currentNodeId, writeValues);
        if (result?.reason === 'node_not_found') {
            this.currentOperator = null;
            this.currentNodeId = null;
            this.render();
            return;
        }

        Object.entries(writeValues).forEach(([name, value]) => this.updateCurrentOperatorParameterValue(name, value));
        this.validationErrors = [];
        this.statusMessage = '图上几何已写回参数';
        this.lastChangedParameterName = null;
        this.dirty = result?.updated === true;
        this.renderValidationErrors();
        this.updateStatus();
    }

    handleGeometryImageBoundsChanged(bounds) {
        this.geometryImageBounds = bounds &&
            Number.isFinite(Number(bounds.width)) &&
            Number.isFinite(Number(bounds.height)) &&
            Number(bounds.width) > 0 &&
            Number(bounds.height) > 0
            ? { width: Number(bounds.width), height: Number(bounds.height) }
            : null;
    }

    syncGeometryEditorFromParams() {
        this.roiEditorPanel?.refreshFromOperator?.({ forceSyncOverlay: true });
    }

    handleGeometryClearRoi() {
        if (!this.currentNodeId || !this.currentOperator) {
            return;
        }

        const config = this.getGeometryConfig();
        const values = config?.clearValues || null;
        if (!values) {
            return;
        }

        const result = this.propertyAdapter.writeParameters(this.currentNodeId, values);
        if (result?.reason === 'node_not_found') {
            this.currentOperator = null;
            this.currentNodeId = null;
            this.render();
            return;
        }

        this.applyValuesToForm(values);
        Object.entries(values).forEach(([name, value]) => this.updateCurrentOperatorParameterValue(name, value));
        this.statusMessage = '搜索 ROI 已清除';
        this.dirty = result?.updated === true;
        this.updateStatus();
        this.syncGeometryEditorFromParams();
    }

    teardownGeometryEditor() {
        if (!this.roiEditorPanel) {
            return;
        }

        this.roiEditorPanel.destroy?.();
        this.roiEditorPanel = null;
    }

    initGeometryEditor() {
        this.teardownGeometryEditor();

        if (!this.currentOperator || this.currentConnection) {
            return;
        }

        const config = this.getGeometryConfig();
        if (!config?.supported) {
            return;
        }

        const container = this.container.querySelector('[data-property-geometry-editor-container]');
        if (!container) {
            return;
        }

        this.roiEditorPanel = new RoiEditorPanel(container, {
            getOperator: () => this.getGeometryEditorOperator(),
            getPreviewOperator: () => this.currentOperator,
            getRoiConfig: operator => {
                if (this.isCaliperToolOperator()) {
                    return this.getGeometryConfig(this.currentOperator);
                }

                return getOperatorRoiConfig(operator, this.getGeometryEditorOptions());
            },
            previewCoordinator: this.previewCoordinator,
            previewResourcesEnabled: this.previewResourcesEnabled,
            onOpenPreviewImage: this.onOpenPreviewImage,
            onRectChanged: (values, phase) => this.handleGeometryChanged(values, phase),
            onImageBoundsChanged: bounds => this.handleGeometryImageBoundsChanged(bounds),
            onRequestSyncFromParams: () => this.syncGeometryEditorFromParams()
        });
    }

    renderGeometrySection(config) {
        if (!config?.supported) {
            return '';
        }

        return `
            <section class="property-summary-section property-geometry-section" data-property-geometry-section="true">
                <div class="property-geometry-header">
                    <h5>图上几何</h5>
                    ${config.clearValues ? `
                        <button type="button"
                                class="btn btn-secondary btn-sm"
                                data-property-geometry-clear-roi="true">
                            清除 ROI
                        </button>
                    ` : ''}
                </div>
                ${config.description ? `<p class="property-geometry-description">${escapeHtml(config.description)}</p>` : ''}
                <div data-property-geometry-editor-container></div>
            </section>
        `;
    }

    validateValues(values) {
        if (!this.currentOperator) {
            return [];
        }

        return this.validateOperatorModel(this.currentOperator, { values });
    }

    validateOperatorModel(operator, options = {}) {
        const values = options.values || null;
        const parameters = Array.isArray(operator?.parameters) ? operator.parameters : [];
        const errors = [];

        collectEffectiveRequiredParameterErrors(operator, parameters, {
            values,
            getLabel: (parameter, fallbackName) => getParameterLabel(parameter, fallbackName)
        }).forEach(error => {
            errors.push({ name: error.name, message: error.message });
        });

        parameters.forEach(parameter => {
            const name = getParameterName(parameter);
            if (!name) {
                return;
            }

            const state = getParameterEffectiveState(operator, parameter, { values });
            if (state.effectiveDisabled) {
                return;
            }

            const dataType = getParameterDataType(parameter);
            const value = values && Object.prototype.hasOwnProperty.call(values, name)
                ? values[name]
                : getParameterValue(parameter);

            if (!['int', 'integer', 'double', 'float', 'number'].includes(dataType) || isEmptyValue(value)) {
                return;
            }

            const parsed = parseNumericValue(value);
            const label = getParameterLabel(parameter, name);
            if (!parsed.valid) {
                errors.push({ name, message: `${label} 必须是有效数字` });
                return;
            }

            if ((dataType === 'int' || dataType === 'integer') && !Number.isInteger(parsed.value)) {
                errors.push({ name, message: `${label} 必须是整数` });
                return;
            }

            const min = getParameterRangeValue(parameter, 'min', 'Min', 'minValue', 'MinValue');
            const max = getParameterRangeValue(parameter, 'max', 'Max', 'maxValue', 'MaxValue');
            if (min !== null && parsed.value < min) {
                errors.push({ name, message: `${label} 不能小于 ${min}` });
                return;
            }
            if (max !== null && parsed.value > max) {
                errors.push({ name, message: `${label} 不能大于 ${max}` });
            }
        });

        return errors;
    }

    validateCurrentOperator(options = {}) {
        const errors = this.validateValues(this.collectFormValues());
        this.validationErrors = errors;

        if (errors.length > 0) {
            this.statusMessage = '参数校验失败';
            if (options.showToast) {
                this.showToast(`参数校验失败：${errors[0].message}`, 'error');
            }
            this.renderValidationErrors();
            this.updateStatus();
            return false;
        }

        this.renderValidationErrors();
        this.updateStatus();
        return true;
    }

    validateFlowForAction(flowCanvas, options = {}) {
        const action = options.action || '执行';
        if (this.currentOperator && this.validateCurrentOperator({
            showToast: options.showToast === true
        }) === false) {
            return false;
        }

        const nodes = flowCanvas?.nodes instanceof Map
            ? Array.from(flowCanvas.nodes.values())
            : Array.from(this.propertyAdapter.flowCanvasAdapter?.nodes?.values?.() || []);
        for (const node of nodes) {
            if (!node?.id || node.id === this.currentNodeId) {
                continue;
            }

            const operator = this.propertyAdapter.getSelectedOperatorSnapshot(node.id);
            const errors = this.validateOperatorModel(operator);
            if (errors.length === 0) {
                continue;
            }

            this.propertyAdapter.selectNode(node.id);
            if (options.showToast) {
                this.showToast(`${action}前请先修正参数：${errors[0].message}`, 'error');
            }
            return false;
        }

        return true;
    }

    applyChanges(options = {}) {
        const showToast = options.showToast !== false;
        if (!this.currentNodeId || !this.currentOperator) {
            return true;
        }

        let values = this.collectFormValues();
        values = this.normalizeImageAcquisitionValues(values);
        const errors = this.validateValues(values);
        if (errors.length > 0) {
            this.validationErrors = errors;
            this.statusMessage = '参数校验失败';
            if (showToast) {
                this.showToast(`参数校验失败：${errors[0].message}`, 'error');
            }
            this.renderValidationErrors();
            this.updateStatus();
            return false;
        }

        const result = this.propertyAdapter.writeParameters(this.currentNodeId, values);
        this.validationErrors = [];
        this.statusMessage = '参数已更新';
        this.lastChangedParameterName = null;
        this.dirty = result?.updated === true;
        Object.entries(values).forEach(([name, value]) => this.updateCurrentOperatorParameterValue(name, value));
        this.syncImageAcquisitionSourceControls();
        if (showToast) {
            this.showToast('参数已更新', 'success');
        }
        this.renderValidationErrors();
        this.updateStatus();
        return result?.reason !== 'node_not_found';
    }

    syncDraftChanges(options = {}) {
        return this.applyChanges({
            showToast: options.showToast === true
        });
    }

    render() {
        if (this.disposed) {
            return;
        }

        this.teardownGeometryEditor();

        if (!this.currentOperator) {
            if (this.currentConnection) {
                this.renderConnectionSummary();
                return;
            }

            this.container.innerHTML = `
                <section class="property-capability-owner property-capability-empty" data-owner="${PROPERTY_PANEL_CAPABILITY_OWNER_ID}">
                    <div class="property-capability-title">属性面板</div>
                    <p class="property-capability-empty-title">未选择算子</p>
                    <p class="empty-text">请选择一个算子</p>
                </section>
            `;
            return;
        }

        const operator = this.currentOperator;
        const title = operator.title || operator.displayName || operator.type || '算子';
        const type = operator.type || '';
        const typeDisplay = getOperatorTypeDisplayName(type, { includeType: true }) || type;
        const parameters = Array.isArray(operator.parameters) ? operator.parameters : [];
        const geometryConfig = this.getGeometryConfig(operator);

        this.container.innerHTML = `
            <section class="property-capability-owner" data-owner="${PROPERTY_PANEL_CAPABILITY_OWNER_ID}">
                <header class="property-header property-capability-header">
                    <div class="header-text">
                        <div class="property-capability-title">属性面板</div>
                        <h4>${escapeHtml(title)}</h4>
                        <span class="property-type">${escapeHtml(typeDisplay)}</span>
                    </div>
                </header>
                <div class="property-capability-scroll" data-low-height-scroll="true">
                    <section class="property-summary-section">
                        <h5>基础信息</h5>
                        <dl class="property-capability-meta">
                            <div>
                                <dt>当前算子</dt>
                                <dd>${escapeHtml(title)}</dd>
                            </div>
                            <div>
                                <dt>类型</dt>
                                <dd>${escapeHtml(typeDisplay)}</dd>
                            </div>
                        </dl>
                    </section>
                    ${this.renderGeometrySection(geometryConfig)}
                    <section class="property-summary-section">
                        <h5>参数</h5>
                        ${parameters.length === 0
                            ? '<p class="property-summary-empty">当前算子没有可编辑参数</p>'
                            : `<form class="property-form property-capability-form" data-property-capability-form="true">
                                ${parameters.map(parameter => this.renderParameterField(parameter)).join('')}
                                <button type="submit" class="hidden" aria-hidden="true" tabindex="-1">应用</button>
                            </form>`}
                    </section>
                </div>
                <div class="property-capability-status" data-property-capability-status aria-live="polite">
                    ${escapeHtml(this.statusMessage)}
                </div>
            </section>
        `;
        this.renderValidationErrors();
        this.syncImageAcquisitionSourceControls();
        void this.loadCameraBindingsForSelects();
        this.updateStatus();
        this.initGeometryEditor();
    }

    renderConnectionSummary() {
        const connection = this.currentConnection || {};
        this.container.innerHTML = `
            <section class="property-capability-owner inspector-connection-summary" data-owner="${PROPERTY_PANEL_CAPABILITY_OWNER_ID}" data-selection-kind="connection">
                <div class="property-capability-title">属性面板</div>
                <h4>当前选中连线</h4>
                <p>连线当前没有可编辑参数。可在画布中删除或重新连接端口。</p>
                <dl class="inspector-connection-meta">
                    <div>
                        <dt>输出节点</dt>
                        <dd>${escapeHtml(connection.sourceTitle || '-')} / ${escapeHtml(connection.sourcePortName || '-')} (${escapeHtml(connection.sourcePortType || '-')})</dd>
                    </div>
                    <div>
                        <dt>输入节点</dt>
                        <dd>${escapeHtml(connection.targetTitle || '-')} / ${escapeHtml(connection.targetPortName || '-')} (${escapeHtml(connection.targetPortType || '-')})</dd>
                    </div>
                    <div>
                        <dt>连线 ID</dt>
                        <dd>${escapeHtml(connection.id || '-')}</dd>
                    </div>
                </dl>
            </section>
        `;
    }

    renderParameterField(parameter) {
        const name = getParameterName(parameter);
        if (!name) {
            return '';
        }

        const label = getParameterLabel(parameter, name);
        const dataType = getParameterDataType(parameter);
        const description = getParameterDescription(parameter);
        const value = getParameterValue(parameter);
        const values = this.currentOperator ? null : {};
        const effectiveState = getParameterEffectiveState(this.currentOperator, parameter, { values });
        const requiredMark = effectiveState.effectiveRequired ? '<span class="required">*</span>' : '';
        const disabled = effectiveState.effectiveDisabled ? 'disabled aria-disabled="true"' : '';
        const disabledHint = effectiveState.effectiveDisabled && effectiveState.disabledReason
            ? `<p class="form-description parameter-rule-hint">${escapeHtml(effectiveState.disabledReason)}</p>`
            : '';

        return `
            <div class="form-group ${effectiveState.effectiveDisabled ? 'is-rule-disabled' : ''}" data-parameter-name="${escapeAttribute(name)}">
                <label for="v2-param-${escapeAttribute(name)}" class="form-label">${escapeHtml(label)} ${requiredMark}</label>
                ${this.renderInput(parameter, { name, dataType, value, disabled })}
                ${description ? `<p class="form-description">${escapeHtml(description)}</p>` : ''}
                ${disabledHint}
            </div>
        `;
    }

    renderInput(parameter, { name, dataType, value, disabled }) {
        const inputId = `v2-param-${escapeAttribute(name)}`;
        const common = `id="${inputId}" name="${escapeAttribute(name)}" data-property-parameter="true"`;
        const currentValue = value ?? '';
        const controlType = resolveParameterControlType(parameter);

        if (controlType === 'boolean' || controlType === 'bool') {
            return `
                <label class="property-toggle">
                    <input type="checkbox" ${common} data-type="boolean" ${toBoolean(currentValue) ? 'checked' : ''} ${disabled}>
                    <span class="toggle-slider"></span>
                </label>
            `;
        }

        if (controlType === 'enum' || controlType === 'select') {
            const options = getParameterOptions(parameter);
            if (options.length > 0) {
                return `
                    <select ${common} class="form-select" data-type="enum" ${disabled}>
                        ${options.map(option => {
                            const optionLabel = typeof option === 'string' ? option : (option.label ?? option.Label ?? option.value ?? option.Value ?? '');
                            const optionValue = typeof option === 'string' ? option : (option.value ?? option.Value ?? optionLabel);
                            return `<option value="${escapeAttribute(optionValue)}" ${String(optionValue) === String(currentValue) ? 'selected' : ''}>${escapeHtml(optionLabel)}</option>`;
                        }).join('')}
                    </select>
                `;
            }
        }

        if (controlType === 'file') {
            return `
                <div class="file-picker-wrapper">
                    <input type="text"
                           ${common}
                           value="${escapeAttribute(currentValue)}"
                           class="form-input"
                           readonly
                           data-type="file"
                           ${disabled}>
                    <button type="button"
                            class="btn btn-sm btn-secondary btn-pick-file"
                            data-param="${escapeAttribute(name)}"
                            ${disabled ? 'disabled' : ''}>...</button>
                </div>
            `;
        }

        if (controlType === 'cameraBinding') {
            const bindings = this.cameraBindingsCache || [];
            const hasBindings = bindings.length > 0;
            return `
                <select ${common}
                        class="form-select"
                        data-type="string"
                        data-camera-binding-select="true"
                        data-current-value="${escapeAttribute(currentValue)}"
                        ${disabled}>
                    <option value="">-- Select camera --</option>
                    ${hasBindings
                        ? bindings.map(binding => `
                            <option value="${escapeAttribute(binding.id)}" ${String(binding.id) === String(currentValue) ? 'selected' : ''}>
                                ${escapeHtml(binding.displayName || binding.name || binding.id)} (${escapeHtml(binding.serialNumber || '-')})
                            </option>
                        `).join('')
                        : '<option value="" disabled>Loading...</option>'}
                </select>
                ${hasBindings ? '' : '<p class="form-description error" data-camera-binding-hint>Loading camera bindings...</p>'}
            `;
        }

        if (controlType === 'color') {
            const colorValue = currentValue || '#000000';
            return `
                <div class="color-picker-wrapper">
                    <input type="color"
                           ${common}
                           value="${escapeAttribute(colorValue)}"
                           class="form-color-hidden"
                           data-type="color"
                           ${disabled}>
                    <div class="color-preview-box" style="background-color: ${escapeAttribute(colorValue)}">
                        <span class="color-value">${escapeHtml(colorValue)}</span>
                    </div>
                </div>
            `;
        }

        if (['int', 'integer', 'double', 'float', 'number'].includes(controlType)) {
            const min = getParameterRangeValue(parameter, 'min', 'Min', 'minValue', 'MinValue');
            const max = getParameterRangeValue(parameter, 'max', 'Max', 'maxValue', 'MaxValue');
            const step = parameter?.step ?? parameter?.Step ?? (controlType === 'int' || controlType === 'integer' ? 1 : 0.1);
            const hasRange = min !== null && max !== null;
            return `
                <div class="number-input-wrapper">
                    <input type="number"
                           ${common}
                           value="${escapeAttribute(currentValue)}"
                           ${min !== null ? `min="${min}"` : ''}
                           ${max !== null ? `max="${max}"` : ''}
                           step="${escapeAttribute(step)}"
                           class="form-input number-input"
                           data-type="${controlType}"
                           ${disabled}>
                    ${hasRange ? `
                        <input type="range"
                               class="param-slider"
                               min="${min}"
                               max="${max}"
                               step="${escapeAttribute(step)}"
                               value="${escapeAttribute(currentValue)}"
                               ${disabled}>
                    ` : ''}
                </div>
            `;
        }

        return `
            <input type="text"
                   ${common}
                   value="${escapeAttribute(currentValue)}"
                   class="form-input"
                   data-type="${escapeAttribute(dataType || controlType || 'string')}"
                   ${disabled}>
        `;
    }

    findInputForError(name) {
        return Array.from(this.container.querySelectorAll('[data-property-parameter="true"]'))
            .find(input => String(input.name || '').toLowerCase() === String(name || '').toLowerCase()) || null;
    }

    renderValidationErrors() {
        this.container.querySelectorAll('.form-group.invalid').forEach(group => {
            group.classList.remove('invalid');
        });
        this.container.querySelectorAll('[data-validation-error="true"]').forEach(error => {
            error.remove();
        });

        this.validationErrors.forEach(error => {
            const input = this.findInputForError(error.name);
            const group = input?.closest?.('.form-group');
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
    }

    updateStatus() {
        const status = this.container.querySelector('[data-property-capability-status]');
        if (!status) {
            return;
        }

        status.textContent = this.statusMessage || '';
        status.classList.toggle('is-error', this.statusMessage === '参数校验失败');
        status.classList.toggle('is-success', this.statusMessage === '参数已更新');
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
                // Ignore best-effort subscription cleanup failures.
            }
        });
        this.unsubscribes = [];
        this.container.removeEventListener('change', this.handleContainerChange);
        this.container.removeEventListener('click', this.handleContainerClick);
        this.container.removeEventListener('input', this.handleContainerInput);
        this.container.removeEventListener('submit', this.handleContainerSubmit);
        this.teardownGeometryEditor();
        delete this.container.dataset.propertyPanelOwner;
        this.container.innerHTML = '';
        this.currentOperator = null;
        this.currentNodeId = null;
        this.currentConnection = null;
    }
}

export default PropertyPanelCapabilityOwner;
