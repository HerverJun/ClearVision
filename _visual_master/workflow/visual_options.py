#!/usr/bin/env python3
"""Generate and audit function-faithful ClearVision UI visual options D/E.

This workflow is deliberately confined to ``_visual_master``. It imports the
credential resolution, exact-model preflight, path guards, hashing, and PPT
Master backend integration already proven by ``visual_master.py``.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
from concurrent.futures import ThreadPoolExecutor, as_completed
from copy import deepcopy
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw, ImageFont, ImageOps

import visual_master as legacy


ROOT = legacy.ROOT
MANIFEST_PATH = ROOT / "image_prompts.json"
DELIVERY_PATH = ROOT / "manifest.json"
REMAPPING_PATH = ROOT / "functional_remapping.json"
SCHEMA_VERSION = "clearvision-ui-visual-options.v3"
DELIVERY_SCHEMA_VERSION = "clearvision-ui-visual-options-delivery.v3"
OPTIONS = ("D", "E")
ALLOWED_STATUSES = {
    "Pending",
    "Generated",
    "Failed",
    "Needs-Manual",
    "Approved-Candidate",
}
RETRYABLE_STATUSES = {"Pending", "Failed", "Needs-Manual"}
RETRY_MODES = ("legacy", "current-dominant")
CURRENT_DOMINANT_RETRY_MODE = "current-dominant"
CURRENT_DOMINANT_REFERENCE_POLICY = (
    "current-dominant-retry-current-architecture-same-option-masters"
)
CURRENT_DOMINANT_BOARD_WIDTH = 2048
CURRENT_DOMINANT_BOARD_HEIGHT = 1152
CURRENT_DOMINANT_CURRENT_WIDTH = 1536
MASTER_SCREENS = {
    "flow": "05_flow_editor",
    "ai": "13_ai_workspace",
    "settings": "16_system_settings",
}
UPLOAD_APPROVAL_HOST_ENV = "CLEARVISION_VISUAL_UPLOAD_APPROVED_HOST"
UPLOAD_APPROVAL_SCOPE_ENV = "CLEARVISION_VISUAL_UPLOAD_APPROVED_SCOPE"
UPLOAD_APPROVAL_SCOPE = "models-and-clearvision-composite-reference-boards"
TRANSPORT_POLICY = "approved-host-only-https-no-redirects-no-proxy-fixed-generator-v2"
TOP_LEVEL_CONTRACT_KEYS = (
    "schema_version", "model", "model_fallback_allowed", "screen_count", "option_count",
    "entry_count", "text_policy", "functional_authority", "functional_gate",
    "reference_board_policy", "options",
)
ENTRY_CONTRACT_KEYS = (
    "id", "option", "option_name", "screen_id", "filename", "page_name", "purpose",
    "route", "page_role", "master_role", "text_policy", "aspect_ratio", "image_size",
    "model", "current_reference", "current_sha256", "master_references",
    "architecture_references", "architecture_reference_sha256s", "depends_on", "visual_constitution",
    "visual_constitution_sha256",
    "functional_remapping", "functional_audit", "prompt",
)
FUNCTIONAL_AUDIT_CONTRACT_KEYS = (
    "status", "page_exists", "regions_confirmed", "controls_confirmed", "tabs_confirmed",
    "navigation_confirmed", "forbidden_additions", "source_of_truth",
)
REJECT_GATE_KEYS = (
    "no_invented_function", "no_missing_function", "layout_structurally_redesigned",
    "same_option_master_consistency", "d_flow_canvas_dominant",
)
EXPECTED_OUTPUT_FORMAT = "PNG"
OUTPUT_CONTRACTS = {
    "D": {
        "image_size": "4K",
        "dimensions": {"width": 3840, "height": 2160},
        "native_source_required": False,
    },
    "E": {
        "image_size": "4K",
        "dimensions": {"width": 3840, "height": 2160},
        "native_source_required": False,
    },
}
EXPECTED_OUTPUT_ASPECT_RATIO = 16 / 9
MAX_SOURCE_ASPECT_RATIO_RELATIVE_ERROR = 0.01
MIN_SOURCE_PIXEL_COUNT = 655_360
NORMALIZATION_METHOD_NONE = "none-native-contract-match-v1"
NORMALIZATION_METHOD_FIT = "pillow-imageops-fit-lanczos-center-v1"
PREFLIGHT_EVIDENCE_KEYS = (
    "performed", "exact_model", "model_discovered", "model_fallback_allowed",
    "approved_upload_host", "approved_upload_scope", "transport_policy",
    "secure_launcher_sha256", "ppt_image_generator_sha256",
)


OPTION_DEFINITIONS: dict[str, dict[str, Any]] = {
    "D": {
        "name": "Roboflow Workflow Engineering",
        "constitution": "option_D/visual_constitution.md",
        "language": (
            "A modern canvas-first computer-vision workflow engineering environment: very light neutral canvas, "
            "near-white task surfaces, thin cool-gray boundaries, minimal shadow, compact nodes, tiny explicit "
            "ports, fine connections, restrained category accents, strict topology readability, compact 28-32 px "
            "controls, and technical Windows-native typography."
        ),
        "surface_rule": (
            "Use progressive disclosure, search-first operator insertion, contextual inspectors, and task-dependent "
            "Preview/Result workspaces. Avoid permanent large side panels and do not stack every tool around the "
            "canvas at once. Translate the architecture pattern into ClearVision rather than copying Roboflow."
        ),
    },
    "E": {
        "name": "Apple-inspired Premium Engineering",
        "constitution": "option_E/visual_constitution.md",
        "language": (
            "An exceptionally refined high-information-density professional engineering desktop application: "
            "precise typography, disciplined whitespace, controlled contrast, achromatic neutral materials, subtle "
            "purposeful depth, exact iconography, 4-8 px radii, compact 30-34 px controls, and quiet confidence."
        ),
        "surface_rule": (
            "Fundamentally reorganize the workspace around the current task, using adaptive inspectors and refined "
            "contextual surfaces. Do not imitate macOS chrome, Apple marketing, consumer settings, or soft mobile UI."
        ),
    },
}


def output_contract(option: str) -> dict[str, Any]:
    try:
        return OUTPUT_CONTRACTS[option]
    except KeyError as exc:
        raise ValueError(f"Unsupported output contract option: {option}") from exc


def expected_output_dimensions(option: str) -> dict[str, int]:
    return deepcopy(output_contract(option)["dimensions"])


def expected_image_size(option: str) -> str:
    return str(output_contract(option)["image_size"])


def native_source_required(option: str) -> bool:
    return bool(output_contract(option)["native_source_required"])


def page(
    screen_id: str,
    filename: str,
    page_name: str,
    purpose: str,
    current_reference: str,
    route: str,
    family: str,
    regions: list[str],
    controls: list[str],
    tabs: list[str],
    navigation: list[str],
    forbidden: list[str],
    structure: str,
) -> dict[str, Any]:
    return {
        "screen_id": screen_id,
        "filename": filename,
        "page_name": page_name,
        "purpose": purpose,
        "current_reference": current_reference,
        "route": route,
        "family": family,
        "regions": regions,
        "controls": controls,
        "tabs": tabs,
        "navigation": navigation,
        "forbidden": forbidden,
        "structure": structure,
    }


PAGES: list[dict[str, Any]] = [
    page(
        "01_login", "01_login.png", "Login", "Public authentication and session recovery",
        "current/r2/S00-B0.png", "#/login", "settings",
        ["AuthShell", "single login form", "optional session or validation message"],
        ["用户名", "密码", "记住账号", "显示/隐藏密码", "登录"], [], [],
        ["SSO", "注册", "找回密码", "验证码", "多因素认证", "social login", "application navigation"],
        "Keep one focused authentication form and the current recovery-message position. Do not turn login into a marketing page.",
    ),
    page(
        "02_overview", "02_overview.png", "Overview", "System and recent-project operational overview",
        "current/r2/S01-B0.png", "#/overview", "shell",
        ["page header", "continue work", "runtime environment", "available functions"],
        ["刷新概览", "查看全部工程", "查看详情", "继续配置"], [],
        ["工程", "连续检测", "检测结果", "诊断", "关于"],
        ["invented KPI cards", "project analytics", "station telemetry", "PLC dashboard", "alert timeline"],
        "Preserve the current header and the real continue-work, environment, and available-function regions; improve their hierarchy without adding dashboard content.",
    ),
    page(
        "03_projects_data", "03_projects_data.png", "Projects - Data", "Populated project discovery and lifecycle entry",
        "current/r2/S02-B0.png", "#/projects", "shell",
        ["page header", "project command area", "search and sort toolbar", "project table", "pagination"],
        ["刷新工程列表", "导入", "新建工程", "搜索", "排序", "查看详情", "打开", "导出", "删除"], [],
        ["工程库"],
        ["bulk actions", "tag filters", "live run state", "flow count", "operator count", "asset count", "analytics"],
        "Keep the real table columns in order: 名称, 描述, 版本, 修改时间, 最近打开, 操作. Preserve compact row actions and pagination.",
    ),
    page(
        "04_projects_empty", "04_projects_empty.png", "Projects - Empty", "Project library empty state",
        "current/r2/S02-EMPTY.png", "#/projects", "shell",
        ["application shell", "project page header", "project toolbar", "empty-state region"],
        ["刷新工程列表", "导入", "新建工程", "搜索工程", "搜索", "排序", "创建工程"], [], ["工程库"],
        ["sample project cards", "onboarding steps", "import wizard", "statistics", "new navigation modules"],
        "Keep the real project header, search/sort toolbar, and empty table field. Preserve refresh, import, create, search, and sort; the centered empty-state action is the same create-project command, not a new workflow.",
    ),
    page(
        "05_flow_editor", "05_flow_editor.png", "Flow Editor", "Core Flow editor with selected-node Inspector",
        "current/r2/S04-B0.png", "#/projects/:id/workspace", "flow",
        ["application and project context", "command toolbar", "operator discovery rail", "node Inspector", "FlowCanvas", "Preview and result rail", "status strip"],
        [
            "工程列表", "工程详情", "最终判定", "保存", "结果", "检查条件", "正式运行", "运行详情",
            "全局变量", "运行包", "流程模板",
            "搜索算子", "分类", "显示兼容算子", "最近", "收藏", "单击添加", "拖动添加",
            "撤销", "重做", "复制", "粘贴", "副本", "启用/禁用", "删除", "缩小", "100% 重置视图", "放大",
            "节点名称", "启用节点", "断开当前连线", "资源绑定", "常用参数", "高级参数", "专用工作台", "参数校验反馈",
        ],
        [],
        ["概览", "工程", "流程", "检测结果", "算子库"],
        ["second canvas", "second save path", "new run mode", "new toolbar commands", "invented nodes", "new inspector fields"],
        "Treat the current regions as a functional inventory, not fixed geometry. Give the canvas dominant area; relocate the verified operator discovery, contextual Inspector, Preview/result, project commands, canvas edit commands, and status context without adding or dropping any entry.",
    ),
    page(
        "06_flow_validation_error", "06_flow_validation_error.png", "Flow Validation Error", "Selected-node parameter validation state",
        "current/r2/S04-B2.png", "#/projects/:id/workspace", "flow",
        ["same Flow workspace shell", "selected-node Inspector", "validation message", "FlowCanvas", "Preview and result rail"],
        [
            "05 Flow Editor 的全部真实入口", "节点名称", "启用节点", "资源绑定", "常用参数", "高级参数",
            "无效参数字段", "参数校验未通过", "保存", "正式运行", "运行详情",
        ],
        [], ["概览", "工程", "流程", "检测结果", "算子库"],
        ["new validation rules", "auto-fix", "new error actions", "new nodes", "new run controls"],
        "Reuse the same option's structurally redesigned Flow workspace and keep every Flow entry discoverable. Attach the real validation message to the invalid Inspector field without creating an auto-fix or a second error workflow.",
    ),
    page(
        "07_flow_preview_roi", "07_flow_preview_roi.png", "Flow Preview and ROI", "Node Preview with ROI editing draft",
        "current/r2/S05-B2.png", "#/projects/:id/workspace", "flow",
        ["Flow shell", "ROI node Inspector", "FlowCanvas", "node Preview", "result summary", "ROI draft controls", "structured output"],
        [
            "05 Flow Editor 的全部真实入口", "手动预览", "取消预览", "折叠/展开预览区",
            "区域形状", "X", "Y", "Width", "Height", "编辑 ROI", "撤销 ROI 编辑", "重做 ROI 编辑", "放弃", "应用 ROI",
            "图像缩小", "图像放大", "适应预览区", "实际像素", "大图", "像素探针", "结果摘要", "关键输出", "结构化结果",
        ],
        [], ["概览", "工程", "流程", "检测结果", "算子库"],
        ["new ROI types", "new image tools", "camera controls", "new preview modes", "new save channel"],
        "Reorganize the same Flow workspace into a task-specific Preview/ROI context. Increase usable image area while preserving manual preview/cancel, collapse, verified image tools, ROI fields and undo/redo, result summaries, and the cancel/apply boundary.",
    ),
    page(
        "08_run_ng_modal", "08_run_ng_modal.png", "Run Details NG Modal", "Formal-run NG details overlay",
        "current/r2/S06-B0.png", "#/projects/:id/workspace", "flow",
        ["dimmed Flow workspace", "运行详情 modal", "run identity and status", "six real run metrics", "admission checks", "recent result", "run technical information", "diagnostics"],
        ["正式运行", "重新检查", "关闭运行详情", "运行技术信息", "诊断", "查看本次结果"], [], ["概览", "工程", "流程", "检测结果", "算子库"],
        ["new modal tabs", "rerun variants", "export", "approval workflow", "invented metrics", "new NG actions"],
        "Keep a clearly scoped modal over the same option's Flow context. Preserve the current NG identity, the six real metrics, readiness checks, result link, technical disclosure, diagnostics, recheck, rerun, and close path; do not redesign it as a full page.",
    ),
    page(
        "09_results_investigation", "09_results_investigation.png", "Results Investigation", "NG-first inspection evidence review",
        "current/r2/S07-B0.png", "#/results?...", "operations",
        ["view switcher", "result filter/context bar", "result list", "pagination", "selected result detail", "run summary", "image evidence", "diagnostics, defects, and traceability"],
        [
            "返回工作区", "导出完整结果", "刷新检测结果", "数据来源", "本机工程", "执行 / 判定结果", "分页大小", "更多筛选",
            "查看详情", "分页", "与基线对比", "与当前结果对比", "查找失败前成功", "对比选中结果", "evidence export when present",
        ], ["态势总览", "调查详情"],
        ["检测结果"],
        ["invented table columns", "bulk actions", "analytics beyond the existing situation summary", "new comparison mode", "new defect workflow"],
        "Keep 调查详情 active as in CURRENT while retaining the real 态势总览 entry. Preserve list/detail investigation, filters, export/refresh, pagination, OK/NG semantics, evidence, diagnostics, defects, traceability, and verified comparison actions without adding analytics.",
    ),
    page(
        "10_stations_list", "10_stations_list.png", "Stations List", "Station fleet monitoring and scanable status",
        "current/r2/S08-B0.png", "#/stations", "operations",
        ["station monitor header", "read-only and realtime recovery state", "view switcher", "overview summary/statistics entry", "investigation filters", "station table"],
        ["刷新工作站监控", "搜索工作站", "搜索", "连接状态", "运行状态", "station detail link"], ["全站概览", "异常调查"], ["工作站监控"],
        ["create station", "delete station", "edit station", "firmware", "analytics beyond the existing overview summary/statistics", "uncontracted settings", "same-page detail inspector", "same-page live data rail", "same-page result band"],
        "Keep 异常调查 active as in CURRENT with its search, connection/run filters, and station table. Retain the real 全站概览 entry for its existing summary/statistics, but do not render a detail inspector, live-data rail, or result band on this list viewport.",
    ),
    page(
        "11_station_detail", "11_station_detail.png", "Station Detail", "Selected station operational detail",
        "current/r2/S08-B2.png", "#/stations/:id", "operations",
        ["station identity and read-only state", "realtime recovery warning", "status overview", "recent results", "production trace chain", "health snapshot"],
        ["返回工作站列表", "查看结果", "明细数量", "刷新工作站详情", "追溯"], [], ["工作站"],
        ["station edit form", "delete", "create", "firmware controls", "administrator command panel", "Ping", "重载", "停止运行", "部署正式包", "下发测试包", "new tabs", "new device capabilities"],
        "Preserve the CURRENT Engineer/read-only state: identity badges, recovery warning, overview, recent-result trace link, production trace, health snapshot, result count, refresh, and return routes. Do not surface administrator-only commands in this reference state.",
    ),
    page(
        "12_inspection", "12_inspection.png", "Inspection", "Continuous inspection recovery, readiness, metrics, and latest result",
        "current/r2/S09-B0.png", "#/projects/:id/inspection", "operations",
        [
            "exact current application shell and unavailable local-service status",
            "continuous-inspection header and project revision context",
            "realtime recovery status and run actions",
            "six inspection metrics",
            "run and device summary",
            "pre-run checks 6/7",
            "single recent result with diagnostics",
        ],
        [
            "查看检测结果", "启动连续检测", "停止", "核对状态",
            "相机（顶视相机 · 已连接）", "运行技术信息", "诊断", "查看结果",
            "外观 浅色 · 紧凑", "更多", "fixture-engineer / 工程师",
        ], [],
        ["概览", "工程", "连续检测", "检测结果", "算子库"],
        [
            "run mode selector", "single-run control", "acquisition trigger selector",
            "PLC trigger selector", "central evidence image", "flow nodes or minimap",
            "expanded recent-results table", "cycle statistics card", "camera configuration",
            "PLC configuration", "manual upload", "new execution authority", "new analysis tabs",
            "desktop window controls", "settings gear", "alternate local-service state",
            "invented metric values or extra business data",
        ],
        "Preserve the exact CURRENT shell: 概览, 工程, 连续检测, 检测结果, 算子库; 本地服务不可用; 外观 浅色 · 紧凑; 更多; fixture-engineer / 工程师. Preserve the exact CURRENT three-column lower layout. Keep the realtime-recovery row with 启动连续检测, 停止, and 核对状态; the six values 总数 1, 判定 OK / NG 1 / 0, 有效判定良率 100.0%, 判定覆盖率 100.0%, 平均节拍 18 ms, and 执行失败 0; the left 运行与设备 camera summary; the middle 运行前检查 6/7 list; and the right single 近期结果 row with OK, 10:00:02, 18 ms, 0 缺陷, 诊断, and 查看结果. Do not add an evidence canvas, mode selector, trigger selector, extra results, cycle-statistics module, desktop window controls, or alternate service state.",
    ),
    page(
        "13_ai_workspace", "13_ai_workspace.png", "AI Workspace", "AI clarification, build, validation, handoff, and recovery workbench",
        "current/r2/S11-B0.png", "#/ai?sessionId=...", "ai",
        ["exact current application shell", "AI workbench header", "unbound-project status and session version 8", "candidate readiness, pending count, next step and actions", "application preview and local-draft notice", "plan/build summary", "candidate diff", "validation/run-rehearsal/handoff gate", "technical identity disclosure"],
        ["本地服务在线", "外观 浅色·紧凑", "更多", "f06-engineer / 工程师", "交接到工作区审核", "重新校验", "unlabeled history clock icon", "unlabeled diagnostics waveform icon", "查看技术身份"],
        [], ["概览", "工程", "检测结果", "算子库", "AI 工程"],
        ["绑定工程 button", "绑定至工程 button", "application-preview canvas", "application-preview toolbar", "session-version dropdown", "资产库 navigation", "电子库 navigation", "desktop window controls", "chatbot composer", "prompt gallery", "new AI tools", "token usage", "model marketplace", "magic actions", "second write channel", "interactive candidate node canvas", "node dropdowns"],
        "Preserve one full-width work surface with no new sidebar or empty right pane. Keep the exact app navigation labels 概览, 工程, 检测结果, 算子库, AI 工程 and the current service/appearance/more/account context. Keep the two body-header history and diagnostics entries as unlabeled icons in their existing top-right position. 未绑定工程 is a passive status badge, never a button or action. 会话版本 8 is plain static text, never a dropdown. Keep the exact two readiness actions 交接到工作区审核 and 重新校验. Application preview remains the current text-only draft notice, never an image, canvas, mini-workspace, or toolbar. Candidate difference remains a simple read-only text projection, never node cards or a second canvas. Preserve the real plan/build, validation, rehearsal, and handoff evidence without making a chat SaaS.",
    ),
    page(
        "14_ai_failure_recovery", "14_ai_failure_recovery.png", "AI Failure Recovery", "AI blocked/failed recovery state",
        "current/r2/S11-EXCEPTION.png", "#/ai?sessionId=...", "ai",
        ["same AI workbench", "blocked or failed stage", "server replay/recovery state", "warnings", "history/diagnostic evidence"],
        ["existing recovery and history actions only", "核对删除结果 when present"],
        ["会话", "运行 only where already present"], ["AI 工程工作台"],
        ["blind retry", "new write action", "new recovery mode", "auto-fix", "new AI capability", "invented terminal result"],
        "Keep the AI workspace geometry and express the real blocked/failed/recovery state. Make 'do not duplicate writes' and server authority visually credible.",
    ),
    page(
        "15_operator_catalog", "15_operator_catalog.png", "Operator Catalog", "Read-only operator discovery and integrity",
        "current/r2/S12-B0.png", "#/operators", "shell",
        ["page header", "read-only badge", "dense filter toolbar", "operator table", "pagination"],
        ["刷新", "搜索", "分类", "生命周期", "可见范围", "端口", "参数", "清除筛选", "查看详情"], [],
        ["算子库", "operator detail route"],
        ["install operator", "edit operator", "delete operator", "run operator", "marketplace", "new catalog statistics"],
        "Keep the read-only catalog identity and real columns: 算子, 分类, 生命周期, 端口, 参数, 版本, 操作. Preserve dense filtering and pagination.",
    ),
    page(
        "16_system_settings", "16_system_settings.png", "System Settings", "General system configuration and Settings shell",
        "current/r2/S10-B0.png", "#/settings", "settings",
        ["exact current application shell", "Settings page header and description", "settings-loaded and current-account status", "left grouped Settings navigation", "single General settings card", "save-state footer"],
        ["刷新基础设置", "软件标题", "产品主题", "自动启动 (readonly)", "放弃修改", "保存常规设置"],
        [],
        ["总览", "常规", "存储", "运行保护", "安全与用户", "PLC", "TCP", "相机", "工作站通信", "AI 模型", "数据库维护"],
        ["password fields on General", "修改密码 on General", "horizontal duplicate settings tabs", "desktop window controls", "enable readonly auto-start", "new settings category", "new security feature", "system telemetry", "new save path", "second save action"],
        "Preserve the current two-column Settings page exactly: one grouped left navigation and one General card. Do not duplicate navigation horizontally. The visible card contains only 软件标题, 产品主题, and readonly 自动启动, followed by 当前分组已保存, 放弃修改, and 保存常规设置. Password/security fields belong elsewhere and must not appear in this viewport.",
    ),
    page(
        "17_camera_settings", "17_camera_settings.png", "Camera Settings", "Camera binding, acquisition, preview, and trigger input",
        "current/settings/settings-camera-b0-1920x1080-light-compact.png", "#/settings", "settings",
        ["Settings group navigation", "camera discovery", "camera binding", "selected-camera acquisition fields", "trigger input", "debug preview", "resource diagnostics"],
        [
            "刷新", "全部厂商", "华睿", "海康威视", "保存相机绑定", "显示名称", "活动相机", "启用绑定",
            "曝光时间(us)", "增益(dB)", "像素格式", "目标帧率", "触发模式", "硬件触发源", "软件触发源",
            "Enter 光电", "串口光电", "识别输入设备", "测试串口光电", "采集单帧", "开始/停止连续预览", "预览资源诊断",
        ],
        [],
        ["总览", "常规", "存储", "运行保护", "安全与用户", "PLC", "TCP", "相机", "工作站通信", "AI 模型", "数据库维护"],
        ["new camera vendor", "camera SDK status", "firmware", "new calibration control or wizard", "hardware facts", "new acquisition mode", "automatic binding"],
        "Keep the real discovery, binding, acquisition, trigger-input, single-frame/continuous-preview, and diagnostic controls. Settings only explains that calibration is completed in the project-workspace N-point calibration operator; do not render a Settings calibration action or imply real hardware connectivity.",
    ),
    page(
        "18_plc_settings", "18_plc_settings.png", "PLC Settings", "PLC connection and address mapping",
        "current/settings/settings-plc-b0-1920x1080-light-compact.png", "#/settings", "settings",
        ["Settings navigation", "PLC connection card", "protocol-specific fields", "validation summary", "address mapping table", "save footer"],
        [
            "协议 S7/MC/FINS", "心跳间隔", "PLC IP", "端口", "CPU 类型", "Rack", "Slot", "测试连接",
            "保存协议设置", "添加映射", "变量名", "地址", "数据类型", "说明", "可写", "删除", "保存当前映射",
        ],
        [],
        ["总览", "常规", "存储", "运行保护", "安全与用户", "PLC", "TCP", "相机", "工作站通信", "AI 模型", "数据库维护"],
        ["new PLC protocol", "live hardware claim", "PLC program editor", "new mapping columns", "new diagnostics module"],
        "Keep the real connection fields and exact mapping columns 变量名, 地址, 数据类型, 说明, 可写, 操作. Preserve the protocolMismatch dependency and the two separate save boundaries: 保存协议设置 and 保存当前映射. There is no cancel action.",
    ),
    page(
        "19_tcp_settings", "19_tcp_settings.png", "TCP Settings", "TCP profiles, connection control, send/receive diagnostics",
        "current/settings/settings-tcp-b0-dark-1920x1080-dark-compact.png", "#/settings", "settings",
        ["Settings navigation", "profile list", "profile editor", "connection controls", "send/receive debugger", "traffic log"],
        [
            "添加客户端配置", "添加服务端配置", "保存连接配置", "删除", "刷新运行状态",
            "客户端连接/断开 or 服务端启动/停止", "文本/HEX", "发送", "等待响应", "清空日志",
        ],
        [],
        ["总览", "常规", "存储", "运行保护", "安全与用户", "PLC", "TCP", "相机", "工作站通信", "AI 模型", "数据库维护"],
        ["new transport protocol", "packet analyzer", "live network claim", "new profile mode", "new log columns"],
        "Keep the real Client/Server profile list and fields, saved-profile guards, runtime controls, text/HEX sender, latest response, and exact log columns 时间, 方向, 字节, 文本, HEX, 端点.",
    ),
    page(
        "20_station_communication", "20_station_communication.png", "Station Communication", "Studio-to-Station communication configuration",
        "current/settings/settings-station-1920x1080-dark-compact.png", "#/settings", "settings",
        ["Settings navigation", "communication mode", "listener/host configuration", "shared-token operation", "effective-state and restart feedback", "diagnostics/endpoints", "save footer"],
        [
            "刷新", "Disabled", "LocalLoopback", "LanController", "Studio 端口", "LAN 主机", "本机 Station 同步",
            "保留现有 Token", "替换 Token", "输入新 Token", "重新生成 Token", "放弃修改", "保存通信配置",
        ],
        [],
        ["总览", "常规", "存储", "运行保护", "安全与用户", "PLC", "TCP", "相机", "工作站通信", "AI 模型", "数据库维护"],
        ["new station transport", "remote station control", "live connectivity claim", "raw token display or copy", "new token authority", "new sync mode", "snippets", "Station monitor"],
        "Keep the real mode, port/host, local-sync, preserve/replace/regenerate token boundary, discard/save actions, effective-state feedback, restart notice, and diagnostics. There are no snippet, raw-token display/copy, Station-monitor, or generic cancel controls; do not imply a live Station connection.",
    ),
    page(
        "21_ai_model_settings", "21_ai_model_settings.png", "AI Model Settings", "Provider/model configuration and availability",
        "current/settings/settings-ai-model-1920x1080-light-compact.png", "#/settings", "settings",
        ["Settings navigation", "model catalog and visible status", "selected model identity/provider", "endpoint and credential controls", "connection test", "advanced connection and scheduling disclosure", "inference support", "save footer"],
        [
            "刷新模型目录", "新建模型配置", "选择模型", "设为活动模型", "设为规划器模型", "设为影子模型", "删除模型配置",
            "显示名称", "服务商", "模型名", "启用", "服务地址", "API 密钥操作", "测试连接", "读取推理支持", "放弃修改", "保存模型配置",
        ],
        [],
        ["总览", "常规", "存储", "运行保护", "安全与用户", "PLC", "TCP", "相机", "工作站通信", "AI 模型", "数据库维护"],
        ["model quality analytics", "token/cost dashboard", "model marketplace", "prompt presets", "plaintext secret", "new provider ability"],
        "Keep the real model catalog refresh/new/select/activate/planner/shadow/delete commands and their current visible statuses, selected-model form, sensitive-value boundary, test result, inference-support controls, discard, and single save path. 高级连接与调度设置 is a details disclosure, not a tab; AI 模型 is Settings navigation, not a tab.",
    ),
    page(
        "22_diagnostics", "22_diagnostics.png", "Diagnostics", "Local services, owner projection, and technical diagnostics",
        "current/r2/S13-B0.png", "#/diagnostics", "shell",
        ["exact current application shell and user context", "page header", "service/session/desktop-host status", "version and environment summary", "technical diagnostics", "copy feedback"],
        ["复制诊断信息", "刷新", "技术诊断", "外观 浅色 · 紧凑", "更多", "现场工程师 / 工程师"],
        [], ["概览", "工程", "检测结果", "算子库"],
        [
            "execution controls", "service restart", "token display", "API key display",
            "new health telemetry", "new diagnostics actions", "breadcrumb navigation",
            "diagnostics title icon tile", "desktop window controls", "alternate shell controls",
        ],
        "Preserve the exact CURRENT shell with 概览, 工程, 检测结果, 算子库; 本地服务在线; 外观 浅色 · 紧凑; 更多; and 现场工程师 / 工程师. Keep the plain 运行诊断 title without a breadcrumb row or standalone title icon. Keep the three real status groups, environment summary, expandable 技术诊断 projection, and safe copy boundary. This is read-only diagnostics, not a control center.",
    ),
    page(
        "23_about", "23_about.png", "About", "Product, host, backend, license, and support identity",
        "current/r2/S13-B2.png", "#/about", "shell",
        ["About header", "product and version", "license and support", "product composition note"],
        [], [], ["更多", "关于"],
        ["invented version", "invented license state", "update action", "runtime controls", "new support service", "marketing hero"],
        "Keep the compact identity page and the real distinction between Studio configuration and Runtime/Station execution. Do not turn About into settings or marketing.",
    ),
    page(
        "24_forbidden", "24_forbidden.png", "Forbidden", "Authority boundary and recovery route",
        "current/r2/S10-EXCEPTION.png", "#/forbidden", "settings",
        ["AuthShell", "permission warning", "existing recovery guidance"],
        ["返回工程库"], [], [],
        ["retry", "request access workflow", "role-change control or workflow", "login control", "permission editor", "support chat", "new navigation"],
        "Keep a restrained authority-boundary screen with the existing explanation and the single return-to-projects route. Do not create a remediation workflow.",
    ),
]


_REMAPPING_CACHE: dict[str, dict[str, Any]] | None = None


def load_remappings() -> dict[str, dict[str, Any]]:
    global _REMAPPING_CACHE
    if _REMAPPING_CACHE is not None:
        return _REMAPPING_CACHE
    payload = legacy.read_json(REMAPPING_PATH)
    if payload.get("schema_version") != "clearvision-ui-functional-remapping.v1":
        raise ValueError("Unsupported functional remapping schema")
    screens = payload.get("screens")
    if not isinstance(screens, list):
        raise ValueError("functional_remapping.json screens must be an array")
    mapping = {str(item.get("screen_id")): item for item in screens if isinstance(item, dict)}
    expected = {item["screen_id"] for item in PAGES}
    if set(mapping) != expected:
        raise ValueError(
            f"Functional remapping coverage mismatch; missing={sorted(expected - set(mapping))}, "
            f"extra={sorted(set(mapping) - expected)}"
        )
    for screen_id, item in mapping.items():
        for field in ("current_function", "D_location", "E_location", "must_not"):
            if not isinstance(item.get(field), str) or not item[field].strip():
                raise ValueError(f"Functional remapping {screen_id}.{field} is required")
    _REMAPPING_CACHE = mapping
    return mapping


def remapping(option: str, item: dict[str, Any]) -> dict[str, Any]:
    if option not in OPTIONS:
        raise ValueError(f"Unsupported remapping option: {option}")
    return load_remappings()[item["screen_id"]]


def write_functional_mapping_docs() -> None:
    mapping = load_remappings()
    for option in OPTIONS:
        rows = []
        for page_def in PAGES:
            item = mapping[page_def["screen_id"]]
            current_function = item["current_function"].replace("|", "\\|")
            target_location = item[f"{option}_location"].replace("|", "\\|")
            must_not = item["must_not"].replace("|", "\\|")
            rows.append(
                f"| `{page_def['screen_id']}` | {page_def['page_name']} | {current_function} | "
                f"{target_location} | {must_not} |"
            )
        document = f"""# Option {option} Functional Mapping

