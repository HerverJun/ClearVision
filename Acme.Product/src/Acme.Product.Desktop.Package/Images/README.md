# MSIX 打包资源文件

本文件夹需要以下图像资源文件用于MSIX打包：

## 必需文件

| 文件名 | 尺寸 | 说明 |
|--------|------|------|
| Square44x44Logo.png | 44x44 | 应用图标（小） |
| Square150x150Logo.png | 150x150 | 应用图标（中） |
| Wide310x150Logo.png | 310x150 | 宽版应用图标 |
| StoreLogo.png | 50x50 | 商店图标 |
| SplashScreen.png | 620x300 | 启动屏幕 |

## 可选文件（推荐添加）

| 文件名 | 尺寸 | 说明 |
|--------|------|------|
| Square71x71Logo.png | 71x71 | 应用图标（列表视图） |
| Square89x89Logo.png | 89x89 | 应用图标 |
| Square107x107Logo.png | 107x107 | 应用图标 |
| Square142x142Logo.png | 142x142 | 应用图标 |
| Square284x284Logo.png | 284x284 | 应用图标（大） |
| Square310x310Logo.png | 310x310 | 应用图标（大） |

## 图标设计建议

- 使用透明背景的PNG格式
- 主要图标颜色建议：#1890ff（蓝色）
- 保持简洁，在44x44小尺寸下仍可识别
- 可以包含"CV"或眼睛/相机等视觉元素

## 生成脚本

可以使用以下PowerShell脚本生成简单的占位图标：

```powershell
# 需要安装 ImageMagick
$size = 150
$filename = "Square150x150Logo.png"
convert -size ${size}x${size} xc:#1890ff -pointsize 30 -fill white -gravity center -annotate +0+0 "CV" $filename
```

或使用在线图标生成器创建专业的应用图标。