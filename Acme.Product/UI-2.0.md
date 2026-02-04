# ClearVision UI 2.0 - 数字水墨设计系统

> **设计理念**: 雅韵国风 × 现代科技  
> **设计主题**: 数字水墨 (Digital Ink)  
> **版本**: 2.0  
> **更新日期**: 2026-02-03

---

## 1. 设计哲学

### 1.1 概念来源

融合中国传统水墨画的**留白意境**与现代数字界面的**科技光感**，创造"数字水墨"视觉语言：

- **水墨意境**: 渐变晕染、虚实结合、留白呼吸
- **科技光感**: 霓虹边缘、微光浮动、流畅动效
- **国风元素**: 窗棂纹理、卷轴形态、传统色谱

### 1.2 核心原则

| 原则 | 国风表达 | 科技表达 |
|------|----------|----------|
| **留白** | 水墨画的虚实留白 | 界面呼吸空间 |
| **层次** | 远近景深 | Z轴层级与阴影 |
| **流动** | 水墨晕染 | 流畅动画过渡 |
| **光感** | 月光/烛光意境 | 霓虹发光效果 |

---

## 2. 色彩系统

### 2.1 主色调 - 黛蓝墨韵

```css
/* 主色 - 黛蓝 (中国传统色彩) */
--ink-primary: #1a3a52;        /* 深黛蓝 - 主背景 */
--ink-secondary: #2d4a5e;      /* 中黛蓝 - 次级背景 */
--ink-tertiary: #3d5a6e;       /* 浅黛蓝 - 悬浮背景 */

/* 强调色 - 朱砂霓虹 */
--cinnabar: #e74c3c;           /* 朱砂红 - 主要强调 */
--cinnabar-glow: rgba(231, 76, 60, 0.4);  /* 朱砂光晕 */
--gold-accent: #d4af37;        /* 鎏金色 - 次要强调 */
--gold-glow: rgba(212, 175, 55, 0.3);     /* 鎏金光晕 */

/* 中性色 - 宣纸墨韵 */
--paper-white: #f8f6f0;        /* 宣纸白 - 亮色文字 */
--ink-black: #0d1b2a;          /* 墨黑 - 深色背景 */
--ink-gray: #8b9da8;           /* 灰蓝 - 次要文字 */
--mist-gray: rgba(139, 157, 168, 0.15);  /* 雾灰 - 边框/分隔 */
```

### 2.2 渐变系统 - 水墨晕染

```css
/* 水墨渐变 */
--gradient-ink: linear-gradient(
    180deg,
    rgba(26, 58, 82, 0.95) 0%,
    rgba(13, 27, 42, 0.98) 100%
);

--gradient-card: linear-gradient(
    145deg,
    rgba(45, 74, 94, 0.6) 0%,
    rgba(26, 58, 82, 0.4) 100%
);

/* 霓虹光晕渐变 */
--gradient-cinnabar-glow: radial-gradient(
    ellipse at center,
    rgba(231, 76, 60, 0.15) 0%,
    transparent 70%
);

--gradient-gold-glow: radial-gradient(
    ellipse at top,
    rgba(212, 175, 55, 0.1) 0%,
    transparent 60%
);

/* 状态色 - 国风语义 */
--success-jade: #2ecc71;       /* 翡翠绿 - 成功 */
--warning-amber: #f39c12;      /* 琥珀黄 - 警告 */
--error-cinnabar: #c0392b;     /* 深朱砂 - 错误 */
--info-azure: #3498db;         /* 天青蓝 - 信息 */
```

### 2.3 语义色彩映射

| 状态 | 颜色 | 光晕效果 |
|------|------|----------|
| OK/通过 | --success-jade | 0 0 20px rgba(46, 204, 113, 0.3) |
| NG/缺陷 | --error-cinnabar | 0 0 20px rgba(192, 57, 43, 0.4) |
| 警告 | --warning-amber | 0 0 15px rgba(243, 156, 18, 0.3) |
| 运行中 | --cinnabar | 0 0 25px rgba(231, 76, 60, 0.5) |

