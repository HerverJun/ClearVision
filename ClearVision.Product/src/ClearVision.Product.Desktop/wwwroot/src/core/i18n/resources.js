const zhCN = {
    app: {
        title: 'ClearVision',
        startingImports: '开始加载模块',
        loadingHistory: '正在加载检测历史数据',
        noProjectHistory: '没有打开的工程，跳过加载历史数据',
        advancedAnalyticsUnavailable: '高级分析 API 未接入，显示暂无数据',
        deepReportUnavailable: '深度报告 API 未接入',
        imageLoaded: '图像已加载',
        inspectionComplete: '检测完成',
        resultSavedOutsideInspectionView: '检测完成但不在检测视图，已保存结果',
        autoSaveCompleted: '自动保存完成',
        autoSaveStarted: '自动保存已启动，间隔',
        manualSaveCompleted: '手动触发保存完成',
        projectExported: '工程已导出',
        projectImported: '工程已导入'
    },
    common: {
        unavailable: '未接入',
        noData: '暂无数据',
        unknown: '未知'
    },
    errors: {
        jsonPayloadParseFailed: 'JSON payload 解析失败',
        serverAnalyticsRefreshFailed: '刷新服务端分析失败',
        serverHistoryRefreshFailed: '刷新服务端历史失败',
        defectDistributionFailed: '获取缺陷分布失败',
        trendAnalysisFailed: '获取趋势分析失败',
        advancedAnalyticsFailed: '高级分析数据获取失败',
        analysisReportFailed: '加载分析报告失败',
        statisticsFailed: '加载统计数据失败'
    },
    results: {
        panelInitialized: '结果面板初始化完成',
        details: '查看结果详情',
        sseStreamFailed: '结果 SSE 流失败',
        sseStreamEnded: '结果 SSE 流已结束，准备重连',
        sseConnectionFailed: '结果 SSE 连接失败，准备重连'
    }
};

const resources = {
    'zh-CN': zhCN,
    zh: zhCN
};

let currentLocale = 'zh-CN';

export function setLocale(locale) {
    if (resources[locale]) {
        currentLocale = locale;
    }
}

export function getLocale() {
    return currentLocale;
}

export function t(key, fallback = key) {
    const root = resources[currentLocale] || zhCN;
    const value = key
        .split('.')
        .reduce((current, part) => (current && current[part] !== undefined ? current[part] : undefined), root);

    return typeof value === 'string' ? value : fallback;
}

export default {
    getLocale,
    setLocale,
    t
};
