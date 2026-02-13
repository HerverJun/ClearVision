/**
 * 主应用入口 - S4-006: 端到端集成
 * Sprint 4: 前后端集成与用户体验闭环
 */

import { Dialog } from './shared/components/dialog.js';

// ============================================
// 全局错误捕获 - 用于调试
// ============================================
// 存储错误日志
window._errorLogs = [];

window.onerror = function(message, source, lineno, colno, error) {
    const errorInfo = `[Global Error] ${message} at ${source}:${lineno}`;
    console.error(errorInfo);
    window._errorLogs.push({
        type: 'Error',
        message: message,
        source: source,
        line: lineno,
        time: new Date().toLocaleTimeString()
    });
    const debugDiv = document.getElementById('debug-errors');
    if (debugDiv) {
        debugDiv.innerHTML += `<div style="color:red;margin:2px 0">❌ ${message} (Line ${lineno})</div>`;
    }
    return false;
};

window.addEventListener('unhandledrejection', function(event) {
    const errorMsg = event.reason?.message || event.reason;
    console.error('[Unhandled Promise Rejection]', errorMsg);
    window._errorLogs.push({
        type: 'Promise',
        message: errorMsg,
        time: new Date().toLocaleTimeString()
    });
    const debugDiv = document.getElementById('debug-errors');
    if (debugDiv) {
        debugDiv.innerHTML += `<div style="color:orange;margin:2px 0">⚠️ Promise: ${errorMsg}</div>`;
    }
});

console.log('[App] Starting module imports...');

import webMessageBridge from './core/messaging/webMessageBridge.js';
import httpClient from './core/messaging/httpClient.js';
import { createSignal } from './core/state/store.js';
import FlowCanvas from './core/canvas/flowCanvas.js';
import { ImageViewerComponent } from './features/image-viewer/imageViewer.js';
import { OperatorLibraryPanel } from './features/operator-library/operatorLibrary.js';
import inspectionController from './features/inspection/inspectionController.js';
import { showToast, createModal, closeModal, createInput, createLabeledInput, createButton } from './shared/components/uiComponents.js';
import { PropertyPanel } from './features/flow-editor/propertyPanel.js';
import { ProjectView } from './features/project/projectView.js';
import projectManager from './features/project/projectManager.js';
import { ResultPanel } from './features/results/resultPanel.js';
import settingsModal from './features/settings/settingsModal.js';

// 全局状态
const [getCurrentView, setCurrentView, subscribeView] = createSignal('flow');
const [getSelectedOperator, setSelectedOperator, subscribeSelectedOperator] = createSignal(null);
const [getOperatorLibrary, setOperatorLibrary, subscribeOperatorLibrary] = createSignal([]);
const [getCurrentProject, setCurrentProject, subscribeCurrentProject] = createSignal(null);

// 组件实例
let imageViewer = null;
let operatorLibraryPanel = null;
let flowCanvas = null;
let propertyPanel = null;
let projectView = null;
let resultPanel = null;

/**
 * 初始化应用
 */
