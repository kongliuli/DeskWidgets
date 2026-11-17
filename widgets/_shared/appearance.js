export function normalizeAppearance(value) {
  const number = (key, fallback, maximum) => Number.isFinite(value?.[key]) ? Math.round(Math.max(0, Math.min(maximum, value[key]))) : fallback;
  return { transparency: number('transparency', 15, 100), blur: number('blur', 8, 24) };
}

export function applyAppearance(widget, value) {
  const appearance = normalizeAppearance(value);
  widget.style.setProperty('--surface-alpha', String(1 - appearance.transparency / 100));
  widget.style.setProperty('--glass-blur', `${appearance.blur}px`);
  widget.dataset.glass = appearance.blur > 0 ? 'on' : 'off';
  widget.dataset.backgroundHidden = String(appearance.transparency === 100);
  for (const video of widget.querySelectorAll('.background video')) {
    if (appearance.transparency === 100) video.pause();
    else if (video.paused) video.play().catch(() => { });
  }
  widget.dispatchEvent(new CustomEvent('appearancechange', { detail: appearance }));
  return appearance;
}

export function mountAppearanceControls(widget, form) {
  const fieldset = document.createElement('fieldset');
  fieldset.className = 'appearance-controls';
  fieldset.innerHTML = `<legend>外观</legend>
    <label for="appearance-transparency">背景透明度<output data-transparency>15%</output><input id="appearance-transparency" name="transparency" type="range" min="0" max="100" step="1" value="15" title="仅调整背景；文字与按钮不变"></label>
    <label for="appearance-blur">毛玻璃强度<output data-blur>8 px</output><input id="appearance-blur" name="glassBlur" type="range" min="0" max="24" step="1" value="8" title="模糊组件内部背景；0 表示关闭"></label>
    <p>0% 背景完整，100% 背景透明；文字与按钮保持清晰。毛玻璃模糊组件内部背景。</p>`;
  form.prepend(fieldset);
  const transparency = fieldset.querySelector('[name=transparency]');
  const blur = fieldset.querySelector('[name=glassBlur]');
  const read = () => { try { return normalizeAppearance(JSON.parse(localStorage.getItem('appearance'))); } catch { return normalizeAppearance(); } };
  let saved = read();
  const preview = () => {
    const value = applyAppearance(widget, { transparency: Number(transparency.value), blur: Number(blur.value) });
    fieldset.querySelector('[data-transparency]').value = `${value.transparency}%`;
    fieldset.querySelector('[data-blur]').value = value.blur ? `${value.blur} px` : '关闭';
  };
  const reset = () => {
    saved = read(); transparency.value = saved.transparency; blur.value = saved.blur; preview();
  };
  transparency.addEventListener('input', preview); blur.addEventListener('input', preview);
  form.closest('dialog')?.addEventListener('close', () => applyAppearance(widget, saved));
  reset();
  return {
    reset,
    save() {
      const value = normalizeAppearance({ transparency: Number(transparency.value), blur: Number(blur.value) });
      try { localStorage.setItem('appearance', JSON.stringify(value)); } catch { throw new Error('无法保存外观设置，请检查浏览器配置目录'); }
      saved = value; applyAppearance(widget, value);
    }
  };
}
