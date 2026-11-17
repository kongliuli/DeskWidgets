# Weather Card：程序化天气卡片

插件目录为 `widgets/weather-card/`，包含清单、HTML、CSS、主控制器和独立 Canvas 2D 引擎五个文件。天气引擎不依赖 npm、图像素材或前端构建工具；外观控件复用宿主 `_shared/appearance.js` 和 CSS。

## 使用

构建后从托盘“加载挂件 → Weather Card → 打开新实例”加载。默认南京（32.0603, 118.7969），点击城市名称搜索全球城市；城市仅保存于当前实例的 localStorage。右上烧杯按钮打开模拟器，可预览七种天气、昼夜和 0–100 km/h 风速。“恢复实时”退出预览；预览不会额外请求天气，也不会修改城市或把模拟天气写入缓存。

清单使用 `defaultWidth/defaultHeight`（340×210），宿主同时兼容旧插件的 `width/height`。最小尺寸 260×160。卡片实际高度低于 180 时隐藏逐小时胶囊；更大尺寸自动放大数字和间距。

新增的外观按钮可调整背景透明度和毛玻璃强度，独立保存，文字保持清晰；背景全透明时暂停 Canvas。见 [组件外观说明](widget-appearance.md)。

## 引擎

- CSS 注册三个 `<color>` 天空属性，以 1.8 秒插值完成昼夜与天气色调过渡。
- Canvas 根据自身 CSS 尺寸和 devicePixelRatio 分配 backing store，ResizeObserver 与窗口 resize/DPI 变化共同更新。
- 太阳光晕、体积光束、微尘、视差繁星和流星；缓存的径向渐变云纹理按风速、风向与景深漂移。
- 雨滴对象池固定 350 个，风驱动横向速度、重力与终端下落速度；底部采用有界水花池。
- 180 个分层雪花，按场景强度激活；旧雨雪停止重生并自然离开画面。雾气采用超大径向渐变纹理叠加。
- 雷暴每 4–10 秒产生分形主闪电及分支，配合两次短促衰减光脉冲。减少动态效果偏好开启时显示静态天气，不播放闪电。
- 雨、雪与雷暴最多 30 FPS，平静场景最多 20 FPS；旧降水在新场景帧率下自然退场。页面隐藏会取消 RAF，恢复可见时重置帧间隔，避免补跑隐藏期间的时间。pagehide 释放观察器、监听器和请求。

前端隐藏暂停不等于整个桌面宿主或 GPU 占用归零，WebView2、宿主合成和其他挂件仍有基础开销；本项目不把帧率上限等同于实测功耗。

## 数据与宿主契约

[Open-Meteo Forecast API](https://open-meteo.com/en/docs) 每 15 分钟更新一次；使用 `timeformat=unixtime` 消除时区和夏令时歧义，再按返回的 IANA 时区显示整点。通常请求 `forecast_days=1`；若午夜前不足五个未来整点，补取两日预报。补取失败保留有效当前数据，缺失时段显示占位，不伪造预报。

[Geocoding API](https://open-meteo.com/en/docs/geocoding-api) 使用 name、count=5、language=zh。请求有超时和取消控制，切换城市会放弃旧请求；断网时明确标注缓存时间，未获取过数据时显示失败和重试入口。默认免费端点限非商业用途，遵循 Open-Meteo / CC BY 4.0 标注要求。

天气业务与引擎文件采用相对引用，当前宿主使用每实例的 `https://w-<id>.widgets.local/` 映射，并将公共外观控件映射到 `https://assets.widgets.local/`。使用旧的单一 `widgets.local` 宿主时也需提供公共资源映射。非交互区左键发送 `startDrag`，关闭发送 `closeWidget`。编辑面板通过现有 `setInteractive` 临时允许键盘输入；支持 `hostState.locked`。

## 验证

```powershell
node tests/weather-card.test.mjs
dotnet build
powershell -NoProfile -ExecutionPolicy Bypass -File tests/host-smoke.ps1
```

Node 仅用于开发断言。模型测试覆盖全部 WMO 代码、错误数据、跨午夜/DST、粒子池复用和自然退场、闪电分支、20/30 FPS 与隐藏暂停。宿主测试使用隔离目录和受控 API 响应，实际加载 ES Modules、运行七场景昼夜预览、操作风速/城市、验证请求次数、三种尺寸与高 DPI backing store，并保留 PNG 和测试回执。受控截图中的温度不是实时观测值。