function initializeApp() {
    console.log('[App] 初始化应用...');
    
    // 添加错误显示区域
    const debugErrors = document.createElement('div');
    debugErrors.id = 'debug-errors';
    debugErrors.style.cssText = 'position:fixed;bottom:5px;left:5px;right:300px;max-height:150px;overflow:auto;background:rgba(0,0,0,0.8);color:#0f0;padding:10px;font-family:monospace;font-size:11px;z-index:99998;border-radius:4px;display:none;';
    document.body.appendChild(debugErrors);
    
    // 添加调试标记到页面
    const debugIndicator = document.createElement('div');
    debugIndicator.id = 'js-loaded-indicator';
    debugIndicator.style.cssText = 'position:fixed;top:5px;right:5px;background:#52c41a;color:white;padding:4px 8px;border-radius:4px;font-size:12px;z-index:99999;cursor:pointer;';
    debugIndicator.textContent = 'JS已加载 ✓';
    debugIndicator.onclick = () => {
        const btnCount = document.querySelectorAll('button').length;
        const hasErrors = debugErrors.children.length > 0;
        alert(`JavaScript运行正常！\n按钮数量: ${btnCount}\n错误数量: ${debugErrors.children.length}\n\n点击确定显示/隐藏错误日志`);
        debugErrors.style.display = debugErrors.style.display === 'none' ? 'block' : 'none';
    };
    document.body.appendChild(debugIndicator);
    
    console.log('[App] Debug indicators added');
    
    // 初始化导航
    initializeNavigation();
    
    // 初始化算子库面板
    initializeOperatorLibraryPanel();
    
    // 初始化流程编辑器
    initializeFlowEditor();
    
    // 初始化图像查看器
    initializeImageViewer();
    
    // 初始化 WebMessage 通信
    initializeWebMessage();
    
    // 初始化检测控制器
    initializeInspectionController();
    
    // 初始化属性面板
    initializePropertyPanel();

    // 初始化工程视图
    initializeProjectView();

    // 初始化结果面板（数显功能）
    initializeResultPanel();

    // 初始化主题
    initializeTheme();

    console.log('[App] 应用初始化完成');

    // 初始化工具栏按钮
    initializeToolbar();
    
    // 显示欢迎消息
    showToast('ClearVision 已就绪', 'success');
}

/**
 * 初始化导航
 */
function initializeNavigation() {
    const navButtons = document.querySelectorAll('.nav-btn');
    
    navButtons.forEach(btn => {
        btn.addEventListener('click', () => {
            // 更新活动状态
            navButtons.forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            
            // 切换视图
            const view = btn.dataset.view;
            setCurrentView(view);
            switchView(view);
        });
    });
}

/**
 * 切换视图
 */
function switchView(view) {
    const flowEditor = document.getElementById('flow-editor');
    const imageViewerContainer = document.getElementById('image-viewer');
    const resultsViewContainer = document.getElementById('results-view');
    const projectViewContainer = document.getElementById('project-view');

    // 隐藏所有视图
    flowEditor?.classList.add('hidden');
    imageViewerContainer?.classList.add('hidden');
    resultsViewContainer?.classList.add('hidden');
    projectViewContainer?.classList.add('hidden');

    switch (view) {
        case 'flow':
            flowEditor?.classList.remove('hidden');
            break;
        case 'inspection':
            imageViewerContainer?.classList.remove('hidden');
            // 【关键修复】视图可见后，重新计算画布尺寸
            if (window.imageViewer && window.imageViewer.imageCanvas) {
                // 延迟一帧以确保DOM已完成布局
                requestAnimationFrame(() => {
                    window.imageViewer.imageCanvas.resize();
                    // 如果已有图像，重新适应屏幕
                    if (window.imageViewer.imageCanvas.image) {
                        window.imageViewer.imageCanvas.resetView();
                    }
                });
            }
            break;
        case 'results':
            resultsViewContainer?.classList.remove('hidden');
            console.log('[App] 切换到结果视图');
            // 加载历史检测数据
            if (resultPanel) {
                loadInspectionHistory();
            }
            break;
        case 'project':
            projectViewContainer?.classList.remove('hidden');
            console.log('[App] 切换到工程视图');
            // 刷新工程列表
            if (projectView) {
                projectView.refresh();
            }
            break;
        default:
            flowEditor?.classList.remove('hidden');
            break;
    }
}

/**
 * 初始化算子库面板
 */
function initializeOperatorLibraryPanel() {
    const container = document.getElementById('operator-library');
    if (!container) {
        console.error('[App] 找不到算子库容器');
        return;
    }
    
    operatorLibraryPanel = new OperatorLibraryPanel('operator-library');
    
    // 设置拖拽回调
    operatorLibraryPanel.onOperatorDragStart = (operatorData) => {
        console.log('[App] 开始拖拽算子:', operatorData.type);
    };
    
    // 设置选中回调
    operatorLibraryPanel.onOperatorSelected = (operatorData) => {
        console.log('[App] 选中算子:', operatorData.type);
        setSelectedOperator(operatorData);
    };
    
    console.log('[App] 算子库面板初始化完成');
}

