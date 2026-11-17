export const SCENES = ['clear', 'partly-cloudy', 'overcast', 'rain', 'thunderstorm', 'snow', 'fog'];
export const SKY_COLORS = {
  clear: [['#1764a2', '#4598cb', '#a2d3e5'], ['#070e29', '#15294b', '#344c69']],
  'partly-cloudy': [['#2b6590', '#7198b4', '#b7cbd5'], ['#0b142a', '#273751', '#586579']],
  overcast: [['#3d5368', '#6b8294', '#a8b8c2'], ['#111b2c', '#28374a', '#516073']],
  rain: [['#263f58', '#4e6d86', '#8299aa'], ['#0a1527', '#1b3048', '#394f65']],
  thunderstorm: [['#191d35', '#343b57', '#62758a'], ['#080c1b', '#1d2037', '#343e57']],
  snow: [['#617a91', '#9aafc0', '#d0dce4'], ['#192338', '#3a4b65', '#788b9e']],
  fog: [['#607c8c', '#95acb6', '#c4d0d4'], ['#1d2c3a', '#435564', '#798c96']]
};
const clamp = (n, min, max) => Math.max(min, Math.min(max, n));
const random = (min, max) => min + Math.random() * (max - min);
const TAU = Math.PI * 2;
const CLOUD_AMOUNTS = { clear: .03, 'partly-cloudy': .65, overcast: 1, rain: 1, thunderstorm: 1, snow: .65, fog: .3 };

export class WeatherEngine {
  constructor(canvas, root = canvas.parentElement) {
    this.canvas = canvas;
    this.root = root;
    this.ctx = canvas.getContext('2d', { alpha: true });
    if (!this.ctx) throw new Error('当前浏览器无法创建 Canvas 2D 渲染上下文');
    this.width = 1; this.height = 1; this.dpr = 1;
    this.state = { scene: 'overcast', intensity: .5, isDay: true, windSpeed: 0, windDirection: 270 };
    this.time = 0; this.frameCount = 0; this.lastFrame = 0; this.rafId = 0;
    this.running = false; this.destroyed = false; this.enabled = true;
    this.daylight = 1; this.cloudAmount = 1; this.clearAmount = 0; this.fogAmount = 0;
    this.rainPool = Array.from({ length: 350 }, () => ({ active: false }));
    this.snowPool = Array.from({ length: 180 }, () => ({ active: false }));
    this.splashes = Array.from({ length: 48 }, () => ({ life: 0 }));
    this.stars = Array.from({ length: 85 }, () => ({ x: Math.random(), y: Math.random() * .8, depth: random(.3, 1), phase: random(0, TAU) }));
    this.dust = Array.from({ length: 26 }, () => ({ x: Math.random(), y: Math.random(), phase: random(0, TAU), size: random(.5, 1.6) }));
    this.clouds = Array.from({ length: 10 }, (_, i) => ({ x: random(-.4, 1.2), y: random(-.15, .7), depth: .3 + (i % 3) * .32, phase: random(0, TAU) }));
    this.fog = Array.from({ length: 7 }, () => ({ x: Math.random(), y: random(.1, 1), phase: random(0, TAU), speed: random(.001, .004) }));
    this.meteors = Array.from({ length: 3 }, () => ({ life: 0 }));
    this.nextMeteor = random(10, 24); this.nextStrike = random(4, 10); this.strike = null;
    this.cloudTexture = this.makeCloudTexture();
    this.snowTextures = [this.makeSnowTexture(.4), this.makeSnowTexture(.65), this.makeSnowTexture(.18)];
    this.fogTexture = this.makeFogTexture();
    this.motion = matchMedia('(prefers-reduced-motion: reduce)');
    this.onVisibility = () => document.hidden ? this.pause() : this.start();
    this.onMotion = () => { this.pause(); this.start(); };
    this.onResize = () => this.resize();
    this.motion.addEventListener('change', this.onMotion);
    document.addEventListener('visibilitychange', this.onVisibility);
    window.addEventListener('resize', this.onResize);
    this.observer = new ResizeObserver(this.onResize);
    this.observer.observe(root);
    this.tick = timestamp => {
      if (!this.running) return;
      const budget = 1000 / this.fps;
      if (!this.lastFrame || timestamp - this.lastFrame >= budget) {
        const elapsed = this.lastFrame ? (timestamp - this.lastFrame) / 1000 : 1 / this.fps;
        this.lastFrame = timestamp - ((timestamp - (this.lastFrame || timestamp)) % budget);
        if (this.dpr !== window.devicePixelRatio) this.resize();
        this.render(Math.min(elapsed, .08));
      }
      this.rafId = requestAnimationFrame(this.tick);
    };
    this.resize(); this.setWeather(this.state); this.start();
  }

