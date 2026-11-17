# 组件背景透明度与毛玻璃

所有内置组件按实例保存外观设置。时钟、旧天气和黄历使用原设置按钮；Canvas Weather Card 使用右上角新增的外观滑块图标。

- **背景透明度：0–100%**。0% 显示完整背景，100% 隐藏背景层；文字、按钮、边框和数据保持清晰。默认 15%。
- **毛玻璃强度：0–24 px**。0 关闭组件背景滤镜，默认 8 px；图片、视频和 Canvas 气象画面都可作为磨砂素材。
- 滑块即时预览，保存后写入该实例 localStorage 的 `appearance`，取消或 Esc 恢复之前的值。各实例的 profile/origin 原本已隔离，不会共用设置。

背景、玻璃层和阴影统一位于独立 surface 中，仅该层改变透明度；不使用 Window.Opacity 或根元素 opacity，所以不会淡化文字。100% 透明时保留极低 alpha 的命中底层，避免分层窗口的透明区域穿透鼠标而无法拖动。背景全透明会暂停视频、CSS 云层以及天气 Canvas；降低毛玻璃强度到 0 会关闭主要展示层的滤镜。

这里的 backdrop-filter 模糊的是**组件内部**的背景图片、视频或 Canvas；透明部分可透出桌面，但不提供桌面壁纸或其他应用的系统级实时 Acrylic 模糊。当前宿主为 Windows 10 的透明 WPF/WebView2 窗口，未加入未公开的系统合成接口，也未捕获屏幕或桌面截图。

共享实现位于 `_shared/appearance.js` 和 `_shared/appearance.css`，新插件可以复用。Weather Card 天气引擎本身仍独立，外观控件复用宿主公共模块。

验证命令：

```powershell
node tests/appearance.test.mjs
node tests/weather-card.test.mjs
dotnet build -o .scratch/appearance
powershell -NoProfile -ExecutionPolicy Bypass -File tests/host-smoke.ps1 -BuildDirectory .scratch/appearance
```

实际宿主回归覆盖背景独立 alpha、真实 backdrop-filter 属性、取消回滚、实例隔离、保存恢复、全透明 PNG 的低 alpha 命中底层、视频恢复、Canvas 全透明暂停与取消后恢复。

技术边界参考：[MDN backdrop-filter](https://developer.mozilla.org/en-US/docs/Web/CSS/Reference/Properties/backdrop-filter)、[微软 DwmEnableBlurBehindWindow](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmenableblurbehindwindow)。