/**
 * 初始化图像查看器
 */
function initializeImageViewer() {
    const container = document.getElementById('image-viewer');
    if (!container) {
        console.error('[App] 找不到图像查看器容器');
        return;
    }
    
    // 清空容器并初始化图像查看器组件
    imageViewer = new ImageViewerComponent('image-viewer');
    window.imageViewer = imageViewer;
    
    // 设置图像加载回调
    imageViewer.onImageLoaded = (img) => {
        console.log('[App] 图像已加载:', img.width, 'x', img.height);
    };
    
    // 设置标注点击回调
    imageViewer.onAnnotationClicked = (annotation) => {
        console.log('[App] 点击标注:', annotation);
    };
    
    console.log('[App] 图像查看器初始化完成');
}

/**
 * 初始化检测控制器
 */
function initializeInspectionController() {
    // 设置检测完成回调
    inspectionController.onInspectionCompleted = (result) => {
        console.log('[App] 检测完成:', result);

        // 如果有处理后的图像，在查看器中显示
        if (result.outputImage && window.imageViewer) {
            const imageData = `data:image/png;base64,${result.outputImage}`;
            window.imageViewer.loadImage(imageData);
        }

        // 添加结果到数显面板
        if (resultPanel) {
            resultPanel.addResult({
                status: result.status,
                defects: result.defects || [],
                processingTime: result.processingTimeMs,
                timestamp: new Date().toISOString(),
                confidenceScore: result.confidenceScore,
                imageData: result.outputImage // 使用 outputImage
            });
        }

        // 更新右侧结果面板（简化显示）
        updateResultsPanel(result);

        // 如果有缺陷，在图像查看器中显示
        if (result.defects && result.defects.length > 0 && window.imageViewer) {
            window.imageViewer.showDefects(result.defects);
        }

        // 显示结果提示
        const status = result.status === 'OK' ? 'success' : 'warning';
        const message = result.status === 'OK'
            ? '检测通过 (OK)'
            : `检测到 ${result.defects?.length || 0} 个缺陷`;
        showToast(message, status);
    };
    
    // 设置检测错误回调
    inspectionController.onInspectionError = (error) => {
        console.error('[App] 检测错误:', error);
        showToast('检测失败: ' + error.message, 'error');
    };
    
    console.log('[App] 检测控制器初始化完成');
}

/**
 * 初始化属性面板
 */
function initializePropertyPanel() {
    const container = document.getElementById('property-panel');
    if (!container) {
        console.error('[App] 找不到属性面板容器');
        return;
    }

    propertyPanel = new PropertyPanel('property-panel');

    // 订阅选中算子变化
    subscribeSelectedOperator((operator) => {
        if (operator) {
            console.log('[App] 选中算子变化:', operator.title || operator.type);
            propertyPanel.setOperator(operator);
        } else {
            propertyPanel.clear();
        }
    });

    // 设置参数变更回调
    propertyPanel.onChange((values) => {
        console.log('[App] 算子参数变更:', values);
        // 更新流程图中对应节点的参数
        const operator = getSelectedOperator();
        if (operator && flowCanvas) {
            const node = flowCanvas.nodes.get(operator.id);
            if (node) {
                node.parameters = operator.parameters;
            }
        }
    });

    console.log('[App] 属性面板初始化完成');
}

/**
 * 初始化工程视图
 */