CURRENT screenshots and current ClearVision code are the functional authority. This mapping changes visual placement only. Generated text and data are never product truth.

| Screen | Page | CURRENT function | Option {option} location | Explicit exclusion |
| --- | --- | --- | --- | --- |
{"\n".join(rows)}

Coverage: `{len(rows)}/{len(PAGES)}` frozen screens. Functional target: `0 additions / 0 omissions`.
"""
        (option_root(option) / "functional_mapping.md").write_text(document, encoding="utf-8")


def architecture_guide(option: str) -> Path:
    target = option_root(option) / "references" / f"{option.lower()}_flow_architecture_blueprint.png"
    target.parent.mkdir(parents=True, exist_ok=True)
    width, height = 1200, 900
    background, frame, canvas_color, panel, line, accent = (
        ("#eef1f4", "#ffffff", "#fafbfc", "#ffffff", "#cbd2d9", "#166f9f")
        if option == "D"
        else ("#dfe3e8", "#f8f9fa", "#f4f5f7", "#ffffff", "#b8c0c8", "#b6453c")
    )
    image = Image.new("RGB", (width, height), background)
    draw = ImageDraw.Draw(image)
    font = legacy.load_font(22)
    small = legacy.load_font(17)
    draw.rounded_rectangle((20, 20, width - 20, height - 20), radius=16, fill=frame, outline=line, width=2)
    if option == "D":
        draw.rounded_rectangle((78, 42, 900, 98), radius=10, fill=panel, outline=line, width=2)
        draw.text((98, 59), "ONE QUIET COMMAND BAR", fill="#26313a", font=small)
        draw.rounded_rectangle((40, 120, 92, 842), radius=10, fill="#20262d", outline="#20262d")
        draw.text((48, 146), "N\nA\nV", fill="#f3f5f7", font=small, spacing=10)
        draw.rounded_rectangle((110, 120, 900, 842), radius=10, fill=canvas_color, outline=line, width=2)
        draw.text((370, 445), "PRIMARY CANVAS 65-75%", fill="#46525d", font=font)
        draw.rounded_rectangle((138, 150, 390, 430), radius=10, fill=panel, outline=accent, width=2)
        draw.text((164, 178), "SEARCH-FIRST\nOPERATOR PALETTE\nON DEMAND", fill="#26313a", font=small, spacing=9)
        draw.rounded_rectangle((925, 120, 1160, 570), radius=12, fill=panel, outline=line, width=2)
        draw.text((950, 156), "CONTEXTUAL\nINSPECTOR", fill="#26313a", font=small, spacing=9)
        draw.rounded_rectangle((925, 600, 1160, 842), radius=12, fill="#141a20", outline=line, width=2)
        draw.text((950, 635), "PREVIEW / ROI /\nRESULT ON DEMAND", fill="#f4f6f8", font=small, spacing=9)
        draw.text((112, 805), "STRUCTURE ONLY - NEVER COPY LABELS OR FUNCTIONS", fill="#697580", font=small)
    else:
        width, height = 2048, 1152
        image = Image.new("RGB", (width, height), "#e2e6ea")
        draw = ImageDraw.Draw(image)
        current_path = legacy.root_path("current/r2/S04-B0.png", must_exist=True)
        with Image.open(current_path) as current_image:
            current = current_image.convert("RGB")

        # Reuse CURRENT pixels as factual material, but remap them into the target geometry.
        top = current.crop((0, 0, 1920, 122)).resize(
            (2048, 146), Image.Resampling.LANCZOS
        )
        canvas_body = current.crop((356, 166, 1572, 1035)).resize(
            (1524, 936), Image.Resampling.LANCZOS
        )
        canvas_toolbar = current.crop((363, 131, 1564, 165)).resize(
            (780, 42), Image.Resampling.LANCZOS
        )
        inspector = current.crop((68, 122, 347, 1035)).resize(
            (440, 936), Image.Resampling.LANCZOS
        )

        image.paste(top, (0, 0))
        image.paste(canvas_body, (84, 146))
        image.paste(inspector, (1608, 146))
        draw = ImageDraw.Draw(image)

        draw.rectangle((0, 146, 84, 1136), fill="#20262d")
        rail_font = legacy.load_font(18)
        draw.ellipse((22, 174, 62, 214), outline="#f3f5f7", width=2)
        draw.line((42, 184, 42, 204), fill="#f3f5f7", width=2)
        draw.line((32, 194, 52, 194), fill="#f3f5f7", width=2)
        draw.text((20, 224), "ADD", fill="#f3f5f7", font=rail_font)
        draw.ellipse((25, 282, 53, 310), outline="#f3f5f7", width=2)
        draw.line((51, 306, 64, 319), fill="#f3f5f7", width=2)
        draw.text((8, 328), "SEARCH", fill="#f3f5f7", font=rail_font)

        draw.rounded_rectangle(
            (398, 168, 1178, 210), radius=7, fill="#ffffff", outline="#c5ccd3", width=2
        )
        image.paste(canvas_toolbar, (398, 168))
        draw.rectangle((84, 1082, 2048, 1136), fill="#f7f8f9", outline="#b8c0c8", width=2)
        strip_font = legacy.load_font(20)
        draw.text((118, 1097), "PREVIEW / ROI / RESULT COLLAPSED", fill="#26313a", font=strip_font)
        draw.text((1530, 1097), "CURRENT TASK PANEL ONLY", fill="#b6453c", font=strip_font)
        draw.rectangle((0, 1136, 2048, 1152), fill="#20262d")
        draw.line((84, 146, 84, 1136), fill="#b8c0c8", width=2)
        draw.line((1608, 146, 1608, 1082), fill="#b8c0c8", width=2)
    image.save(target, format="PNG", optimize=True)
    return target


def settings_architecture_guide(option: str) -> Path:
    if option != "E":
        raise ValueError("The premium continuous-settings blueprint is Option E only")
    target = option_root(option) / "references" / "e_settings_architecture_blueprint.png"
    target.parent.mkdir(parents=True, exist_ok=True)
    width, height = 1200, 900
    image = Image.new("RGB", (width, height), "#dfe3e8")
    draw = ImageDraw.Draw(image)
    title_font = legacy.load_font(22)
    small = legacy.load_font(17)
    line = "#b8c0c8"
    draw.rounded_rectangle((20, 20, 1180, 880), radius=16, fill="#f8f9fa", outline=line, width=2)
    draw.rounded_rectangle((54, 42, 1146, 104), radius=10, fill="#ffffff", outline=line, width=2)
    draw.text((82, 62), "COMPACT PRODUCT + SETTINGS COMMAND LAYER", fill="#26313a", font=small)
    draw.rounded_rectangle((54, 124, 246, 842), radius=8, fill="#f1f3f5", outline=line, width=2)
    draw.text((82, 156), "GROUPED SOURCE LIST\n18%", fill="#26313a", font=small, spacing=10)
    draw.rounded_rectangle((264, 124, 1146, 842), radius=8, fill="#ffffff", outline=line, width=2)
    draw.text((294, 154), "CONTINUOUS SETTINGS WORKBENCH - NO LARGE CARD", fill="#26313a", font=title_font)
    draw.line((294, 220, 1116, 220), fill=line, width=2)
    draw.text((294, 242), "COMPACT TITLE / STATUS / REFRESH", fill="#697580", font=small)
    draw.line((294, 318, 1116, 318), fill=line, width=2)
    draw.text((294, 344), "ALIGNED FIELD GROUP 1", fill="#26313a", font=small)
    draw.line((294, 440, 1116, 440), fill=line, width=2)
    draw.text((294, 466), "ALIGNED FIELD GROUP 2 / READ-ONLY STATE", fill="#26313a", font=small)
    draw.line((294, 650, 1116, 650), fill=line, width=2)
    draw.text((294, 680), "ONE RESTRAINED SAVE BOUNDARY", fill="#b6453c", font=small)
    draw.rounded_rectangle((886, 748, 1116, 806), radius=6, fill="#166f9f", outline="#166f9f")
    draw.text((930, 766), "PRIMARY SAVE", fill="#ffffff", font=small)
    draw.text((294, 816), "STRUCTURE ONLY - NEVER COPY LABELS OR FUNCTIONS", fill="#697580", font=small)
    image.save(target, format="PNG", optimize=True)
    return target


def flow_state_architecture_guide(screen_id: str) -> Path:
    specifications = {
        "06_flow_validation_error": {
            "filename": "d_06_flow_validation_architecture_blueprint.png",
            "boundaries": (68, 1502, 1832),
            "labels": (
                "DISCOVERY\n68 px",
                "DOTTED FLOW CANVAS\n1434 px / 70%\nMUST DOMINATE",
                "CONTEXTUAL INSPECTOR\n330 px",
                "PREVIEW / RESULT EDGE\n216 px",
            ),
        },
        "07_flow_preview_roi": {
            "filename": "d_07_flow_roi_architecture_blueprint.png",
            "boundaries": (68, 1320, 1320),
            "labels": (
                "DISCOVERY\n56-68 px\nON DEMAND",
                "DOTTED FLOW CANVAS\nRIGHT EDGE x=1320\n59-61% VISUAL SHARE",
                "",
                "PREVIEW / ROI / RESULT\n728 px / 35.5%\nREAL CURRENT RASTER",
            ),
        },
    }
    if screen_id not in specifications:
        raise ValueError(f"Unsupported flow-state architecture guide: {screen_id}")
    specification = specifications[screen_id]
    target = option_root("D") / "references" / specification["filename"]
    target.parent.mkdir(parents=True, exist_ok=True)

    width, height = 2048, 1152
    header_bottom, status_top = 160, 1100
    discovery_right, canvas_right, inspector_right = specification["boundaries"]
    image = Image.new("RGB", (width, height), "#eef1f4")
    draw = ImageDraw.Draw(image)
    title_font = legacy.load_font(28)
    label_font = legacy.load_font(22)
    compact_font = legacy.load_font(15)

    draw.rectangle((0, 0, width - 1, header_bottom - 1), fill="#f8f9fa", outline="#b8c2cc", width=2)
    draw.text((28, 28), "CLEARVISION FLOW STATE - HARD GEOMETRY BLUEPRINT", fill="#26313a", font=title_font)
    draw.text(
        (28, 82),
        "STRUCTURE ONLY / CURRENT CONTENT ONLY / ALIGN MAJOR EDGES EXACTLY",
        fill="#b6453c",
        font=label_font,
    )
    draw.rectangle((0, header_bottom, discovery_right - 1, status_top - 1), fill="#20262d")
    draw.rectangle((discovery_right, header_bottom, canvas_right - 1, status_top - 1), fill="#fafbfc")
    if inspector_right > canvas_right:
        draw.rectangle((canvas_right, header_bottom, inspector_right - 1, status_top - 1), fill="#ffffff")
    draw.rectangle((inspector_right, header_bottom, width - 1, status_top - 1), fill="#f7f9fb")
    draw.rectangle((0, status_top, width - 1, height - 1), fill="#f8f9fa", outline="#b8c2cc", width=2)

    for boundary in sorted({discovery_right, canvas_right, inspector_right}):
        draw.line((boundary, header_bottom, boundary, status_top), fill="#b6453c", width=5)
        draw.text((boundary + 8, header_bottom + 12), f"x={boundary}", fill="#b6453c", font=compact_font)

    discovery_label, canvas_label, inspector_label, preview_label = specification["labels"]
    draw.multiline_text((8, 245), discovery_label, fill="#f3f5f7", font=compact_font, spacing=7)
    draw.multiline_text(
        (discovery_right + 80, 540), canvas_label, fill="#26313a", font=title_font, spacing=10
    )
    if inspector_label:
        draw.multiline_text(
            (canvas_right + 18, 500), inspector_label, fill="#26313a", font=compact_font, spacing=8
        )
    draw.multiline_text(
        (inspector_right + 18, 500), preview_label, fill="#26313a", font=compact_font, spacing=8
    )
    draw.text(
        (28, status_top + 14),
        "PRESERVE THE VERIFIED STATUS STRIP; DO NOT ADD A CAPABILITY RAIL OR CONTROL",
        fill="#46525d",
        font=compact_font,
    )
    image.save(target, format="PNG", optimize=True)
    return target


def expected_architecture_references(option: str, item: dict[str, Any]) -> list[str]:
    screen_id = item["screen_id"]
    if option == "D" and screen_id == "06_flow_validation_error":
        return ["option_D/references/d_06_flow_validation_architecture_blueprint.png"]
    if option == "D" and screen_id == "07_flow_preview_roi":
        return ["option_D/references/d_07_flow_roi_architecture_blueprint.png"]
    if screen_id == MASTER_SCREENS["flow"]:
        return [f"option_{option}/references/{option.lower()}_flow_architecture_blueprint.png"]
    if option == "E" and screen_id == MASTER_SCREENS["settings"]:
        return ["option_E/references/e_settings_architecture_blueprint.png"]
    return []


def architecture_references(option: str, item: dict[str, Any]) -> list[str]:
    expected = expected_architecture_references(option, item)
    if not expected:
        return []
    # Official third-party screenshots remain on the human research board only.
    # The model receives the ClearVision-only structural translation so labels
    # and product capabilities from those screenshots cannot leak into output.
    if option == "D" and item["screen_id"] in {
        "06_flow_validation_error",
        "07_flow_preview_roi",
    }:
        generated = legacy.rel(flow_state_architecture_guide(item["screen_id"]))
    elif option == "E" and item["screen_id"] == MASTER_SCREENS["settings"]:
        generated = legacy.rel(settings_architecture_guide(option))
    else:
        generated = legacy.rel(architecture_guide(option))
    if generated != expected[0]:
        raise RuntimeError(f"Architecture reference path drifted for option {option}")
    return expected


def utc_now() -> str:
    return legacy.utc_now()


def option_root(option: str) -> Path:
    if option not in OPTIONS:
        raise ValueError(f"Unsupported option: {option}")
    path = (ROOT / f"option_{option}").resolve()
    if not path.is_relative_to(ROOT.resolve()):
        raise ValueError(f"Option path escapes _visual_master: {path}")
    return path


def option_image(option: str, directory: str, filename: str, *, must_exist: bool = False) -> Path:
    if directory not in {"masters", "screens", "iterations"}:
        raise ValueError(f"Unsupported option image directory: {directory}")
    return legacy.safe_named_path(option_root(option) / directory, filename, must_exist=must_exist)


def entry_id(option: str, screen_id: str) -> str:
    return f"{option}_{screen_id}"


def master_path(option: str, family: str) -> str:
    screen_id = MASTER_SCREENS[family]
    page_def = next(item for item in PAGES if item["screen_id"] == screen_id)
    return f"option_{option}/masters/{page_def['filename']}"


def master_references(option: str, item: dict[str, Any]) -> list[str]:
    screen_id = item["screen_id"]
    if screen_id == MASTER_SCREENS["flow"]:
        return []
    if screen_id == MASTER_SCREENS["ai"]:
        return [master_path(option, "flow")]
    if screen_id == MASTER_SCREENS["settings"]:
        return [master_path(option, "flow"), master_path(option, "ai")]
    if item["family"] == "flow":
        return [master_path(option, "flow")]
    if item["family"] == "ai":
        return [master_path(option, "ai")]
    if item["family"] == "settings":
        return [master_path(option, "settings")]
    if item["family"] == "operations":
        return [master_path(option, "flow"), master_path(option, "ai")]
    return [master_path(option, "settings")]


def depends_on(option: str, item: dict[str, Any]) -> list[str]:
    refs = master_references(option, item)
    dependencies: list[str] = []
    for value in refs:
        filename = Path(value).name
        master_page = next(page_def for page_def in PAGES if page_def["filename"] == filename)
        dependencies.append(entry_id(option, master_page["screen_id"]))
    return dependencies


def joined(values: list[str]) -> str:
    return "; ".join(values) if values else "none beyond what is visible in CURRENT_REFERENCE"


def make_prompt(option: str, item: dict[str, Any], constitution_text: str) -> str:
    design = OPTION_DEFINITIONS[option]
    mapping = remapping(option, item)
    output_dimensions = expected_output_dimensions(option)
    output_resolution_block = (
        "OUTPUT RESOLUTION - BINDING\n"
        f"Request exact gpt-image-2 {expected_image_size(option)} output at "
        f"{output_dimensions['width']}x{output_dimensions['height']}. Compose on a 1920x1080 "
        "logical desktop grid rendered at 2x density, so the 4K canvas adds crispness without "
        "shrinking type, controls, icons, hit targets, or spacing.\n\n"
    )
    layout_clarification = ""
    if option == "D":
        architecture_directive = """Treat the supplied ClearVision screenshot as a FUNCTIONAL INVENTORY, not as a layout template.
Preserve every real ClearVision capability visible or verified in the product, but DO NOT preserve the existing panel geometry.
Redesign the application as a modern canvas-first computer vision workflow engineering environment inspired by the interaction architecture of Roboflow Workflows.
For Flow screens, give the workflow canvas dominant screen real estate. Use compact lightweight operator nodes, thin connections, tiny explicit ports, subtle category accents, strong topology readability, a low-noise neutral canvas, search-first operator insertion, contextual properties, progressive disclosure, and task-dependent panels.
Avoid permanent large side panels unless the current task requires them. Avoid stacking every function around the canvas simultaneously.
The result must look structurally redesigned, not cosmetically reskinned.
Do not invent any capability, button, metric, node type, business data, AI function, device feature, workflow function, or Roboflow business feature that does not exist in ClearVision."""
    else:
        architecture_directive = """Treat the supplied ClearVision screenshot as a FUNCTIONAL INVENTORY, not as a layout template.
Preserve every real ClearVision capability visible or verified in the product, but DO NOT preserve the existing panel geometry.
Redesign the application as if an exceptional product-design team applied Apple-level typography hierarchy, whitespace discipline, controlled contrast, refined material, precise iconography, subtle purposeful depth, and quiet confidence to a high-information-density industrial engineering desktop application.
The result must be a fundamentally reorganized professional work instrument, not current ClearVision with a white background, larger spacing, or rounder corners.
Do not imitate an Apple website, macOS window chrome, Finder, System Settings, consumer cards, mobile controls, or decorative translucency.
Do not invent any capability, button, metric, node type, business data, AI function, device feature, or workflow function that does not exist in ClearVision."""
        if item["screen_id"] == "05_flow_editor":
            layout_clarification = """
E FLOW MASTER GEOMETRY CLARIFICATION
In the functional inventory, the phrases "operator discovery rail" and "Preview and result rail" name existing capabilities, not required permanent columns. In this selected-node target state, operator discovery must be closed to one slim Add/Search entry with no Recent, Favorite, category, or operator rows visible until invoked. Preview, ROI, and Result must be collapsed to the bottom mode strip. The only open task panel is a narrow selected-node Inspector occupying about 18-20% of the 16:9 frame. Put the verified canvas edit and zoom actions in a compact floating strip inside the canvas rather than another full-width toolbar. The continuous canvas must occupy 70-75% of the task area. Use one compact top command layer; low-frequency verified actions may use the existing overflow entry. This geometry is binding and overrides the CURRENT panel proportions while preserving every verified entry.

E FLOW MASTER FUNCTIONAL FIDELITY LOCK
Preserve these CURRENT facts exactly and do not replace them with plausible data: project "瓶盖检测 A", version 1.0.0, saved state; selected node "Inspector Source"; status "执行成功"; operator type 20; description "将灰度图像转换为二值图像。"; latest duration 9 ms; ports 0 input and 1 output; Source output "Binary" / "二值图" with type Image; Target input "Image". Resource binding is "FilePath" with the helper "延后到 Host file picker", an empty path field, and "选择文件". Keep the real common parameters Text, Count with value 0 and range 0-10, and Enabled. Operator type 20 has no dedicated workbench: show the quiet unavailable state and never add a workbench name, binding, configure action, or F11 Patch. Preserve "正式运行就绪", "状态已读取", and "运行检查通过". Keep 技术状态 collapsed without inventing a value. Do not invent or rename project, resource, operator, port, status, or parameter data.

The shell identity and utility cluster is also locked. At the top right preserve only "本地服务在线", the existing refresh icon, "更多", avatar "F", and user "f03-engineer". Do not add a language selector, help, notification bell, HQ-engineer, settings, or any other utility. Render the bottom label as "预览 / ROI / 结果（已折叠）" in Simplified Chinese. Use a green success/check treatment for passed validation; never pair a warning icon with passed text.
"""
        elif item["screen_id"] == "16_system_settings":
            layout_clarification = """
E SETTINGS MASTER ARCHITECTURE LOCK
The architecture blueprint is binding. Use one compact product-and-settings command layer, a grouped source list occupying about 18% of the frame, and one continuous General settings workbench filling the remaining width and useful height. The General workbench must NOT be enclosed in a large rounded card, floating panel, dashboard tile, or hero container. Do not leave a theatrical empty lower half. Integrate a compact title, exact loaded/account status, and the existing refresh action at the top of the workbench; align the two editable fields and the one read-only compatibility field on a strict form grid separated by quiet rules; anchor exactly one restrained save boundary to the bottom of that same continuous workbench. Keep page titles compact and product-scale. This geometry overrides the CURRENT card proportions while preserving every verified entry.

E SETTINGS MASTER FUNCTIONAL FIDELITY LOCK
Preserve the CURRENT shell and navigation facts exactly. The shell is ClearVision STUDIO with 概览, 工程, 检测结果, 算子库, 设置; the utility area contains 本地服务在线, 外观 浅色 · 紧凑, 更多, avatar F, fixture-settings, 管理员, and the existing account chevron. Do not add another Settings phase, language selector, help, notifications, desktop window controls, or another utility.

Preserve the grouped source list exactly: 设置分组; 可查看并管理全部分组; 总览; group label 基础设置; 常规; 存储; 运行保护; 安全与用户; group label 设备与通信; PLC; TCP; 相机; 工作站通信; group label 系统服务; AI 模型; 数据库维护. Do not rename, omit, duplicate, or invent a category.

Preserve the exact General content and state: page title 设置; description "管理基础配置、设备连接和系统维护。每个分组独立保存，未保存内容会在离开前提醒。"; status 设置已加载; 当前账户：管理员; action 刷新基础设置; section 常规设置; description "设置软件标题和默认主题。顶栏中的个人外观偏好不会被此处覆盖。"; 软件标题 value "ClearVision Browser" with helper "显示在软件窗口和产品标识附近。"; 产品主题 value "浅色" with helper "作为未设置个人外观偏好时的默认主题。"; 自动启动（兼容字段） value "未启用" in a visibly read-only field with helper "当前版本不使用此项，保持只读。"; footer status 当前分组已保存; actions 放弃修改 and 保存常规设置 shown in their existing clean-state disabled treatment. Do not enable auto-start or the clean-state actions. Do not add a password, security control, telemetry, second save action, or any other field.
"""
    return f"""Use case: ui-mockup
Asset type: one shippable-fidelity desktop product UI reference at 16:9
Primary request: Redesign the existing ClearVision {item['page_name']} screen as Option {option} - {design['name']}.

INPUT IMAGE ROLES
The left/largest region is CURRENT_REFERENCE and is the sole functional authority. Same-option Master Screens are visual-system references only. Architecture references are structure/style references only and may contain third-party labels or functions that must never be copied. Produce one screen, never reproduce the reference board.

ARCHITECTURE DIRECTIVE
{architecture_directive}

FUNCTIONAL AUTHORITY - ZERO ADDITIONS / ZERO OMISSIONS
Preserve product semantics and functional hierarchy, but redesign the visual presentation at a premium professional product level. Preserve the inventory and identity of verified navigation, tabs, buttons, fields, panels, table columns, and states. Existing elements may move, resize, regroup, collapse, progressively disclose, or be restyled, but no functional element may be added, removed, renamed, or reinterpreted. Every verified function needs a credible entry even when its panel is collapsed. If an element cannot be confirmed from CURRENT_REFERENCE or the supplied contract, omit it. Prefer omission over invented functional content.

REAL PAGE AND PURPOSE
Route/entry: {item['route']}
Purpose: {item['purpose']}
Current function: {mapping['current_function']}
Confirmed regions: {joined(item['regions'])}.
Confirmed controls/fields: {joined(item['controls'])}.
Confirmed tabs: {joined(item['tabs'])}.
Confirmed navigation: {joined(item['navigation'])}.
Do not infer any control from a familiar product pattern. The contract and CURRENT_REFERENCE define the complete functional set for this target.

FUNCTIONAL REMAPPING - TARGET LAYOUT AUTHORITY
{mapping[f'{option}_location']}
This is a placement and grouping change only. Preserve discoverability and 100% existing functional coverage. Keep boundaries clear, spacing systematic, controls implementable, information dense without crowding, and the current task surface dominant.{layout_clarification}

VISUAL CONSTITUTION - BINDING STYLE AND LAYOUT POLICY
{constitution_text.strip()}
The Visual Constitution governs presentation only. It never overrides CURRENT_REFERENCE, the verified functional inventory, or the zero-addition/zero-omission contract.

OPTION {option} VISUAL LANGUAGE
{design['language']} {design['surface_rule']} Use ClearVision cinnabar #b6453c only for identity and the active Product Shell/navigation/grouped-rail selection; use action blue #166f9f only for commands, focus, and selected canvas objects; use OK #16866f only for success/online, NG #d12f3f only for NG/error/destructive boundaries, and warning #9b6a13 only for warning/recovery. Never use blue as alternate navigation selection or red as normal object selection. Use familiar restrained icons, Windows-native typography, and no viewport-scaled or poster-size text.

TEXT AND DATA POLICY
Keep the interface in concise Simplified Chinese where CURRENT_REFERENCE uses Chinese. The generated image is a visual design reference, not the source of truth for product copy, workflow names, device facts, model names, addresses, numbers, or business data. Do not translate the product shell into English. When exact text is uncertain, preserve a quiet existing control shape instead of inventing authoritative-looking copy.

PROHIBITIONS
Page-specific exclusion: {mapping['must_not']}
Never add: {joined(item['forbidden'])}. Never add any plausible button, module, tab, navigation entry, status, workflow, AI ability, device ability, dashboard, statistic card, chart, shortcut, telemetry, or business data. No generic SaaS dashboard, card mosaic, nested cards, large pills, oversized hero title, glassmorphism, decorative gradient, glow, cyberpunk/game HUD, concept art, poster layout, fictional device, invented logo, watermark, third-party brand, or impractical geometry.

