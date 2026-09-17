# 沥青拌合站软件图标 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将沥青拌合站主题图标接入 Windows WPF 应用，使发布后的桌面快捷方式不再显示默认控制面板图标。

**Architecture:** 使用生成的透明 PNG 作为源图，转换为包含常用 Windows 尺寸的 `.ico` 文件；在 `AsphaltPlantManager.App.csproj` 设置 `ApplicationIcon`，不改业务代码和数据目录。

**Tech Stack:** WPF、.NET 8、Windows `.ico`、ImageGen 生成 PNG、PowerShell/.NET 图像转换工具。

## Global Constraints

- 只修改图标相关资源和项目配置。
- 不生成安装包；安装包留到所有功能修改完成后统一生成。
- 图标不放文字，缩小后仍能辨认沥青拌合站。

### Task 1: 生成并检查图标源图

**Files:**
- Create: `src/AsphaltPlantManager.App/Assets/asphalt-plant-icon.png`

- [ ] **Step 1:** 用内置图像生成工具生成透明方形图：深蓝圆角底、橙黄色沥青料流、简化拌合站塔楼轮廓、无文字无水印。
- [ ] **Step 2:** 检查图像主体居中、边缘透明、缩小后轮廓清楚。

### Task 2: 转换 Windows 图标并接入项目

**Files:**
- Create: `src/AsphaltPlantManager.App/Assets/asphalt-plant.ico`
- Modify: `src/AsphaltPlantManager.App/AsphaltPlantManager.App.csproj`

- [ ] **Step 1:** 从源 PNG 生成 16、24、32、48、64、128、256 像素的 ICO 图标。
- [ ] **Step 2:** 在项目属性中设置 `<ApplicationIcon>Assets\asphalt-plant.ico</ApplicationIcon>`。
- [ ] **Step 3:** 将 PNG/ICO 标记为项目资源并复制到发布输出。

### Task 3: 验证

**Files:**
- Test: `src/AsphaltPlantManager.App/AsphaltPlantManager.App.csproj`

- [ ] **Step 1:** 运行 Release 构建，确认图标文件被编译进应用且无警告错误。
- [ ] **Step 2:** 检查 `git diff --check` 和工作树，确认没有生成安装包或修改无关文件。