function initializeProjectView() {
    const container = document.getElementById('project-view');
    if (!container) {
        console.warn('[App] 工程视图容器未找到，将在首次切换到工程视图时初始化');
        return;
    }

    projectView = new ProjectView('project-view');

    // 监听工程打开事件
    window.addEventListener('projectOpened', (event) => {
        const project = event.detail;
        setCurrentProject(project);

        // 更新状态栏
        const projectNameEl = document.getElementById('project-name');
        if (projectNameEl) {
            projectNameEl.textContent = project.name;
        }

        // 加载流程到画布
        if (project.flow && window.flowCanvas) {
            console.log('[App] projectOpened - 加载流程数据:', project.flow);
            window.flowCanvas.deserialize(project.flow);
        } else if (window.flowCanvas) {
            // 【修复】如果没有流程数据，清空画布
            console.log('[App] projectOpened - 工程没有流程数据，清空画布');
            window.flowCanvas.clear();
        }

        // 切换到流程视图
        setCurrentView('flow');
        switchView('flow');

        // 更新导航按钮
        document.querySelectorAll('.nav-btn').forEach(btn => {
            btn.classList.remove('active');
            if (btn.dataset.view === 'flow') {
                btn.classList.add('active');
            }
        });
    });

    console.log('[App] 工程视图初始化完成');
}

/**
 * 初始化结果面板（数显功能）
 */
function initializeResultPanel() {
    const container = document.getElementById('results-view-content');
    if (!container) {
        console.warn('[App] 结果视图容器未找到');
        return;
    }

    resultPanel = new ResultPanel('results-view-content');

    // 设置结果点击回调
    resultPanel.onResultClick = (result) => {
        console.log('[App] 点击结果:', result);
        // 可以在这里显示结果详情或跳转到图像查看器
        if (result.imageData && imageViewer) {
            imageViewer.loadImage(result.imageData);
            setCurrentView('inspection');
            switchView('inspection');
        }
    };

    // 绑定导出按钮
    const exportBtn = document.getElementById('btn-export-results');
    if (exportBtn) {
        exportBtn.addEventListener('click', () => {
            resultPanel.exportResults('csv');
        });
    }

    // 绑定清空按钮
    const clearBtn = document.getElementById('btn-clear-results');
    if (clearBtn) {
        clearBtn.addEventListener('click', () => {
            if (confirm('确定要清空所有检测结果吗？')) {
                resultPanel.clear();
                showToast('检测结果已清空', 'success');
            }
        });
    }

    console.log('[App] 结果面板初始化完成（数显功能）');
}

/**
 * 加载检测历史数据
 */
async function loadInspectionHistory() {
    const project = getCurrentProject();
    if (!project) {
        console.log('[App] 没有打开的工程，跳过加载历史数据');
        return;
    }

    try {
        console.log('[App] 正在加载检测历史数据...');
        // 调用后端 API 获取历史数据
        const response = await httpClient.get(`/inspection/history/${project.id}?limit=50`);

        if (response && Array.isArray(response)) {
            // 清空现有数据并加载历史数据
            resultPanel.clear();
            response.forEach(result => {
                resultPanel.addResult({
                    status: result.status,
                    defects: result.defects || [],
                    processingTime: result.processingTimeMs,
                    timestamp: result.timestamp,
                    confidenceScore: result.confidenceScore,
                    imageData: result.imageData
                });
            });
            console.log(`[App] 已加载 ${response.length} 条历史检测记录`);
        }
    } catch (error) {
        console.error('[App] 加载检测历史数据失败:', error);
        // 不显示错误提示，因为这是后台加载
    }
}

/**
 * 更新右侧结果面板（简化显示）
 */
function updateResultsPanel(data) {
    // 更新结果面板 - 显示检测结果
    const resultsPanel = document.getElementById('results-panel');
    if (resultsPanel) {
        // 清空现有内容
        resultsPanel.innerHTML = '';

        // 显示检测状态
        const statusDiv = document.createElement('div');
        statusDiv.className = 'result-status';
        statusDiv.textContent = `检测状态: ${data.status || '未知'}`;
        resultsPanel.appendChild(statusDiv);

        // 显示缺陷列表（如果有）
        if (data.defects && data.defects.length > 0) {
            const defectsList = document.createElement('ul');
            defectsList.className = 'defects-list';
            data.defects.forEach(defect => {
                const li = document.createElement('li');
                li.textContent = `${defect.type}: 置信度 ${(defect.confidence * 100).toFixed(1)}%`;
                defectsList.appendChild(li);
            });
            resultsPanel.appendChild(defectsList);
        }

        // 显示处理时间
        if (data.processingTimeMs) {
            const timeDiv = document.createElement('div');
            timeDiv.className = 'processing-time';
            timeDiv.textContent = `处理时间: ${data.processingTimeMs}ms`;
            resultsPanel.appendChild(timeDiv);
        }
    }
}