  get fps() {
    const scene = this.state.scene;
    return scene === 'rain' || scene === 'thunderstorm' || scene === 'snow' ? 30 : 20;
  }

  setWeather(next) {
    if (!SCENES.includes(next.scene)) throw new TypeError('未知天气场景');
    const oldScene = this.state.scene;
    this.state = {
      scene: next.scene, isDay: Boolean(next.isDay),
      intensity: clamp(Number.isFinite(next.intensity) ? next.intensity : .5, .1, 1),
      windSpeed: clamp(Number.isFinite(next.windSpeed) ? next.windSpeed : 0, 0, 250),
      windDirection: Number.isFinite(next.windDirection) ? ((next.windDirection % 360) + 360) % 360 : 270
    };
    const colors = SKY_COLORS[this.state.scene][this.state.isDay ? 0 : 1];
    ['--sky-top', '--sky-mid', '--sky-bottom'].forEach((name, i) => this.root.style.setProperty(name, colors[i]));
    this.root.dataset.scene = this.state.scene;
    this.root.dataset.day = String(this.state.isDay);
    if (oldScene !== this.state.scene && this.state.scene === 'thunderstorm') this.nextStrike = this.time + random(4, 10);
    if (this.enabled && this.motion?.matches && !document.hidden) {
      this.rainPool.forEach(p => { p.active = false; }); this.snowPool.forEach(p => { p.active = false; }); this.strike = null;
      this.render(1 / 30);
    }
  }

  resize() {
    if (this.destroyed) return;
    const rect = this.canvas.getBoundingClientRect();
    this.width = Math.max(1, rect.width); this.height = Math.max(1, rect.height);
    this.dpr = window.devicePixelRatio || 1;
    this.canvas.width = Math.round(this.width * this.dpr);
    this.canvas.height = Math.round(this.height * this.dpr);
    this.ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
    if (this.enabled && this.motion?.matches && !document.hidden) this.render(0);
  }

  start() {
    if (this.destroyed || !this.enabled || document.hidden || this.running) return;
    this.resize();
    if (this.motion.matches) { this.render(1 / 30); return; }
    this.running = true; this.lastFrame = 0;
    this.rafId = requestAnimationFrame(this.tick);
  }

  pause() { this.running = false; cancelAnimationFrame(this.rafId); this.rafId = 0; this.lastFrame = 0; }
  setEnabled(enabled) { this.enabled = Boolean(enabled); if (this.enabled) this.start(); else this.pause(); }
  destroy() {
    this.pause(); this.destroyed = true; this.observer.disconnect();
    document.removeEventListener('visibilitychange', this.onVisibility);
    window.removeEventListener('resize', this.onResize);
    this.motion.removeEventListener('change', this.onMotion);
  }

  makeCloudTexture() {
    const texture = document.createElement('canvas'); texture.width = 320; texture.height = 160;
    const ctx = texture.getContext('2d');
    for (const [x, y, r] of [[75, 88, 60], [130, 65, 67], [185, 78, 65], [237, 94, 53], [150, 109, 72]]) {
      const glow = ctx.createRadialGradient(x, y - r * .22, 0, x, y, r);
      glow.addColorStop(0, 'rgba(239,246,251,.43)'); glow.addColorStop(.45, 'rgba(201,218,232,.25)'); glow.addColorStop(1, 'rgba(174,196,216,0)');
      ctx.fillStyle = glow; ctx.fillRect(x - r, y - r, r * 2, r * 2);
    }
    return texture;
  }

  makeSnowTexture(softness) {
    const texture = document.createElement('canvas'); texture.width = texture.height = 24;
    const ctx = texture.getContext('2d'), glow = ctx.createRadialGradient(12, 12, 0, 12, 12, 11);
    glow.addColorStop(0, '#fff'); glow.addColorStop(softness, '#eef7ffdf'); glow.addColorStop(1, '#e4f5ff00');
    ctx.fillStyle = glow; ctx.fillRect(0, 0, 24, 24); return texture;
  }