---

## 3. 布局系统重构

### 3.1 整体架构 - 浮动层叠

**摒弃刻板三栏，采用"浮动卡片+层叠深度"布局：**

```
┌─────────────────────────────────────────────────────────────┐
│  顶部导航栏 (浮动玻璃态)                                      │
│  ┌───────────────────────────────────────────────────────┐  │
│  │ Logo │ 导航胶囊 │                    │ 操作组 │ 主题 │  │
│  └───────────────────────────────────────────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│   ┌──────────┐  ┌──────────────────────────┐  ┌──────────┐  │
│   │          │  │                          │  │          │  │
│   │  左侧面板 │  │      中央工作区           │  │ 右侧面板  │  │
│   │  (浮动)   │  │    (水墨背景+网格)        │  │  (浮动)   │  │
│   │          │  │                          │  │          │  │
│   └──────────┘  └──────────────────────────┘  └──────────┘  │
│                                                             │
│   ┌─────────────────────────────────────────────────────┐   │
│   │ 底部状态栏 (浮动玻璃态)                               │   │
│   └─────────────────────────────────────────────────────┘   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 3.2 关键布局变量

```css
/* 布局尺寸 - 更紧凑优雅 */
--header-height: 56px;           /* 降低高度 */
--footer-height: 28px;           /* 更细的状态栏 */
--sidebar-width: 280px;          /* 稍窄侧边栏 */
--sidebar-collapsed: 56px;       /* 折叠宽度 */
--content-gap: 16px;             /* 内容间距 */
--card-radius: 16px;             /* 更大圆角 */
--card-padding: 20px;            /* 卡片内边距 */

/* 浮动偏移 */
--float-offset: 12px;            /* 浮动卡片偏移 */
--float-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
```

### 3.3 玻璃态卡片系统

```css
/* 主玻璃态卡片 */
.glass-card {
    background: var(--gradient-card);
    backdrop-filter: blur(20px) saturate(180%);
    -webkit-backdrop-filter: blur(20px) saturate(180%);
    border: 1px solid rgba(255, 255, 255, 0.08);
    border-radius: var(--card-radius);
    box-shadow: 
        var(--float-shadow),
        inset 0 1px 0 rgba(255, 255, 255, 0.05);
}

/* 玻璃态头部 */
.glass-header {
    background: rgba(26, 58, 82, 0.85);
    backdrop-filter: blur(16px);
    border-bottom: 1px solid rgba(255, 255, 255, 0.06);
}

/* 霓虹边框卡片 (选中状态) */
.neon-card {
    background: rgba(45, 74, 94, 0.3);
    border: 1px solid var(--cinnabar);
    box-shadow: 
        0 0 0 1px rgba(231, 76, 60, 0.3),
        0 0 20px rgba(231, 76, 60, 0.2),
        inset 0 1px 0 rgba(255, 255, 255, 0.1);
}
```

---

## 4. 组件设计规范

### 4.1 导航胶囊 (Navigation Capsule)

**替代传统的按钮组，采用融合设计：**

```css
.nav-capsule {
    display: inline-flex;
    background: rgba(13, 27, 42, 0.6);
    border: 1px solid rgba(255, 255, 255, 0.08);
    border-radius: 100px;          /* 胶囊形状 */
    padding: 4px;
    gap: 4px;
}

.nav-capsule-item {
    padding: 8px 20px;
    border-radius: 100px;
    color: var(--ink-gray);
    font-size: 13px;
    font-weight: 500;
    transition: all 0.3s var(--ease-smooth);
    position: relative;
    overflow: hidden;
}

.nav-capsule-item.active {
    background: var(--gradient-cinnabar-glow);
    color: var(--paper-white);
    box-shadow: 
        0 0 20px rgba(231, 76, 60, 0.3),
        inset 0 1px 0 rgba(255, 255, 255, 0.2);
}