/**
 * 初始化算子库
 */
async function initializeOperatorLibrary() {
    try {
        // 从后端获取算子库
        const operators = await httpClient.get('/operators/library');
        setOperatorLibrary(operators);
        renderOperatorLibrary(operators);
    } catch (error) {
        console.error('[App] 加载算子库失败:', error);
        // 使用默认算子数据
        renderOperatorLibrary(getDefaultOperators());
    }
}

/**
 * 渲染算子库
 */
function renderOperatorLibrary(operators) {
    const container = document.getElementById('operator-library');
    
    // 按类别分组
    const categories = groupByCategory(operators);
    
    container.innerHTML = Object.entries(categories).map(([category, items]) => `
        <div class="operator-category">
            <div class="category-title">${category}</div>
            ${items.map(op => `
                <div class="operator-item" draggable="true" data-type="${op.type}">
                    <div class="operator-icon">${op.iconName?.charAt(0).toUpperCase() || '?'}</div>
                    <span class="operator-name">${op.displayName}</span>
                </div>
            `).join('')}
        </div>
    `).join('');
    
    // 添加拖拽事件
    container.querySelectorAll('.operator-item').forEach(item => {
        item.addEventListener('dragstart', handleDragStart);
    });
}

/**
 * 按类别分组
 */
function groupByCategory(operators) {
    return operators.reduce((acc, op) => {
        const category = op.category || '其他';
        if (!acc[category]) {
            acc[category] = [];
        }
        acc[category].push(op);
        return acc;
    }, {});
}

/**
 * 获取默认算子数据
 */
function getDefaultOperators() {
    return [
        { type: 'ImageAcquisition', displayName: '图像采集', category: '输入', iconName: 'camera' },
        { type: 'Filtering', displayName: '滤波', category: '预处理', iconName: 'filter' },
        { type: 'EdgeDetection', displayName: '边缘检测', category: '特征提取', iconName: 'edge' },
        { type: 'Thresholding', displayName: '二值化', category: '预处理', iconName: 'threshold' },
        { type: 'ResultOutput', displayName: '结果输出', category: '输出', iconName: 'output' }
    ];
}

/**
 * 处理拖拽开始
 */
function handleDragStart(event) {
    const operatorType = event.target.dataset.type;
    event.dataTransfer.setData('operatorType', operatorType);
}

/**
 * 初始化流程编辑器
 */