{output_resolution_block}OUTPUT
Return one single full-screen ClearVision desktop UI only. Do not return a collage, reference board, before/after comparison, presentation slide, device mockup, or explanatory annotation. The result must be precise enough for later pixel-level frontend reconstruction while remaining subordinate to CURRENT_REFERENCE for all functionality and copy.
"""


def expected_top_contract() -> dict[str, Any]:
    return {
        "schema_version": SCHEMA_VERSION,
        "model": "gpt-image-2",
        "model_fallback_allowed": False,
        "screen_count": len(PAGES),
        "option_count": len(OPTIONS),
        "entry_count": len(PAGES) * len(OPTIONS),
        "text_policy": "preserve-semantic-no-copy",
        "functional_authority": "current screenshot and current ClearVision code",
        "functional_gate": "Only functional_audit.status == passed may enter generation.",
        "reference_board_policy": (
            "CURRENT_REFERENCE is the functional inventory; D/E structural references and same-option "
            "Masters occupy a substantial but explicitly non-functional style region."
        ),
        "options": {
            option: {
                "name": OPTION_DEFINITIONS[option]["name"],
                "constitution": OPTION_DEFINITIONS[option]["constitution"],
                "master_chain": [
                    entry_id(option, MASTER_SCREENS["flow"]),
                    entry_id(option, MASTER_SCREENS["ai"]),
                    entry_id(option, MASTER_SCREENS["settings"]),
                ],
            }
            for option in OPTIONS
        },
    }


def expected_functional_audit(item: dict[str, Any]) -> dict[str, Any]:
    return {
        "status": "passed",
        "page_exists": True,
        "regions_confirmed": item["regions"],
        "controls_confirmed": item["controls"],
        "tabs_confirmed": item["tabs"],
        "navigation_confirmed": item["navigation"],
        "forbidden_additions": item["forbidden"],
        "source_of_truth": (
            "current screenshot plus current ClearVision code; screenshot controls viewport visibility"
        ),
    }


def expected_entry_contract(option: str, item: dict[str, Any]) -> dict[str, Any]:
    if option not in OPTIONS:
        raise ValueError(f"Unsupported option contract: {option}")
    current = legacy.root_path(item["current_reference"], must_exist=True)
    mapping = remapping(option, item)
    is_master = item["screen_id"] in MASTER_SCREENS.values()
    architecture_refs = expected_architecture_references(option, item)
    constitution_relative = OPTION_DEFINITIONS[option]["constitution"]
    constitution_path = legacy.root_path(constitution_relative, must_exist=True)
    constitution_text = constitution_path.read_text(encoding="utf-8")
    return {
        "id": entry_id(option, item["screen_id"]),
        "option": option,
        "option_name": OPTION_DEFINITIONS[option]["name"],
        "screen_id": item["screen_id"],
        "filename": item["filename"],
        "page_name": item["page_name"],
        "purpose": item["purpose"],
        "route": item["route"],
        "page_role": "anchor" if is_master else "local",
        "master_role": next(
            (name for name, value in MASTER_SCREENS.items() if value == item["screen_id"]),
            None,
        ),
        "text_policy": "preserve-semantic-no-copy",
        "aspect_ratio": "16:9",
        "image_size": expected_image_size(option),
        "model": "gpt-image-2",
        "current_reference": item["current_reference"],
        "current_sha256": legacy.sha256(current),
        "master_references": master_references(option, item),
        "architecture_references": architecture_refs,
        "architecture_reference_sha256s": reference_hashes(architecture_refs),
        "depends_on": depends_on(option, item),
        "visual_constitution": constitution_relative,
        "visual_constitution_sha256": legacy.sha256(constitution_path),
        "functional_remapping": {
            "current_function": mapping["current_function"],
            "target_location": mapping[f"{option}_location"],
            "must_not": mapping["must_not"],
        },
        "functional_audit": expected_functional_audit(item),
        "prompt": make_prompt(option, item, constitution_text),
    }


def contract_sha256(contract: dict[str, Any]) -> str:
    encoded = json.dumps(
        contract, ensure_ascii=False, sort_keys=True, separators=(",", ":")
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def entry_contract_projection(entry: dict[str, Any]) -> dict[str, Any]:
    projected = {key: deepcopy(entry.get(key)) for key in ENTRY_CONTRACT_KEYS}
    audit = entry.get("functional_audit")
    projected["functional_audit"] = {
        key: deepcopy(audit.get(key)) if isinstance(audit, dict) else None
        for key in FUNCTIONAL_AUDIT_CONTRACT_KEYS
    }
    return projected


def reject_gate_template(option: str, item: dict[str, Any]) -> dict[str, Any]:
    return {
        "status": "pending",
        "gates": {
            "no_invented_function": "pending",
            "no_missing_function": "pending",
            "layout_structurally_redesigned": "pending",
            "same_option_master_consistency": "pending",
            "d_flow_canvas_dominant": (
                "pending" if option == "D" and item["family"] == "flow" else "not-applicable"
            ),
        },
    }


def make_entry(option: str, item: dict[str, Any]) -> dict[str, Any]:
    architecture_references(option, item)
    contract = expected_entry_contract(option, item)
    entry = deepcopy(contract)
    entry["functional_audit"]["audited_at"] = utc_now()
    entry["contract_sha256"] = contract_sha256(contract)
    entry["status"] = "Pending"
    entry["reject_gate"] = reject_gate_template(option, item)
    return entry


def initialize(force: bool) -> None:
    if MANIFEST_PATH.is_file() and not force:
        existing = legacy.read_json(MANIFEST_PATH)
        if existing.get("schema_version") == SCHEMA_VERSION:
            raise ValueError("Options manifest already exists; pass --force only to intentionally reset it.")
        raise ValueError("A different manifest exists; archive it and pass --force to initialize options.")
    for option in OPTIONS:
        for directory in ("masters", "screens", "iterations", "references", "research", "comparison"):
            (option_root(option) / directory).mkdir(parents=True, exist_ok=True)
    (ROOT / "audit" / "comparison_DE").mkdir(parents=True, exist_ok=True)
    (ROOT / "audit" / "logs" / "options").mkdir(parents=True, exist_ok=True)
    (ROOT / "workflow" / "reference_boards" / "options").mkdir(parents=True, exist_ok=True)
    write_functional_mapping_docs()
    entries = [make_entry(option, item) for option in OPTIONS for item in PAGES]
    payload = expected_top_contract()
    payload.update({"generated_at": utc_now(), "entries": entries})
    legacy.atomic_write_json(MANIFEST_PATH, payload)
    write_readiness_artifacts(payload)
    print(f"Initialized {len(entries)} entries: {len(PAGES)} screens x {len(OPTIONS)} options")


def _invalidate_contract_dependent_evidence(
    entry: dict[str, Any], item: dict[str, Any], old_contract_sha256: str | None,
    reason: str = "immutable visual contract changed",
) -> None:
    evidence_keys = (
        "status", "output", "reference_board", "generated_at", "sha256", "actual_dimensions",
        "generation", "reject_gate", "master_path", "master_sha256", "master_source_sha256",
        "selected_for_master_chain_at", "approval_scope",
    )
    entry["stale_previous_evidence"] = {
        key: deepcopy(entry[key]) for key in evidence_keys if key in entry
    }
    for key in evidence_keys:
        entry.pop(key, None)
    entry["status"] = "Needs-Manual"
    entry["stale_reason"] = reason
    entry["stale_contract_sha256"] = old_contract_sha256
    entry["staled_at"] = utc_now()
    entry["reject_gate"] = reject_gate_template(entry["option"], item)


def refresh_contracts() -> None:
    """Refresh exact functional contracts and stale incompatible generated evidence."""
    manifest = load_manifest()
    if manifest.get("schema_version") != SCHEMA_VERSION:
        raise ValueError("refresh requires the v3 options manifest")
    entries = manifest.get("entries")
    if not isinstance(entries, list):
        raise ValueError("refresh requires entries to be an array")
    page_by_id = {item["screen_id"]: item for item in PAGES}
    expected_ids = {
        entry_id(option, item["screen_id"]) for option in OPTIONS for item in PAGES
    }
    seen_ids: set[str] = set()
    for index, entry in enumerate(entries):
        if not isinstance(entry, dict):
            raise ValueError(f"entries[{index}] must be an object")
        option = entry.get("option")
        screen_id = entry.get("screen_id")
        if option not in OPTIONS or screen_id not in page_by_id:
            raise ValueError(f"entries[{index}] has an unknown option or screen_id")
        expected_id = entry_id(option, screen_id)
        if entry.get("id") != expected_id or expected_id in seen_ids:
            raise ValueError(f"entries[{index}] identity is invalid or duplicated")
        seen_ids.add(expected_id)
    if seen_ids != expected_ids:
        raise ValueError(
            f"refresh coverage mismatch; missing={sorted(expected_ids - seen_ids)}, "
            f"extra={sorted(seen_ids - expected_ids)}"
        )

    stale_count = 0
    restored_stale_count = 0
    for entry in entries:
        item = page_by_id[entry["screen_id"]]
        option = entry["option"]
        architecture_references(option, item)
        expected = expected_entry_contract(option, item)
        new_contract_sha256 = contract_sha256(expected)
        old_contract_sha256 = entry.get("contract_sha256")
        stale_previous_evidence = entry.get("stale_previous_evidence")
        revalidated_stale_evidence = (
            isinstance(stale_previous_evidence, dict)
            and entry.get("stale_contract_sha256") == new_contract_sha256
        )
        if revalidated_stale_evidence:
            for key, value in stale_previous_evidence.items():
                entry[key] = deepcopy(value)
            entry.pop("stale_previous_evidence", None)
            entry.pop("stale_reason", None)
            entry.pop("stale_contract_sha256", None)
            entry.pop("staled_at", None)
            restored_stale_count += 1
        old_projection = entry_contract_projection(entry)
        generation = entry.get("generation")
        reference_evidence_changed = False
        if isinstance(generation, dict):
            try:
                reference_evidence_changed = (
                    generation.get("master_reference_hashes")
                    != reference_hashes(expected["master_references"])
                    or generation.get("architecture_reference_hashes")
                    != reference_hashes(expected["architecture_references"])
                )
            except (ValueError, FileNotFoundError):
                reference_evidence_changed = True
        if revalidated_stale_evidence and reference_evidence_changed:
            revalidated_stale_evidence = False
            restored_stale_count -= 1
        had_generated_evidence = (
            entry.get("status") in {"Generated", "Approved-Candidate"}
            or any(key in entry for key in ("output", "sha256", "generation", "master_path"))
            or entry.get("reject_gate", {}).get("status") == "passed"
        )
        previous_audit = entry.get("functional_audit", {})
        for key, value in expected.items():
            entry[key] = deepcopy(value)
        entry["functional_audit"]["audited_at"] = utc_now()
        if isinstance(previous_audit, dict) and previous_audit.get("audited_at"):
            entry["functional_audit"]["previous_audited_at"] = previous_audit["audited_at"]
        entry["contract_sha256"] = new_contract_sha256
        contract_changed = (
            old_contract_sha256 != new_contract_sha256
            or old_projection != expected
            or reference_evidence_changed
        )
        if revalidated_stale_evidence:
            contract_changed = False
        if had_generated_evidence and contract_changed:
            _invalidate_contract_dependent_evidence(
                entry,
                item,
                old_contract_sha256,
                "immutable visual contract or reference content changed",
            )
            stale_count += 1
        elif not contract_changed:
            entry.pop("stale_reason", None)
            entry.pop("stale_contract_sha256", None)
            entry.pop("staled_at", None)

    for key, value in expected_top_contract().items():
        manifest[key] = deepcopy(value)
    manifest["contracts_refreshed_at"] = utc_now()
    write_functional_mapping_docs()
    legacy.atomic_write_json(MANIFEST_PATH, manifest)
    write_readiness_artifacts(manifest)
    print(
        f"Refreshed exact functional contracts for {len(entries)} entries; "
        f"staled generated evidence: {stale_count}; "
        f"restored revalidated stale evidence: {restored_stale_count}"
    )


def load_manifest() -> dict[str, Any]:
    if not MANIFEST_PATH.is_file():
        raise FileNotFoundError(f"Missing manifest: {MANIFEST_PATH}")
    return legacy.read_json(MANIFEST_PATH)


def expected_master_refs(option: str, screen_id: str) -> list[str]:
    item = next(page_def for page_def in PAGES if page_def["screen_id"] == screen_id)
    return master_references(option, item)


def reference_hashes(references: list[str]) -> dict[str, str]:
    return {
        reference: legacy.sha256(legacy.root_path(reference, must_exist=True))
        for reference in references
    }


def expected_reject_gate_values(option: str, page_def: dict[str, Any]) -> dict[str, str]:
    return {
        key: (
            "not-applicable"
            if key == "d_flow_canvas_dominant" and (
                option != "D" or page_def["family"] != "flow"
            )
            else "passed"
        )
        for key in REJECT_GATE_KEYS
    }


def preflight_evidence_errors(manifest: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    preflight = manifest.get("preflight")
    if not isinstance(preflight, dict) or preflight.get("performed") is not True:
        return ["authenticated /models preflight is missing"]
    if preflight.get("exact_model") != "gpt-image-2" or preflight.get("model_discovered") is not True:
        errors.append("preflight did not prove exact gpt-image-2")
    if preflight.get("model_fallback_allowed") is not False:
        errors.append("preflight must forbid model fallback")
    if preflight.get("approved_upload_scope") != UPLOAD_APPROVAL_SCOPE:
        errors.append("preflight upload scope drifted")
    approved_host = preflight.get("approved_upload_host")
    if not isinstance(approved_host, str) or not approved_host:
        errors.append("preflight approved host is missing")
    else:
        try:
            if legacy._canonical_approved_host(approved_host) != approved_host:
                errors.append("preflight approved host is not canonical")
        except RuntimeError as exc:
            errors.append(f"preflight approved host is invalid: {exc}")
    if preflight.get("transport_policy") != TRANSPORT_POLICY:
        errors.append("preflight transport policy is missing or stale")
    secured_launcher = legacy.SECURE_PPT_IMAGE_GEN
    if not secured_launcher.is_file():
        errors.append("secured PPT Master launcher is missing")
    elif preflight.get("secure_launcher_sha256") != legacy.sha256(secured_launcher):
        errors.append("preflight secured launcher hash drifted")
    if not legacy.PPT_IMAGE_GEN.is_file():
        errors.append("PPT Master image generator is missing")
    elif preflight.get("ppt_image_generator_sha256") != legacy.sha256(legacy.PPT_IMAGE_GEN):
        errors.append("preflight PPT Master generator hash drifted")
    if not isinstance(preflight.get("checked_at"), str) or not preflight.get("checked_at"):
        errors.append("preflight timestamp is missing")
    expected_evidence_sha256 = contract_sha256({
        key: preflight.get(key) for key in PREFLIGHT_EVIDENCE_KEYS
    })
    if preflight.get("evidence_sha256") != expected_evidence_sha256:
        errors.append("preflight evidence hash drifted")
    return errors


def active_preflight_sha256(manifest: dict[str, Any]) -> str | None:
    preflight = manifest.get("preflight")
    return preflight.get("evidence_sha256") if isinstance(preflight, dict) else None


def current_dominant_style_cells(
    style_reference_count: int,
) -> list[tuple[int, int, int, int]]:
    if style_reference_count < 0:
        raise ValueError("Style reference count cannot be negative")
    if style_reference_count == 0:
        return []
    style_width = CURRENT_DOMINANT_BOARD_WIDTH - CURRENT_DOMINANT_CURRENT_WIDTH
    gutter = 8 if style_reference_count > 1 else 0
    usable_height = CURRENT_DOMINANT_BOARD_HEIGHT - gutter * (style_reference_count - 1)
    if usable_height < style_reference_count:
        raise ValueError("Too many style references for the current-dominant board")
    base_height = usable_height // style_reference_count
    cells: list[tuple[int, int, int, int]] = []
    top = 0
    for index in range(style_reference_count):
        cell_height = (
            base_height
            if index < style_reference_count - 1
            else CURRENT_DOMINANT_BOARD_HEIGHT - top
        )
        cells.append((CURRENT_DOMINANT_CURRENT_WIDTH, top, style_width, cell_height))
        top += cell_height + gutter
    return cells


def current_dominant_reference_layout(entry: dict[str, Any]) -> dict[str, Any]:
    style_reference_paths = [
        *entry.get("architecture_references", []),
        *entry.get("master_references", []),
    ]
    style_width = CURRENT_DOMINANT_BOARD_WIDTH - CURRENT_DOMINANT_CURRENT_WIDTH
    return {
        "width": CURRENT_DOMINANT_BOARD_WIDTH,
        "height": CURRENT_DOMINANT_BOARD_HEIGHT,
        "current_reference_width": CURRENT_DOMINANT_CURRENT_WIDTH,
        "style_reference_width": style_width,
        "current_reference_fraction": 0.75,
        "style_reference_fraction": 0.25,
        "style_reference_paths": style_reference_paths,
    }


def generation_evidence_errors(
    entry: dict[str, Any], page_def: dict[str, Any], manifest: dict[str, Any]
) -> list[str]:
    label = str(entry.get("id") or "entry")
    errors: list[str] = []
    option = str(entry.get("option"))
    expected_dimensions = expected_output_dimensions(option)
    preflight = manifest.get("preflight")
    errors.extend(f"{label}: {error}" for error in preflight_evidence_errors(manifest))
    generation = entry.get("generation")
    if not isinstance(generation, dict):
        return errors + [f"{label}: generation evidence is missing"]
    retry_mode = generation.get("retry_mode")
    if retry_mode is not None and retry_mode not in RETRY_MODES:
        errors.append(f"{label}: generation retry mode is unsupported")
    if retry_mode == CURRENT_DOMINANT_RETRY_MODE:
        instruction = generation.get("iteration_instruction")
        instruction_sha256 = generation.get("iteration_instruction_sha256")
        valid_instruction = isinstance(instruction, str) and bool(instruction.strip())
        if not valid_instruction:
            errors.append(f"{label}: current-dominant iteration instruction is missing")
        elif instruction_sha256 != hashlib.sha256(
            instruction.strip().encode("utf-8")
        ).hexdigest():
            errors.append(f"{label}: current-dominant iteration instruction hash drifted")
        if generation.get("current_reference_sha256") != entry.get("current_sha256"):
            errors.append(f"{label}: current-dominant CURRENT reference hash drifted")
        if generation.get("reference_board_layout") != current_dominant_reference_layout(entry):
            errors.append(f"{label}: current-dominant reference board layout drifted")
        if generation.get("reference_policy") != CURRENT_DOMINANT_REFERENCE_POLICY:
            errors.append(f"{label}: current-dominant reference policy drifted")
        if generation.get("iteration_target_uploaded") is not False:
            errors.append(f"{label}: current-dominant retry did not prove target omission")
        if generation.get("iteration_target_sha256") is not None:
            errors.append(f"{label}: current-dominant retry retained a target hash")
        if generation.get("iteration_target_archived_as") is not None:
            errors.append(f"{label}: current-dominant retry retained a target archive binding")
    provenance_mode = generation.get("provenance_mode")
    if provenance_mode is not None:
        restored_provenance_modes = {
            "restored-audited-gpt-image-2-iteration",
            "controlled-picture-layer-touchup-of-restored-gpt-image-2-iteration",
        }
        controlled_touchup_modes = {
            "controlled-picture-layer-touchup-of-restored-gpt-image-2-iteration",
            "controlled-picture-layer-touchup-of-generated-gpt-image-2-output",
        }
        supported_provenance_modes = (
            restored_provenance_modes
            | controlled_touchup_modes
        )
        if provenance_mode not in supported_provenance_modes:
            errors.append(f"{label}: generation provenance mode is unsupported")
        if provenance_mode in restored_provenance_modes:
            restoration = generation.get("restoration_evidence")
            if not isinstance(restoration, dict):
                errors.append(f"{label}: restored generation evidence is missing")
            else:
                restored_path = restoration.get("archived_iteration")
                try:
                    archived = legacy.root_path(str(restored_path), must_exist=True)
                    expected_parent = (
                        option_root(str(entry.get("option"))) / "iterations"
                    ).resolve()
                    if archived.parent.resolve() != expected_parent:
                        errors.append(
                            f"{label}: restored iteration is outside the option archive"
                        )
                    archived_sha256 = legacy.sha256(archived)
                    if restoration.get("archived_sha256") != archived_sha256:
                        errors.append(f"{label}: restored iteration hash drifted")
                    if generation.get("source_sha256") != archived_sha256:
                        errors.append(
                            f"{label}: generation source is not the restored iteration"
                        )
                    if generation.get("iteration_target_sha256") != archived_sha256:
                        errors.append(
                            f"{label}: iteration target is not the restored iteration"
                        )
                except (ValueError, FileNotFoundError) as exc:
                    errors.append(
                        f"{label}: restored iteration could not be verified: {exc}"
                    )
                expected_restored_output = (
                    generation.get("source_sha256")
                    if provenance_mode
                    == "controlled-picture-layer-touchup-of-restored-gpt-image-2-iteration"
                    else entry.get("sha256")
                )
                if restoration.get("restored_output_sha256") != expected_restored_output:
                    errors.append(f"{label}: restored output hash drifted")
                if (
                    not isinstance(restoration.get("restored_at"), str)
                    or not restoration.get("restored_at")
                ):
                    errors.append(f"{label}: restoration timestamp is missing")
                if (
                    not isinstance(restoration.get("note"), str)
                    or not restoration.get("note", "").strip()
                ):
                    errors.append(f"{label}: restoration audit note is missing")
        if provenance_mode in controlled_touchup_modes:
            touchup = generation.get("touchup_evidence")
            if not isinstance(touchup, dict):
                errors.append(f"{label}: controlled touchup evidence is missing")
            else:
                spec_payload: dict[str, Any] | None = None
                try:
                    spec_path = legacy.root_path(str(touchup.get("spec")), must_exist=True)
                    expected_specs = (ROOT / "workflow" / "touchups").resolve()
                    if spec_path.parent.resolve() != expected_specs:
                        errors.append(f"{label}: touchup spec is outside the controlled directory")
                    if touchup.get("spec_sha256") != legacy.sha256(spec_path):
                        errors.append(f"{label}: touchup spec hash drifted")
                    spec_payload = json.loads(spec_path.read_text(encoding="utf-8"))
                except (ValueError, FileNotFoundError) as exc:
                    errors.append(f"{label}: touchup spec could not be verified: {exc}")
                if isinstance(spec_payload, dict):
                    try:
                        archived_source = legacy.root_path(
                            str(spec_payload.get("source_iteration")),
                            must_exist=True,
                        )
                        expected_parent = (
                            option_root(str(entry.get("option"))) / "iterations"
                        ).resolve()
                        if archived_source.parent.resolve() != expected_parent:
                            errors.append(
                                f"{label}: touchup source is outside the option archive"
                            )
                        archived_source_sha256 = legacy.sha256(archived_source)
                        if spec_payload.get("source_sha256") != archived_source_sha256:
                            errors.append(f"{label}: touchup spec source hash drifted")
                        if touchup.get("source_sha256") != archived_source_sha256:
                            errors.append(f"{label}: touchup source hash drifted")
                    except (ValueError, FileNotFoundError) as exc:
                        errors.append(
                            f"{label}: touchup source could not be verified: {exc}"
                        )
                if provenance_mode == (
                    "controlled-picture-layer-touchup-of-restored-gpt-image-2-iteration"
                ):
                    if touchup.get("source_sha256") != generation.get("source_sha256"):
                        errors.append(f"{label}: restored touchup source hash drifted")
                else:
                    if touchup.get("base_output_sha256") != touchup.get("source_sha256"):
                        errors.append(f"{label}: generated touchup base output hash drifted")
                    if touchup.get("provider_source_sha256") != generation.get("source_sha256"):
                        errors.append(f"{label}: generated touchup provider source hash drifted")
                if touchup.get("output_sha256") != entry.get("sha256"):
                    errors.append(f"{label}: touchup output hash drifted")
                if not isinstance(touchup.get("applied_at"), str) or not touchup.get("applied_at"):
                    errors.append(f"{label}: touchup timestamp is missing")
    if generation.get("model") != "gpt-image-2":
        errors.append(f"{label}: generation model is not exact gpt-image-2")
    if generation.get("functional_gate") != "passed":
        errors.append(f"{label}: generation functional gate is not passed")
    if generation.get("contract_sha256") != entry.get("contract_sha256"):
        errors.append(f"{label}: generation contract is stale")
    try:
        constitution_path = legacy.root_path(
            str(entry.get("visual_constitution")), must_exist=True
        )
        live_constitution_sha256 = legacy.sha256(constitution_path)
        if entry.get("visual_constitution_sha256") != live_constitution_sha256:
            errors.append(f"{label}: visual constitution contract drifted")
        if generation.get("visual_constitution_sha256") != live_constitution_sha256:
            errors.append(f"{label}: generation visual constitution drifted")
    except (ValueError, FileNotFoundError) as exc:
        errors.append(f"{label}: visual constitution could not be verified: {exc}")
    if generation.get("quality") != "high":
        errors.append(f"{label}: generation quality is not high")
    if generation.get("aspect_ratio") != entry.get("aspect_ratio"):
        errors.append(f"{label}: generation aspect ratio drifted")
    if generation.get("image_size") != entry.get("image_size"):
        errors.append(f"{label}: generation image size drifted")
    if generation.get("output_format") != "png":
        errors.append(f"{label}: generation output format is not PNG")
    source_dimensions = generation.get("source_dimensions")
    valid_source_dimensions = (
        isinstance(source_dimensions, dict)
        and isinstance(source_dimensions.get("width"), int)
        and isinstance(source_dimensions.get("height"), int)
        and source_dimensions["width"] > 0
        and source_dimensions["height"] > 0
    )
    if not valid_source_dimensions:
        errors.append(f"{label}: generation source dimensions are missing or invalid")
    else:
        source_ratio = source_dimensions["width"] / source_dimensions["height"]
        relative_error = abs(source_ratio - EXPECTED_OUTPUT_ASPECT_RATIO) / EXPECTED_OUTPUT_ASPECT_RATIO
        if relative_error > MAX_SOURCE_ASPECT_RATIO_RELATIVE_ERROR:
            errors.append(f"{label}: generation source aspect ratio is outside normalization tolerance")
        if source_dimensions["width"] * source_dimensions["height"] < MIN_SOURCE_PIXEL_COUNT:
            errors.append(f"{label}: generation source pixel count is below normalization minimum")
        if native_source_required(option) and source_dimensions != expected_dimensions:
            errors.append(
                f"{label}: generation source dimensions do not match native "
                f"{expected_dimensions['width']}x{expected_dimensions['height']} contract"
            )
    if generation.get("source_format") != EXPECTED_OUTPUT_FORMAT:
        errors.append(f"{label}: generation source format is not PNG")
    source_sha256 = generation.get("source_sha256")
    if not (
        isinstance(source_sha256, str)
        and len(source_sha256) == 64
        and all(character in "0123456789abcdef" for character in source_sha256)
    ):
        errors.append(f"{label}: generation source hash is missing or invalid")
    normalization_applied = generation.get("normalization_applied")
    if not isinstance(normalization_applied, bool):
        errors.append(f"{label}: generation normalization flag is missing or invalid")
    if generation.get("normalized_dimensions") != expected_dimensions:
        errors.append(f"{label}: generation normalized dimensions drifted")
    if generation.get("normalization_contract_sha256") != normalization_contract_sha256(option):
        errors.append(f"{label}: generation normalization contract drifted")
    if valid_source_dimensions and isinstance(normalization_applied, bool):
        expected_applied = source_dimensions != expected_dimensions
        if normalization_applied != expected_applied:
            errors.append(f"{label}: generation normalization flag contradicts source dimensions")
        expected_method = (
            NORMALIZATION_METHOD_FIT if expected_applied else NORMALIZATION_METHOD_NONE
        )
        if generation.get("normalization_method") != expected_method:
            errors.append(f"{label}: generation normalization method contradicts source dimensions")
        controlled_derivative = (
            provenance_mode
            == "controlled-picture-layer-touchup-of-restored-gpt-image-2-iteration"
        )
        if (
            not expected_applied
            and source_sha256 != generation.get("output_sha256")
            and not controlled_derivative
        ):
            errors.append(f"{label}: unnormalized source hash differs from output")
    if isinstance(preflight, dict):
        if generation.get("approved_upload_host") != preflight.get("approved_upload_host"):
            errors.append(f"{label}: generation approved host differs from preflight")
        if generation.get("approved_upload_scope") != preflight.get("approved_upload_scope"):
            errors.append(f"{label}: generation approved scope differs from preflight")
        if generation.get("preflight_evidence_sha256") != preflight.get("evidence_sha256"):
            errors.append(f"{label}: generation is not bound to the active preflight")
        if generation.get("transport_policy") != preflight.get("transport_policy"):
            errors.append(f"{label}: generation transport policy differs from preflight")
        if generation.get("secure_launcher_sha256") != preflight.get("secure_launcher_sha256"):
            errors.append(f"{label}: generation secured launcher differs from preflight")
        if generation.get("ppt_image_generator_sha256") != preflight.get("ppt_image_generator_sha256"):
            errors.append(f"{label}: generation PPT Master generator differs from preflight")
    try:
        if generation.get("master_reference_hashes") != reference_hashes(entry["master_references"]):
            errors.append(f"{label}: generation Master references drifted")
        if generation.get("architecture_reference_hashes") != reference_hashes(
            entry.get("architecture_references", [])
        ):
            errors.append(f"{label}: generation architecture references drifted")
    except (KeyError, ValueError, FileNotFoundError) as exc:
        errors.append(f"{label}: generation references could not be verified: {exc}")
    reference_board = entry.get("reference_board")
    try:
        board_path = legacy.root_path(str(reference_board), must_exist=True)
        if generation.get("reference_board_sha256") != legacy.sha256(board_path):
            errors.append(f"{label}: generation reference board drifted")
    except (ValueError, FileNotFoundError) as exc:
        errors.append(f"{label}: generation reference board could not be verified: {exc}")
    if generation.get("output_sha256") != entry.get("sha256"):
        errors.append(f"{label}: generation is not bound to the current output")
    return errors


def output_file_errors(
    candidate: Path, option: str
) -> tuple[list[str], dict[str, int] | None]:
    errors: list[str] = []
    dimensions: dict[str, int] | None = None
    expected_dimensions = expected_output_dimensions(option)
    try:
        with Image.open(candidate) as opened:
            dimensions = {"width": opened.width, "height": opened.height}
            image_format = opened.format
            opened.verify()
        if image_format != EXPECTED_OUTPUT_FORMAT:
            errors.append("output content is not PNG")
        if dimensions != expected_dimensions:
            errors.append(
                "output dimensions must be "
                f"{expected_dimensions['width']}x{expected_dimensions['height']}"
            )
    except OSError as exc:
        errors.append(f"output image cannot be decoded: {exc}")
    return errors, dimensions


def normalization_contract_sha256(option: str) -> str:
    return contract_sha256({
        "option": option,
        "target_dimensions": expected_output_dimensions(option),
        "target_format": EXPECTED_OUTPUT_FORMAT,
        "native_source_required": native_source_required(option),
        "max_source_aspect_ratio_relative_error": MAX_SOURCE_ASPECT_RATIO_RELATIVE_ERROR,
        "min_source_pixel_count": MIN_SOURCE_PIXEL_COUNT,
        "native_method": NORMALIZATION_METHOD_NONE,
        "fit_method": NORMALIZATION_METHOD_FIT,
    })


def normalize_generated_png(candidate: Path, option: str) -> dict[str, Any]:
    """Validate a native source or normalize an allowed near-16:9 PNG."""
    expected_dimensions = expected_output_dimensions(option)
    with Image.open(candidate) as opened:
        source_dimensions = {"width": opened.width, "height": opened.height}
        source_format = opened.format
        opened.verify()
    if source_format != EXPECTED_OUTPUT_FORMAT:
        raise RuntimeError("GPT-image output content is not PNG")
    if native_source_required(option) and source_dimensions != expected_dimensions:
        raise RuntimeError(
            "GPT-image output must be native "
            f"{expected_dimensions['width']}x{expected_dimensions['height']} for option {option}; "
            f"received {source_dimensions['width']}x{source_dimensions['height']}; "
            "upscaling is forbidden"
        )
    source_pixel_count = source_dimensions["width"] * source_dimensions["height"]
    if source_pixel_count < MIN_SOURCE_PIXEL_COUNT:
        raise RuntimeError(
            "GPT-image output is too small for audited normalization; "
            f"received {source_dimensions['width']}x{source_dimensions['height']}"
        )
    source_ratio = source_dimensions["width"] / source_dimensions["height"]
    relative_error = abs(source_ratio - EXPECTED_OUTPUT_ASPECT_RATIO) / EXPECTED_OUTPUT_ASPECT_RATIO
    if relative_error > MAX_SOURCE_ASPECT_RATIO_RELATIVE_ERROR:
        raise RuntimeError(
            "GPT-image output aspect ratio is outside the 16:9 normalization tolerance; "
            f"received {source_dimensions['width']}x{source_dimensions['height']}"
        )
    source_sha256 = legacy.sha256(candidate)
    normalization_applied = source_dimensions != expected_dimensions
    normalization_method = (
        NORMALIZATION_METHOD_FIT if normalization_applied else NORMALIZATION_METHOD_NONE
    )
    if normalization_applied:
        normalized_path = candidate.with_name(f"{candidate.stem}_normalized.png")
        with Image.open(candidate) as opened:
            normalized = ImageOps.fit(
                opened.convert("RGB"),
                (
                    expected_dimensions["width"],
                    expected_dimensions["height"],
                ),
                method=Image.Resampling.LANCZOS,
                centering=(0.5, 0.5),
            )
            normalized.save(normalized_path, format="PNG", optimize=True)
        os.replace(normalized_path, candidate)
    file_errors, normalized_dimensions = output_file_errors(candidate, option)
    if file_errors or normalized_dimensions is None:
        raise RuntimeError(
            "Normalized GPT-image output failed delivery validation: "
            + "; ".join(file_errors or ["dimensions unavailable"])
        )
    return {
        "source_dimensions": source_dimensions,
        "source_format": source_format,
        "source_sha256": source_sha256,
        "normalization_applied": normalization_applied,
        "normalization_method": normalization_method,
        "normalized_dimensions": normalized_dimensions,
        "normalization_contract_sha256": normalization_contract_sha256(option),
    }


def candidate_evidence_errors(
    entry: dict[str, Any], page_def: dict[str, Any], manifest: dict[str, Any]
) -> list[str]:
    label = str(entry.get("id") or "entry")
    errors: list[str] = []
    try:
        candidate = option_image(
            str(entry.get("option")), "screens", str(entry.get("filename")), must_exist=True
        )
        if entry.get("output") != legacy.rel(candidate):
            errors.append(f"{label}: output path does not match managed screen")
        digest = legacy.sha256(candidate)
        if entry.get("sha256") != digest:
            errors.append(f"{label}: output hash drifted")
        file_errors, dimensions = output_file_errors(candidate, str(entry.get("option")))
        errors.extend(f"{label}: {error}" for error in file_errors)
        if dimensions is not None and entry.get("actual_dimensions") != dimensions:
            errors.append(f"{label}: recorded output dimensions drifted")
    except (OSError, ValueError, FileNotFoundError) as exc:
        errors.append(f"{label}: output could not be verified: {exc}")
    if entry.get("status") not in {"Generated", "Approved-Candidate"}:
        errors.append(f"{label}: status is not output-ready")
    errors.extend(generation_evidence_errors(entry, page_def, manifest))
    if entry.get("depends_on"):
        errors.extend(dependency_evidence_errors(entry, manifest))
    return errors


def review_evidence_errors(
    entry: dict[str, Any], page_def: dict[str, Any], manifest: dict[str, Any]
) -> list[str]:
    label = str(entry.get("id") or "entry")
    errors: list[str] = []
    review = entry.get("reject_gate")
    if not isinstance(review, dict) or review.get("status") != "passed":
        return [f"{label}: reject gate is not passed"]
    if review.get("gates") != expected_reject_gate_values(
        str(entry.get("option")), page_def
    ):
        errors.append(f"{label}: reject gate contains an unpassed or invalid gate")
    if review.get("reviewed_sha256") != entry.get("sha256"):
        errors.append(f"{label}: reject gate is not bound to the current output")
    if review.get("contract_sha256") != entry.get("contract_sha256"):
        errors.append(f"{label}: reject gate contract is stale")
    try:
        constitution_path = legacy.root_path(
            str(entry.get("visual_constitution")), must_exist=True
        )
        live_constitution_sha256 = legacy.sha256(constitution_path)
        if entry.get("visual_constitution_sha256") != live_constitution_sha256:
            errors.append(f"{label}: visual constitution contract drifted")
        if (
            review.get("reviewed_visual_constitution_sha256")
            != live_constitution_sha256
        ):
            errors.append(f"{label}: reject gate visual constitution drifted")
    except (ValueError, FileNotFoundError) as exc:
        errors.append(f"{label}: visual constitution could not be verified: {exc}")
    generation = entry.get("generation")
    if not isinstance(generation, dict) or review.get(
        "reviewed_generation_sha256"
    ) != contract_sha256(generation):
        errors.append(f"{label}: reject gate is not bound to generation evidence")
    if review.get("reviewed_preflight_sha256") != active_preflight_sha256(manifest):
        errors.append(f"{label}: reject gate is not bound to the active preflight")
    if not isinstance(review.get("reviewed_at"), str) or not review.get("reviewed_at"):
        errors.append(f"{label}: reject gate timestamp is missing")
    if not isinstance(review.get("note"), str) or not review.get("note", "").strip():
        errors.append(f"{label}: reject gate audit note is missing")
    if entry.get("status") not in {"Generated", "Approved-Candidate"}:
        errors.append(f"{label}: reviewed status is not delivery-ready")
    return errors


def dependency_evidence_errors(
    entry: dict[str, Any], manifest: dict[str, Any]
) -> list[str]:
    errors: list[str] = []
    by_id = {
        item.get("id"): item
        for item in manifest.get("entries", [])
        if isinstance(item, dict)
    }
    for dependency_id in entry.get("depends_on", []):
        dependency = by_id.get(dependency_id)
        if not isinstance(dependency, dict):
            errors.append(f"missing declared Master dependency {dependency_id}")
            continue
        if dependency.get("status") != "Approved-Candidate":
            errors.append(f"dependency {dependency_id} is not an Approved-Candidate Master")
            continue
        page_def = next(
            (item for item in PAGES if item["screen_id"] == dependency.get("screen_id")),
            None,
        )
        if not isinstance(page_def, dict):
            errors.append(f"dependency {dependency_id} has no frozen page contract")
            continue
        errors.extend(candidate_evidence_errors(dependency, page_def, manifest))
        errors.extend(review_evidence_errors(dependency, page_def, manifest))
        try:
            master = option_image(
                str(dependency.get("option")),
                "masters",
                str(dependency.get("filename")),
                must_exist=True,
            )
            digest = legacy.sha256(master)
            if dependency.get("master_path") != legacy.rel(master):
                errors.append(f"dependency {dependency_id} Master path drifted")
            if (
                dependency.get("master_sha256") != digest
                or dependency.get("master_sha256") != dependency.get("sha256")
                or dependency.get("master_source_sha256") != dependency.get("sha256")
            ):
                errors.append(f"dependency {dependency_id} Master provenance drifted")
        except (ValueError, FileNotFoundError) as exc:
            errors.append(f"dependency {dependency_id} Master could not be verified: {exc}")
    return errors


def validate_manifest(
    manifest: dict[str, Any], *, require_masters: bool = False, require_outputs: bool = False,
    require_reviews: bool = False,
) -> list[str]:
    errors: list[str] = []
    if not isinstance(manifest, dict):
        return ["manifest must be an object"]
    expected_top = expected_top_contract()
    for key in TOP_LEVEL_CONTRACT_KEYS:
        if manifest.get(key) != expected_top[key]:
            errors.append(f"top-level immutable contract drift: {key}")
    if require_masters or require_outputs or require_reviews:
        errors.extend(
            f"final readiness preflight: {error}"
            for error in preflight_evidence_errors(manifest)
        )
    entries = manifest.get("entries")
    if not isinstance(entries, list):
        return errors + ["entries must be an array"]
    expected_ids = [
        entry_id(option, item["screen_id"]) for option in OPTIONS for item in PAGES
    ]
    actual_ids = [entry.get("id") if isinstance(entry, dict) else None for entry in entries]
    if actual_ids != expected_ids:
        errors.append("entry order/coverage must exactly match frozen D/E screen contracts")
    ids: set[str] = set()
    prompt_terms = (
        "FUNCTIONAL INVENTORY, not as a layout template",
        "ZERO ADDITIONS / ZERO OMISSIONS",
        "FUNCTIONAL REMAPPING - TARGET LAYOUT AUTHORITY",
        "visual design reference",
        "Prefer omission over invented functional content",
        "PROHIBITIONS",
    )
    for option in OPTIONS:
        option_entries = [
            entry for entry in entries if isinstance(entry, dict) and entry.get("option") == option
        ]
        expected_filenames = [item["filename"] for item in PAGES]
        filenames = {entry.get("filename") for entry in option_entries}
        if filenames != set(expected_filenames):
            missing = sorted(set(expected_filenames) - filenames)
            extra = sorted(str(value) for value in filenames - set(expected_filenames))
            errors.append(f"Option {option} coverage mismatch; missing={missing}, extra={extra}")
    for index, entry in enumerate(entries):
        label = f"entries[{index}]"
        if not isinstance(entry, dict):
            errors.append(f"{label} must be an object")
            continue
        option = entry.get("option")
        screen_id = entry.get("screen_id")
        if option not in OPTIONS:
            errors.append(f"{label}.option is invalid")
            continue
        expected_id = entry_id(option, str(screen_id))
        if entry.get("id") != expected_id:
            errors.append(f"{label}.id must be {expected_id}")
        elif expected_id in ids:
            errors.append(f"duplicate id: {expected_id}")
        ids.add(expected_id)
        page_defs = [item for item in PAGES if item["screen_id"] == screen_id]
        if len(page_defs) != 1:
            errors.append(f"{label}.screen_id is unknown")
            continue
        page_def = page_defs[0]
        try:
            expected_contract = expected_entry_contract(option, page_def)
        except (FileNotFoundError, ValueError) as exc:
            errors.append(f"{label}.expected contract could not be resolved: {exc}")
            continue
        actual_contract = entry_contract_projection(entry)
        for key in ENTRY_CONTRACT_KEYS:
            if actual_contract.get(key) != expected_contract.get(key):
                if key == "functional_audit":
                    for audit_key in FUNCTIONAL_AUDIT_CONTRACT_KEYS:
                        if actual_contract[key].get(audit_key) != expected_contract[key].get(audit_key):
                            errors.append(
                                f"{label}.functional_audit immutable contract drift: {audit_key}"
                            )
                else:
                    errors.append(f"{label} immutable contract drift: {key}")
        expected_contract_sha256 = contract_sha256(expected_contract)
        if entry.get("contract_sha256") != expected_contract_sha256:
            errors.append(f"{label}.contract_sha256 drifted")
        if entry.get("status") not in ALLOWED_STATUSES:
            errors.append(f"{label}.status is unsupported")
        current = entry.get("current_reference")
        try:
            current_path = legacy.root_path(str(current), must_exist=True)
            if entry.get("current_sha256") != legacy.sha256(current_path):
                errors.append(f"{label}.current_sha256 drifted")
        except (ValueError, FileNotFoundError) as exc:
            errors.append(f"{label}.current_reference: {exc}")
        refs = entry.get("master_references", [])
        if isinstance(refs, list):
            if require_masters:
                for ref in refs:
                    try:
                        legacy.root_path(ref, must_exist=True)
                    except (ValueError, FileNotFoundError) as exc:
                        errors.append(f"{label}.master_references: {exc}")
            for ref in refs:
                if not str(ref).startswith(f"option_{option}/masters/"):
                    errors.append(f"{label} references a cross-option Master")
        else:
            errors.append(f"{label}.master_references must be an array")
        architecture_refs = entry.get("architecture_references", [])
        if not isinstance(architecture_refs, list):
            errors.append(f"{label}.architecture_references must be an array")
        else:
            for ref in architecture_refs:
                try:
                    legacy.root_path(ref, must_exist=True)
                except (ValueError, FileNotFoundError) as exc:
                    errors.append(f"{label}.architecture_references: {exc}")
        try:
            constitution_path = legacy.root_path(
                str(entry.get("visual_constitution")), must_exist=True
            )
            if entry.get("visual_constitution_sha256") != legacy.sha256(
                constitution_path
            ):
                errors.append(f"{label}.visual_constitution_sha256 drifted")
        except (ValueError, FileNotFoundError) as exc:
            errors.append(f"{label}.visual_constitution: {exc}")
        prompt = entry.get("prompt")
        if not isinstance(prompt, str) or len(prompt) < 1800 or any(term not in prompt for term in prompt_terms):
            errors.append(f"{label}.prompt lacks required functional invariants")
        review = entry.get("reject_gate")
        if not isinstance(review, dict) or set(review.get("gates", {})) != set(REJECT_GATE_KEYS):
            errors.append(f"{label}.reject_gate must contain the exact five gates")
        elif option != "D" or page_def["family"] != "flow":
            if review["gates"].get("d_flow_canvas_dominant") != "not-applicable":
                errors.append(f"{label}.d_flow_canvas_dominant must be not-applicable")
        if require_outputs:
            errors.extend(
                f"{label}: {error}" for error in candidate_evidence_errors(
                    entry, page_def, manifest
                )
            )
        if require_reviews:
            errors.extend(
                f"{label}: {error}" for error in review_evidence_errors(
                    entry, page_def, manifest
                )
            )
    if require_masters:
        by_id = {
            entry.get("id"): entry for entry in entries if isinstance(entry, dict)
        }
        for option in OPTIONS:
            for role, screen_id in MASTER_SCREENS.items():
                master_id = entry_id(option, screen_id)
                master_entry = by_id.get(master_id)
                if not isinstance(master_entry, dict):
                    errors.append(f"Missing declared Master entry: {master_id}")
                    continue
                page_def = next(item for item in PAGES if item["screen_id"] == screen_id)
                try:
                    master_file = option_image(
                        option, "masters", page_def["filename"], must_exist=True
                    )
                    if master_entry.get("status") != "Approved-Candidate":
                        errors.append(f"{master_id} is not an Approved-Candidate Master")
                    if master_entry.get("master_path") != legacy.rel(master_file):
                        errors.append(f"{master_id}.master_path does not match managed Master")
                    if master_entry.get("master_sha256") != legacy.sha256(master_file):
                        errors.append(f"{master_id}.master_sha256 drifted")
                    if master_entry.get("master_sha256") != master_entry.get("sha256"):
                        errors.append(f"{master_id}.Master is not the reviewed candidate")
                    if master_entry.get("master_source_sha256") != master_entry.get("sha256"):
                        errors.append(f"{master_id}.Master source provenance drifted")
                except (ValueError, FileNotFoundError) as exc:
                    errors.append(f"{master_id}.master: {exc}")
    return errors


def validate_command(require_masters: bool, require_outputs: bool, require_reviews: bool) -> None:
    manifest = load_manifest()
    errors = validate_manifest(
        manifest, require_masters=require_masters, require_outputs=require_outputs,
        require_reviews=require_reviews,
    )
    if errors:
        for error in errors:
            print(f"ERROR: {error}")
        raise SystemExit(1)
    checked = ["structure", "exact immutable contracts", "CURRENT hashes", "D/E coverage"]
    skipped: list[str] = []
    if require_masters:
        checked.append("Master chain")
    else:
        skipped.append("Masters (--require-masters)")
    if require_outputs:
        checked.append("outputs")
    else:
        skipped.append("outputs (--require-outputs)")
    if require_reviews:
        checked.append("manual Reject Gates")
    else:
        skipped.append("manual Reject Gates (--require-reviews)")
    print(
        f"Manifest contracts valid: {len(manifest['entries'])} entries with identical "
        f"{'/'.join(OPTIONS)} coverage. Checked: {', '.join(checked)}."
    )
    if skipped:
        print(f"Readiness: NOT READY. Skipped: {', '.join(skipped)}.")
    else:
        print("Readiness: READY for product-owner visual audit.")


def contract_probes() -> None:
    manifest = load_manifest()
    baseline_errors = validate_manifest(manifest)
    if baseline_errors:
        raise ValueError("Contract probes require a valid base manifest:\n" + "\n".join(baseline_errors))

    probes: list[tuple[str, Any, str]] = [
        (
            "top screen_count",
            lambda payload: payload.__setitem__("screen_count", 999),
            "top-level immutable contract drift: screen_count",
        ),
        (
            "top Master chain",
            lambda payload: payload["options"]["D"]["master_chain"].reverse(),
            "top-level immutable contract drift: options",
        ),
        (
            "entry page_name",
            lambda payload: payload["entries"][0].__setitem__("page_name", "drift"),
            "immutable contract drift: page_name",
        ),
        (
            "functional audit list",
            lambda payload: payload["entries"][0]["functional_audit"]["controls_confirmed"].append("invented"),
            "functional_audit immutable contract drift: controls_confirmed",
        ),
        (
            "architecture reference digest",
            lambda payload: payload["entries"][4]["architecture_reference_sha256s"].__setitem__(
                payload["entries"][4]["architecture_references"][0], "0" * 64
            ),
            "immutable contract drift: architecture_reference_sha256s",
        ),
        (
            "visual constitution digest",
            lambda payload: payload["entries"][0].__setitem__(
                "visual_constitution_sha256", "0" * 64
            ),
            "immutable contract drift: visual_constitution_sha256",
        ),
        (
            "exact prompt",
            lambda payload: payload["entries"][0].__setitem__(
                "prompt", payload["entries"][0]["prompt"] + "\nInvented capability"
            ),
            "immutable contract drift: prompt",
        ),
        (
            "reject gate shape",
            lambda payload: payload["entries"][0]["reject_gate"]["gates"].__setitem__("extra_gate", "passed"),
            "reject_gate must contain the exact five gates",
        ),
        (
            "contract hash",
            lambda payload: payload["entries"][0].__setitem__("contract_sha256", "0" * 64),
            "contract_sha256 drifted",
        ),
        (
            "non-object entry",
            lambda payload: payload["entries"].__setitem__(0, "invalid"),
            "entries[0] must be an object",
        ),
    ]
    for name, mutate, expected_error in probes:
        candidate = deepcopy(manifest)
        mutate(candidate)
        errors = validate_manifest(candidate)
        if not any(expected_error in error for error in errors):
            raise RuntimeError(
                f"Mutation probe '{name}' failed to produce expected error: {expected_error}"
            )
        print(f"PASS: {name}")
    print(f"Contract mutation probes passed: {len(probes)}")
    evidence_probes(manifest)


def evidence_probes(manifest: dict[str, Any]) -> None:
    page_def = next(item for item in PAGES if item["screen_id"] == "05_flow_editor")
    source_entry = next(
        entry for entry in manifest["entries"] if entry["id"] == "D_05_flow_editor"
    )
    entry = deepcopy(source_entry)
    probe_dimensions = expected_output_dimensions(entry["option"])
    probe_board_dir = ROOT / "workflow" / "tmp" / "contract-probes"
    probe_board = legacy.safe_named_path(probe_board_dir, entry["filename"])
    board, _ = build_reference_board(entry, output_override=probe_board)
    preflight = preflight_record("probe.invalid")
    probe_manifest = deepcopy(manifest)
    probe_manifest["preflight"] = preflight
    probe_entry = next(
        item for item in probe_manifest["entries"] if item["id"] == entry["id"]
    )
    probe_entry.update({
        "status": "Generated",
        "reference_board": legacy.rel(board),
        "sha256": "1" * 64,
        "actual_dimensions": deepcopy(probe_dimensions),
        "generation": {
            "model": "gpt-image-2",
            "quality": "high",
            "aspect_ratio": probe_entry["aspect_ratio"],
            "image_size": probe_entry["image_size"],
            "output_format": "png",
            "source_dimensions": deepcopy(probe_dimensions),
            "source_format": EXPECTED_OUTPUT_FORMAT,
            "source_sha256": "1" * 64,
            "normalization_applied": False,
            "normalization_method": NORMALIZATION_METHOD_NONE,
            "normalized_dimensions": deepcopy(probe_dimensions),
            "normalization_contract_sha256": normalization_contract_sha256(entry["option"]),
            "functional_gate": "passed",
            "contract_sha256": probe_entry["contract_sha256"],
            "visual_constitution_sha256": probe_entry["visual_constitution_sha256"],
            "master_reference_hashes": reference_hashes(probe_entry["master_references"]),
            "architecture_reference_hashes": reference_hashes(
                probe_entry["architecture_references"]
            ),
            "approved_upload_host": preflight["approved_upload_host"],
            "approved_upload_scope": preflight["approved_upload_scope"],
            "preflight_evidence_sha256": preflight["evidence_sha256"],
            "transport_policy": preflight["transport_policy"],
            "secure_launcher_sha256": preflight["secure_launcher_sha256"],
            "ppt_image_generator_sha256": preflight["ppt_image_generator_sha256"],
            "reference_board_sha256": legacy.sha256(board),
            "output_sha256": "1" * 64,
        },
    })
    baseline_generation_errors = generation_evidence_errors(
        probe_entry, page_def, probe_manifest
    )
    if baseline_generation_errors:
        raise RuntimeError(
            "Evidence probes require valid generated metadata:\n"
            + "\n".join(baseline_generation_errors)
        )

    current_instruction = (
        "Remove every unverified control and preserve only the CURRENT functional inventory."
    )
    current_board_output = legacy.safe_named_path(
        probe_board_dir, "current-dominant_05_flow_editor.png"
    )
    current_board, current_target_included = build_reference_board(
        entry,
        output_override=current_board_output,
        retry_mode=CURRENT_DOMINANT_RETRY_MODE,
    )
    with Image.open(current_board) as opened:
        if opened.size != (
            CURRENT_DOMINANT_BOARD_WIDTH,
            CURRENT_DOMINANT_BOARD_HEIGHT,
        ):
            raise RuntimeError("Current-dominant board dimensions drifted")
        divider_pixel = opened.convert("RGB").getpixel(
            (CURRENT_DOMINANT_CURRENT_WIDTH, CURRENT_DOMINANT_BOARD_HEIGHT // 2)
        )
    if current_target_included or divider_pixel != (102, 113, 124):
        raise RuntimeError("Current-dominant board did not preserve its 75/25 boundary")
    current_probe_entry = deepcopy(probe_entry)
    current_probe_entry["reference_board"] = legacy.rel(current_board)
    current_probe_entry["iteration_instruction"] = current_instruction
    current_probe_entry["generation"].update({
        "retry_mode": CURRENT_DOMINANT_RETRY_MODE,
        "iteration_instruction": current_instruction,
        "iteration_instruction_sha256": hashlib.sha256(
            current_instruction.encode("utf-8")
        ).hexdigest(),
        "current_reference_sha256": current_probe_entry["current_sha256"],
        "reference_board_layout": current_dominant_reference_layout(
            current_probe_entry
        ),
        "reference_board_sha256": legacy.sha256(current_board),
        "reference_policy": CURRENT_DOMINANT_REFERENCE_POLICY,
        "iteration_target_uploaded": False,
        "iteration_target_sha256": None,
        "iteration_target_archived_as": None,
    })
    current_generation_errors = generation_evidence_errors(
        current_probe_entry, page_def, probe_manifest
    )
    if current_generation_errors:
        raise RuntimeError(
            "Current-dominant evidence probe requires valid metadata:\n"
            + "\n".join(current_generation_errors)
        )
    print("PASS: current-dominant board is 75% CURRENT and contains no iteration target")
    current_dominant_probe_count = 1

    settings_entry = next(
        item for item in manifest["entries"] if item["id"] == "E_16_system_settings"
    )
    settings_style_paths = [
        *settings_entry.get("architecture_references", []),
        *settings_entry.get("master_references", []),
    ]
    settings_layout = current_dominant_reference_layout(settings_entry)
    if (
        len(settings_style_paths) != 3
        or settings_layout["style_reference_paths"] != settings_style_paths
    ):
        raise RuntimeError("E16 current-dominant layout omitted a required style reference")
    settings_board_output = legacy.safe_named_path(
        probe_board_dir, "current-dominant_16_system_settings.png"
    )
    settings_board, settings_target_included = build_reference_board(
        settings_entry,
        output_override=settings_board_output,
        retry_mode=CURRENT_DOMINANT_RETRY_MODE,
    )
    with Image.open(settings_board) as opened:
        settings_board_image = opened.convert("RGB")
    for reference_path, (left, top, cell_width, cell_height) in zip(
        settings_style_paths,
        current_dominant_style_cells(len(settings_style_paths)),
    ):
        expected_cell = fit_image(
            legacy.root_path(reference_path, must_exist=True),
            (cell_width, cell_height),
            "#11161b",
        )
        actual_cell = settings_board_image.crop(
            (left, top, left + cell_width, top + cell_height)
        )
        actual_visible = actual_cell.crop((8, 0, cell_width, cell_height)).tobytes()
        expected_visible = expected_cell.crop((8, 0, cell_width, cell_height)).tobytes()
        if actual_visible != expected_visible:
            raise RuntimeError(
                f"E16 current-dominant board omitted rendered reference: {reference_path}"
            )
    if settings_target_included:
        raise RuntimeError("E16 current-dominant board included an iteration target")
    print("PASS: E16 current-dominant board renders architecture, E05, and E13 references")
    current_dominant_probe_count += 1

    try:
        build_reference_board(
            entry,
            legacy.root_path(entry["current_reference"], must_exist=True),
            output_override=legacy.safe_named_path(
                probe_board_dir, "forbidden-current-dominant-target.png"
            ),
            retry_mode=CURRENT_DOMINANT_RETRY_MODE,
        )
    except ValueError as exc:
        if "forbids uploading an iteration target" not in str(exc):
            raise RuntimeError(
                "Current-dominant target-omission probe raised the wrong error"
            ) from exc
    else:
        raise RuntimeError("Current-dominant retry accepted an iteration target")
    command_entry = deepcopy(entry)
    command_entry["iteration_instruction"] = current_instruction
    current_command = generation_command(
        command_entry,
        current_board,
        probe_board_dir,
        "current-dominant-probe",
        False,
        CURRENT_DOMINANT_RETRY_MODE,
    )
    command_prompt = current_command[3]
    if (
        "approximately 75%" not in command_prompt
        or "rejected previous candidate is not included" not in command_prompt
        or current_command.count("--reference-image") != 1
    ):
        raise RuntimeError("Current-dominant generation command lost its omission contract")
    print("PASS: current-dominant command omits rejected candidates and binds one board")
    current_dominant_probe_count += 1

    current_dominant_mutations: list[tuple[str, Any, str]] = [
        (
            "current-dominant retry mode",
            lambda candidate: candidate["generation"].__setitem__("retry_mode", "other"),
            "retry mode is unsupported",
        ),
        (
            "current-dominant instruction hash",
            lambda candidate: candidate["generation"].__setitem__(
                "iteration_instruction_sha256", "0" * 64
            ),
            "iteration instruction hash drifted",
        ),
        (
            "current-dominant board layout",
            lambda candidate: candidate["generation"]["reference_board_layout"].__setitem__(
                "current_reference_fraction", 0.7
            ),
            "reference board layout drifted",
        ),
        (
            "current-dominant reference policy",
            lambda candidate: candidate["generation"].__setitem__(
                "reference_policy", "other"
            ),
            "reference policy drifted",
        ),
        (
            "current-dominant target upload",
            lambda candidate: candidate["generation"].__setitem__(
                "iteration_target_uploaded", True
            ),
            "did not prove target omission",
        ),
        (
            "current-dominant target hash",
            lambda candidate: candidate["generation"].__setitem__(
                "iteration_target_sha256", "0" * 64
            ),
            "retained a target hash",
        ),
        (
            "current-dominant target archive",
            lambda candidate: candidate["generation"].__setitem__(
                "iteration_target_archived_as", "option_D/iterations/probe.png"
            ),
            "retained a target archive binding",
        ),
    ]
    for name, mutate, expected_error in current_dominant_mutations:
        candidate = deepcopy(current_probe_entry)
        mutate(candidate)
        errors = generation_evidence_errors(candidate, page_def, probe_manifest)
        if not any(expected_error in error for error in errors):
            raise RuntimeError(
                f"Evidence mutation probe '{name}' failed to produce: {expected_error}"
            )
        print(f"PASS: {name}")
    current_dominant_probe_count += len(current_dominant_mutations)

    evidence_mutations: list[tuple[str, Any, str]] = [
        (
            "generation functional gate",
            lambda candidate: candidate["generation"].__setitem__("functional_gate", "rejected"),
            "functional gate is not passed",
        ),
        (
            "generation architecture hashes",
            lambda candidate: candidate["generation"].__setitem__(
                "architecture_reference_hashes", {}
            ),
            "architecture references drifted",
        ),
        (
            "generation visual constitution",
            lambda candidate: candidate["generation"].__setitem__(
                "visual_constitution_sha256", "0" * 64
            ),
            "generation visual constitution drifted",
        ),
        (
            "generation approved scope",
            lambda candidate: candidate["generation"].__setitem__("approved_upload_scope", "other"),
            "approved scope differs from preflight",
        ),
        (
            "generation preflight binding",
            lambda candidate: candidate["generation"].__setitem__(
                "preflight_evidence_sha256", "0" * 64
            ),
            "not bound to the active preflight",
        ),
        (
            "generation reference board",
            lambda candidate: candidate["generation"].__setitem__(
                "reference_board_sha256", "0" * 64
            ),
            "reference board drifted",
        ),
        (
            "generation output binding",
            lambda candidate: candidate["generation"].__setitem__("output_sha256", "0" * 64),
            "not bound to the current output",
        ),
        (
            "generation source dimensions",
            lambda candidate: candidate["generation"].__setitem__(
                "source_dimensions", {"width": 1024, "height": 768}
            ),
            "source aspect ratio is outside normalization tolerance",
        ),
        (
            "generation source format",
            lambda candidate: candidate["generation"].__setitem__("source_format", "JPEG"),
            "source format is not PNG",
        ),
        (
            "generation normalized dimensions",
            lambda candidate: candidate["generation"].__setitem__(
                "normalized_dimensions", {"width": 1672, "height": 941}
            ),
            "normalized dimensions drifted",
        ),
        (
            "generation normalization flag",
            lambda candidate: candidate["generation"].__setitem__(
                "normalization_applied", True
            ),
            "normalization flag contradicts source dimensions",
        ),
        (
            "generation normalization method",
            lambda candidate: candidate["generation"].__setitem__(
                "normalization_method", NORMALIZATION_METHOD_FIT
            ),
            "normalization method contradicts source dimensions",
        ),
        (
            "generation normalization contract",
            lambda candidate: candidate["generation"].__setitem__(
                "normalization_contract_sha256", "0" * 64
            ),
            "normalization contract drifted",
        ),
        (
            "generation native source binding",
            lambda candidate: candidate["generation"].__setitem__(
                "source_sha256", "0" * 64
            ),
            "unnormalized source hash differs from output",
        ),
    ]
    for name, mutate, expected_error in evidence_mutations:
        candidate = deepcopy(probe_entry)
        mutate(candidate)
        errors = generation_evidence_errors(candidate, page_def, probe_manifest)
        if not any(expected_error in error for error in errors):
            raise RuntimeError(
                f"Evidence mutation probe '{name}' failed to produce: {expected_error}"
            )
        print(f"PASS: {name}")
    additional_probe_count = current_dominant_probe_count

    direct_batch_conflicts = selected_dependency_conflicts(
        [
            source_entry,
            next(item for item in manifest["entries"] if item["id"] == "D_13_ai_workspace"),
        ],
        manifest,
    )
    transitive_batch_conflicts = selected_dependency_conflicts(
        [
            source_entry,
            next(item for item in manifest["entries"] if item["id"] == "D_17_camera_settings"),
        ],
        manifest,
    )
    independent_batch_conflicts = selected_dependency_conflicts(
        [
            source_entry,
            next(item for item in manifest["entries"] if item["id"] == "E_05_flow_editor"),
        ],
        manifest,
    )
    if not any(
        "D_13_ai_workspace depends on selected D_05_flow_editor" in conflict
        for conflict in direct_batch_conflicts
    ):
        raise RuntimeError("Direct Master dependency batch probe did not detect the race")
    if not any(
        "D_17_camera_settings depends on selected D_05_flow_editor" in conflict
        for conflict in transitive_batch_conflicts
    ):
        raise RuntimeError("Transitive Master dependency batch probe did not detect the race")
    if independent_batch_conflicts:
        raise RuntimeError("Independent D/E Master batch probe produced a false conflict")
    print("PASS: generation batches reject direct and transitive Master dependency races")
    additional_probe_count += 1

    reviewed = deepcopy(probe_entry)
    reviewed["reject_gate"] = {
        "status": "passed",
        "reviewed_at": utc_now(),
        "review_scope": "manual visual structure and functional-fidelity audit",
        "gates": expected_reject_gate_values("D", page_def),
        "note": "Offline evidence probe.",
        "reviewed_sha256": reviewed["sha256"],
        "contract_sha256": reviewed["contract_sha256"],
        "reviewed_generation_sha256": contract_sha256(reviewed["generation"]),
        "reviewed_preflight_sha256": preflight["evidence_sha256"],
        "reviewed_visual_constitution_sha256": reviewed[
            "visual_constitution_sha256"
        ],
    }
    if review_evidence_errors(reviewed, page_def, probe_manifest):
        raise RuntimeError("Evidence probes require a valid reviewed baseline")
    rejected_review = deepcopy(reviewed)
    rejected_review["reject_gate"]["gates"]["no_invented_function"] = "rejected"
    if not any(
        "unpassed or invalid gate" in error
        for error in review_evidence_errors(rejected_review, page_def, probe_manifest)
    ):
        raise RuntimeError("Reject Gate mutation probe failed")
    print("PASS: rejected gate cannot pass review evidence")
    additional_probe_count += 1

    stale_constitution_review = deepcopy(reviewed)
    stale_constitution_review["reject_gate"][
        "reviewed_visual_constitution_sha256"
    ] = "0" * 64
    if not any(
        "reject gate visual constitution drifted" in error
        for error in review_evidence_errors(
            stale_constitution_review, page_def, probe_manifest
        )
    ):
        raise RuntimeError("Reject Gate constitution mutation probe failed")
    print("PASS: reject gate visual constitution binding")
    additional_probe_count += 1

    from secure_ppt_image_gen import (
        harden_request_context,
        validate_generator_contract,
        validate_outbound_url,
    )
    import secure_ppt_image_gen as secure_launcher

    for name, url, expected_error in (
        ("cross-host request", "https://other.invalid/v1/images/edits", "unapproved host"),
        ("plaintext request", "http://probe.invalid/v1/images/edits", "non-HTTPS"),
        (
            "query-bearing request",
            "https://probe.invalid/v1/images/edits?key=placeholder",
            "query or fragment",
        ),
    ):
        try:
            validate_outbound_url(url, "probe.invalid")
        except RuntimeError as exc:
            if expected_error not in str(exc):
                raise RuntimeError(f"Transport probe '{name}' raised the wrong error") from exc
        else:
            raise RuntimeError(f"Transport probe '{name}' did not fail closed")
        print(f"PASS: {name}")
        additional_probe_count += 1

    class CredentialReadTrap(dict[str, str]):
        def get(self, key: str, default: Any = None) -> Any:
            if key == "OPENAI_API_KEY":
                raise AssertionError("credential was read before endpoint consent")
            return super().get(key, default)

    original_environment = legacy.os.environ
    trapped_environment = CredentialReadTrap(dict(original_environment))
    trapped_environment.update({
        legacy.UPLOAD_APPROVAL_HOST_ENV: "approved.invalid",
        legacy.UPLOAD_APPROVAL_SCOPE_ENV: legacy.UPLOAD_APPROVAL_SCOPE,
        "OPENAI_BASE_URL": "https://unapproved.invalid/v1",
    })
    legacy.os.environ = trapped_environment
    try:
        try:
            legacy.resolve_runtime_environment()
        except AssertionError as exc:
            raise RuntimeError("Credential-order probe read a key before host consent") from exc
        except RuntimeError as exc:
            if "is not approved" not in str(exc):
                raise RuntimeError("Credential-order probe raised the wrong consent error") from exc
        else:
            raise RuntimeError("Credential-order probe did not fail on host mismatch")
    finally:
        legacy.os.environ = original_environment
    print("PASS: endpoint consent precedes credential resolution")
    additional_probe_count += 1

    try:
        legacy._NoRedirectHandler().redirect_request(
            None, None, 302, "Found", {}, "https://other.invalid/v1/models"
        )
    except RuntimeError as exc:
        if "redirect blocked" not in str(exc):
            raise RuntimeError("Model redirect probe raised the wrong error") from exc
    else:
        raise RuntimeError("Model redirect probe did not fail closed")
    print("PASS: model discovery redirect")
    additional_probe_count += 1

    class FakeModelResponse:
        def __init__(self, status: int, payload: bytes) -> None:
            self.status = status
            self.payload = payload

        def getcode(self) -> int:
            return self.status

        def read(self) -> bytes:
            return self.payload

        def __enter__(self) -> "FakeModelResponse":
            return self

        def __exit__(self, *_: Any) -> None:
            return None

    class FakeModelOpener:
        def __init__(self, response: FakeModelResponse) -> None:
            self.response = response

        def open(self, *_: Any, **__: Any) -> FakeModelResponse:
            return self.response

    model_payload = b'{"data":[{"id":"gpt-image-2"}]}'
    fake_runtime = {
        "IMAGE_BACKEND": "openai",
        "OPENAI_API_KEY": "probe-key-placeholder",
        "OPENAI_BASE_URL": "https://probe.invalid/v1",
        "OPENAI_MODEL": "gpt-image-2",
        "OPENAI_OUTPUT_FORMAT": "png",
        "OPENAI_SIZE_PRESET": "gpt-image-2",
        "OPENAI_RESPONSE_FORMAT": "b64_json",
        "OPENAI_QUALITY": "high",
        "OPENAI_BACKGROUND": "auto",
    }
    original_build_opener = legacy.urllib.request.build_opener
    approval_backup = {
        key: os.environ.get(key)
        for key in (
            legacy.UPLOAD_APPROVAL_HOST_ENV,
            legacy.UPLOAD_APPROVAL_SCOPE_ENV,
        )
    }
    os.environ[legacy.UPLOAD_APPROVAL_HOST_ENV] = "probe.invalid"
    os.environ[legacy.UPLOAD_APPROVAL_SCOPE_ENV] = legacy.UPLOAD_APPROVAL_SCOPE
    try:
        for name, approved_host, approved_scope, expected_error in (
            (
                "missing approved host",
                "",
                legacy.UPLOAD_APPROVAL_SCOPE,
                "explicit current-process approval is required",
            ),
            (
                "missing approved scope",
                "probe.invalid",
                "",
                "explicit current-process approval is required",
            ),
            (
                "wrong approved scope",
                "probe.invalid",
                "other-scope",
                "must equal",
            ),
            (
                "invalid approved host",
                "https://probe.invalid",
                legacy.UPLOAD_APPROVAL_SCOPE,
                "must be one bare host name",
            ),
        ):
            os.environ[legacy.UPLOAD_APPROVAL_HOST_ENV] = approved_host
            os.environ[legacy.UPLOAD_APPROVAL_SCOPE_ENV] = approved_scope
            try:
                legacy.require_upload_consent()
            except RuntimeError as exc:
                if expected_error not in str(exc):
                    raise RuntimeError(
                        f"Upload-consent probe '{name}' raised the wrong error"
                    ) from exc
            else:
                raise RuntimeError(
                    f"Upload-consent probe '{name}' did not fail closed"
                )
        os.environ[legacy.UPLOAD_APPROVAL_HOST_ENV] = "probe.invalid"
        os.environ[legacy.UPLOAD_APPROVAL_SCOPE_ENV] = legacy.UPLOAD_APPROVAL_SCOPE
        print("PASS: upload consent rejects missing, wrong-scope, and invalid-host approval")
        additional_probe_count += 1

        for status in (201, 302, 500):
            legacy.urllib.request.build_opener = lambda *_, status=status: FakeModelOpener(
                FakeModelResponse(status, model_payload)
            )
            try:
                legacy.assert_gpt_image_2(fake_runtime)
            except RuntimeError as exc:
                if "HTTP status" not in str(exc):
                    raise RuntimeError(
                        f"Model status probe {status} raised the wrong error"
                    ) from exc
            else:
                raise RuntimeError(f"Model status probe {status} did not fail closed")
            print(f"PASS: model discovery rejects HTTP {status}")
            additional_probe_count += 1

        legacy.urllib.request.build_opener = lambda *_: FakeModelOpener(
            FakeModelResponse(200, b"[]")
        )
        try:
            legacy.assert_gpt_image_2(fake_runtime)
        except RuntimeError as exc:
            if "data array" not in str(exc):
                raise RuntimeError("Model payload probe raised the wrong error") from exc
        else:
            raise RuntimeError("Model payload probe did not fail closed")
        print("PASS: model discovery rejects invalid payload shape")
        additional_probe_count += 1

        legacy.urllib.request.build_opener = lambda *_: FakeModelOpener(
            FakeModelResponse(200, b'{"data":[{"id":"gpt-image-1.5"}]}')
        )
        try:
            legacy.assert_gpt_image_2(fake_runtime)
        except RuntimeError as exc:
            if "exact model gpt-image-2 is unavailable" not in str(exc):
                raise RuntimeError("Exact-model absence probe raised the wrong error") from exc
        else:
            raise RuntimeError("Exact-model absence probe did not fail closed")
        print("PASS: model discovery rejects a valid list without exact gpt-image-2")
        additional_probe_count += 1

        invalid_api_roots = (
            "http://probe.invalid/v1",
            "https://user:pass@probe.invalid/v1",
            "https://probe.invalid/v1?sig=placeholder",
            "https://probe.invalid/v1#fragment",
        )
        for invalid_api_root in invalid_api_roots:
            invalid_runtime = {**fake_runtime, "OPENAI_BASE_URL": invalid_api_root}
            try:
                legacy.assert_gpt_image_2(invalid_runtime)
            except (RuntimeError, ValueError):
                continue
            raise RuntimeError(
                f"Model discovery accepted unsafe API root: {invalid_api_root}"
            )
        print("PASS: model discovery rejects unsafe direct runtime API roots")
        additional_probe_count += 1

        captured_handlers: list[Any] = []

        def proxy_free_opener(*handlers: Any) -> FakeModelOpener:
            captured_handlers.extend(handlers)
            return FakeModelOpener(FakeModelResponse(200, model_payload))

        legacy.urllib.request.build_opener = proxy_free_opener
        legacy.assert_gpt_image_2(fake_runtime)
        if not any(
            isinstance(handler, legacy.urllib.request.ProxyHandler)
            and handler.proxies == {}
            for handler in captured_handlers
        ):
            raise RuntimeError("Model discovery did not disable proxy resolution")
        if not any(
            isinstance(handler, legacy._NoRedirectHandler)
            for handler in captured_handlers
        ):
            raise RuntimeError("Model discovery did not install the no-redirect handler")
        print(
            "PASS: model discovery accepts exact model under HTTP 200 without proxies or redirects"
        )
        additional_probe_count += 1

        environment_probe_values = {
            "HTTP_PROXY": "http://127.0.0.1:9",
            "HTTPS_PROXY": "http://127.0.0.1:9",
            "ALL_PROXY": "http://127.0.0.1:9",
            "NO_PROXY": "probe.invalid",
            "PYTHONPATH": "probe-path",
            "REQUESTS_CA_BUNDLE": "probe-ca",
            "CURL_CA_BUNDLE": "probe-ca",
            "SSL_CERT_FILE": "probe-ca",
            "PYTHONHTTPSVERIFY": "0",
            "CLEARVISION_UNRELATED_SECRET": "probe-secret",
        }
        environment_backup = {
            key: os.environ.get(key) for key in environment_probe_values
        }
        os.environ.update(environment_probe_values)
        try:
            bound_runtime = {
                **fake_runtime,
                **environment_probe_values,
                legacy.PPT_GENERATOR_SHA_ENV: preflight[
                    "ppt_image_generator_sha256"
                ],
                legacy.SECURE_LAUNCHER_SHA_ENV: preflight[
                    "secure_launcher_sha256"
                ],
            }
            child_environment = legacy.build_child_environment(bound_runtime)
        finally:
            for key, value in environment_backup.items():
                if value is None:
                    os.environ.pop(key, None)
                else:
                    os.environ[key] = value
        leaked = sorted(set(environment_probe_values) & set(child_environment))
        if leaked:
            raise RuntimeError(
                "Minimal child environment probe leaked blocked keys: "
                + ", ".join(leaked)
            )
        print("PASS: secured child environment excludes proxy, CA, path, and unrelated secrets")
        additional_probe_count += 1

        stale_runtime = dict(bound_runtime)
        stale_runtime[legacy.PPT_GENERATOR_SHA_ENV] = "0" * 64
        try:
            legacy.build_child_environment(stale_runtime)
        except RuntimeError as exc:
            if "changed after the active preflight" not in str(exc):
                raise RuntimeError("Generator hash probe raised the wrong error") from exc
        else:
            raise RuntimeError("Generator hash probe did not fail closed")
        print("PASS: child launch is bound to the preflight generator hash")
        additional_probe_count += 1

        stale_launcher_runtime = dict(bound_runtime)
        stale_launcher_runtime[legacy.SECURE_LAUNCHER_SHA_ENV] = "0" * 64
        try:
            legacy.build_child_environment(stale_launcher_runtime)
        except RuntimeError as exc:
            if "Secured launcher changed after the active preflight" not in str(exc):
                raise RuntimeError("Launcher hash probe raised the wrong error") from exc
        else:
            raise RuntimeError("Launcher hash probe did not fail closed")
        print("PASS: child launch is bound to the preflight secured-launcher hash")
        additional_probe_count += 1

        query_log_samples = (
            "url=https://probe.invalid/v1/result?sig=opaque-query-secret",
            "url=https://probe.invalid/v1/result?sig%6e=encoded-query-secret",
            "url=https://probe.invalid/v1/result?foo[]=bracket-query-secret",
            "url=https://probe.invalid/v1/result?bare-query-secret#fragment-secret",
            "url=https://probe.invalid/v1/result?foo=quoted-query-secret'tail-secret",
            "component=%3Ffoo=encoded-delimiter-query-secret",
            "component=%23encoded-delimiter-fragment-secret",
            "prefixhttps://user:password@other.invalid/path-secret?word-adjacent-query-secret",
            "prefixhttps%3A%2F%2Fencoded-user%3Aencoded-password%40encoded.invalid%2Fencoded-path%3Fq%3Dencoded-full-query-secret%23encoded-full-fragment-secret",
        )
        sanitized_queries = "\n".join(
            legacy.sanitize_log(sample, fake_runtime)
            for sample in query_log_samples
        )
        if any(
            secret in sanitized_queries
            for secret in (
                "opaque-query-secret",
                "encoded-query-secret",
                "bracket-query-secret",
                "bare-query-secret",
                "fragment-secret",
                "quoted-query-secret",
                "tail-secret",
                "encoded-delimiter-query-secret",
                "encoded-delimiter-fragment-secret",
                "user:password",
                "other.invalid",
                "path-secret",
                "word-adjacent-query-secret",
                "encoded-user",
                "encoded-password",
                "encoded.invalid",
                "encoded-path",
                "encoded-full-query-secret",
                "encoded-full-fragment-secret",
            )
        ):
            raise RuntimeError("Log sanitizer retained a query or fragment value")
        print("PASS: log sanitizer redacts complete query and fragment values")
        additional_probe_count += 1

        fixed_environment_backup = {
            key: os.environ.get(key)
            for key in (
                "IMAGE_BACKEND",
                "OPENAI_MODEL",
                "OPENAI_OUTPUT_FORMAT",
                "OPENAI_RESPONSE_FORMAT",
            )
        }
        os.environ.update(
            {
                "IMAGE_BACKEND": "openai",
                "OPENAI_MODEL": "gpt-image-2",
                "OPENAI_OUTPUT_FORMAT": "png",
                "OPENAI_RESPONSE_FORMAT": "b64_json",
            }
        )
        try:
            validate_generator_contract(
                ["prompt", "--backend", "openai", "--model", "other-model"]
            )
        except RuntimeError as exc:
            if "exact model gpt-image-2" not in str(exc):
                raise RuntimeError("Launcher model probe raised the wrong error") from exc
        else:
            raise RuntimeError("Launcher model probe did not fail closed")
        finally:
            for key, value in fixed_environment_backup.items():
                if value is None:
                    os.environ.pop(key, None)
                else:
                    os.environ[key] = value
        print("PASS: secured launcher forbids model fallback")
        additional_probe_count += 1

        launcher_environment = {
            secure_launcher.APPROVED_HOST_ENV: "probe.invalid",
            secure_launcher.APPROVED_SCOPE_ENV: secure_launcher.APPROVED_SCOPE,
            "OPENAI_BASE_URL": "https://probe.invalid/v1",
            secure_launcher.EXPECTED_LAUNCHER_SHA_ENV: secure_launcher.sha256(
                Path(secure_launcher.__file__).resolve()
            ),
            secure_launcher.EXPECTED_GENERATOR_ENV: str(
                secure_launcher.APPROVED_GENERATOR
            ),
            secure_launcher.EXPECTED_GENERATOR_SHA_ENV: secure_launcher.sha256(
                secure_launcher.APPROVED_GENERATOR
            ),
            "IMAGE_BACKEND": "openai",
            "OPENAI_MODEL": "gpt-image-2",
            "OPENAI_OUTPUT_FORMAT": "png",
            "OPENAI_RESPONSE_FORMAT": "b64_json",
        }
        launcher_environment_backup = {
            key: os.environ.get(key) for key in launcher_environment
        }
        original_launcher_argv = list(secure_launcher.sys.argv)
        original_launcher_sys_path = list(secure_launcher.sys.path)
        original_install_guard = secure_launcher.install_requests_guard
        original_run_path = secure_launcher.runpy.run_path
        guarded_hosts: list[str] = []
        executed_targets: list[tuple[str, str | None]] = []
        os.environ.update(launcher_environment)
        launcher_base_argv = [
            str(Path(secure_launcher.__file__).resolve()),
            str(secure_launcher.APPROVED_GENERATOR),
            "offline probe prompt",
            "--backend",
            "openai",
            "--model",
            "gpt-image-2",
        ]
        secure_launcher.sys.argv = list(launcher_base_argv)
        secure_launcher.install_requests_guard = guarded_hosts.append
        secure_launcher.runpy.run_path = (
            lambda path, run_name=None: executed_targets.append((path, run_name)) or {}
        )
        try:
            for name, environment_overrides, argv, expected_error in (
                (
                    "wrong upload scope",
                    {secure_launcher.APPROVED_SCOPE_ENV: "other-scope"},
                    launcher_base_argv,
                    "scope is not approved",
                ),
                (
                    "stale launcher hash",
                    {secure_launcher.EXPECTED_LAUNCHER_SHA_ENV: "0" * 64},
                    launcher_base_argv,
                    "launcher changed",
                ),
                (
                    "stale generator hash",
                    {secure_launcher.EXPECTED_GENERATOR_SHA_ENV: "0" * 64},
                    launcher_base_argv,
                    "generator changed",
                ),
                (
                    "unapproved generator path",
                    {},
                    [
                        launcher_base_argv[0],
                        str(Path(secure_launcher.__file__).resolve()),
                        *launcher_base_argv[2:],
                    ],
                    "outside the approved workflow path",
                ),
                (
                    "wrong backend argument",
                    {},
                    [
                        *launcher_base_argv[:-3],
                        "other",
                        "--model",
                        "gpt-image-2",
                    ],
                    "only permits the OpenAI backend",
                ),
                (
                    "wrong fixed environment",
                    {"IMAGE_BACKEND": "other"},
                    launcher_base_argv,
                    "requires IMAGE_BACKEND=openai",
                ),
            ):
                os.environ.update(launcher_environment)
                os.environ.update(environment_overrides)
                secure_launcher.sys.argv = list(argv)
                try:
                    secure_launcher.main()
                except RuntimeError as exc:
                    if expected_error.lower() not in str(exc).lower():
                        raise RuntimeError(
                            f"Secured-launcher probe '{name}' raised the wrong error"
                        ) from exc
                else:
                    raise RuntimeError(
                        f"Secured-launcher probe '{name}' did not fail closed"
                    )
            print("PASS: secured launcher rejects stale or unauthorized launch contracts")
            additional_probe_count += 1

            os.environ.update(launcher_environment)
            secure_launcher.sys.argv = list(launcher_base_argv)
            secure_launcher.main()
        finally:
            secure_launcher.install_requests_guard = original_install_guard
            secure_launcher.runpy.run_path = original_run_path
            secure_launcher.sys.argv = original_launcher_argv
            secure_launcher.sys.path[:] = original_launcher_sys_path
            for key, value in launcher_environment_backup.items():
                if value is None:
                    os.environ.pop(key, None)
                else:
                    os.environ[key] = value
        if guarded_hosts != ["probe.invalid"]:
            raise RuntimeError("Secured launcher did not install the approved-host guard")
        if executed_targets != [(str(secure_launcher.APPROVED_GENERATOR), "__main__")]:
            raise RuntimeError("Secured launcher did not execute only the approved generator")
        print("PASS: secured launcher enforces the bound path, hashes, environment, and host")
        additional_probe_count += 1

        class ProbeSession:
            def __init__(self) -> None:
                self.params: dict[str, str] = {}
                self.proxies: dict[str, str] = {}
                self.verify: Any = True
                self.auth: Any = None
                self.cert: Any = None
                self.trust_env = True

        class FalseyDict(dict[str, str]):
            def __bool__(self) -> bool:
                return False

        for name, configure, expected_error in (
            (
                "session query",
                lambda session: session.params.update({"token": "placeholder"}),
                "session containing query parameters",
            ),
            (
                "session proxy",
                lambda session: session.proxies.update(
                    {"https": "http://127.0.0.1:9"}
                ),
                "session using a proxy",
            ),
            (
                "disabled TLS",
                lambda session: setattr(session, "verify", 0),
                "without standard TLS verification",
            ),
            (
                "falsey session query",
                lambda session: setattr(
                    session, "params", FalseyDict({"token": "placeholder"})
                ),
                "session containing query parameters",
            ),
            (
                "session auth",
                lambda session: setattr(session, "auth", ("user", "password")),
                "session containing implicit authentication",
            ),
            (
                "session client certificate",
                lambda session: setattr(session, "cert", "client.pem"),
                "session containing a client certificate",
            ),
            (
                "falsey session auth",
                lambda session: setattr(session, "auth", False),
                "session containing implicit authentication",
            ),
            (
                "falsey session client certificate",
                lambda session: setattr(session, "cert", ""),
                "session containing a client certificate",
            ),
        ):
            session = ProbeSession()
            configure(session)
            try:
                harden_request_context(session, {})  # type: ignore[arg-type]
            except RuntimeError as exc:
                if expected_error not in str(exc):
                    raise RuntimeError(
                        f"Request-context probe '{name}' raised the wrong error"
                    ) from exc
            else:
                raise RuntimeError(
                    f"Request-context probe '{name}' did not fail closed"
                )
        for name, kwargs, expected_error in (
            (
                "request proxy",
                {"proxies": {"https": "http://127.0.0.1:9"}},
                "request using an explicit proxy",
            ),
            (
                "request disabled TLS",
                {"verify": False},
                "request without standard TLS verification",
            ),
            (
                "request auth",
                {"auth": ("user", "password")},
                "request containing implicit authentication",
            ),
            (
                "request client certificate",
                {"cert": "client.pem"},
                "request containing a client certificate",
            ),
            (
                "falsey request auth",
                {"auth": False},
                "request containing implicit authentication",
            ),
            (
                "falsey request client certificate",
                {"cert": ""},
                "request containing a client certificate",
            ),
        ):
            try:
                harden_request_context(ProbeSession(), kwargs)  # type: ignore[arg-type]
            except RuntimeError as exc:
                if expected_error not in str(exc):
                    raise RuntimeError(
                        f"Request-context probe '{name}' raised the wrong error"
                    ) from exc
            else:
                raise RuntimeError(
                    f"Request-context probe '{name}' did not fail closed"
                )
        try:
            harden_request_context(
                ProbeSession(),
                {"params": FalseyDict({"token": "placeholder"})},
            )  # type: ignore[arg-type]
        except RuntimeError as exc:
            if "request containing query parameters" not in str(exc):
                raise RuntimeError(
                    "Falsey request-params probe raised the wrong error"
                ) from exc
        else:
            raise RuntimeError("Falsey request-params probe did not fail closed")
        clean_session = ProbeSession()
        clean_kwargs: dict[str, Any] = {"allow_redirects": True}
        harden_request_context(clean_session, clean_kwargs)  # type: ignore[arg-type]
        if (
            clean_session.trust_env is not False
            or clean_kwargs.get("verify") is not True
            or clean_kwargs.get("allow_redirects") is not False
        ):
            raise RuntimeError("Request-context hardening did not set secure defaults")
        print(
            "PASS: requests guard rejects query, proxy, weak TLS, auth, and client-certificate state"
        )
        additional_probe_count += 1

        class FakeRedirectResponse:
            status_code = 302

            def __init__(self) -> None:
                self.closed = False

            def close(self) -> None:
                self.closed = True

        original_session_request = secure_launcher.requests.sessions.Session.request
        redirect_response = FakeRedirectResponse()

        def fake_redirect_request(*_: Any, **__: Any) -> FakeRedirectResponse:
            return redirect_response

        secure_launcher.requests.sessions.Session.request = fake_redirect_request
        try:
            secure_launcher.install_requests_guard("probe.invalid")
            try:
                secure_launcher.requests.Session().request(
                    "GET", "https://probe.invalid/v1/images/edits"
                )
            except RuntimeError as exc:
                if "redirect blocked" not in str(exc):
                    raise RuntimeError("Image redirect probe raised the wrong error") from exc
            else:
                raise RuntimeError("Image redirect probe did not fail closed")
            if not redirect_response.closed:
                raise RuntimeError("Image redirect probe did not close the response")
        finally:
            secure_launcher.requests.sessions.Session.request = original_session_request
        print("PASS: image API response redirects are closed and rejected")
        additional_probe_count += 1
    finally:
        legacy.urllib.request.build_opener = original_build_opener
        for key, value in approval_backup.items():
            if value is None:
                os.environ.pop(key, None)
            else:
                os.environ[key] = value

    temp_root = ROOT / "workflow" / "tmp"
    temp_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="evidence_probe_", dir=temp_root) as directory:
        normalized_endpoint_png = Path(directory) / "normalized-endpoint.png"
        Image.new("RGB", (1672, 941), "#101010").save(normalized_endpoint_png, format="PNG")
        normalization_option = "E"
        normalization_dimensions = expected_output_dimensions(normalization_option)
        normalization = normalize_generated_png(normalized_endpoint_png, normalization_option)
        errors, dimensions = output_file_errors(normalized_endpoint_png, normalization_option)
        if errors or dimensions != normalization_dimensions:
            raise RuntimeError("Near-16:9 normalization probe did not produce the E delivery contract")
        if (
            normalization["source_dimensions"] != {"width": 1672, "height": 941}
            or normalization["normalized_dimensions"] != normalization_dimensions
            or normalization["normalization_applied"] is not True
            or normalization["normalization_method"] != NORMALIZATION_METHOD_FIT
        ):
            raise RuntimeError("Near-16:9 normalization evidence probe drifted")
        print("PASS: near-16:9 endpoint output remains auditably normalized for option E")
        additional_probe_count += 1

        native_d_png = Path(directory) / "native-d-4k.png"
        Image.new("RGB", (3840, 2160), "#101010").save(native_d_png, format="PNG")
        native_option = "D"
        native_dimensions = expected_output_dimensions(native_option)
        native_evidence = normalize_generated_png(native_d_png, native_option)
        errors, dimensions = output_file_errors(native_d_png, native_option)
        if errors or dimensions != native_dimensions:
            raise RuntimeError("Native D 4K probe did not preserve the delivery contract")
        if (
            native_evidence["source_dimensions"] != native_dimensions
            or native_evidence["normalized_dimensions"] != native_dimensions
            or native_evidence["normalization_applied"] is not False
            or native_evidence["normalization_method"] != NORMALIZATION_METHOD_NONE
        ):
            raise RuntimeError("Native D 4K evidence probe drifted")
        print("PASS: native 3840x2160 endpoint output is accepted for option D without normalization")
        additional_probe_count += 1

        normalized_d_png = Path(directory) / "normalized-d.png"
        Image.new("RGB", (1672, 941), "#101010").save(normalized_d_png, format="PNG")
        normalized_d_evidence = normalize_generated_png(normalized_d_png, native_option)
        errors, dimensions = output_file_errors(normalized_d_png, native_option)
        if errors or dimensions != native_dimensions:
            raise RuntimeError("Near-16:9 D normalization probe did not produce 3840x2160")
        if (
            normalized_d_evidence["source_dimensions"] != {"width": 1672, "height": 941}
            or normalized_d_evidence["normalized_dimensions"] != native_dimensions
            or normalized_d_evidence["normalization_applied"] is not True
            or normalized_d_evidence["normalization_method"] != NORMALIZATION_METHOD_FIT
        ):
            raise RuntimeError("Near-16:9 D normalization evidence probe drifted")
        print("PASS: option D records and normalizes provider output to 3840x2160")
        additional_probe_count += 1

        off_ratio_png = Path(directory) / "off-ratio.png"
        Image.new("RGB", (1024, 768), "#101010").save(off_ratio_png, format="PNG")
        try:
            normalize_generated_png(off_ratio_png, normalization_option)
        except RuntimeError as exc:
            if "outside the 16:9 normalization tolerance" not in str(exc):
                raise RuntimeError("Off-ratio normalization probe raised the wrong error") from exc
        else:
            raise RuntimeError("Off-ratio normalization probe did not fail closed")
        print("PASS: off-ratio endpoint output is rejected before normalization")
        additional_probe_count += 1

        small_png = Path(directory) / "small.png"
        Image.new("RGB", (1024, 576), "#101010").save(small_png, format="PNG")
        errors, _ = output_file_errors(small_png, native_option)
        if not any("output dimensions must be" in error for error in errors):
            raise RuntimeError("Output dimension probe did not fail closed")
        print("PASS: non-contract output dimensions")
        additional_probe_count += 1

        disguised_jpeg = Path(directory) / "disguised.png"
        Image.new("RGB", (64, 64), "#101010").save(disguised_jpeg, format="JPEG")
        errors, _ = output_file_errors(disguised_jpeg, native_option)
        if not any("output content is not PNG" in error for error in errors):
            raise RuntimeError("Output format probe did not fail closed")
        print("PASS: non-PNG output content")
        additional_probe_count += 1

    shutil.rmtree(probe_board_dir, ignore_errors=True)
    print(
        "Evidence/security mutation probes passed: "
        f"{len(evidence_mutations) + additional_probe_count}"
    )


def build_readiness_manifest(manifest: dict[str, Any]) -> dict[str, Any]:
    entries = [entry for entry in manifest.get("entries", []) if isinstance(entry, dict)]
    by_id = {entry.get("id"): entry for entry in entries}
    status_counts = {
        status: sum(1 for entry in entries if entry.get("status") == status)
        for status in sorted(ALLOWED_STATUSES)
    }
    functional_ready = sum(
        1 for entry in entries
        if isinstance(entry.get("functional_audit"), dict)
        and entry["functional_audit"].get("status") == "passed"
        and entry["functional_audit"].get("page_exists") is True
    )
    master_records: dict[str, list[dict[str, Any]]] = {option: [] for option in OPTIONS}
    master_ready = 0
    for option in OPTIONS:
        for role, screen_id in MASTER_SCREENS.items():
            page_def = next(item for item in PAGES if item["screen_id"] == screen_id)
            current_id = entry_id(option, screen_id)
            entry = by_id.get(current_id, {})
            path = option_image(option, "masters", page_def["filename"])
            exists = path.is_file()
            digest = legacy.sha256(path) if exists else None
            candidate_errors = (
                candidate_evidence_errors(entry, page_def, manifest)
                if isinstance(entry, dict)
                else ["Master entry is missing"]
            )
            review_ready_for_master = not review_evidence_errors(
                entry, page_def, manifest
            )
            ready = (
                exists
                and entry.get("status") == "Approved-Candidate"
                and entry.get("master_path") == legacy.rel(path)
                and entry.get("master_sha256") == digest
                and entry.get("master_sha256") == entry.get("sha256")
                and entry.get("master_source_sha256") == entry.get("sha256")
                and not candidate_errors
                and review_ready_for_master
            )
            master_ready += int(ready)
            master_records[option].append({
                "id": current_id,
                "role": role,
                "path": legacy.rel(path),
                "exists": exists,
                "sha256": digest,
                "status": entry.get("status", "Missing"),
                "ready": ready,
            })

    output_ready = 0
    review_ready = 0
    entry_records: list[dict[str, Any]] = []
    for entry in entries:
        option = entry.get("option")
        filename = entry.get("filename")
        candidate = (
            option_image(option, "screens", filename)
            if option in OPTIONS and isinstance(filename, str)
            else None
        )
        exists = bool(candidate and candidate.is_file())
        digest = legacy.sha256(candidate) if candidate and exists else None
        page_def = next(
            (item for item in PAGES if item["screen_id"] == entry.get("screen_id")),
            None,
        )
        output_errors = (
            candidate_evidence_errors(entry, page_def, manifest)
            if isinstance(page_def, dict)
            else ["screen contract is missing"]
        )
        output_is_ready = not output_errors
        review = entry.get("reject_gate")
        review_is_ready = output_is_ready and not review_evidence_errors(
            entry, page_def, manifest
        )
        output_ready += int(output_is_ready)
        review_ready += int(review_is_ready)
        entry_records.append({
            "id": entry.get("id"),
            "option": option,
            "screen_id": entry.get("screen_id"),
            "filename": filename,
            "status": entry.get("status"),
            "contract_sha256": entry.get("contract_sha256"),
            "functional_audit": entry.get("functional_audit", {}).get("status"),
            "current_reference": entry.get("current_reference"),
            "master_references": entry.get("master_references", []),
            "output": legacy.rel(candidate) if candidate and exists else None,
            "output_sha256": digest,
            "output_ready": output_is_ready,
            "reject_gate": review.get("status") if isinstance(review, dict) else "missing",
            "review_ready": review_is_ready,
        })

    preflight = manifest.get("preflight")
    preflight_ready = not preflight_evidence_errors(manifest)
    structural_errors = validate_manifest(manifest)
    strict_errors = validate_manifest(
        manifest, require_masters=True, require_outputs=True, require_reviews=True
    )
    blockers: list[str] = []
    if structural_errors:
        blockers.append(f"manifest/contracts invalid ({len(structural_errors)} errors)")
    if not preflight_ready:
        blockers.append("authenticated /models preflight not performed with explicit upload approval")
    if master_ready != len(OPTIONS) * len(MASTER_SCREENS):
        blockers.append(f"Master Screens ready {master_ready}/{len(OPTIONS) * len(MASTER_SCREENS)}")
    if output_ready != len(PAGES) * len(OPTIONS):
        blockers.append(f"generated outputs ready {output_ready}/{len(PAGES) * len(OPTIONS)}")
    if review_ready != len(PAGES) * len(OPTIONS):
        blockers.append(f"manual Reject Gates ready {review_ready}/{len(PAGES) * len(OPTIONS)}")
    ready = not blockers and not strict_errors

    screens = []
    records_by_key = {
        (record["screen_id"], record["option"]): record for record in entry_records
    }
    for page_def in PAGES:
        screens.append({
            "screen_id": page_def["screen_id"],
            "filename": page_def["filename"],
            "page_name": page_def["page_name"],
            "purpose": page_def["purpose"],
            "current_reference": page_def["current_reference"],
            "options": {
                option: records_by_key.get((page_def["screen_id"], option))
                for option in OPTIONS
            },
        })
    return {
        "schema_version": DELIVERY_SCHEMA_VERSION,
        "generated_at": utc_now(),
        "approval_status": "awaiting-product-owner-selection",
        "model": "gpt-image-2",
        "configured_model_fallback_allowed": False,
        "screen_count": len(PAGES),
        "option_count": len(OPTIONS),
        "entry_count": len(PAGES) * len(OPTIONS),
        "options": list(OPTIONS),
        "identical_coverage": actual_ids_match(entries),
        "functional_audit": {
            "passed": functional_ready,
            "total": len(PAGES) * len(OPTIONS),
        },
        "preflight": deepcopy(preflight) if isinstance(preflight, dict) else {
            "performed": False,
            "exact_model": "gpt-image-2",
            "model_discovered": False,
            "approved_upload_host": None,
            "approved_upload_scope": None,
        },
        "status_counts": status_counts,
        "masters": master_records,
        "readiness": {
            "overall": "READY" if ready else "NOT_READY",
            "manifest_contracts_valid": not structural_errors,
            "preflight_ready": preflight_ready,
            "masters_ready": master_ready,
            "masters_total": len(OPTIONS) * len(MASTER_SCREENS),
            "outputs_ready": output_ready,
            "outputs_total": len(PAGES) * len(OPTIONS),
            "reviews_ready": review_ready,
            "reviews_total": len(PAGES) * len(OPTIONS),
            "blockers": blockers,
        },
        "copy_and_data_policy": "Generated copy and data are not product authority.",
        "entries": entry_records,
        "screens": screens,
    }


def actual_ids_match(entries: list[dict[str, Any]]) -> bool:
    expected = [entry_id(option, item["screen_id"]) for option in OPTIONS for item in PAGES]
    return [entry.get("id") for entry in entries] == expected


def write_readiness_artifacts(manifest: dict[str, Any]) -> dict[str, Any]:
    payload = build_readiness_manifest(manifest)
    legacy.atomic_write_json(DELIVERY_PATH, payload)
    readiness = payload["readiness"]
    status_counts = payload["status_counts"]
    index = f"""# ClearVision D/E Visual Master Readiness