/* 水墨晕染悬停效果 */
.nav-capsule-item::before {
    content: '';
    position: absolute;
    inset: 0;
    background: radial-gradient(
        circle at center,
        rgba(231, 76, 60, 0.2) 0%,
        transparent 70%
    );
    opacity: 0;
    transition: opacity 0.3s;
}

.nav-capsule-item:hover::before {
    opacity: 1;
}
```

### 4.2 水墨按钮 (Ink Button)

```css
/* 主按钮 - 朱砂霓虹 */
.btn-ink-primary {
    position: relative;
    padding: 10px 24px;
    background: linear-gradient(135deg, #e74c3c 0%, #c0392b 100%);
    color: var(--paper-white);
    border: none;
    border-radius: 8px;
    font-size: 14px;
    font-weight: 600;
    cursor: pointer;
    overflow: hidden;
    transition: all 0.3s var(--ease-smooth);
    box-shadow: 
        0 4px 15px rgba(231, 76, 60, 0.3),
        0 0 0 1px rgba(231, 76, 60, 0.5),
        inset 0 1px 0 rgba(255, 255, 255, 0.2);
}

.btn-ink-primary:hover {
    transform: translateY(-2px);
    box-shadow: 
        0 8px 25px rgba(231, 76, 60, 0.4),
        0 0 0 1px rgba(231, 76, 60, 0.6),
        0 0 30px rgba(231, 76, 60, 0.3),
        inset 0 1px 0 rgba(255, 255, 255, 0.3);
}

/* 水墨扩散效果 */
.btn-ink-primary::after {
    content: '';
    position: absolute;
    top: 50%;
    left: 50%;
    width: 0;
    height: 0;
    background: radial-gradient(
        circle,
        rgba(255, 255, 255, 0.4) 0%,
        transparent 60%
    );
    transform: translate(-50%, -50%);
    transition: width 0.6s, height 0.6s;
}

.btn-ink-primary:active::after {
    width: 300px;
    height: 300px;
}

/* 次级按钮 - 鎏金边框 */
.btn-ink-secondary {
    padding: 10px 24px;
    background: transparent;
    color: var(--gold-accent);
    border: 1px solid var(--gold-accent);
    border-radius: 8px;
    font-size: 14px;
    font-weight: 500;
    cursor: pointer;
    transition: all 0.3s var(--ease-smooth);
}

.btn-ink-secondary:hover {
    background: rgba(212, 175, 55, 0.1);
    box-shadow: 
        0 0 20px rgba(212, 175, 55, 0.2),
        inset 0 0 10px rgba(212, 175, 55, 0.1);
}
```

### 4.3 算子卡片 (Operator Card)

```css
.operator-card {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 14px 16px;
    background: rgba(45, 74, 94, 0.3);
    border: 1px solid rgba(255, 255, 255, 0.05);
    border-radius: 12px;
    cursor: grab;
    transition: all 0.3s var(--ease-smooth);
    position: relative;
    overflow: hidden;
}

/* 左侧霓虹条 */
.operator-card::before {
    content: '';
    position: absolute;
    left: 0;
    top: 50%;
    transform: translateY(-50%);
    width: 3px;
    height: 0;
    background: var(--cinnabar);
    box-shadow: 0 0 10px var(--cinnabar);
    transition: height 0.3s var(--ease-smooth);
}

.operator-card:hover {
    background: rgba(45, 74, 94, 0.5);
    border-color: rgba(231, 76, 60, 0.3);
    transform: translateX(4px);
    box-shadow: 
        0 4px 20px rgba(0, 0, 0, 0.2),
        0 0 0 1px rgba(231, 76, 60, 0.1);
}

.operator-card:hover::before {
    height: 60%;
}

.operator-card.dragging {
    opacity: 0.9;
    transform: scale(1.05) rotate(1deg);
    box-shadow: 
        0 20px 40px rgba(0, 0, 0, 0.3),
        0 0 0 2px var(--cinnabar),
        0 0 30px rgba(231, 76, 60, 0.3);
}

/* 算子图标 */
.operator-icon {
    width: 36px;
    height: 36px;
    display: flex;
    align-items: center;
    justify-content: center;
    background: linear-gradient(135deg, rgba(231, 76, 60, 0.2) 0%, rgba(192, 57, 43, 0.1) 100%);
    border: 1px solid rgba(231, 76, 60, 0.3);
    border-radius: 10px;
    color: var(--cinnabar);
    font-size: 16px;
    box-shadow: 
        inset 0 1px 0 rgba(255, 255, 255, 0.1),
        0 2px 8px rgba(231, 76, 60, 0.2);
}
```

### 4.4 工程卡片 (Project Card)

```css
.project-card {
    background: var(--gradient-card);
    border: 1px solid rgba(255, 255, 255, 0.06);
    border-radius: var(--card-radius);
    padding: 24px;
    cursor: pointer;
    transition: all 0.4s var(--ease-smooth);
    position: relative;
    overflow: hidden;
}

/* 顶部光晕 */
.project-card::before {
    content: '';
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    height: 1px;
    background: linear-gradient(
        90deg,
        transparent 0%,
        rgba(212, 175, 55, 0.5) 50%,
        transparent 100%
    );
    opacity: 0;
    transition: opacity 0.3s;
}

.project-card:hover {
    transform: translateY(-6px) scale(1.02);
    border-color: rgba(212, 175, 55, 0.2);
    box-shadow: 
        0 20px 40px rgba(0, 0, 0, 0.3),
        0 0 0 1px rgba(212, 175, 55, 0.1),
        inset 0 1px 0 rgba(255, 255, 255, 0.1);
}

.project-card:hover::before {
    opacity: 1;
}

/* 工程名称 */
.project-card-title {
    font-size: 18px;
    font-weight: 600;
    color: var(--paper-white);
    margin-bottom: 8px;
    letter-spacing: 0.5px;
}

/* 工程描述 */
.project-card-desc {
    font-size: 13px;
    color: var(--ink-gray);
    line-height: 1.6;
    margin-bottom: 16px;
}

/* 元信息 */
.project-card-meta {
    display: flex;
    gap: 16px;
    font-size: 12px;
    color: var(--ink-gray);
    padding-top: 16px;
    border-top: 1px solid rgba(255, 255, 255, 0.05);
}
```

### 4.5 输入框 (Ink Input)

```css
.ink-input-group {
    position: relative;
    margin-bottom: 20px;
}

.ink-input {
    width: 100%;
    padding: 14px 16px;
    background: rgba(13, 27, 42, 0.5);
    border: 1px solid rgba(255, 255, 255, 0.08);
    border-radius: 10px;
    color: var(--paper-white);
    font-size: 14px;
    transition: all 0.3s var(--ease-smooth);
}

.ink-input:focus {
    outline: none;
    border-color: var(--cinnabar);
    box-shadow: 
        0 0 0 3px rgba(231, 76, 60, 0.1),
        0 0 20px rgba(231, 76, 60, 0.1);
    background: rgba(13, 27, 42, 0.7);
}

/* 浮动标签 */
.ink-input-label {
    position: absolute;
    left: 16px;
    top: 50%;
    transform: translateY(-50%);
    color: var(--ink-gray);
    font-size: 14px;
    pointer-events: none;
    transition: all 0.3s var(--ease-smooth);
    background: transparent;
    padding: 0 4px;
}

.ink-input:focus + .ink-input-label,
.ink-input:not(:placeholder-shown) + .ink-input-label {
    top: 0;
    font-size: 11px;
    color: var(--cinnabar);
    background: var(--ink-primary);
}
```

---

## 5. 动效系统

### 5.1 动画曲线

```css
/* 国风缓动 - 水墨流动感 */
--ease-ink: cubic-bezier(0.4, 0, 0.2, 1);        /* 标准水墨 */
--ease-ink-smooth: cubic-bezier(0.25, 0.1, 0.25, 1);  /* 丝滑 */
--ease-ink-bounce: cubic-bezier(0.34, 1.56, 0.64, 1); /* 弹性 */
--ease-ink-dramatic: cubic-bezier(0.87, 0, 0.13, 1);  /* 戏剧性 */

/* 时长 */
--duration-instant: 150ms;
--duration-fast: 250ms;
--duration-normal: 350ms;
--duration-slow: 500ms;
--duration-dramatic: 800ms;
```

### 5.2 关键动画

```css
/* 水墨晕染入场 */
@keyframes inkFadeIn {
    from {
        opacity: 0;
        transform: translateY(20px);
        filter: blur(10px);
    }
    to {
        opacity: 1;
        transform: translateY(0);
        filter: blur(0);
    }
}

/* 霓虹脉冲 */
@keyframes neonPulse {
    0%, 100% {
        box-shadow: 
            0 0 5px var(--cinnabar),
            0 0 10px var(--cinnabar),
            0 0 15px rgba(231, 76, 60, 0.5);
    }
    50% {
        box-shadow: 
            0 0 10px var(--cinnabar),
            0 0 20px var(--cinnabar),
            0 0 30px rgba(231, 76, 60, 0.8);
    }
}

/* 光晕呼吸 */
@keyframes glowBreathe {
    0%, 100% {
        opacity: 0.4;
    }
    50% {
        opacity: 0.8;
    }
}

/* 流光边框 */
@keyframes borderFlow {
    0% {
        background-position: 0% 50%;
    }
    100% {
        background-position: 200% 50%;
    }
}

/* 悬浮效果 */
@keyframes float {
    0%, 100% {
        transform: translateY(0);
    }
    50% {
        transform: translateY(-6px);
    }
}
```

---

## 6. 背景与纹理

### 6.1 水墨背景

```css
/* 主背景 - 深黛蓝渐变 */
.workspace {
    background: 
        /* 顶部光晕 */
        radial-gradient(ellipse at 50% 0%, rgba(231, 76, 60, 0.08) 0%, transparent 50%),
        /* 底部暗角 */
        radial-gradient(ellipse at 50% 100%, rgba(13, 27, 42, 0.8) 0%, transparent 50%),
        /* 主背景 */
        linear-gradient(180deg, var(--ink-primary) 0%, var(--ink-black) 100%);
}

/* 宣纸纹理叠加 */
.workspace::before {
    content: '';
    position: absolute;
    inset: 0;
    background-image: url("data:image/svg+xml,%3Csvg viewBox='0 0 400 400' xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='noiseFilter'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='4' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23noiseFilter)'/%3E%3C/svg%3E");
    opacity: 0.03;
    pointer-events: none;
}

/* 微妙网格 */
.workspace-grid {
    background-image: 
        linear-gradient(rgba(255, 255, 255, 0.02) 1px, transparent 1px),
        linear-gradient(90deg, rgba(255, 255, 255, 0.02) 1px, transparent 1px);
    background-size: 40px 40px;
}
```

### 6.2 窗棂纹理 (可选装饰)

```css
/* 传统窗棂装饰元素 */
.lattice-decoration {
    position: absolute;
    width: 200px;
    height: 200px;
    border: 1px solid rgba(212, 175, 55, 0.1);
    opacity: 0.3;
    pointer-events: none;
}

.lattice-decoration::before,
.lattice-decoration::after {
    content: '';
    position: absolute;
    border: 1px solid rgba(212, 175, 55, 0.1);
}

.lattice-decoration::before {
    inset: 20px;
    transform: rotate(45deg);
}

.lattice-decoration::after {
    inset: 40px;
}
```

---

## 7. 空状态设计

### 7.1 工程列表空状态

```html
<div class="empty-state">
    <div class="empty-icon">
        <svg viewBox="0 0 64 64" class="ink-icon">
            <!-- 水墨风格图标 -->
            <circle cx="32" cy="32" r="28" fill="none" stroke="currentColor" stroke-width="1" opacity="0.3"/>
            <circle cx="32" cy="32" r="20" fill="none" stroke="currentColor" stroke-width="1" opacity="0.5"/>
            <path d="M32 16 L32 48 M16 32 L48 32" stroke="currentColor" stroke-width="1" opacity="0.4"/>
        </svg>
    </div>
    <h3 class="empty-title">暂无工程</h3>
    <p class="empty-desc">点击下方按钮创建您的第一个视觉检测工程</p>
    <button class="btn-ink-primary">
        <span class="btn-icon">+</span>
        新建工程
    </button>
</div>
```

```css
.empty-state {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    padding: 60px 40px;
    text-align: center;
}

.empty-icon {
    width: 80px;
    height: 80px;
    color: var(--ink-gray);
    margin-bottom: 24px;
    opacity: 0.6;
}

.empty-title {
    font-size: 20px;
    font-weight: 600;
    color: var(--paper-white);
    margin-bottom: 8px;
    letter-spacing: 1px;
}

.empty-desc {
    font-size: 14px;
    color: var(--ink-gray);
    margin-bottom: 24px;
    max-width: 300px;
    line-height: 1.6;
}
```

---

## 8. 响应式适配

### 8.1 断点设计

```css
/* 大屏桌面 */
@media (min-width: 1600px) {
    :root {
        --sidebar-width: 320px;
        --content-gap: 24px;
    }
}

/* 标准桌面 */
@media (max-width: 1440px) {
    :root {
        --sidebar-width: 260px;
    }
}

/* 平板 */
@media (max-width: 1024px) {
    :root {
        --sidebar-width: 220px;
        --card-padding: 16px;
    }
    
    .nav-capsule-item {
        padding: 6px 16px;
        font-size: 12px;
    }
}

/* 移动端 */
@media (max-width: 768px) {
    :root {
        --header-height: 48px;
    }
    
    .sidebar {
        position: fixed;
        z-index: 100;
        transform: translateX(-110%);
        transition: transform 0.4s var(--ease-ink-smooth);
    }
    
    .sidebar.open {
        transform: translateX(0);
    }
    
    .nav-capsule {
        display: none;
    }
    
    .mobile-nav-toggle {
        display: flex;
    }
}
```

---

## 9. 实施计划

### Phase 1: 色彩与变量系统 (4h)
- [ ] 重写 `variables.css`，建立国风色彩系统
- [ ] 定义所有渐变、阴影、动画变量
- [ ] 创建暗色/亮色主题映射

### Phase 2: 布局重构 (6h)
- [ ] 重写 `main.css` 布局系统
- [ ] 实现浮动卡片布局
- [ ] 玻璃态头部和底部
- [ ] 水墨背景效果

### Phase 3: 组件升级 (8h)
- [ ] 重写 `ui-components.css`
- [ ] 导航胶囊组件
- [ ] 水墨按钮系统
- [ ] 算子卡片 redesign
- [ ] 工程卡片 redesign
- [ ] 输入框组件

### Phase 4: 动效系统 (4h)
- [ ] 实现所有关键动画
- [ ] 入场动画
- [ ] 交互反馈动画
- [ ] 霓虹光效

### Phase 5: 空状态与细节 (2h)
- [ ] 设计空状态组件
- [ ] 优化滚动条样式
- [ ] 添加装饰性元素

### Phase 6: 响应式适配 (2h)
- [ ] 实现响应式断点
- [ ] 移动端适配
- [ ] 触摸优化

**总计: 26小时**

---

## 10. 设计原则检查清单

实施时请确保：

- [ ] **留白充足**: 每个元素都有呼吸空间
- [ ] **层次清晰**: 通过阴影和透明度建立深度
- [ ] **色彩克制**: 主色不超过3种，强调色点睛
- [ ] **动效优雅**: 动画流畅不突兀，有水墨流动感
- [ ] **光感自然**: 霓虹效果柔和不刺眼
- [ ] **字体清晰**: 字号不小于12px，行高充足
- [ ] **边框含蓄**: 使用透明度边框，避免生硬线条
- [ ] **交互反馈**: 每个可交互元素都有悬停/点击状态

---

*设计系统 v2.0 - 数字水墨*