function initializeFlowEditor() {
    const canvas = document.getElementById('flow-canvas');
    if (!canvas) {
        console.error('[App] 找不到流程编辑器画布');
        return;
    }
    
    // 使用 FlowCanvas 类初始化
    flowCanvas = new FlowCanvas('flow-canvas');
    
    // 设置节点选中回调
    flowCanvas.onNodeSelected = (node) => {
        if (node) {
            console.log('[App] 节点选中:', node.title || node.type);
            // 构造算子数据传递给属性面板
            setSelectedOperator({
                id: node.id,
                type: node.type,
                title: node.title,
                parameters: node.parameters || []
            });
        } else {
            setSelectedOperator(null);
        }
    };
    
    // 保存到全局以便其他函数使用
    window.flowCanvas = flowCanvas;
    
    // 添加拖放支持
    canvas.addEventListener('dragover', (e) => {
        e.preventDefault();
        e.dataTransfer.dropEffect = 'copy';
        canvas.classList.add('drag-over');
    });
    
    canvas.addEventListener('dragleave', () => {
        canvas.classList.remove('drag-over');
    });
    
    canvas.addEventListener('drop', (e) => {
        e.preventDefault();
        canvas.classList.remove('drag-over');
        
        let operatorData = null;
        
        // 尝试从 dataTransfer 获取算子类型
        let operatorType = e.dataTransfer.getData('operatorType');
        
        // 如果从 operator-library 拖拽，数据格式可能不同
        if (!operatorType) {
            try {
                // 优先从全局变量获取备选数据 (针对 WebView2 环境)
                if (window.__draggingOperatorData) {
                    operatorData = window.__draggingOperatorData;
                    operatorType = operatorData.type;
                    console.log('[App] 从全局变量获取拖拽数据:', operatorType);
                    // 使用完立即清理
                    window.__draggingOperatorData = null;
                } else {
                    const jsonStr = e.dataTransfer.getData('application/json');
                    if (jsonStr) {
                        operatorData = JSON.parse(jsonStr);
                        operatorType = operatorData.type;
                        console.log('[App] 从 dataTransfer 获取拖拽数据:', operatorType);
                    }
                }
            } catch (err) {
                console.warn('[App] 无法解析拖拽数据');
            }
        }
        
        if (operatorType) {
            const rect = canvas.getBoundingClientRect();
            const x = (e.clientX - rect.left - flowCanvas.offset.x) / flowCanvas.scale;
            const y = (e.clientY - rect.top - flowCanvas.offset.y) / flowCanvas.scale;
            addOperatorToFlow(operatorType, x, y, operatorData);
        }
    });
    
    console.log('[App] 流程编辑器初始化完成');
}

/**
 * 添加算子到流程
 */
/**
 * 添加算子到流程
 */
function addOperatorToFlow(type, x, y, data = null) {
    console.log('[App] 添加算子:', type, '位置:', x, y);
    
    if (!window.flowCanvas) {
        console.error('[App] FlowCanvas 未初始化');
        return;
    }
    
    // 算子配置
    const operatorConfigs = {
        'ImageAcquisition': { title: '图像采集', color: '#52c41a', icon: '📷' },
        'Filtering': { title: '滤波', color: '#1890ff', icon: '🔍' },
        'EdgeDetection': { title: '边缘检测', color: '#722ed1', icon: '〰️' },
        'Thresholding': { title: '二值化', color: '#eb2f96', icon: '⚫' },
        'Morphology': { title: '形态学', color: '#fa8c16', icon: '🔄' },
        'BlobAnalysis': { title: 'Blob分析', color: '#13c2c2', icon: '🔵' },
        'TemplateMatching': { title: '模板匹配', color: '#f5222d', icon: '🎯' },
        'Measurement': { title: '测量', color: '#2f54eb', icon: '📏' },
        'DeepLearning': { title: '深度学习', color: '#a0d911', icon: '🧠' },
        'ResultOutput': { title: '结果输出', color: '#595959', icon: '📤' }
    };
    
    // 优先使用传入数据的配置，否则使用默认配置
    const defaultConfig = operatorConfigs[type] || { title: type, color: '#1890ff', icon: '📦' };
    
    const nodeConfig = {
        title: data?.displayName || defaultConfig.title,
        color: defaultConfig.color,
        icon: data?.icon || defaultConfig.icon,
        // 传递参数 - 使用深拷贝确保每个节点有独立的参数副本
        parameters: data?.parameters?.map(p => ({...p})) || [],
        // 传递端口配置 (如果有) 或使用默认值
        inputs: data?.inputPorts?.map(p => ({name: p.name, type: p.dataType})) || [{ name: 'input', type: 'any' }],
        outputs: data?.outputPorts?.map(p => ({name: p.name, type: p.dataType})) || [{ name: 'output', type: 'any' }]
    };
    
    // 添加节点到画布
    const node = window.flowCanvas.addNode(type, x, y, nodeConfig);
    
    console.log('[App] 算子已添加:', node);
    
    // 选中该节点
    window.flowCanvas.selectedNode = node.id;
    window.flowCanvas.render();
}

/**
 * 初始化 WebMessage 通信
 */