CURRENT screenshots and current ClearVision code remain the functional authority. Generated copy, data, device facts, workflow names, and state labels are never product truth.

## Active Scope

- Options: `D - Roboflow Workflow Engineering`, `E - Apple-inspired Premium Engineering`.
- Frozen real screens/states: `{payload['screen_count']}`; planned candidates: `{payload['entry_count']}`.
- Functional contracts passed: `{payload['functional_audit']['passed']}/{payload['functional_audit']['total']}`.
- Generated outputs ready: `{readiness['outputs_ready']}/{readiness['outputs_total']}`.
- Master Screens ready: `{readiness['masters_ready']}/{readiness['masters_total']}`.
- Manual Reject Gates ready: `{readiness['reviews_ready']}/{readiness['reviews_total']}`.
- Model preflight ready: `{str(readiness['preflight_ready']).lower()}`; exact model: `gpt-image-2`; fallback allowed: `false`.
- Overall readiness: `{readiness['overall']}`.
- Status counts: `Pending={status_counts.get('Pending', 0)}`, `Generated={status_counts.get('Generated', 0)}`, `Needs-Manual={status_counts.get('Needs-Manual', 0)}`, `Failed={status_counts.get('Failed', 0)}`, `Approved-Candidate={status_counts.get('Approved-Candidate', 0)}`.