  makeFogTexture() {
    const texture = document.createElement('canvas'); texture.width = 256; texture.height = 128;
    const ctx = texture.getContext('2d');
    ctx.scale(2, 1);
    const glow = ctx.createRadialGradient(64, 64, 0, 64, 64, 64);
    glow.addColorStop(0, '#dce9ec60'); glow.addColorStop(.45, '#dce9ec30'); glow.addColorStop(1, '#dce9ec00');
    ctx.fillStyle = glow; ctx.fillRect(0, 0, 128, 128); return texture;
  }

  render(dt) {
    this.time += dt; this.frameCount++;
    const ctx = this.ctx, w = this.width, h = this.height, scene = this.state.scene;
    const mix = this.motion.matches ? 1 : 1 - Math.exp(-dt * 2.2);
    const cloudTarget = CLOUD_AMOUNTS[scene];
    this.daylight += ((this.state.isDay ? 1 : 0) - this.daylight) * mix;
    this.cloudAmount += (cloudTarget - this.cloudAmount) * mix;
    this.clearAmount += ((scene === 'clear' || scene === 'partly-cloudy' ? 1 : 0) - this.clearAmount) * mix;
    this.fogAmount += ((scene === 'fog' ? this.state.intensity : 0) - this.fogAmount) * mix;
    ctx.clearRect(0, 0, w, h);
    // Project a four-metre precipitation volume: wind and terminal fall speeds share px/metre units.
    const wind = -Math.sin(this.state.windDirection * Math.PI / 180) * this.state.windSpeed / 3.6 * (h / 4);
    this.drawSun(this.clearAmount * this.daylight);
    this.drawStars(this.clearAmount * (1 - this.daylight), dt);
    this.drawClouds(wind * .035, dt);
    this.drawDust(this.clearAmount * this.daylight, dt);
    this.drawRain(wind, dt);
    this.drawSnow(wind, dt);
    this.drawFog(dt);
    this.drawLightning(dt);
    ctx.globalAlpha = 1;
  }

  drawSun(amount) {
    if (amount < .005) return;
    const ctx = this.ctx, x = this.width * .83, y = this.height * .15, radius = Math.max(this.width, this.height) * .8;
    ctx.save(); ctx.globalAlpha = amount;
    const halo = ctx.createRadialGradient(x, y, 0, x, y, radius);
    halo.addColorStop(0, '#fff7d6b8'); halo.addColorStop(.09, '#fff5d765'); halo.addColorStop(.4, '#fff4d01c'); halo.addColorStop(1, '#fff4d000');
    ctx.fillStyle = halo; ctx.fillRect(0, 0, this.width, this.height);
    ctx.translate(x, y); ctx.rotate(this.time * .014);
    ctx.globalAlpha = amount * .13; ctx.fillStyle = halo;
    // Broad tapered wedges create slowly rotating light shafts without per-pixel noise.
    for (let i = 0; i < 7; i++) {
      const angle = i * TAU / 7;
      ctx.beginPath(); ctx.moveTo(0, 0); ctx.arc(0, 0, radius, angle, angle + .12); ctx.closePath(); ctx.fill();
    }
    ctx.restore();
  }

  drawStars(amount, dt) {
    const ctx = this.ctx;
    if (amount > .005) {
      ctx.save(); ctx.fillStyle = '#deedff';
      for (const star of this.stars) {
        ctx.globalAlpha = amount * (.35 + .3 * Math.sin(this.time * (.5 + star.depth) + star.phase));
        ctx.beginPath(); ctx.arc(star.x * this.width + Math.sin(this.time * .035 + star.phase) * star.depth * 2, star.y * this.height, .45 + star.depth * .65, 0, TAU); ctx.fill();
      }
      ctx.restore();
      if (this.time > this.nextMeteor && !this.motion.matches) {
        const meteor = this.meteors.find(p => p.life <= 0);
        if (meteor) Object.assign(meteor, { x: random(0, this.width * .7), y: random(0, this.height * .35), life: 1 });
        this.nextMeteor = this.time + random(12, 28);
      }
    }
    for (const meteor of this.meteors) {
      if (meteor.life <= 0) continue;
      meteor.life -= dt; meteor.x += dt * 150; meteor.y += dt * 65;
      ctx.save(); ctx.globalAlpha = Math.max(0, meteor.life) * amount * .35;
      const trail = ctx.createLinearGradient(meteor.x - 45, meteor.y - 20, meteor.x, meteor.y);
      trail.addColorStop(0, '#deefff00'); trail.addColorStop(1, '#deefff'); ctx.strokeStyle = trail; ctx.lineWidth = 1;
      ctx.beginPath(); ctx.moveTo(meteor.x - 45, meteor.y - 20); ctx.lineTo(meteor.x, meteor.y); ctx.stroke(); ctx.restore();
    }
  }