function initializeWebMessage() {
    // 注册消息处理器
    webMessageBridge.on('operatorExecuted', (data) => {
        console.log('[App] 算子执行完成:', data);
        updateResults(data);
    });
    
    webMessageBridge.on('inspectionCompleted', (data) => {
        console.log('[App] 检测完成:', data);
        updateResults(data);
    });
}

/**
 * 处理新建工程
 */
function handleNewProject() {
    const nameInput = createLabeledInput({ label: '工程名称', required: true, placeholder: 'Project_' + Date.now() });
    const descInput = createLabeledInput({ label: '描述', placeholder: '工程描述...' });
    
    const content = document.createElement('div');
    content.appendChild(nameInput);
    content.appendChild(descInput);
    
    let modalOverlay = null;

    const btnCancel = createButton({ 
        text: '取消', 
        type: 'secondary', 
        onClick: () => closeModal(modalOverlay) 
    });
    
    const btnCreate = createButton({ 
        text: '创建', 
        onClick: () => {
            const name = nameInput.querySelector('input').value;
            const desc = descInput.querySelector('input').value;
            
            if (!name) { 
                showToast('请输入工程名称', 'warning'); 
                return; 
            }
            
            createProject(name, desc)
                .then(() => {
                    closeModal(modalOverlay);
                    // 切换到流程视图
                    switchView('flow'); 
                    document.querySelector('[data-view="flow"]')?.click();
                })
                .catch(err => {
                    // error handled in createProject
                });
        } 
    });
    
    modalOverlay = createModal({
        title: '新建工程',
        content,
        footer: [btnCancel, btnCreate],
        width: '400px'
    });
}


/**
 * 初始化工具栏按钮
 */
function initializeToolbar() {
    // 注意："新建"和"导入图片"按钮已移至工程分页
    // 由 projectView.js 处理
    
    // 保存按钮
    const saveBtn = document.getElementById('btn-save');
    if (saveBtn) {
        saveBtn.addEventListener('click', async () => {
            console.log('[App] 保存工程');
            try {
                const project = getCurrentProject();
                if (project) {
                    // 【修复】使用 projectManager.saveProject 正确保存工程
                    // 先同步当前工程数据到 projectManager
                    projectManager.currentProject = project;
                    
                    // 将流程数据序列化
                    if (window.flowCanvas) {
                        project.flow = window.flowCanvas.serialize();
                        console.log('[App] 流程数据已序列化:', project.flow);
                    }
                    
                    // 调用 projectManager 的保存方法（会分别调用 /projects/{id} 和 /projects/{id}/flow）
                    await projectManager.saveProject(project);
                    showToast('工程已保存', 'success');
                } else {
                    showToast('请先创建或打开工程', 'warning');
                }
            } catch (error) {
                console.error('[App] 保存失败:', error);
                showToast('保存失败: ' + error.message, 'error');
            }
        });
    }
    
    // 运行按钮
    const runBtn = document.getElementById('btn-run');
    if (runBtn) {
        runBtn.addEventListener('click', async () => {
            console.log('[App] 运行检测');
            const project = getCurrentProject();
            
            if (!project) {
                showToast('请先打开或创建工程', 'warning');
                return;
            }
            
            if (!window.flowCanvas || window.flowCanvas.nodes.size === 0) {
                showToast('请先添加算子到流程', 'warning');
                return;
            }
            
            try {
                // 切换到检测视图
                setCurrentView('inspection');
                switchView('inspection');
                
                // 更新导航按钮状态
                document.querySelectorAll('.nav-btn').forEach(btn => {
                    btn.classList.remove('active');
                    if (btn.dataset.view === 'inspection') {
                        btn.classList.add('active');
                    }
                });
                
                // 设置当前工程
                inspectionController.setProject(project.id);
                
                // 如果有加载的图像，执行检测
                // 优先使用导入的测试图像
                const testImage = imageViewer?.currentTestImage;
                
                if (testImage) {
                    showToast('使用导入图像执行检测...', 'info');
                    await inspectionController.executeSingle(testImage);
                } else {
                    // 【关键修复】即使没有显式加载图像，也允许执行。
                    // 图像可能由流程内部的“图像采集”算子从文件加载。
                    showToast('开始执行检测流程...', 'info');
                    await inspectionController.executeSingle();
                }
            } catch (error) {
                console.error('[App] 运行检测失败:', error);
                showToast('检测失败: ' + error.message, 'error');
            }
        });
    }
    
    // 设置按钮
    const settingsBtn = document.getElementById('btn-settings');
    if (settingsBtn) {
        settingsBtn.addEventListener('click', () => {
            console.log('[App] 打开设置');
            settingsModal.open();
        });
    }
}