## Current Blockers

{chr(10).join(f'- {blocker}' for blocker in readiness['blockers']) or '- None.'}

## Audit Entry Points

- Active machine-readable readiness: `_visual_master/manifest.json`.
- Frozen contracts/prompts: `_visual_master/image_prompts.json`.
- Functional remapping: `_visual_master/functional_remapping.json`.
- The gated `audit` command writes D/E comparisons under `_visual_master/audit/comparison_DE/`; it runs only after all outputs and manual Reject Gates pass.
- Product-owner C/D/E review: `_visual_master/audit/comparison_CDE/audit_index.md`.
- `_visual_master/audit/comparison/` and top-level `option_A/`, `option_B/`, `option_C/` remain archived A/B/C provenance and are not D/E generation inputs; Option C is retained separately in the C/D/E product-owner comparison.
"""
    (ROOT / "audit" / "audit_index.md").write_text(index, encoding="utf-8")
    return payload


def preflight_record(
    approved_host: str, runtime: dict[str, str] | None = None
) -> dict[str, Any]:
    secure_launcher_sha256 = (
        runtime.get(legacy.SECURE_LAUNCHER_SHA_ENV) if runtime else None
    ) or legacy.sha256(legacy.SECURE_PPT_IMAGE_GEN)
    generator_sha256 = (
        runtime.get(legacy.PPT_GENERATOR_SHA_ENV) if runtime else None
    ) or legacy.sha256(legacy.PPT_IMAGE_GEN)
    if secure_launcher_sha256 != legacy.sha256(legacy.SECURE_PPT_IMAGE_GEN):
        raise RuntimeError("Secured launcher changed after model preflight")
    if generator_sha256 != legacy.sha256(legacy.PPT_IMAGE_GEN):
        raise RuntimeError("PPT Master generator changed after model preflight")
    record = {
        "performed": True,
        "exact_model": "gpt-image-2",
        "model_discovered": True,
        "model_fallback_allowed": False,
        "approved_upload_host": approved_host,
        "approved_upload_scope": UPLOAD_APPROVAL_SCOPE,
        "transport_policy": TRANSPORT_POLICY,
        "secure_launcher_sha256": secure_launcher_sha256,
        "ppt_image_generator_sha256": generator_sha256,
        "checked_at": utc_now(),
    }
    record["evidence_sha256"] = contract_sha256({
        key: record.get(key) for key in PREFLIGHT_EVIDENCE_KEYS
    })
    return record


def preflight_command() -> None:
    manifest = load_manifest()
    errors = validate_manifest(manifest)
    if errors:
        raise ValueError("Manifest invalid:\n" + "\n".join(errors))
    runtime = legacy.preflight()
    approved_host = legacy.require_upload_consent(runtime.get("OPENAI_BASE_URL"))
    manifest["preflight"] = preflight_record(approved_host, runtime)
    legacy.atomic_write_json(MANIFEST_PATH, manifest)
    write_readiness_artifacts(manifest)


def fit_image(path: Path, size: tuple[int, int], background: str = "#171c22") -> Image.Image:
    with Image.open(path) as opened:
        image = opened.convert("RGB")
    contained = ImageOps.contain(image, size, Image.Resampling.LANCZOS)
    canvas = Image.new("RGB", size, background)
    x = (size[0] - contained.width) // 2
    y = (size[1] - contained.height) // 2
    canvas.paste(contained, (x, y))
    return canvas


def build_reference_board(
    entry: dict[str, Any], iteration_target: Path | None = None,
    output_override: Path | None = None,
    *, retry_mode: str = "legacy",
) -> tuple[Path, bool]:
    if retry_mode not in RETRY_MODES:
        raise ValueError(f"Unsupported retry mode: {retry_mode}")
    if retry_mode == CURRENT_DOMINANT_RETRY_MODE and iteration_target is not None:
        raise ValueError("current-dominant retry forbids uploading an iteration target")
    current = legacy.root_path(entry["current_reference"], must_exist=True)
    masters = [legacy.root_path(value, must_exist=True) for value in entry["master_references"]]
    architecture = [
        legacy.root_path(value, must_exist=True) for value in entry.get("architecture_references", [])
    ]
    style_references = architecture + masters
    if output_override is None:
        output_dir = ROOT / "workflow" / "reference_boards" / "options" / entry["option"]
        output = legacy.safe_named_path(output_dir, entry["filename"])
    else:
        output = output_override.resolve()
        allowed_probe_root = (ROOT / "workflow" / "tmp" / "contract-probes").resolve()
        if not output.is_relative_to(allowed_probe_root):
            raise ValueError("Reference-board override must stay under workflow/tmp/contract-probes")
    output.parent.mkdir(parents=True, exist_ok=True)

    if retry_mode == CURRENT_DOMINANT_RETRY_MODE:
        width = CURRENT_DOMINANT_BOARD_WIDTH
        height = CURRENT_DOMINANT_BOARD_HEIGHT
        current_width = CURRENT_DOMINANT_CURRENT_WIDTH
        board = Image.new("RGB", (width, height), "#11161b")
        board.paste(fit_image(current, (current_width, height), "#171c22"), (0, 0))
        for reference, (left, top, cell_width, cell_height) in zip(
            style_references,
            current_dominant_style_cells(len(style_references)),
        ):
            board.paste(
                fit_image(reference, (cell_width, cell_height), "#11161b"),
                (left, top),
            )
        draw = ImageDraw.Draw(board)
        draw.line((current_width, 0, current_width, height), fill="#66717c", width=6)
        board.save(output, format="PNG", optimize=True)
        return output, False

    if iteration_target is not None:
        width, height = 3840, 2160
        cell_width, cell_height = width // 2, height // 2
        board = Image.new("RGB", (width, height), "#11161b")
        board.paste(fit_image(current, (cell_width, cell_height), "#171c22"), (0, 0))
        board.paste(
            fit_image(iteration_target, (cell_width, cell_height), "#171c22"),
            (cell_width, 0),
        )
        for index, master in enumerate(style_references[:2]):
            board.paste(
                fit_image(master, (cell_width, cell_height), "#11161b"),
                (index * cell_width, cell_height),
            )
        draw = ImageDraw.Draw(board)
        draw.line((cell_width, 0, cell_width, height), fill="#66717c", width=6)
        draw.line((0, cell_height, width, cell_height), fill="#66717c", width=6)
        board.save(output, format="PNG", optimize=True)
        return output, True

    width, height = 2048, 1152
    if style_references:
        current_width = 1248
        strip_width = width - current_width
        board = Image.new("RGB", (width, height), "#11161b")
        board.paste(fit_image(current, (current_width, height), "#171c22"), (0, 0))
        draw = ImageDraw.Draw(board)
        draw.line((current_width, 0, current_width, height), fill="#66717c", width=6)
        if len(style_references) == 1:
            board.paste(
                fit_image(style_references[0], (strip_width, height), "#11161b"),
                (current_width, 0),
            )
        else:
            gutter = 8
            cell_height = (height - gutter) // 2
            board.paste(
                fit_image(style_references[0], (strip_width, cell_height), "#11161b"),
                (current_width, 0),
            )
            board.paste(
                fit_image(
                    style_references[1],
                    (strip_width, height - cell_height - gutter),
                    "#11161b",
                ),
                (current_width, cell_height + gutter),
            )
    else:
        board = fit_image(current, (width, height), "#171c22")
    board.save(output, format="PNG", optimize=True)
    return output, False


def functional_gate_errors(
    entry: dict[str, Any], manifest: dict[str, Any] | None = None
) -> list[str]:
    errors: list[str] = []
    try:
        page_def = next(
            item for item in PAGES if item["screen_id"] == entry.get("screen_id")
        )
        expected = expected_entry_contract(str(entry.get("option")), page_def)
        if entry_contract_projection(entry) != expected:
            errors.append("immutable entry contract drifted")
        if entry.get("contract_sha256") != contract_sha256(expected):
            errors.append("contract_sha256 drifted")
    except (StopIteration, FileNotFoundError, ValueError) as exc:
        errors.append(f"entry contract could not be resolved: {exc}")
    audit = entry.get("functional_audit")
    if not isinstance(audit, dict) or audit.get("status") != "passed":
        errors.append("functional_audit.status is not passed")
    if not isinstance(audit, dict) or audit.get("page_exists") is not True:
        errors.append("page existence is not confirmed")
    try:
        legacy.root_path(entry["current_reference"], must_exist=True)
    except (KeyError, ValueError, FileNotFoundError) as exc:
        errors.append(str(exc))
    prompt = entry.get("prompt", "")
    if "Never add any plausible button" not in prompt or "Prefer omission over invented functional content" not in prompt:
        errors.append("prompt lacks no-invention constraints")
    for ref in entry.get("master_references", []):
        try:
            legacy.root_path(ref, must_exist=True)
        except (ValueError, FileNotFoundError) as exc:
            errors.append(str(exc))
    for ref in entry.get("architecture_references", []):
        try:
            legacy.root_path(ref, must_exist=True)
        except (ValueError, FileNotFoundError) as exc:
            errors.append(str(exc))
    if entry.get("depends_on"):
        if manifest is None:
            errors.append("Master dependency evidence was not supplied to the functional gate")
        else:
            errors.extend(dependency_evidence_errors(entry, manifest))
    return errors


def generation_command(
    entry: dict[str, Any], reference_image: Path, output_dir: Path, stem: str,
    iteration_target_included: bool, retry_mode: str = "legacy",
) -> list[str]:
    if retry_mode not in RETRY_MODES:
        raise ValueError(f"Unsupported retry mode: {retry_mode}")
    if retry_mode == CURRENT_DOMINANT_RETRY_MODE:
        if iteration_target_included:
            raise ValueError("current-dominant retry cannot include an iteration target")
        reference_note = (
            "CURRENT-DOMINANT RETRY BOARD: CURRENT_REFERENCE occupies approximately 75% of the board and is the "
            "sole functional authority, never a layout template. The narrow right strip contains only architecture "
            "or same-option Master references for structure/style. The rejected previous candidate is not included "
            "in the board and is not uploaded. Produce one single full-screen UI, never the board or a collage. "
            "Apply the explicit correction while preserving the complete CURRENT functional inventory and the "
            "approved D/E remapping. "
        )
    elif iteration_target_included:
        reference_note = (
            "ITERATION REFERENCE BOARD: top-left is CURRENT_REFERENCE and is the sole functional authority, never a "
            "layout template; top-right is ITERATION_TARGET to correct; bottom cells are architecture or same-option "
            "Master references for structure/style only. Produce one single full-screen UI, never the reference board "
            "or a collage. Preserve the CURRENT functional inventory, apply the explicit correction, keep the option's "
            "Master language, and retain the approved D/E remapping rather than the old geometry. "
        )
    else:
        reference_note = (
            "REFERENCE BOARD LAYOUT: CURRENT_REFERENCE is the large left screen and is functional inventory only, "
            "not a layout template. The right region contains architecture references and/or same-option Masters for "
            "structure and visual-system guidance only; it may contain third-party functions or labels that must never "
            "enter ClearVision. The output must be one single screen, never the board itself. "
        )
    return [
        str(legacy.PPT_PYTHON),
        str(legacy.SECURE_PPT_IMAGE_GEN),
        str(legacy.PPT_IMAGE_GEN),
        reference_note
        + entry["prompt"]
        + (
            "\n\nITERATION CORRECTION\n" + entry["iteration_instruction"]
            if entry.get("iteration_instruction")
            else ""
        ),
        "--backend", "openai",
        "--model", "gpt-image-2",
        "--aspect_ratio", entry["aspect_ratio"],
        "--image_size", entry["image_size"],
        "--reference-image", str(reference_image),
        "--output", str(output_dir),
        "--filename", stem,
    ]


def generate_one(
    entry: dict[str, Any], runtime: dict[str, str], manifest: dict[str, Any],
    retry_mode: str = "legacy",
) -> dict[str, Any]:
    if retry_mode not in RETRY_MODES:
        return {
            "status": "Needs-Manual",
            "last_error": f"Unsupported retry mode: {retry_mode}",
            "failed_at": utc_now(),
        }
    iteration_instruction = str(entry.get("iteration_instruction") or "").strip()
    if retry_mode == CURRENT_DOMINANT_RETRY_MODE and not iteration_instruction:
        return {
            "status": "Needs-Manual",
            "last_error": "current-dominant retry requires an audited iteration instruction",
            "failed_at": utc_now(),
        }
    option = entry["option"]
    gate_errors = functional_gate_errors(entry, manifest)
    if gate_errors:
        return {
            "status": "Needs-Manual",
            "last_error": "Functional gate blocked generation: " + "; ".join(gate_errors),
            "failed_at": utc_now(),
        }
    approved_host = legacy.require_upload_consent(runtime.get("OPENAI_BASE_URL"))
    candidate = option_image(option, "screens", entry["filename"])
    iteration_target = (
        candidate
        if retry_mode == "legacy" and iteration_instruction and candidate.is_file()
        else None
    )
    iteration_target_sha256 = legacy.sha256(iteration_target) if iteration_target else None
    board, iteration_target_included = build_reference_board(
        entry, iteration_target, retry_mode=retry_mode
    )
    reference_board_sha256 = legacy.sha256(board)
    reference_image = board
    child_env = legacy.build_child_environment(
        runtime,
        expected_generator_sha256=manifest["preflight"][
            "ppt_image_generator_sha256"
        ],
        expected_secure_launcher_sha256=manifest["preflight"][
            "secure_launcher_sha256"
        ],
    )
    temp_parent = ROOT / "workflow" / "tmp" / option
    temp_parent.mkdir(parents=True, exist_ok=True)
    temp_dir = Path(tempfile.mkdtemp(prefix=f"{entry['screen_id']}_", dir=temp_parent))
    temp_stem = f"{Path(entry['filename']).stem}_generated"
    log_path = ROOT / "audit" / "logs" / "options" / f"{entry['id']}.log"
    log_path.parent.mkdir(parents=True, exist_ok=True)
    delivery_temporary: Path | None = None
    try:
        command = generation_command(
            entry, reference_image, temp_dir, temp_stem, iteration_target_included,
            retry_mode,
        )
        result = subprocess.run(
            command,
            cwd=legacy.PPT_ROOT,
            env=child_env,
            text=True,
            capture_output=True,
            check=False,
        )
        combined = legacy.sanitize_log(result.stdout + "\n" + result.stderr, runtime)
        log_path.write_text(combined.strip() + "\n", encoding="utf-8")
        temporary = legacy.safe_named_path(temp_dir, f"{temp_stem}.png")
        if result.returncode != 0 or not temporary.is_file():
            excerpt = " ".join(combined.strip().split())[-1400:]
            raise RuntimeError(excerpt or f"PPT Master exited {result.returncode}")
        normalization = normalize_generated_png(temporary, option)
        dimensions = normalization["normalized_dimensions"]
        candidate.parent.mkdir(parents=True, exist_ok=True)
        archived = None
        if candidate.is_file():
            iteration_dir = option_root(option) / "iterations"
            iteration_dir.mkdir(parents=True, exist_ok=True)
            sequence = len(list(iteration_dir.glob(f"{candidate.stem}_v*.png"))) + 1
            archived = legacy.safe_named_path(iteration_dir, f"{candidate.stem}_v{sequence}.png")
            shutil.copy2(candidate, archived)
        with tempfile.NamedTemporaryFile(
            dir=candidate.parent,
            prefix=f".{candidate.stem}_delivery_",
            suffix=".tmp",
            delete=False,
        ) as handle:
            delivery_temporary = Path(handle.name)
        shutil.copyfile(temporary, delivery_temporary)
        os.replace(delivery_temporary, candidate)
        delivery_temporary = None
        output_sha256 = legacy.sha256(candidate)
        return {
            "status": "Generated",
            "output": legacy.rel(candidate),
            "reference_board": legacy.rel(board),
            "generated_at": utc_now(),
            "sha256": output_sha256,
            "actual_dimensions": dimensions,
            "generation": {
                "model": "gpt-image-2",
                "quality": "high",
                "aspect_ratio": entry["aspect_ratio"],
                "image_size": entry["image_size"],
                "output_format": "png",
                **normalization,
                "functional_gate": "passed",
                "contract_sha256": entry["contract_sha256"],
                "visual_constitution_sha256": entry[
                    "visual_constitution_sha256"
                ],
                "master_reference_hashes": reference_hashes(entry["master_references"]),
                "architecture_reference_hashes": reference_hashes(
                    entry.get("architecture_references", [])
                ),
                "approved_upload_host": approved_host,
                "approved_upload_scope": UPLOAD_APPROVAL_SCOPE,
                "preflight_evidence_sha256": manifest["preflight"]["evidence_sha256"],
                "transport_policy": manifest["preflight"]["transport_policy"],
                "secure_launcher_sha256": manifest["preflight"]["secure_launcher_sha256"],
                "ppt_image_generator_sha256": manifest["preflight"]["ppt_image_generator_sha256"],
                "reference_board_sha256": reference_board_sha256,
                "output_sha256": output_sha256,
                "reference_policy": (
                    "iteration-board-current-target-architecture-same-option-masters"
                    if iteration_target_included
                    else "current-functional-inventory-plus-architecture-or-same-option-master-region"
                ),
                "iteration_target_sha256": iteration_target_sha256,
                "iteration_target_archived_as": (
                    legacy.rel(archived)
                    if iteration_target_included and archived
                    else None
                ),
                **(
                    {
                        "retry_mode": CURRENT_DOMINANT_RETRY_MODE,
                        "iteration_instruction": iteration_instruction,
                        "iteration_instruction_sha256": hashlib.sha256(
                            iteration_instruction.encode("utf-8")
                        ).hexdigest(),
                        "current_reference_sha256": entry["current_sha256"],
                        "reference_board_layout": current_dominant_reference_layout(entry),
                        "reference_policy": CURRENT_DOMINANT_REFERENCE_POLICY,
                        "iteration_target_uploaded": False,
                    }
                    if retry_mode == CURRENT_DOMINANT_RETRY_MODE
                    else {}
                ),
            },
            "archived_previous_output": legacy.rel(archived) if archived else None,
            "reject_gate": reject_gate_template(
                option,
                next(item for item in PAGES if item["screen_id"] == entry["screen_id"]),
            ),
        }
    except Exception as exc:
        return {
            "status": "Failed",
            "last_error": legacy.sanitize_log(str(exc), runtime)[:2000],
            "failed_at": utc_now(),
        }
    finally:
        if delivery_temporary is not None and delivery_temporary.is_file():
            delivery_temporary.unlink()
        resolved_temp = temp_dir.resolve()
        if resolved_temp.is_relative_to((ROOT / "workflow" / "tmp").resolve()):
            shutil.rmtree(resolved_temp, ignore_errors=True)


def select_entries(
    manifest: dict[str, Any], ids: set[str] | None, option: str | None,
    role: str | None, force: bool,
) -> list[dict[str, Any]]:
    selected = [
        entry for entry in manifest["entries"]
        if (not ids or entry["id"] in ids)
        and (not option or entry["option"] == option)
        and (not role or entry["page_role"] == role)
    ]
    if ids:
        missing = ids - {entry["id"] for entry in selected}
        if missing:
            raise ValueError(f"Unknown or filtered ids: {', '.join(sorted(missing))}")
    if not force:
        selected = [entry for entry in selected if entry["status"] in RETRYABLE_STATUSES]
    return selected


def selected_dependency_conflicts(
    selected: list[dict[str, Any]], manifest: dict[str, Any]
) -> list[str]:
    selected_ids = {entry["id"] for entry in selected}
    by_id = {
        entry["id"]: entry
        for entry in manifest.get("entries", [])
        if isinstance(entry, dict) and isinstance(entry.get("id"), str)
    }
    conflicts: set[str] = set()
    for entry in selected:
        pending = list(entry.get("depends_on") or [])
        visited: set[str] = set()
        while pending:
            dependency_id = pending.pop()
            if dependency_id in visited:
                continue
            visited.add(dependency_id)
            if dependency_id in selected_ids:
                conflicts.add(
                    f"{entry['id']} depends on selected {dependency_id}"
                )
            dependency = by_id.get(dependency_id)
            if dependency is not None:
                pending.extend(dependency.get("depends_on") or [])
    return sorted(conflicts)


def generate(
    ids: set[str] | None, option: str | None, role: str | None,
    force: bool, concurrency: int, iteration_instruction: str | None,
    iteration_instructions: dict[str, str] | None, retry_mode: str,
) -> None:
    if retry_mode not in RETRY_MODES:
        raise ValueError(f"Unsupported retry mode: {retry_mode}")
    if iteration_instruction and iteration_instructions:
        raise ValueError(
            "Use either --iteration-instruction or --iteration-instructions-file, not both"
        )
    if retry_mode == CURRENT_DOMINANT_RETRY_MODE and not (
        (isinstance(iteration_instruction, str) and iteration_instruction.strip())
        or iteration_instructions
    ):
        raise ValueError(
            "--retry-mode current-dominant requires an iteration instruction"
        )
    manifest = load_manifest()
    errors = validate_manifest(manifest, require_masters=False, require_outputs=False)
    if errors:
        raise ValueError("Manifest invalid:\n" + "\n".join(errors))
    selected = select_entries(manifest, ids, option, role, force)
    if not selected:
        print("No retryable entries selected.")
        return
    if iteration_instructions:
        known_ids = {
            entry["id"] for entry in manifest["entries"] if isinstance(entry, dict)
        }
        unknown_instruction_ids = set(iteration_instructions) - known_ids
        if unknown_instruction_ids:
            raise ValueError(
                "Iteration instruction file contains unknown ids: "
                + ", ".join(sorted(unknown_instruction_ids))
            )
        missing_instruction_ids = {
            entry["id"] for entry in selected
        } - set(iteration_instructions)
        if missing_instruction_ids:
            raise ValueError(
                "Iteration instruction file is missing selected ids: "
                + ", ".join(sorted(missing_instruction_ids))
            )
    dependency_conflicts = selected_dependency_conflicts(selected, manifest)
    if dependency_conflicts:
        raise ValueError(
            "Generation batch mixes entries with their direct or transitive Master dependencies. "
            "Generate, review, and promote each Master stage before its downstream stage: "
            + "; ".join(dependency_conflicts)
        )
    ready: list[dict[str, Any]] = []
    blocked: list[tuple[dict[str, Any], list[str]]] = []
    for entry in selected:
        gate_errors = functional_gate_errors(entry, manifest)
        if gate_errors:
            blocked.append((entry, gate_errors))
        else:
            ready.append(entry)
    if not ready:
        for entry, gate_errors in blocked:
            entry["status"] = "Needs-Manual"
            entry["last_error"] = "Functional gate blocked generation: " + "; ".join(gate_errors)
            entry["failed_at"] = utc_now()
            print(f"[{entry['id']}] BLOCKED: {entry['last_error']}")
        legacy.atomic_write_json(MANIFEST_PATH, manifest)
        write_readiness_artifacts(manifest)
        raise SystemExit(1)
    runtime = legacy.preflight()
    approved_host = legacy.require_upload_consent(runtime.get("OPENAI_BASE_URL"))
    if iteration_instruction:
        for entry in selected:
            entry["iteration_instruction"] = iteration_instruction.strip()
    elif iteration_instructions:
        for entry in selected:
            entry["iteration_instruction"] = iteration_instructions[entry["id"]]
    for entry, gate_errors in blocked:
        entry["status"] = "Needs-Manual"
        entry["last_error"] = "Functional gate blocked generation: " + "; ".join(gate_errors)
        entry["failed_at"] = utc_now()
        print(f"[{entry['id']}] BLOCKED: {entry['last_error']}")
    manifest["preflight"] = preflight_record(approved_host, runtime)
    legacy.atomic_write_json(MANIFEST_PATH, manifest)
    by_id = {entry["id"]: entry for entry in manifest["entries"]}
    failures = 0
    with ThreadPoolExecutor(max_workers=max(1, min(concurrency, 4))) as executor:
        futures = {
            executor.submit(generate_one, entry, runtime, manifest, retry_mode): entry["id"]
            for entry in ready
        }
        for future in as_completed(futures):
            current_id = futures[future]
            result = future.result()
            target = by_id[current_id]
            target.update({key: value for key, value in result.items() if value is not None})
            if result["status"] != "Generated":
                failures += 1
                print(f"[{current_id}] {result['status']}: {result.get('last_error', 'unknown error')}")
            else:
                target.pop("last_error", None)
                target.pop("failed_at", None)
                target.pop("iteration_instruction", None)
                for promotion_key in (
                    "master_path", "master_sha256", "master_source_sha256",
                    "selected_for_master_chain_at", "approval_scope",
                ):
                    target.pop(promotion_key, None)
                print(f"[{current_id}] generated {target['output']}")
            legacy.atomic_write_json(MANIFEST_PATH, manifest)
    write_readiness_artifacts(manifest)
    if failures:
        raise SystemExit(1)


def review_entries(ids: set[str], decision: str, note: str) -> None:
    if not ids:
        raise ValueError("review requires --ids")
    if not note.strip():
        raise ValueError("review requires a substantive audit note")
    manifest = load_manifest()
    errors = validate_manifest(manifest)
    if errors:
        raise ValueError("Manifest invalid:\n" + "\n".join(errors))
    by_id = {entry["id"]: entry for entry in manifest["entries"]}
    missing = ids - by_id.keys()
    if missing:
        raise ValueError(f"Unknown ids: {', '.join(sorted(missing))}")
    for current_id in sorted(ids):
        entry = by_id[current_id]
        if entry.get("status") not in {"Generated", "Approved-Candidate"}:
            raise ValueError(f"{current_id} must be Generated before Reject Gate review")
        page_def = next(item for item in PAGES if item["screen_id"] == entry["screen_id"])
        evidence_errors = candidate_evidence_errors(entry, page_def, manifest)
        if evidence_errors:
            raise ValueError(
                f"{current_id} generation evidence is incomplete or stale:\n"
                + "\n".join(evidence_errors)
            )
        candidate = option_image(entry["option"], "screens", entry["filename"], must_exist=True)
        generation = entry["generation"]
        gates = {
            "no_invented_function": "passed" if decision == "pass" else "rejected",
            "no_missing_function": "passed" if decision == "pass" else "rejected",
            "layout_structurally_redesigned": "passed" if decision == "pass" else "rejected",
            "same_option_master_consistency": "passed" if decision == "pass" else "rejected",
            "d_flow_canvas_dominant": (
                "passed" if decision == "pass" else "rejected"
            ) if entry["option"] == "D" and page_def["family"] == "flow" else "not-applicable",
        }
        entry["reject_gate"] = {
            "status": "passed" if decision == "pass" else "rejected",
            "reviewed_at": utc_now(),
            "review_scope": "manual visual structure and functional-fidelity audit",
            "gates": gates,
            "note": note.strip(),
            "reviewed_sha256": legacy.sha256(candidate),
            "contract_sha256": entry["contract_sha256"],
            "reviewed_generation_sha256": contract_sha256(generation),
            "reviewed_preflight_sha256": manifest["preflight"]["evidence_sha256"],
            "reviewed_visual_constitution_sha256": entry[
                "visual_constitution_sha256"
            ],
        }
        if decision == "reject":
            entry["status"] = "Needs-Manual"
        print(f"[{current_id}] reject gate {entry['reject_gate']['status']}")
    legacy.atomic_write_json(MANIFEST_PATH, manifest)
    write_readiness_artifacts(manifest)


def promote(ids: set[str]) -> None:
    if not ids:
        raise ValueError("promote requires --ids")
    manifest = load_manifest()
    errors = validate_manifest(manifest)
    if errors:
        raise ValueError("Manifest invalid:\n" + "\n".join(errors))
    by_id = {entry["id"]: entry for entry in manifest["entries"]}
    missing = ids - by_id.keys()
    if missing:
        raise ValueError(f"Unknown ids: {', '.join(sorted(missing))}")
    for current_id in sorted(ids):
        entry = by_id[current_id]
        if entry.get("master_role") not in MASTER_SCREENS:
            raise ValueError(f"{current_id} is not a declared Master Screen")
        if entry.get("status") != "Generated":
            raise ValueError(f"{current_id} must be Generated before Master promotion")
        if entry.get("reject_gate", {}).get("status") != "passed":
            raise ValueError(f"{current_id} must pass the Reject Gate before Master promotion")
        page_def = next(item for item in PAGES if item["screen_id"] == entry["screen_id"])
        evidence_errors = candidate_evidence_errors(entry, page_def, manifest)
        if evidence_errors:
            raise ValueError(
                f"{current_id} generation evidence is incomplete or stale:\n"
                + "\n".join(evidence_errors)
            )
        review_errors = review_evidence_errors(entry, page_def, manifest)
        if review_errors:
            raise ValueError(
                f"{current_id} Reject Gate evidence is incomplete or stale:\n"
                + "\n".join(review_errors)
            )
        review = entry["reject_gate"]
        candidate = option_image(entry["option"], "screens", entry["filename"], must_exist=True)
        if entry.get("sha256") != legacy.sha256(candidate):
            raise ValueError(f"{current_id} candidate hash drifted; regenerate or audit before promotion")
        if review.get("reviewed_sha256") != entry.get("sha256"):
            raise ValueError(f"{current_id} Reject Gate is not bound to the current candidate")
        generation = entry.get("generation")
        if not isinstance(generation, dict) or review.get(
            "reviewed_generation_sha256"
        ) != contract_sha256(generation):
            raise ValueError(f"{current_id} Reject Gate is not bound to generation evidence")
        if review.get("contract_sha256") != entry.get("contract_sha256"):
            raise ValueError(f"{current_id} Reject Gate contract is stale")
        if review.get("reviewed_preflight_sha256") != manifest["preflight"]["evidence_sha256"]:
            raise ValueError(f"{current_id} Reject Gate preflight is stale")
        for reference in entry.get("master_references", []):
            reference_path = legacy.root_path(reference, must_exist=True)
            dependency_id = next(
                dependency for dependency in entry["depends_on"]
                if Path(by_id[dependency]["filename"]).name == reference_path.name
            )
            dependency = by_id[dependency_id]
            if dependency.get("status") != "Approved-Candidate":
                raise ValueError(f"{current_id} depends on unpromoted Master {dependency_id}")
            dependency_page = next(
                item for item in PAGES if item["screen_id"] == dependency["screen_id"]
            )
            dependency_errors = candidate_evidence_errors(
                dependency, dependency_page, manifest
            )
            dependency_review_errors = review_evidence_errors(
                dependency, dependency_page, manifest
            )
            if dependency_errors:
                raise ValueError(
                    f"{current_id} dependency evidence is stale for {dependency_id}:\n"
                    + "\n".join(dependency_errors)
                )
            if dependency_review_errors:
                raise ValueError(
                    f"{current_id} dependency Reject Gate is stale for {dependency_id}:\n"
                    + "\n".join(dependency_review_errors)
                )
            if (
                dependency.get("master_sha256") != legacy.sha256(reference_path)
                or dependency.get("master_sha256") != dependency.get("sha256")
                or dependency.get("master_source_sha256") != dependency.get("sha256")
            ):
                raise ValueError(f"{current_id} dependency Master hash drifted: {dependency_id}")
        target = option_image(entry["option"], "masters", entry["filename"])
        target.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(dir=target.parent, suffix=".tmp", delete=False) as handle:
            temporary = Path(handle.name)
        shutil.copy2(candidate, temporary)
        os.replace(temporary, target)
        entry["status"] = "Approved-Candidate"
        entry["master_path"] = legacy.rel(target)
        entry["master_sha256"] = legacy.sha256(target)
        entry["master_source_sha256"] = entry["sha256"]
        entry["selected_for_master_chain_at"] = utc_now()
        entry["approval_scope"] = "selected-for-chain-not-product-owner-approved"
        print(f"[{current_id}] promoted as {entry['option']} {entry['master_role']} Master")
    legacy.atomic_write_json(MANIFEST_PATH, manifest)
    write_readiness_artifacts(manifest)


def restore_iteration(current_id: str, iteration_filename: str, note: str) -> None:
    manifest = load_manifest()
    errors = validate_manifest(manifest)
    if errors:
        raise ValueError("Manifest invalid:\n" + "\n".join(errors))
    by_id = {entry["id"]: entry for entry in manifest["entries"]}
    if current_id not in by_id:
        raise ValueError(f"Unknown id: {current_id}")
    entry = by_id[current_id]
    expected_prefix = f"{Path(entry['filename']).stem}_v"
    if not iteration_filename.startswith(expected_prefix) or not iteration_filename.endswith(".png"):
        raise ValueError("Iteration filename does not belong to the selected entry")
    source = option_image(entry["option"], "iterations", iteration_filename, must_exist=True)
    target = option_image(entry["option"], "screens", entry["filename"])
    with Image.open(source) as opened:
        dimensions = {"width": opened.width, "height": opened.height}
        image_format = opened.format
        opened.verify()
    expected_dimensions = expected_output_dimensions(entry["option"])
    if image_format != EXPECTED_OUTPUT_FORMAT or dimensions != expected_dimensions:
        raise ValueError(
            "Restored iteration must be a valid "
            f"{expected_dimensions['width']}x{expected_dimensions['height']} PNG"
        )
    preflight_errors = preflight_evidence_errors(manifest)
    if preflight_errors:
        raise ValueError("Restored iteration requires active exact-model preflight:\n" + "\n".join(preflight_errors))
    source_sha256 = legacy.sha256(source)
    board, _ = build_reference_board(entry, source)
    reference_board_sha256 = legacy.sha256(board)
    if target.is_file():
        iteration_dir = option_root(entry["option"]) / "iterations"
        sequence = len(list(iteration_dir.glob(f"{target.stem}_v*.png"))) + 1
        backup = legacy.safe_named_path(iteration_dir, f"{target.stem}_v{sequence}.png")
        shutil.copy2(target, backup)
    with tempfile.NamedTemporaryFile(dir=target.parent, suffix=".tmp", delete=False) as handle:
        temporary = Path(handle.name)
    shutil.copy2(source, temporary)
    os.replace(temporary, target)
    restored_at = utc_now()
    output_sha256 = legacy.sha256(target)
    preflight = manifest["preflight"]
    entry.update({
        "status": "Generated",
        "output": legacy.rel(target),
        "reference_board": legacy.rel(board),
        "generated_at": restored_at,
        "sha256": output_sha256,
        "actual_dimensions": dimensions,
        "restored_from": legacy.rel(source),
        "restored_at": restored_at,
        "restoration_note": note,
        "generation": {
            "model": "gpt-image-2",
            "quality": "high",
            "aspect_ratio": entry["aspect_ratio"],
            "image_size": entry["image_size"],
            "output_format": "png",
            "source_dimensions": dimensions,
            "source_format": image_format,
            "source_sha256": source_sha256,
            "normalization_applied": False,
            "normalization_method": NORMALIZATION_METHOD_NONE,
            "normalized_dimensions": dimensions,
            "normalization_contract_sha256": normalization_contract_sha256(entry["option"]),
            "functional_gate": "passed",
            "contract_sha256": entry["contract_sha256"],
            "visual_constitution_sha256": entry["visual_constitution_sha256"],
            "master_reference_hashes": reference_hashes(entry["master_references"]),
            "architecture_reference_hashes": reference_hashes(
                entry.get("architecture_references", [])
            ),
            "approved_upload_host": preflight["approved_upload_host"],
            "approved_upload_scope": preflight["approved_upload_scope"],
            "preflight_evidence_sha256": preflight["evidence_sha256"],
            "transport_policy": preflight["transport_policy"],
            "secure_launcher_sha256": preflight["secure_launcher_sha256"],
            "ppt_image_generator_sha256": preflight["ppt_image_generator_sha256"],
            "reference_board_sha256": reference_board_sha256,
            "output_sha256": output_sha256,
            "reference_policy": (
                "restored-audited-gpt-image-2-iteration-board-current-target-"
                "architecture-same-option-masters"
            ),
            "iteration_target_sha256": source_sha256,
            "iteration_target_archived_as": legacy.rel(source),
            "provenance_mode": "restored-audited-gpt-image-2-iteration",
            "restoration_evidence": {
                "archived_iteration": legacy.rel(source),
                "archived_sha256": source_sha256,
                "restored_output_sha256": output_sha256,
                "restored_at": restored_at,
                "note": note,
                "policy": (
                    "archive was created by the manifest generation loop before replacement; "
                    "restoration is explicit and remains subject to the full manual Reject Gate"
                ),
            },
        },
        "reject_gate": reject_gate_template(
            entry["option"],
            next(item for item in PAGES if item["screen_id"] == entry["screen_id"]),
        ),
    })
    entry.pop("last_error", None)
    for evidence_key in (
        "failed_at", "archived_previous_output", "master_path",
        "master_sha256", "master_source_sha256", "selected_for_master_chain_at",
        "approval_scope",
    ):
        entry.pop(evidence_key, None)
    legacy.atomic_write_json(MANIFEST_PATH, manifest)
    write_readiness_artifacts(manifest)
    print(f"[{current_id}] restored {iteration_filename}")


def apply_picture_layer_touchup(current_id: str, spec_value: str) -> None:
    manifest = load_manifest()
    errors = validate_manifest(manifest)
    if errors:
        raise ValueError("Manifest invalid:\n" + "\n".join(errors))
    by_id = {entry["id"]: entry for entry in manifest["entries"]}
    if current_id not in by_id:
        raise ValueError(f"Unknown id: {current_id}")
    entry = by_id[current_id]
    if entry.get("status") not in {"Generated", "Approved-Candidate"}:
        raise ValueError(
            "Picture-layer touchup requires a Generated candidate or a promoted Master candidate"
        )
    if (
        entry.get("status") == "Approved-Candidate"
        and entry.get("master_role") not in MASTER_SCREENS
    ):
        raise ValueError("Only a declared Master may be touched up after promotion")

    spec_path = legacy.root_path(spec_value, must_exist=True)
    expected_spec_parent = (ROOT / "workflow" / "touchups").resolve()
    if spec_path.parent.resolve() != expected_spec_parent:
        raise ValueError("Touchup spec must be directly under workflow/touchups")
    spec = json.loads(spec_path.read_text(encoding="utf-8"))
    if spec.get("schema_version") != "clearvision-controlled-picture-layer-touchup.v1":
        raise ValueError("Unsupported touchup schema")
    if spec.get("entry_id") != current_id:
        raise ValueError("Touchup spec entry_id does not match --id")
    source = legacy.root_path(str(spec.get("source_iteration")), must_exist=True)
    expected_source_parent = (option_root(entry["option"]) / "iterations").resolve()
    if source.parent.resolve() != expected_source_parent:
        raise ValueError("Touchup source must be an archived same-option iteration")
    source_sha256 = legacy.sha256(source)
    if spec.get("source_sha256") != source_sha256:
        raise ValueError("Touchup source hash does not match the frozen spec")

    candidate = option_image(entry["option"], "screens", entry["filename"], must_exist=True)
    candidate_sha256 = legacy.sha256(candidate)
    base_generation = entry.get("generation")
    if not isinstance(base_generation, dict):
        raise ValueError("Picture-layer touchup requires generation evidence")
    base_provenance_mode = base_generation.get("provenance_mode")
    direct_touchup_mode = (
        "controlled-picture-layer-touchup-of-generated-gpt-image-2-output"
    )
    restored_touchup_mode = (
        "controlled-picture-layer-touchup-of-restored-gpt-image-2-iteration"
    )
    if base_provenance_mode is None:
        if candidate_sha256 != source_sha256:
            raise ValueError(
                "Active candidate must first be restored to the spec source iteration"
            )
        if base_generation.get("output_sha256") != source_sha256:
            raise ValueError(
                "Generated candidate hash does not match its frozen touchup source"
            )
        touchup_provenance_mode = direct_touchup_mode
    elif base_provenance_mode == "restored-audited-gpt-image-2-iteration":
        if candidate_sha256 != source_sha256:
            raise ValueError(
                "Active candidate must first be restored to the spec source iteration"
            )
        if base_generation.get("source_sha256") != source_sha256:
            raise ValueError(
                "Restored candidate hash does not match its frozen touchup source"
            )
        touchup_provenance_mode = restored_touchup_mode
    elif base_provenance_mode == direct_touchup_mode:
        prior_touchup = base_generation.get("touchup_evidence")
        if not isinstance(prior_touchup, dict):
            raise ValueError("Existing direct touchup evidence is missing")
        if (
            candidate_sha256 != entry.get("sha256")
            or prior_touchup.get("output_sha256") != candidate_sha256
        ):
            raise ValueError("Existing direct touchup output hash drifted")
        if prior_touchup.get("base_output_sha256") != source_sha256:
            raise ValueError(
                "Replacement touchup must use the same immutable generated base output"
            )
        if prior_touchup.get("provider_source_sha256") != base_generation.get(
            "source_sha256"
        ):
            raise ValueError("Existing direct touchup provider source hash drifted")
        touchup_provenance_mode = direct_touchup_mode
    elif base_provenance_mode == restored_touchup_mode:
        prior_touchup = base_generation.get("touchup_evidence")
        if not isinstance(prior_touchup, dict):
            raise ValueError("Existing restored touchup evidence is missing")
        if (
            candidate_sha256 != entry.get("sha256")
            or prior_touchup.get("output_sha256") != candidate_sha256
        ):
            raise ValueError("Existing restored touchup output hash drifted")
        if (
            prior_touchup.get("source_sha256") != source_sha256
            or base_generation.get("source_sha256") != source_sha256
        ):
            raise ValueError(
                "Replacement touchup must use the same immutable restored source"
            )
        touchup_provenance_mode = restored_touchup_mode
    else:
        raise ValueError("Picture-layer touchup source provenance is unsupported")
    with Image.open(source) as opened:
        if opened.format != EXPECTED_OUTPUT_FORMAT:
            raise ValueError("Touchup source must be PNG")
        expected_dimensions = expected_output_dimensions(entry["option"])
        if {"width": opened.width, "height": opened.height} != expected_dimensions:
            raise ValueError(
                "Touchup source must be "
                f"{expected_dimensions['width']}x{expected_dimensions['height']}"
            )
        image = opened.convert("RGB")
        immutable_source = image.copy()

    def rgb(value: Any, field: str) -> tuple[int, int, int]:
        if not (
            isinstance(value, list)
            and len(value) == 3
            and all(isinstance(channel, int) and 0 <= channel <= 255 for channel in value)
        ):
            raise ValueError(f"{field} must be a three-channel RGB list")
        return value[0], value[1], value[2]

    def box(value: Any, field: str) -> tuple[int, int, int, int]:
        if not (
            isinstance(value, list)
            and len(value) == 4
            and all(isinstance(coordinate, int) for coordinate in value)
        ):
            raise ValueError(f"{field} must be a four-integer box")
        left, top, right, bottom = value
        if not (0 <= left <= right < image.width and 0 <= top <= bottom < image.height):
            raise ValueError(f"{field} is outside the image")
        return left, top, right, bottom

    operations = spec.get("operations")
    if not isinstance(operations, list) or not operations:
        raise ValueError("Touchup spec must contain operations")
    draw = ImageDraw.Draw(image)
    windows_fonts = Path(r"C:\Windows\Fonts").resolve()

    def controlled_font(operation: dict[str, Any], index: int) -> ImageFont.FreeTypeFont:
        font_path = Path(str(operation.get("font_path"))).resolve()
        if font_path.parent != windows_fonts or not font_path.is_file():
            raise ValueError("Touchup font must be an existing Windows font")
        if operation.get("font_sha256") != legacy.sha256(font_path):
            raise ValueError("Touchup font hash drifted")
        font_size = operation.get("font_size")
        if not isinstance(font_size, int) or not 8 <= font_size <= 48:
            raise ValueError("Touchup font size is outside the controlled range")
        return ImageFont.truetype(str(font_path), font_size)

    for index, operation in enumerate(operations):
        if not isinstance(operation, dict):
            raise ValueError(f"Touchup operation {index} must be an object")
        operation_type = operation.get("type")
        operation_box = box(operation.get("box"), f"operations[{index}].box")
        if operation_type == "line":
            points = operation.get("points")
            if not (
                isinstance(points, list)
                and len(points) >= 2
                and all(
                    isinstance(point, list)
                    and len(point) == 2
                    and all(isinstance(coordinate, int) for coordinate in point)
                    for point in points
                )
            ):
                raise ValueError(f"operations[{index}].points must contain integer pairs")
            if any(
                not (
                    operation_box[0] <= point[0] <= operation_box[2]
                    and operation_box[1] <= point[1] <= operation_box[3]
                )
                for point in points
            ):
                raise ValueError(f"operations[{index}].points escape the declared box")
            line_width = operation.get("width")
            if not isinstance(line_width, int) or not 1 <= line_width <= 12:
                raise ValueError(f"operations[{index}].width must be between 1 and 12")
            draw.line(
                [(point[0], point[1]) for point in points],
                fill=rgb(operation.get("fill"), f"operations[{index}].fill"),
                width=line_width,
            )
            continue
        if operation_type in {"rounded_rect", "ellipse"}:
            outline_value = operation.get("outline")
            outline = (
                None
                if outline_value is None
                else rgb(outline_value, f"operations[{index}].outline")
            )
            outline_width = operation.get("width", 1 if outline is not None else 0)
            if not isinstance(outline_width, int) or not 0 <= outline_width <= 12:
                raise ValueError(f"operations[{index}].width must be between 0 and 12")
            shape_fill = rgb(operation.get("fill"), f"operations[{index}].fill")
            if operation_type == "rounded_rect":
                radius = operation.get("radius")
                if not isinstance(radius, int) or not 1 <= radius <= 64:
                    raise ValueError(f"operations[{index}].radius must be between 1 and 64")
                draw.rounded_rectangle(
                    operation_box,
                    radius=radius,
                    fill=shape_fill,
                    outline=outline,
                    width=outline_width,
                )
            else:
                draw.ellipse(
                    operation_box,
                    fill=shape_fill,
                    outline=outline,
                    width=outline_width,
                )
            continue
        if operation_type == "draw_text":
            position = operation.get("position")
            if not (
                isinstance(position, list)
                and len(position) == 2
                and all(isinstance(coordinate, int) for coordinate in position)
            ):
                raise ValueError(f"operations[{index}].position must contain two integers")
            text_value = operation.get("text")
            if not isinstance(text_value, str) or not text_value:
                raise ValueError("Touchup drawing text is missing")
            anchor = operation.get("anchor")
            if anchor not in {None, "lm", "rm", "mm"}:
                raise ValueError(f"operations[{index}].anchor is unsupported")
            font = controlled_font(operation, index)
            text_bbox = draw.textbbox(
                (position[0], position[1]), text_value, font=font, anchor=anchor
            )
            if not (
                operation_box[0] <= text_bbox[0]
                and operation_box[1] <= text_bbox[1]
                and text_bbox[2] <= operation_box[2] + 1
                and text_bbox[3] <= operation_box[3] + 1
            ):
                raise ValueError(f"operations[{index}].text escapes the declared box")
            draw.text(
                (position[0], position[1]),
                text_value,
                font=font,
                fill=rgb(operation.get("text_fill"), f"operations[{index}].text_fill"),
                anchor=anchor,
            )
            continue
        if operation_type in {"copy_rect", "copy_resize"}:
            source_box = box(operation.get("source_box"), f"operations[{index}].source_box")
            destination_width = operation_box[2] - operation_box[0] + 1
            destination_height = operation_box[3] - operation_box[1] + 1
            source_width = source_box[2] - source_box[0] + 1
            source_height = source_box[3] - source_box[1] + 1
            if operation_type == "copy_rect" and (
                (destination_width, destination_height) != (source_width, source_height)
            ):
                raise ValueError("copy_rect source and destination dimensions must match")
            patch = immutable_source.crop(
                (source_box[0], source_box[1], source_box[2] + 1, source_box[3] + 1)
            )
            if operation_type == "copy_resize":
                patch = patch.resize(
                    (destination_width, destination_height),
                    Image.Resampling.LANCZOS,
                )
            image.paste(patch, (operation_box[0], operation_box[1]))
            continue
        if operation_type in {
            "paste_reference",
            "paste_master_reference",
            "paste_option_candidate_reference",
        }:
            reference = legacy.root_path(str(operation.get("reference_path")), must_exist=True)
            if operation_type == "paste_reference":
                allowed_reference_root = (ROOT / "current").resolve()
                restriction_error = "paste_reference is restricted to an audited CURRENT asset"
            elif operation_type == "paste_master_reference":
                allowed_reference_root = (option_root(entry["option"]) / "masters").resolve()
                restriction_error = (
                    "paste_master_reference is restricted to a same-option Master asset"
                )
            else:
                allowed_reference_root = (option_root(entry["option"]) / "screens").resolve()
                restriction_error = (
                    "paste_option_candidate_reference is restricted to a same-option screen asset"
                )
            if not reference.resolve().is_relative_to(allowed_reference_root):
                raise ValueError(restriction_error)
            if operation.get("reference_sha256") != legacy.sha256(reference):
                raise ValueError("Picture Layer reference asset hash drifted")
            source_value = operation.get("source_box")
            if not (
                isinstance(source_value, list)
                and len(source_value) == 4
                and all(isinstance(coordinate, int) for coordinate in source_value)
            ):
                raise ValueError(f"operations[{index}].source_box must be a four-integer box")
            with Image.open(reference) as opened_reference:
                reference_image = opened_reference.convert("RGB")
            source_left, source_top, source_right, source_bottom = source_value
            if not (
                0 <= source_left <= source_right < reference_image.width
                and 0 <= source_top <= source_bottom < reference_image.height
            ):
                raise ValueError(f"operations[{index}].source_box is outside the reference asset")
            destination_width = operation_box[2] - operation_box[0] + 1
            destination_height = operation_box[3] - operation_box[1] + 1
            patch = reference_image.crop(
                (source_left, source_top, source_right + 1, source_bottom + 1)
            ).resize(
                (destination_width, destination_height),
                Image.Resampling.LANCZOS,
            )
            image.paste(patch, (operation_box[0], operation_box[1]))
            continue
        if operation_type == "blend_rect":
            background = rgb(
                operation.get("fill"), f"operations[{index}].fill"
            )
            opacity = operation.get("opacity")
            if (
                isinstance(opacity, bool)
                or not isinstance(opacity, (int, float))
                or not 0 <= opacity <= 1
            ):
                raise ValueError(
                    f"operations[{index}].opacity must be a number from 0 to 1"
                )
            region = image.crop(
                (
                    operation_box[0],
                    operation_box[1],
                    operation_box[2] + 1,
                    operation_box[3] + 1,
                )
            )
            overlay = Image.new("RGB", region.size, background)
            image.paste(
                Image.blend(region, overlay, float(opacity)),
                (operation_box[0], operation_box[1]),
            )
            continue
        background = rgb(operation.get("fill"), f"operations[{index}].fill")
        draw.rectangle(operation_box, fill=background)
        if operation_type == "fill_rect":
            continue
        if operation_type != "replace_text":
            raise ValueError(f"Unsupported touchup operation: {operation_type}")
        position = operation.get("position")
        if not (
            isinstance(position, list)
            and len(position) == 2
            and all(isinstance(coordinate, int) for coordinate in position)
        ):
            raise ValueError(f"operations[{index}].position must contain two integers")
        text_value = operation.get("text")
        if not isinstance(text_value, str) or not text_value:
            raise ValueError("Touchup replacement text is missing")
        draw.text(
            (position[0], position[1]),
            text_value,
            font=controlled_font(operation, index),
            fill=rgb(operation.get("text_fill"), f"operations[{index}].text_fill"),
        )

    with tempfile.NamedTemporaryFile(
        dir=candidate.parent,
        prefix=f".{candidate.stem}_touchup_",
        suffix=".tmp",
        delete=False,
    ) as handle:
        temporary = Path(handle.name)
    try:
        image.save(temporary, format="PNG", optimize=True)
        file_errors, dimensions = output_file_errors(temporary, entry["option"])
        if file_errors or dimensions is None:
            raise ValueError("Controlled touchup output invalid: " + "; ".join(file_errors))
        os.replace(temporary, candidate)
    finally:
        if temporary.is_file():
            temporary.unlink()

    applied_at = utc_now()
    output_sha256 = legacy.sha256(candidate)
    generation = deepcopy(entry["generation"])
    touchup_evidence = {
        "spec": legacy.rel(spec_path),
        "spec_sha256": legacy.sha256(spec_path),
        "source_sha256": source_sha256,
        "output_sha256": output_sha256,
        "operation_count": len(operations),
        "applied_at": applied_at,
        "policy": (
            "deterministic factual correction layer only; archived gpt-image-2 "
            "base output remains immutable"
        ),
    }
    if base_provenance_mode in {None, direct_touchup_mode}:
        touchup_evidence.update({
            "base_output_sha256": source_sha256,
            "provider_source_sha256": generation.get("source_sha256"),
        })
    generation.update({
        "output_sha256": output_sha256,
        "provenance_mode": touchup_provenance_mode,
        "touchup_evidence": touchup_evidence,
    })
    entry.update({
        "status": "Generated",
        "generated_at": applied_at,
        "sha256": output_sha256,
        "actual_dimensions": dimensions,
        "generation": generation,
        "controlled_picture_layer": generation["touchup_evidence"],
        "reject_gate": reject_gate_template(
            entry["option"],
            next(item for item in PAGES if item["screen_id"] == entry["screen_id"]),
        ),
    })
    if base_provenance_mode is None:
        for restoration_key in ("restored_from", "restored_at", "restoration_note"):
            entry.pop(restoration_key, None)
    for promotion_key in (
        "master_path", "master_sha256", "master_source_sha256",
        "selected_for_master_chain_at", "approval_scope",
    ):
        entry.pop(promotion_key, None)
    legacy.atomic_write_json(MANIFEST_PATH, manifest)
    write_readiness_artifacts(manifest)
    print(f"[{current_id}] applied controlled picture-layer touchup {legacy.rel(spec_path)}")


def rebind_master_references(ids: set[str], note: str) -> None:
    if not ids:
        raise ValueError("rebind-master-references requires --ids")
    if not note.strip():
        raise ValueError("rebind-master-references requires a substantive audit note")
    manifest = load_manifest()
    errors = validate_manifest(manifest)
    if errors:
        raise ValueError("Manifest invalid:\n" + "\n".join(errors))
    by_id = {entry["id"]: entry for entry in manifest["entries"]}
    missing = ids - by_id.keys()
    if missing:
        raise ValueError(f"Unknown ids: {', '.join(sorted(missing))}")
    for current_id in sorted(ids):
        entry = by_id[current_id]
        if entry.get("status") not in {"Generated", "Approved-Candidate"}:
            raise ValueError(f"{current_id} must have a current output before reference rebind")
        if not entry.get("master_references"):
            raise ValueError(f"{current_id} has no Master references to rebind")
        candidate = option_image(
            entry["option"], "screens", entry["filename"], must_exist=True
        )
        output_sha256 = legacy.sha256(candidate)
        if entry.get("sha256") != output_sha256:
            raise ValueError(f"{current_id} output changed before reference rebind")
        generation = entry.get("generation")
        if not isinstance(generation, dict):
            raise ValueError(f"{current_id} generation evidence is missing")
        previous_hashes = generation.get("master_reference_hashes")
        current_hashes = reference_hashes(entry["master_references"])
        if previous_hashes == current_hashes:
            print(f"[{current_id}] Master references already current")
            continue
        for dependency_id in entry.get("depends_on", []):
            dependency = by_id.get(dependency_id)
            if not isinstance(dependency, dict) or dependency.get("status") != "Approved-Candidate":
                raise ValueError(f"{current_id} depends on an unpromoted Master {dependency_id}")
            dependency_master = option_image(
                dependency["option"], "masters", dependency["filename"], must_exist=True
            )
            dependency_hash = legacy.sha256(dependency_master)
            if (
                dependency.get("master_sha256") != dependency_hash
                or dependency.get("master_sha256") != dependency.get("sha256")
            ):
                raise ValueError(f"{current_id} dependency Master hash drifted: {dependency_id}")
        rebound_at = utc_now()
        generation["master_reference_hashes"] = current_hashes
        generation["master_reference_rebind_evidence"] = {
            "previous_master_reference_hashes": previous_hashes,
            "current_master_reference_hashes": current_hashes,
            "output_sha256_before": output_sha256,
            "output_sha256_after": output_sha256,
            "rebound_at": rebound_at,
            "note": note.strip(),
            "policy": (
                "metadata-only rebind after a bounded same-option Master Picture Layer correction; "
                "candidate output bytes remain unchanged"
            ),
        }
        entry["generation"] = generation
        entry["reject_gate"] = reject_gate_template(
            entry["option"],
            next(item for item in PAGES if item["screen_id"] == entry["screen_id"]),
        )
        print(f"[{current_id}] rebound Master references without changing output bytes")
    legacy.atomic_write_json(MANIFEST_PATH, manifest)
    write_readiness_artifacts(manifest)


def load_font(size: int) -> ImageFont.ImageFont:
    return legacy.load_font(size)


def comparison_sheet(page_def: dict[str, Any], entries: dict[str, dict[str, Any]], output: Path) -> None:
    cell_width, image_height, label_height = 900, 506, 50
    labels = ["CURRENT"] + [f"OPTION {option}" for option in OPTIONS]
    paths = [legacy.root_path(page_def["current_reference"], must_exist=True)] + [
        option_image(option, "screens", page_def["filename"], must_exist=True) for option in OPTIONS
    ]
    total_columns = len(paths)
    canvas = Image.new("RGB", (cell_width * total_columns, image_height + label_height), "#171c22")
    draw = ImageDraw.Draw(canvas)
    font = load_font(21)
    for index, (label, path) in enumerate(zip(labels, paths)):
        x = index * cell_width
        canvas.paste(fit_image(path, (cell_width, image_height), "#222932"), (x, label_height))
        suffix = "FUNCTION AUTHORITY" if index == 0 else OPTION_DEFINITIONS[OPTIONS[index - 1]]["name"]
        draw.text((x + 18, 13), f"{label} | {suffix}", fill="#f0f3f5", font=font)
        if index:
            draw.line((x, 0, x, image_height + label_height), fill="#66717c", width=2)
    output.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(output, format="PNG", optimize=True)


def single_option_comparison_sheet(page_def: dict[str, Any], option: str, output: Path) -> None:
    cell_width, image_height, label_height = 960, 540, 50
    paths = [
        legacy.root_path(page_def["current_reference"], must_exist=True),
        option_image(option, "screens", page_def["filename"], must_exist=True),
    ]
    labels = ["CURRENT | FUNCTION AUTHORITY", f"OPTION {option} | {OPTION_DEFINITIONS[option]['name']}"]
    canvas = Image.new("RGB", (cell_width * 2, image_height + label_height), "#171c22")
    draw = ImageDraw.Draw(canvas)
    font = load_font(21)
    for index, (label, path) in enumerate(zip(labels, paths)):
        x = index * cell_width
        canvas.paste(fit_image(path, (cell_width, image_height), "#222932"), (x, label_height))
        draw.text((x + 18, 13), label, fill="#f0f3f5", font=font)
        if index:
            draw.line((x, 0, x, image_height + label_height), fill="#66717c", width=2)
    output.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(output, format="PNG", optimize=True)


def contact_sheet(
    items: list[tuple[str, Path]],
    output: Path,
    title: str,
    columns: int = 4,
    card_width: int = 720,
    preview_height: int = 388,
) -> None:
    if not items:
        return
    card_height, title_height = preview_height + 57, 62
    rows = (len(items) + columns - 1) // columns
    canvas = Image.new("RGB", (columns * card_width, title_height + rows * card_height), "#171c22")
    draw = ImageDraw.Draw(canvas)
    draw.text((22, 15), title, fill="#f4f6f7", font=load_font(27))
    label_font = load_font(18)
    for index, (label, path) in enumerate(items):
        row, column = divmod(index, columns)
        x, y = column * card_width, title_height + row * card_height
        canvas.paste(fit_image(path, (card_width - 30, preview_height), "#222932"), (x + 15, y + 10))
        draw.text((x + 18, y + preview_height + 20), label, fill="#d8dde1", font=label_font)
    output.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(output, format="PNG", optimize=True)


def research_sheet() -> None:
    items = [
        (
            "D STRUCTURE | ClearVision-only target architecture",
            option_root("D") / "references" / "d_flow_architecture_blueprint.png",
        ),
        (
            "OFFICIAL ROBoflow | compact topology and node weight",
            option_root("D") / "references" / "official" / "01_build_workflow_overview.png",
        ),
        (
            "OFFICIAL Roboflow | editor-to-test entry relationship",
            option_root("D") / "references" / "official" / "03_test_button_editor.png",
        ),
        (
            "OFFICIAL Roboflow | contextual testing pane relationship",
            option_root("D") / "references" / "official" / "04_test_pane.png",
        ),
    ]
    for _, path in items:
        if not path.is_file():
            raise FileNotFoundError(f"Missing research reference: {path}")
    output = option_root("D") / "research" / "roboflow_reference_board.png"
    contact_sheet(
        items,
        output,
        "Option D Reference Board | Architecture patterns only, never product functions",
        columns=2,
        card_width=900,
        preview_height=500,
    )
    print(f"Research sheet: {output}")


def master_sheet() -> None:
    manifest = load_manifest()
    by_id = {entry["id"]: entry for entry in manifest["entries"]}
    items: list[tuple[str, Path]] = []
    for option in OPTIONS:
        for role in ("flow", "ai", "settings"):
            current_id = entry_id(option, MASTER_SCREENS[role])
            entry = by_id[current_id]
            master = option_image(option, "masters", entry["filename"])
            if master.is_file():
                items.append((f"{option}_{role.upper()} | {OPTION_DEFINITIONS[option]['name']}", master))
    contact_sheet(
        items,
        ROOT / "audit" / "comparison_DE" / "de_master_contact_sheet.png",
        "ClearVision D/E Master Screens",
        columns=3,
    )
    print(f"Master sheet entries: {len(items)}")


def audit() -> None:
    manifest = load_manifest()
    errors = validate_manifest(
        manifest, require_masters=True, require_outputs=True, require_reviews=True
    )
    if errors:
        raise ValueError("Final audit blocked:\n" + "\n".join(errors))
    by_screen_option = {
        (entry["screen_id"], entry["option"]): entry for entry in manifest["entries"]
    }
    comparison_items: list[tuple[str, Path]] = []
    option_items: dict[str, list[tuple[str, Path]]] = {option: [] for option in OPTIONS}
    option_comparison_items: dict[str, list[tuple[str, Path]]] = {
        option: [] for option in OPTIONS
    }
    delivery_screens: list[dict[str, Any]] = []
    comparison_dir = ROOT / "audit" / "comparison_DE"
    comparison_dir.mkdir(parents=True, exist_ok=True)
    for page_def in PAGES:
        entries = {option: by_screen_option[(page_def["screen_id"], option)] for option in OPTIONS}
        comparison = legacy.safe_named_path(comparison_dir, page_def["filename"])
        comparison_sheet(page_def, entries, comparison)
        comparison_items.append((page_def["screen_id"], comparison))
        options_payload: dict[str, Any] = {}
        for option in OPTIONS:
            entry = entries[option]
            candidate = option_image(option, "screens", page_def["filename"], must_exist=True)
            option_items[option].append((page_def["screen_id"], candidate))
            option_comparison = option_root(option) / "comparison" / page_def["filename"]
            single_option_comparison_sheet(page_def, option, option_comparison)
            option_comparison_items[option].append((page_def["screen_id"], option_comparison))
            with Image.open(candidate) as opened:
                dimensions = {"width": opened.width, "height": opened.height}
            options_payload[option] = {
                "file": legacy.rel(candidate),
                "sha256": legacy.sha256(candidate),
                "dimensions": dimensions,
                "status": entry["status"],
                "master_references": entry["master_references"],
                "functional_audit": entry["functional_audit"]["status"],
                "reject_gate": entry["reject_gate"]["status"],
                "comparison": legacy.rel(option_comparison),
            }
        current = legacy.root_path(page_def["current_reference"], must_exist=True)
        delivery_screens.append({
            "screen_id": page_def["screen_id"],
            "filename": page_def["filename"],
            "page_name": page_def["page_name"],
            "purpose": page_def["purpose"],
            "current_reference": page_def["current_reference"],
            "current_sha256": legacy.sha256(current),
            "comparison": legacy.rel(comparison),
            "options": options_payload,
        })
    for option in OPTIONS:
        contact_sheet(
            option_items[option],
            comparison_dir / f"option_{option}_contact_sheet.png",
            f"ClearVision Option {option} - {OPTION_DEFINITIONS[option]['name']}",
        )
        contact_sheet(
            option_comparison_items[option],
            option_root(option) / "comparison" / "contact_sheet.png",
            f"ClearVision CURRENT | {option} - Page Comparisons",
            columns=2,
            card_width=960,
            preview_height=290,
        )
    master_sheet()
    contact_sheet(
        comparison_items,
        comparison_dir / "de_comparison_index.png",
        "ClearVision CURRENT | D | E - Comparison Index",
        columns=2,
        card_width=1080,
        preview_height=160,
    )
    payload = build_readiness_manifest(manifest)
    if payload["readiness"]["overall"] != "READY":
        raise RuntimeError("Final audit readiness aggregation did not resolve to READY")
    payload["screens"] = delivery_screens
    payload["comparison_index"] = legacy.rel(comparison_dir / "de_comparison_index.png")
    legacy.atomic_write_json(DELIVERY_PATH, payload)
    rows = []
    for screen in delivery_screens:
        option_cells = " | ".join(
            f"`option_{option}/screens/{screen['filename']}`" for option in OPTIONS
        )
        rows.append(
            f"| `{screen['screen_id']}` | {screen['page_name']} | `{screen['current_reference']}` | "
            f"{option_cells} |"
        )
    coverage = ", ".join(f"{option}={len(option_items[option])}" for option in OPTIONS)
    header_cells = " | ".join(OPTIONS)
    separator_cells = " | ".join("---" for _ in OPTIONS)
    directions = "\n".join(
        f"- {option} - {OPTION_DEFINITIONS[option]['name']}: {OPTION_DEFINITIONS[option]['language']}"
        for option in OPTIONS
    )
    chains = "\n".join(
        f"- `{option}_FLOW_MASTER -> {option}_AI_MASTER -> {option}_SETTINGS_MASTER`"
        for option in OPTIONS
    )
    contact_files = ", ".join(f"`option_{option}_contact_sheet.png`" for option in OPTIONS)
    audit_index = f"""# ClearVision D/E Visual Audit Index