  drawClouds(wind, dt) {
    const ctx = this.ctx;
    for (const cloud of this.clouds) {
      const width = this.width * (.75 + cloud.depth * .8), height = width * .5;
      cloud.x += (wind * cloud.depth + 3) * dt / this.width;
      if (cloud.x > 1.6) cloud.x = -1.6;
      if (cloud.x < -1.6) cloud.x = 1.6;
      if (this.cloudAmount < .005) continue;
      ctx.save(); ctx.globalAlpha = this.cloudAmount * (.3 + .6 * cloud.depth) * (.3 + .7 * this.daylight);
      ctx.drawImage(this.cloudTexture, cloud.x * this.width - width * .3, cloud.y * this.height + Math.sin(this.time * .03 + cloud.phase) * 3, width, height);
      ctx.restore();
    }
  }

  drawDust(amount, dt) {
    if (amount < .005) return;
    const ctx = this.ctx; ctx.save(); ctx.fillStyle = '#fff2ca';
    for (const particle of this.dust) {
      particle.y -= dt * .007;
      if (particle.y < -.05) particle.y = 1.05;
      ctx.globalAlpha = amount * (.12 + .12 * Math.sin(this.time + particle.phase));
      ctx.beginPath(); ctx.arc(particle.x * this.width + Math.sin(this.time * .2 + particle.phase) * 5, particle.y * this.height, particle.size, 0, TAU); ctx.fill();
    }
    ctx.restore();
  }

  spawnRain(particle) {
    const depth = random(.4, 1);
    const terminal = random(7, 10) * (this.height / 4) * depth;
    Object.assign(particle, { active: true, x: random(-this.width * .3, this.width * 1.3), y: -random(5, this.height * .35), vx: 0, vy: terminal * random(.75, .95), terminal, depth, alpha: 0 });
    if (this.motion.matches) { particle.y = random(0, this.height); particle.alpha = 1; }
  }

  drawRain(wind, dt) {
    const raining = this.state.scene === 'rain' || this.state.scene === 'thunderstorm';
    const count = raining ? Math.round(350 * this.state.intensity) : 0, ctx = this.ctx;
    ctx.save(); ctx.strokeStyle = '#d7eaff';
    for (let i = 0; i < this.rainPool.length; i++) {
      const p = this.rainPool[i];
      if (!p.active) { if (i >= count) continue; this.spawnRain(p); }
      p.vx += (wind * p.depth - p.vx) * Math.min(1, dt * 3); p.vy = Math.min(p.terminal, p.vy + dt * 9.81 * (this.height / 4) * p.depth);
      p.x += p.vx * dt; p.y += p.vy * dt; p.alpha = Math.min(1, p.alpha + dt * 3);
      if (p.y >= this.height || p.x < -this.width * .4 || p.x > this.width * 1.4) {
        if (p.y >= this.height && p.x >= 0 && p.x <= this.width) {
          const splash = this.splashes.find(s => s.life <= 0);
          if (splash) Object.assign(splash, { life: .32, x: p.x, size: random(3, 8) });
        }
        p.active = false; continue;
      }
      const length = (7 + this.state.intensity * 18) * p.depth;
      ctx.globalAlpha = p.alpha * (.12 + p.depth * .28); ctx.lineWidth = .5 + p.depth * .55;
      ctx.beginPath(); ctx.moveTo(p.x, p.y); ctx.lineTo(p.x - p.vx / p.vy * length, p.y - length); ctx.stroke();
    }
    for (const splash of this.splashes) {
      if (splash.life <= 0) continue;
      splash.life -= dt; const progress = 1 - splash.life / .32;
      ctx.globalAlpha = Math.max(0, 1 - progress) * .32; ctx.lineWidth = .6;
      ctx.beginPath(); ctx.ellipse(splash.x, this.height - 2, Math.max(.1, splash.size * progress), Math.max(.1, splash.size * progress * .38), 0, Math.PI, TAU); ctx.stroke();
    }
    ctx.restore();
  }