/**
 * 加载工程
 */
async function loadProject(projectId) {
    try {
        const project = await httpClient.get(`/projects/${projectId}`);
        setCurrentProject(project);
        
        // 更新状态栏
        const projectNameEl = document.getElementById('project-name');
        if (projectNameEl) {
            projectNameEl.textContent = project.name;
        }
        
        // 加载流程到画布
        if (project.flow && window.flowCanvas) {
            console.log('[App] 加载流程数据:', project.flow);
            window.flowCanvas.deserialize(project.flow);
        } else if (window.flowCanvas) {
            // 【修复】如果没有流程数据，清空画布
            console.log('[App] 工程没有流程数据，清空画布');
            window.flowCanvas.clear();
        }
        
        // 设置检测控制器的工程
        inspectionController.setProject(projectId);
        
        showToast(`工程 "${project.name}" 已加载`, 'success');
        return project;
    } catch (error) {
        console.error('[App] 加载工程失败:', error);
        showToast('加载工程失败: ' + error.message, 'error');
        throw error;
    }
}

/**
 * 创建新工程
 */
async function createProject(name, description = '') {
    try {
        const project = await httpClient.post('/projects', {
            name,
            description
        });
        
        setCurrentProject(project);
        
        // 更新状态栏
        const projectNameEl = document.getElementById('project-name');
        if (projectNameEl) {
            projectNameEl.textContent = project.name;
        }
        
        // 清空画布
        if (window.flowCanvas) {
            window.flowCanvas.clear();
        }
        
        // 设置检测控制器的工程
        inspectionController.setProject(project.id);
        
        showToast(`工程 "${name}" 已创建`, 'success');
        return project;
    } catch (error) {
        console.error('[App] 创建工程失败:', error);

        // 处理连接错误，提供更友好的提示
        let errorMsg = error.message;
        if (errorMsg.includes('无法连接到后端服务')) {
            // 使用 dialog 显示详细错误，而不是 toast
            Dialog.alert(
                '连接失败',
                errorMsg.replace(/\n/g, '<br>'),
                null
            );
        } else {
            showToast('创建工程失败: ' + errorMsg, 'error');
        }
        throw error;
    }
}

/**
 * 初始化主题
 */
function initializeTheme() {
    // 读取保存的主题
    const savedTheme = localStorage.getItem('cv_theme') || 'light';
    document.documentElement.dataset.theme = savedTheme;

    // 绑定切换按钮
    const themeToggle = document.getElementById('btn-theme-toggle');
    if (themeToggle) {
        themeToggle.addEventListener('click', toggleTheme);
    }
}

/**
 * 切换主题
 */
function toggleTheme() {
    const current = document.documentElement.dataset.theme;
    const next = current === 'dark' ? 'light' : 'dark';
    document.documentElement.dataset.theme = next;
    localStorage.setItem('cv_theme', next);

    // 显示提示
    const message = next === 'dark' ? '已切换到暗色模式' : '已切换到亮色模式';
    showToast(message, 'info');
}

// 启动应用
document.addEventListener('DOMContentLoaded', initializeApp);

export { 
    getCurrentView, 
    setCurrentView, 
    getSelectedOperator, 
    setSelectedOperator,
    getCurrentProject,
    setCurrentProject,
    loadProject,
    createProject,
    imageViewer,
    operatorLibraryPanel,
    flowCanvas
};