All generated images are visual references. Current ClearVision screenshots and code remain authoritative for copy, controls, routes, workflow names, state, and business data.

## Delivery Status

- Frozen real screens/states: `{len(PAGES)}`.
- Option coverage: `{coverage}`.
- Model: exact `gpt-image-2`; fallback: `false`.
- Functional manifest gate: passed for all `{len(PAGES) * len(OPTIONS)}` entries.
- Five-part Reject Gate: passed for all generated entries.
- Product-owner status: awaiting selection; no visual option is approved yet.

## Design Directions

{directions}

## Master Chains

{chains}

## Fast Review

- `de_comparison_index.png`: overview of every CURRENT/D/E page comparison.
- {contact_files}: whole-option scans.
- `de_master_contact_sheet.png`: all six Master Screens.
- Individual page sheets in this directory preserve the same `CURRENT | D | E` order.
- `option_D/comparison/contact_sheet.png` and `option_E/comparison/contact_sheet.png`: direct CURRENT-to-option review.

## Page Mapping

| Screen | Page | Current | {header_cells} |
| --- | --- | --- | {separator_cells} |
{"\n".join(rows)}
"""
    (comparison_dir / "audit_index.md").write_text(audit_index, encoding="utf-8")
    print(f"Final comparisons: {len(comparison_items)}")
    print(f"Delivery manifest: {DELIVERY_PATH}")


def status() -> None:
    manifest = load_manifest()
    print(f"Schema: {manifest.get('schema_version')}")
    for option in OPTIONS:
        entries = [entry for entry in manifest["entries"] if entry["option"] == option]
        counts = {state: sum(1 for entry in entries if entry["status"] == state) for state in sorted(ALLOWED_STATUSES)}
        print(f"Option {option}: {counts}")


def parse_ids(value: str | None) -> set[str] | None:
    if value is None:
        return None
    return {item.strip() for item in value.split(",") if item.strip()}


def load_iteration_instructions(value: str | None) -> dict[str, str] | None:
    if value is None:
        return None
    path = legacy.root_path(value, must_exist=True)
    if path.suffix.lower() != ".json":
        raise ValueError("Iteration instruction file must be JSON")
    payload = legacy.read_json(path)
    if not isinstance(payload, dict):
        raise ValueError("Iteration instruction file must contain an object")
    if payload.get("schema_version") != "clearvision-ui-iteration-instructions.v1":
        raise ValueError("Iteration instruction file schema is unsupported")
    instructions = payload.get("instructions")
    if not isinstance(instructions, dict) or not instructions:
        raise ValueError("Iteration instruction file has no instructions")
    normalized: dict[str, str] = {}
    for current_id, instruction in instructions.items():
        if not isinstance(current_id, str) or not current_id.strip():
            raise ValueError("Iteration instruction ids must be non-empty strings")
        if not isinstance(instruction, str) or not instruction.strip():
            raise ValueError(f"Iteration instruction is empty: {current_id}")
        normalized[current_id.strip()] = instruction.strip()
    return normalized


def main() -> None:
    parser = argparse.ArgumentParser(description="ClearVision D/E Visual Master workflow")
    subparsers = parser.add_subparsers(dest="command", required=True)
    init_parser = subparsers.add_parser("init", help="Initialize the frozen 24 x 2 manifest")
    init_parser.add_argument("--force", action="store_true")
    subparsers.add_parser("refresh", help="Refresh frozen functional contracts without resetting outputs")
    validate_parser = subparsers.add_parser("validate", help="Validate coverage and functional gates")
    validate_parser.add_argument("--require-masters", action="store_true")
    validate_parser.add_argument("--require-outputs", action="store_true")
    validate_parser.add_argument("--require-reviews", action="store_true")
    subparsers.add_parser("contract-probes", help="Run offline immutable-contract mutation probes")
    subparsers.add_parser("readiness", help="Refresh the honest D/E readiness manifest and index")
    subparsers.add_parser("preflight", help="Require exact gpt-image-2 from /models")
    generate_parser = subparsers.add_parser("generate", help="Generate selected retryable entries")
    generate_parser.add_argument("--ids", help="Comma-separated ids, for example D_05_flow_editor")
    generate_parser.add_argument("--option", choices=OPTIONS)
    generate_parser.add_argument("--role", choices=("anchor", "local"))
    generate_parser.add_argument("--force", action="store_true")
    generate_parser.add_argument("--concurrency", type=int, default=3)
    generate_parser.add_argument("--iteration-instruction", help="Audited single-entry correction appended to the prompt")
    generate_parser.add_argument(
        "--iteration-instructions-file",
        help="Root-relative audited JSON mapping of entry ids to correction instructions",
    )
    generate_parser.add_argument(
        "--retry-mode", choices=RETRY_MODES, default="legacy",
        help="Reference-board policy; current-dominant omits the rejected candidate",
    )
    review_parser = subparsers.add_parser("review", help="Record the five-part manual Reject Gate")
    review_parser.add_argument("--ids", required=True)
    review_parser.add_argument("--decision", choices=("pass", "reject"), required=True)
    review_parser.add_argument("--note", required=True)
    promote_parser = subparsers.add_parser("promote", help="Promote inspected same-option Master candidates")
    promote_parser.add_argument("--ids", required=True)
    restore_parser = subparsers.add_parser("restore", help="Restore an audited generated iteration")
    restore_parser.add_argument("--id", required=True)
    restore_parser.add_argument("--iteration", required=True, help="Bare filename from option_X/iterations")
    restore_parser.add_argument("--note", required=True)
    touchup_parser = subparsers.add_parser(
        "touchup", help="Apply a deterministic factual Picture Layer correction"
    )
    touchup_parser.add_argument("--id", required=True)
    touchup_parser.add_argument("--spec", required=True, help="Controlled spec under workflow/touchups")
    rebind_parser = subparsers.add_parser(
        "rebind-master-references",
        help="Rebind changed same-option Master hashes without changing candidate pixels",
    )
    rebind_parser.add_argument("--ids", required=True)
    rebind_parser.add_argument("--note", required=True)
    subparsers.add_parser("master-sheet", help="Build the available D/E Master contact sheet")
    subparsers.add_parser("research-sheet", help="Build the audited Option D official-reference board")
    subparsers.add_parser("audit", help="Build final CURRENT/D/E comparisons and delivery manifests")
    subparsers.add_parser("status", help="Print per-option status counts")
    args = parser.parse_args()
    try:
        if args.command == "init":
            initialize(args.force)
        elif args.command == "refresh":
            refresh_contracts()
        elif args.command == "validate":
            validate_command(
                args.require_masters, args.require_outputs, args.require_reviews
            )
        elif args.command == "contract-probes":
            contract_probes()
        elif args.command == "readiness":
            write_readiness_artifacts(load_manifest())
        elif args.command == "preflight":
            preflight_command()
        elif args.command == "generate":
            if not 1 <= args.concurrency <= 4:
                raise ValueError("--concurrency must be between 1 and 4")
            generate(
                parse_ids(args.ids), args.option, args.role, args.force,
                args.concurrency, args.iteration_instruction,
                load_iteration_instructions(args.iteration_instructions_file),
                args.retry_mode,
            )
        elif args.command == "review":
            review_entries(
                parse_ids(args.ids) or set(), args.decision, args.note
            )
        elif args.command == "promote":
            promote(parse_ids(args.ids) or set())
        elif args.command == "restore":
            restore_iteration(args.id, args.iteration, args.note)
        elif args.command == "touchup":
            apply_picture_layer_touchup(args.id, args.spec)
        elif args.command == "rebind-master-references":
            rebind_master_references(parse_ids(args.ids) or set(), args.note)
        elif args.command == "master-sheet":
            master_sheet()
        elif args.command == "research-sheet":
            research_sheet()
        elif args.command == "audit":
            audit()
        elif args.command == "status":
            status()
    except (FileNotFoundError, KeyError, ValueError, RuntimeError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        raise SystemExit(1) from exc


if __name__ == "__main__":
    main()
