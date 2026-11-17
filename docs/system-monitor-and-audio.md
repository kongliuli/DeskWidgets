# 系统资源与音量组件

## 系统资源（system-monitor）

从托盘加载“系统资源 · CPU GPU 内存”。默认进度条，可直接点组件内标签切换雷达、独立环形图、折线。设置中可开关 CPU/GPU/内存和暂停采样；透明度及毛玻璃沿用公共外观控件。

- CPU：GetSystemTimes 的累计内核/用户/空闲时间差，显示整机忙碌百分比，不是 DeskWidgets 的进程占用。超过 64 个逻辑处理器时 API 只覆盖当前处理器组，状态会说明；此口径也可能与任务管理器的频率加权值不同。
- 内存：GlobalMemoryStatusEx 的总物理内存与可用物理内存，显示已用百分比及已用/总 GiB。
- GPU：PDH `GPU Engine(*)/Utilization Percentage`，同一 LUID、物理适配器、引擎的各进程数据求和，再取最忙引擎。多卡时取所有可读引擎的最高值，不把多个引擎相加或求平均。依赖系统/驱动计数器；不可用显示 `--`，预热和真实 0% 区分。
- 每 2 秒采样一次，多开共享后台采样。关闭全部实例、暂停或页面隐藏后释放订阅，不在 UI 线程轮询 PDH，也不运行 PowerShell 子进程。
- 环形图分别显示每种资源已用/余量，三者不是一份可相加的饼。雷达至少需要三项，关闭部分指标后自动显示条形；折线保留最多 60 个真实时间戳样本，暂停和缺失数据之间不连成假趋势。

## 音量与输出电平（audio-control）

读取默认多媒体播放端点，可用滑块设置系统主音量，按钮静音/取消静音。电平可在设置中关闭；启用电平时最多 10 Hz，关闭后只以 1 Hz 更新音量状态。默认设备会周期重新识别。

电平是 IAudioMeterInformation 的**主音量衰减前峰值**，按 `20*log10(peak)` 显示 dBFS。静音/主音量降低时该信号峰值仍可能存在，不能把它当成最终扬声器输出。零峰值显示 −∞，不可用显示 `--`。它不是经过校准的房间声压 dB SPL，也不采集麦克风或录制 PCM 音频。独占模式下，若设备没有硬件电平计，软件表可能报告 0。

只有用户操作音量/静音控件才提交写请求；锁定状态禁止写操作。宿主 `--self-test` 模式只回显合法命令用于回归，不改变系统当前音量或静音。

## 锁定与拖动

锁定后隐藏其他工具按钮、宿主拖拽柄和功能控件，只保留“解锁组件”；数据仍可继续更新。设置/搜索弹层会关闭，城市切换等按钮禁用。解锁恢复之前的控件状态。

网页拖动采用 pointer capture，并发送 `startDrag / dragMove / endDrag / cancelDrag`；宿主保留浏览器的鼠标捕获与 WPF 鼠标松开通道，不再用 GetAsyncKeyState 的物理鼠标状态决定拖动结束。虚框仍独立显示，松手后只移动真实窗口一次。特别需要验证沉底/置顶切换、远程鼠标与跨屏情形。

本轮有上限的 `input-trace.jsonl` 仅记录组件名称、拖拽阶段、置顶状态和按键/捕获状态，用于定位沉底输入问题，不记录其他应用内容或全局鼠标轨迹。

## 验证范围

模型测试：CPU 空闲时间扣除、GPU 引擎合并、无数据与零值、dBFS 换算、有界历史及布局回退。宿主回归：实际系统 CPU/内存/GPU 尝试、只读音频接口、四种布局、指标开关、暂停/恢复、尺寸、锁定、订阅清理与音量命令测试回显。真实音量写入未在自动测试中执行。

参考：[GPU 统计口径](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/)、[GetSystemTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes)、[IAudioEndpointVolume](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nn-endpointvolume-iaudioendpointvolume)、[IAudioMeterInformation](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nn-endpointvolume-iaudiometerinformation)。
