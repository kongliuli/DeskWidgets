export function bindWidgetDrag(widget, send) {
  let pointer = null, scheduled = 0;
  const finish = cancelled => {
    if (pointer === null) return;
    const id = pointer; pointer = null;
    cancelAnimationFrame(scheduled); scheduled = 0;
    send(cancelled ? 'cancelDrag' : 'endDrag');
    if (widget.hasPointerCapture?.(id)) widget.releasePointerCapture(id);
  };
  widget.addEventListener('pointerdown', event => {
    if (event.button !== 0 || widget.classList.contains('locked') || event.target.closest('button,input,select,textarea,label,dialog,a,.simulator')) return;
    event.preventDefault(); pointer = event.pointerId;
    try { widget.setPointerCapture(pointer); } catch { /* Synthetic test events have no native pointer. */ }
    send('startDrag');
  });
  widget.addEventListener('pointermove', event => {
    if (pointer !== event.pointerId || scheduled) return;
    scheduled = requestAnimationFrame(() => { scheduled = 0; if (pointer !== null) send('dragMove'); });
  });
  widget.addEventListener('pointerup', event => { if (event.pointerId === pointer) finish(false); });
  widget.addEventListener('pointercancel', () => finish(true));
  widget.addEventListener('lostpointercapture', () => finish(true));
  widget.addEventListener('keydown', event => { if (event.key === 'Escape') finish(true); });
  window.addEventListener('pagehide', () => finish(true), { once: true });
}

export function applyWidgetLock(widget, locked) {
  widget.classList.toggle('locked', locked);
  for (const control of widget.querySelectorAll('button,input,select,textarea')) {
    if (control.dataset.action === 'toggleLock') {
      control.setAttribute('aria-pressed', String(locked));
      control.setAttribute('aria-label', locked ? '解锁组件' : '锁定组件');
      control.title = locked ? '解锁组件' : '锁定组件';
      continue;
    }
    if (locked && !control.hasAttribute('data-lock-disabled')) { control.dataset.lockDisabled = String(control.disabled); control.disabled = true; }
    if (!locked && control.hasAttribute('data-lock-disabled')) { control.disabled = control.dataset.lockDisabled === 'true'; delete control.dataset.lockDisabled; }
  }
  if (locked) {
    const simulator = widget.querySelector('.simulator');
    if (simulator) simulator.hidden = true;
    widget.querySelector('#preview-toggle')?.setAttribute('aria-expanded', 'false');
    for (const dialog of widget.querySelectorAll('dialog[open]')) dialog.close();
  }
}