  drawSnow(wind, dt) {
    const count = this.state.scene === 'snow' ? Math.round(35 + this.state.intensity * 145) : 0;
    const ctx = this.ctx;
    for (let i = 0; i < this.snowPool.length; i++) {
      const p = this.snowPool[i];
      if (!p.active) {
        if (i >= count) continue;
        const layer = i % 3, depth = .3 + layer * .35;
        Object.assign(p, { active: true, x: random(-this.width * .2, this.width * 1.2), y: -random(4, this.height * .4), layer, depth, size: 2 + depth * 5, phase: random(0, TAU), alpha: 0 });
        if (this.motion.matches) { p.y = random(0, this.height); p.alpha = 1; }
      }
      p.x += (wind * .3 * p.depth + Math.sin(this.time * .9 + p.phase) * 12 * p.depth) * dt;
      p.y += (15 + p.depth * 42) * dt; p.alpha = Math.min(1, p.alpha + dt * 1.5);
      if (p.y > this.height + 10 || p.x < -this.width * .3 || p.x > this.width * 1.3) { p.active = false; continue; }
      ctx.save(); ctx.globalAlpha = p.alpha * (.35 + p.depth * .5);
      ctx.drawImage(this.snowTextures[p.layer], p.x - p.size / 2, p.y - p.size / 2, p.size, p.size); ctx.restore();
    }
  }

  drawFog(dt) {
    for (const fog of this.fog) {
      fog.x += fog.speed * dt;
      if (fog.x > 1.5) fog.x = -.5;
      if (this.fogAmount < .005) continue;
      const width = this.width * 2.6;
      this.ctx.save(); this.ctx.globalAlpha = this.fogAmount * (.3 + .2 * Math.sin(this.time * .06 + fog.phase));
      this.ctx.drawImage(this.fogTexture, (fog.x - 1) * this.width, fog.y * this.height - width * .2 + Math.sin(this.time * .04 + fog.phase) * 12, width, width * .5); this.ctx.restore();
    }
  }

  createBolt() {
    const points = [{ x: random(this.width * .45, this.width * .9), y: -5 }, { x: random(this.width * .3, this.width * .9), y: this.height * .85 }];
    for (let depth = 0; depth < 4; depth++) {
      for (let i = points.length - 1; i > 0; i--) {
        const a = points[i - 1], b = points[i];
        points.splice(i, 0, { x: (a.x + b.x) / 2 + random(-1, 1) * this.width * .12 / (depth + 1), y: (a.y + b.y) / 2 });
      }
    }
    const branches = [5, 10].map(index => {
      const origin = points[index];
      return [origin, { x: origin.x + random(-30, 30), y: origin.y + this.height * .08 }, { x: origin.x + random(-50, 50), y: origin.y + this.height * .22 }];
    });
    return { age: 0, points, branches };
  }

  drawLightning(dt) {
    if (this.state.scene === 'thunderstorm' && this.time >= this.nextStrike && !this.motion.matches) {
      this.strike = this.createBolt(); this.nextStrike = this.time + random(4, 10);
    }
    if (!this.strike) return;
    this.strike.age += dt;
    const age = this.strike.age;
    if (age > .5) { this.strike = null; return; }
    const pulse = Math.exp(-age * 35) + (age > .13 ? Math.exp(-(age - .13) * 28) * .65 : 0);
    const ctx = this.ctx; ctx.save();
    ctx.fillStyle = `rgba(207,228,255,${pulse * .2})`; ctx.fillRect(0, 0, this.width, this.height);
    ctx.globalAlpha = Math.min(1, pulse); ctx.strokeStyle = '#dcecff'; ctx.shadowColor = '#b7d8ff'; ctx.shadowBlur = 14;
    for (const [i, points] of [this.strike.points, ...this.strike.branches].entries()) {
      ctx.lineWidth = i === 0 ? 1.5 : .6; ctx.beginPath(); points.forEach((point, index) => index ? ctx.lineTo(point.x, point.y) : ctx.moveTo(point.x, point.y)); ctx.stroke();
    }
    ctx.restore();
  }
}
