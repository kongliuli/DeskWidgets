import { mountAppearanceControls } from './appearance.js';
import { bindWidgetDrag, applyWidgetLock } from './drag.js';

export const send = (type, fields = {}) => window.chrome?.webview?.postMessage({ type, ...fields });
export function readPreference(key, fallback) {
  try { return JSON.parse(localStorage.getItem(key)) ?? fallback; } catch { return fallback; }
}
export function savePreference(key, value) {
  try { localStorage.setItem(key, JSON.stringify(value)); return true; } catch { return false; }
}
export async function loadManifest() {
  const response = await fetch('./widget.json', { cache: 'no-store' });
  if (!response.ok) throw new Error('无法读取插件清单');
  return response.json();
}
export function mountHost(widget) {
  widget.insertAdjacentHTML('afterbegin', `<div class="widget-surface" aria-hidden="true"><div class="background"></div><div class="background-glass"></div><div class="shade"></div></div><nav class="tools" aria-label="挂件控制">
    <button data-action="settings" title="设置" aria-label="设置"><svg viewBox="0 0 16 16"><path d="M3 4h10M3 12h10M6 2v4m4 4v4"/></svg></button>
    <button data-action="toggleTopmost" title="置顶 / 沉底" aria-label="置顶 / 沉底" aria-pressed="false"><svg viewBox="0 0 16 16"><path d="M5 2h6l-1 5 3 3H3l3-3zM8 10v4"/></svg></button>
    <button data-action="toggleLock" title="锁定 / 解锁" aria-label="锁定 / 解锁" aria-pressed="false"><svg viewBox="0 0 16 16"><rect x="3" y="7" width="10" height="7" rx="2"/><path d="M5 7V4a3 3 0 0 1 6 0v3"/></svg></button>
    <button data-action="closeWidget" title="关闭" aria-label="关闭">×</button></nav>`);
  widget.querySelectorAll('[data-action]').forEach(button => button.addEventListener('click', () => {
    if (button.dataset.action !== 'settings') send(button.dataset.action);
  }));
  bindWidgetDrag(widget, send);
  window.chrome?.webview?.addEventListener('message', ({ data }) => {
    if (data.type !== 'hostState') return;
    applyWidgetLock(widget, data.locked);
    widget.querySelector('[data-action=toggleLock]').setAttribute('aria-pressed', String(data.locked));
    widget.querySelector('[data-action=toggleTopmost]').setAttribute('aria-pressed', String(data.topmost));
  });
}
export function applyBackground(widget, background = { type: 'none' }) {
  widget.dataset.backgroundType = background.type;
  const layer = widget.querySelector('.background');
  layer.replaceChildren();
  if (background.type === 'scene') {
    const scene = ['utc', 'beijing', 'new-york', 'los-angeles', 'clouds', 'almanac'].includes(background.scene) ? background.scene : 'utc';
    const element = document.createElement('div');
    element.className = `scene ${scene}`;
    const source = `https://assets.widgets.local/backgrounds/${scene}-v1.png`;
    element.style.backgroundImage = `url("${source}")`;
    const probe = new Image();
    probe.onerror = () => { element.style.backgroundImage = `url("https://assets.widgets.local/scenes/${scene}.svg")`; };
    probe.src = source;
    if (scene === 'clouds') {
      for (const depth of ['far', 'middle', 'near']) {
        const cloud = document.createElement('div');
        cloud.className = `cloud-layer ${depth}`;
        element.append(cloud);
      }
    }
    layer.append(element);
  } else if (background.type === 'image' || background.type === 'video') {
    const url = new URL(background.path, location.href);
    if (url.origin !== location.origin) throw new Error('背景文件请放在当前插件目录，填写相对路径');
    const element = document.createElement(background.type === 'video' ? 'video' : 'img');
    element.src = url.href;
    if (element instanceof HTMLVideoElement) { element.autoplay = widget.dataset.backgroundHidden !== 'true'; element.loop = true; element.muted = true; element.playsInline = true; }
    element.addEventListener('error', () => { widget.dataset.backgroundError = 'true'; });
    layer.append(element);
  }
}
export function setupSettings(widget, defaults, extraMarkup = '', afterSave = () => {}, validateExtra = () => ({})) {
  let preferences = readPreference('settings', {});
  if (!preferences || typeof preferences !== 'object' || Array.isArray(preferences)) preferences = {};
  const current = () => ({ ...defaults, ...preferences });
  let settings = current();
  try { applyBackground(widget, settings.background); } catch { applyBackground(widget, defaults.background); }
  const dialog = document.createElement('dialog');
  dialog.className = 'widget-settings';
  dialog.innerHTML = `<h2>挂件设置</h2><form>${extraMarkup}<label>背景<select name="backgroundType"><option value="scene">插件默认动态场景</option><option value="image">图片 / GIF / 动态 WebP</option><option value="video">循环视频 MP4 / WebM</option><option value="none">纯色半透明</option></select></label><label>插件内背景路径<input name="backgroundPath" placeholder="media/background.gif" maxlength="500"></label><p class="error" role="alert"></p><div class="actions"><button type="button" data-cancel>取消</button><button type="submit">保存</button></div></form>`;
  widget.append(dialog);
  const form = dialog.querySelector('form');
  const appearance = mountAppearanceControls(widget, form);
  widget.querySelector('[data-action=settings]').addEventListener('click', () => {
    appearance.reset();
    settings = current();
    form.elements.backgroundType.value = settings.background?.type ?? 'scene';
    form.elements.backgroundPath.value = settings.background?.path ?? '';
    for (const input of form.querySelectorAll('[data-setting]')) {
      if (input.type === 'checkbox') input.checked = Boolean(settings[input.name]);
      else input.value = settings[input.name] ?? '';
    }
    dialog.querySelector('.error').textContent = '';
    send('setInteractive', { enabled: true });
    dialog.showModal();
    setTimeout(() => (form.querySelector('input[type=range],[data-setting],select')?.focus()), 150);
  });
  dialog.querySelector('[data-cancel]').addEventListener('click', () => dialog.close());
  dialog.addEventListener('close', () => send('setInteractive', { enabled: false }));
  form.addEventListener('submit', event => {
    event.preventDefault();
    try {
      const background = { type: form.elements.backgroundType.value, path: form.elements.backgroundPath.value.trim(), scene: defaults.background?.scene };
      if (['image', 'video'].includes(background.type) && !background.path) throw new Error('请填写本地背景路径');
      const extra = validateExtra(new FormData(form));
      applyBackground(widget, background);
      preferences = { ...preferences, ...extra, background };
      if (!savePreference('settings', preferences)) throw new Error('无法保存设置，请检查浏览器配置目录');
      appearance.save();
      dialog.close(); afterSave(current());
    } catch (error) { dialog.querySelector('.error').textContent = error.message; }
  });
  return settings;
}
