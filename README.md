# 像素飞机大战

Unity 竖屏像素射击游戏，包含自动射击、敌机与地面坦克、循环滚动地图、生命补给、护盾与清屏炸弹。

## 打开工程

使用 **Unity 2022.3.62f2c1**，通过 Unity Hub 添加仓库根目录。导入后打开 `Assets/Main.unity`，点击 Play。

仓库包含 `Assets`、`Packages`、`ProjectSettings` 和资源对应的 `.meta` 文件。首次导入需要重新生成 Unity 缓存。

## 操作

- 鼠标按住相对拖动，或使用 WASD / 方向键移动；手机使用单指拖动。
- 飞机自动射击；拾取生命、护盾和炸弹道具。
- 界面按钮用于开始、暂停、继续与重新开始；Esc 暂停或继续。

## 内容

- 飞机与坦克采用独立移动、开火和受击逻辑，坦克由底盘与炮塔组合。
- 地图使用地块拼接道路、水域、岸线、建筑和植被，并循环滚动。
- UI 包含素材数字、中文字体、按钮按压反馈、技能时间和独立战斗视口。
- 道具以 1 倍基础缩放显示，保留呼吸动画，碰撞半径随缩放同步调整。

## 制作要求与代码

- [制作要求](Docs/制作要求.md)
- 主要玩法：`Assets/Scripts/PixelAirBattle.cs`
- 地图：`Assets/Scripts/PixelTerrain.cs`
- 数字显示：`Assets/Scripts/SpriteNumbers.cs`
- 按钮反馈：`Assets/Scripts/ButtonFeedback.cs`
- 自动化自测：`Assets/Scripts/GameSelfTest.cs`

## 构建

Windows 构建入口为 `BuildGame.BuildWindows`，Android APK 为 `BuildGame.BuildAndroid`。可通过 Unity 的 `-executeMethod` 调用，使用 `-buildOutput` 指定输出文件。

构建前安装对应平台模块；Android 使用 IL2CPP，支持 ARM64 与 ARMv7，最低 Android API 23。构建入口会刷新资源导入设置并重建主场景，执行前请保存并提交场景改动。

## 验证范围

已有 2026-10-14 的 Windows 自动化自测记录，报告为通过且运行错误数为 0，覆盖地图连接、道具尺寸与碰撞、菜单状态、坦克和战斗规则。该记录属于历史构建，本次上传没有重新构建或运行测试。

现有 Android 安装包早于源码中的道具尺寸调整，且启动闪烁修复没有真机确认。本仓库源码不代表该旧 APK 的完整内容；需要 Android 验收时应从此工程重新构建并进行设备测试。

## 素材说明

工程保留随附字体许可证。仓库提供项目展示与学习入口，不额外授予第三方美术和字体的再分发或商业使用权，复用前请核对原素材许可。
