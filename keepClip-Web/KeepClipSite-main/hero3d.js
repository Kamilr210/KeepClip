(() => {
  const canvas = document.getElementById('heroCanvas');
  const section = document.querySelector('.hero-section');
  if (!canvas || !section) return;
  if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) { canvas.remove(); return; }
  if (window.matchMedia('(max-width: 900px)').matches) { canvas.remove(); return; }

  const probe = document.createElement('canvas');
  const gl = probe.getContext('webgl2') || probe.getContext('webgl');
  if (!gl) { canvas.remove(); return; }

  const V = '20260918ae';
  const CHARACTER = 'models/character-a.glb?v=' + V;
  const SKIN = 'models/Textures/texture-steve.png?v=' + V;
  const SHOTS = 9;
  const REST_HOLD = 4;
  const ENTRY_DURATION = 3.2;
  const ENTRY_SIZE = 0.22;

  const load = (src) => new Promise((resolve, reject) => {
    const s = document.createElement('script');
    s.src = src;
    s.onload = resolve;
    s.onerror = reject;
    document.head.appendChild(s);
  });

  const start = async () => {
    await load('vendor/three/three-lite.min.js?v=' + V);
    const T = window.THREE_LITE;
    if (!T) return;

    const renderer = new T.WebGLRenderer({ canvas, alpha: true, antialias: true, powerPreference: 'high-performance' });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.outputColorSpace = T.SRGBColorSpace;
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = T.PCFSoftShadowMap;

    const scene = new T.Scene();
    const camera = new T.PerspectiveCamera(30, 1, 0.1, 200);

    scene.add(new T.HemisphereLight(0xdcd6ff, 0x1a0f2e, 1.6));
    const key = new T.DirectionalLight(0xffffff, 2.4);
    key.position.set(-6, 12, 9);
    key.castShadow = true;
    key.shadow.mapSize.set(1024, 1024);
    key.shadow.camera.left = -30;
    key.shadow.camera.right = 30;
    key.shadow.camera.top = 20;
    key.shadow.camera.bottom = -20;
    key.shadow.camera.far = 60;
    scene.add(key);
    const rim = new T.DirectionalLight(0x00bfff, 1.4);
    rim.position.set(8, 6, -8);
    scene.add(rim);
    const fill = new T.DirectionalLight(0xa122ff, 0.9);
    fill.position.set(-8, 3, -4);
    scene.add(fill);

    const floor = new T.Mesh(new T.PlaneGeometry(200, 120), new T.ShadowMaterial({ opacity: 0.45 }));
    floor.rotation.x = -Math.PI / 2;
    floor.position.y = 0;
    floor.receiveShadow = true;
    scene.add(floor);

    const loader = new T.GLTFLoader();
    const gltf = (url) => new Promise((resolve, reject) => loader.load(url, resolve, undefined, reject));
    const [charGltf, skin] = await Promise.all([
      gltf(CHARACTER),
      new Promise((resolve, reject) => new T.TextureLoader().load(SKIN, resolve, undefined, reject)),
    ]);
    skin.colorSpace = T.SRGBColorSpace;
    skin.flipY = false;
    skin.wrapS = T.RepeatWrapping;
    skin.wrapT = T.RepeatWrapping;
    skin.magFilter = T.NearestFilter;
    skin.minFilter = T.LinearMipmapLinearFilter;
    skin.generateMipmaps = true;

    const body = new T.Group();
    scene.add(body);
    const actor = new T.Group();
    body.add(actor);
    const model = charGltf.scene;
    const fadeMats = [];
    model.traverse((o) => { if (o.isMesh) { o.castShadow = true; o.receiveShadow = false; o.material = o.material.clone(); o.material.map = skin; o.material.transparent = true; fadeMats.push(o.material); } });
    actor.add(model);

    const rifle = new T.Group();
    const wood = new T.MeshStandardMaterial({ color: 0x7a4a22, roughness: 0.75, metalness: 0.05, transparent: true });
    const steel = new T.MeshStandardMaterial({ color: 0x23232a, roughness: 0.45, metalness: 0.7, transparent: true });
    const gunmetal = new T.MeshStandardMaterial({ color: 0x3b3b44, roughness: 0.4, metalness: 0.8, transparent: true });
    fadeMats.push(wood, steel, gunmetal);
    const part = (mat, w, h, d, x, y, z, rx = 0) => {
      const m = new T.Mesh(new T.BoxGeometry(w, h, d), mat);
      m.position.set(x, y, z);
      m.rotation.x = rx;
      m.castShadow = true;
      rifle.add(m);
      return m;
    };
    part(steel, 0.09, 0.13, 0.52, 0, 0, 0.1);
    part(steel, 0.075, 0.035, 0.46, 0, 0.08, 0.08);
    part(gunmetal, 0.04, 0.04, 0.5, 0, 0.03, 0.78);
    part(gunmetal, 0.035, 0.035, 0.3, 0, 0.09, 0.5);
    part(wood, 0.095, 0.1, 0.3, 0, 0, 0.5);
    part(gunmetal, 0.04, 0.1, 0.05, 0, 0.09, 0.93);
    part(gunmetal, 0.05, 0.05, 0.1, 0, 0.03, 1.05);
    part(gunmetal, 0.05, 0.04, 0.04, 0, 0.11, 0.3);
    part(wood, 0.07, 0.12, 0.48, 0, -0.05, -0.4, -0.22);
    part(wood, 0.06, 0.17, 0.07, 0, -0.14, -0.03, 0.35);
    part(steel, 0.06, 0.22, 0.09, 0, -0.17, 0.2, -0.4);
    part(steel, 0.06, 0.15, 0.09, 0, -0.31, 0.31, -0.8);
    part(steel, 0.02, 0.015, 0.09, 0, -0.085, 0.06);
    part(gunmetal, 0.03, 0.03, 0.06, 0.06, 0.03, 0.02);
    const arm = model.getObjectByName('arm-left');
    const torso = model.getObjectByName('torso');
    const head = model.getObjectByName('head');
    const armFar = model.getObjectByName('arm-right');
    const legNear = model.getObjectByName('leg-left');
    const legFar = model.getObjectByName('leg-right');
    const pinnedArm = Math.PI - 0.35;
    arm.add(rifle);
    const GRIP = new T.Vector3(0.2, -0.9, 0.14);
    const gripPose = new T.Object3D();
    gripPose.rotation.set(-Math.PI / 2, 0, -0.5);
    const gripQ = gripPose.quaternion.clone().invert();
    const Z_AXIS = new T.Vector3(0, 0, 1);
    const qFall = new T.Quaternion();
    const restPose = new T.Object3D();
    restPose.lookAt(-1, 0, 0);
    restPose.rotateZ(-0.35);
    const restQ = restPose.quaternion.clone();
    let rifleFree = false;
    let rifleSettle = false;
    let rifleAngle = 0;
    let rifleOmega = 0;
    const rifleVel = new T.Vector3();
    const spinQ = new T.Quaternion();
    const dropRifle = () => {
      scene.attach(rifle);
      rifle.position.z = Math.max(rifle.position.z, LOGO_Z + 0.6);
      rifle.renderOrder = 1;
      rifleFree = true;
      rifleSettle = false;
      rifleAngle = 0;
      rifleVel.set(-logoW * 0.3, 5.5, 0.6);
      const drop = Math.max(0.1, rifle.position.y - (groundY + 0.42));
      const flight = (rifleVel.y + Math.sqrt(rifleVel.y * rifleVel.y + 48 * drop)) / 24;
      rifleOmega = Math.PI * 2 / flight;
    };
    let carry = 1;
    let shotPending = false;
    rifle.position.copy(GRIP);
    rifle.scale.setScalar(1.2);

    const mixer = new T.AnimationMixer(model);
    const clips = charGltf.animations;
    const clip = (name) => clips.find((c) => c.name === name);
    const sprint = mixer.clipAction(clip('sprint'));
    const idle = mixer.clipAction(clip('idle'));
    [sprint, idle].forEach((a) => { a.enabled = true; a.setEffectiveWeight(0); a.play(); });
    let current = null;
    const fadeTo = (action, duration = 0.18) => {
      if (current === action) return;
      if (current) current.crossFadeTo(action, duration, false);
      else action.setEffectiveWeight(1);
      action.reset().play();
      action.setEffectiveWeight(1);
      current = action;
    };

    const flashCanvas = document.createElement('canvas');
    flashCanvas.width = 128;
    flashCanvas.height = 128;
    const fx = flashCanvas.getContext('2d');
    const grad = fx.createRadialGradient(64, 64, 4, 64, 64, 60);
    grad.addColorStop(0, 'rgba(255,255,230,1)');
    grad.addColorStop(0.35, 'rgba(255,214,107,0.85)');
    grad.addColorStop(1, 'rgba(255,140,40,0)');
    fx.fillStyle = grad;
    fx.fillRect(0, 0, 128, 128);
    const flashTex = new T.CanvasTexture(flashCanvas);
    const flash = new T.Sprite(new T.SpriteMaterial({ map: flashTex, blending: T.AdditiveBlending, depthWrite: false, transparent: true }));
    flash.scale.set(0.9, 0.9, 1);
    flash.visible = false;
    rifle.add(flash);
    flash.position.set(0, 0.03, 1.14);

    const flashLight = new T.DirectionalLight(0xffc266, 0);
    flashLight.position.set(-4, 3, 6);
    scene.add(flashLight);

    const tracerMat = new T.MeshBasicMaterial({ color: 0xffd86b, transparent: true, opacity: 0.95 });
    const tracers = [];
    const spawnTracer = () => {
      const muzzle = new T.Vector3();
      flash.getWorldPosition(muzzle);
      const direction = new T.Vector3(0, 0, 1).applyQuaternion(rifle.getWorldQuaternion(new T.Quaternion()));
      const m = new T.Mesh(new T.PlaneGeometry(1.4, 0.045), tracerMat);
      m.position.copy(muzzle).addScaledVector(direction, 0.7);
      m.rotation.z = Math.atan2(direction.y, direction.x);
      scene.add(m);
      tracers.push({ mesh: m, velocity: direction.multiplyScalar(40), life: 0 });
    };

    const shells = [];
    const shellMat = new T.MeshStandardMaterial({ color: 0xe8c24a, metalness: 0.8, roughness: 0.3 });
    const spawnShell = () => {
      const origin = new T.Vector3();
      rifle.getWorldPosition(origin);
      const m = new T.Mesh(new T.CircleGeometry(0.045, 8), shellMat);
      m.position.copy(origin).add(new T.Vector3(0.35, 0.15, 0.35));
      scene.add(m);
      shells.push({ mesh: m, vx: 1.2 + Math.random() * 0.8, vy: 1.8 + Math.random() * 0.8, spin: 10 + Math.random() * 10, life: 0 });
    };

    const logoCanvas = document.createElement('canvas');
    const lc = logoCanvas.getContext('2d');
    const logoFont = '900 200px "Segoe UI", -apple-system, Roboto, sans-serif';
    lc.font = logoFont;
    const keepW = lc.measureText('KEEP').width;
    const clipW = lc.measureText('CLIP').width;
    logoCanvas.width = Math.ceil(keepW + clipW + 96);
    logoCanvas.height = 272;
    lc.font = logoFont;
    lc.textBaseline = 'alphabetic';
    lc.shadowColor = 'rgba(0,0,0,0.6)';
    lc.shadowBlur = 26;
    lc.shadowOffsetY = 12;
    const startX = 48;
    const baseline = 200;
    lc.fillStyle = '#ffffff';
    lc.fillText('KEEP', startX, baseline);
    lc.fillStyle = '#00bfff';
    lc.fillText('CLIP', startX + keepW, baseline);
    const logoTex = new T.CanvasTexture(logoCanvas);
    logoTex.colorSpace = T.SRGBColorSpace;
    const LOGO_Z = 0.9;
    const logoAspect = logoCanvas.height / logoCanvas.width;
    const logo = new T.Mesh(new T.PlaneGeometry(1, logoAspect), new T.MeshBasicMaterial({ map: logoTex, transparent: true, depthWrite: false }));
    logo.position.z = LOGO_Z;
    logo.visible = false;
    scene.add(logo);
    const logoShadow = new T.Mesh(new T.CircleGeometry(1, 24), new T.MeshBasicMaterial({ color: 0x000000, transparent: true, opacity: 0 }));
    logoShadow.rotation.x = -Math.PI / 2;
    scene.add(logoShadow);

    const dustCanvas = document.createElement('canvas');
    dustCanvas.width = 128;
    dustCanvas.height = 128;
    const dc = dustCanvas.getContext('2d');
    const blob = (cx, cy, r, a) => {
      const g = dc.createRadialGradient(cx, cy, 0, cx, cy, r);
      g.addColorStop(0, 'rgba(255,255,255,' + a + ')');
      g.addColorStop(0.5, 'rgba(255,255,255,' + a * 0.45 + ')');
      g.addColorStop(1, 'rgba(255,255,255,0)');
      dc.fillStyle = g;
      dc.fillRect(0, 0, 128, 128);
    };
    blob(64, 64, 46, 0.75);
    for (let i = 0; i < 7; i++) {
      const a = (i / 7) * Math.PI * 2;
      blob(64 + Math.cos(a) * 26, 64 + Math.sin(a) * 22, 24 + (i % 3) * 5, 0.5);
    }
    const dustTex = new T.CanvasTexture(dustCanvas);
    const dust = [];
    for (let i = 0; i < 36; i++) {
      const m = new T.Sprite(new T.SpriteMaterial({ map: dustTex, color: 0xc6bdb4, transparent: true, depthWrite: false, opacity: 0 }));
      m.visible = false;
      scene.add(m);
      dust.push({ mesh: m, vx: 0, vy: 0, gravity: 6, drag: 0, spin: 0, peak: 0.6, life: 0, ttl: 0.8 });
    }
    const emit = (x, y, z, vx, vy, size, ttl, gravity, drag) => {
      const p = dust.find((d) => !d.mesh.visible);
      if (!p) return;
      p.mesh.visible = true;
      p.mesh.position.set(x, y, z);
      p.vx = vx;
      p.vy = vy;
      p.gravity = gravity;
      p.drag = drag;
      p.spin = (Math.random() - 0.5) * 2;
      p.peak = 0.45 + Math.random() * 0.25;
      p.life = 0;
      p.ttl = ttl;
      p.mesh.material.rotation = Math.random() * Math.PI * 2;
      p.mesh.material.opacity = p.peak;
      p.mesh.scale.set(size, size, 1);
    };
    const puff = (x, y) => {
      for (let i = 0; i < 10; i++) {
        emit(x + (Math.random() - 0.5) * 1.2, y + 0.1, 0.6, (i % 2 ? 1 : -1) * (1.5 + Math.random() * 2.2), 1.2 + Math.random() * 2, 0.7 + Math.random() * 0.4, 0.9, 5, 0.5);
      }
    };
    const wave = (x, halfW, y) => {
      for (let side = -1; side <= 1; side += 2) {
        for (let i = 0; i < 8; i++) {
          const k = i / 7;
          emit(x + side * halfW * (0.5 + 0.5 * k), y + 0.05 + Math.random() * 0.2, 1.0,
            side * (1.2 + k * 2 + Math.random()), 0.4 + Math.random() * 1.0, 0.9 + Math.random() * 0.6, 1.0 + Math.random() * 0.4, 1.2, 2.5);
        }
      }
    };

    const state = { phase: 'enter', t: 0, x: 0, z: 0, shotsLeft: SHOTS, nextShot: 0, recoil: 0, recoilT: 1, squash: 0, squashT: 0, struggle: 0, crawl: 0, reach: 0, collapse: 0, logoY: 0, logoVy: 0, shake: 0 };
    let entryX = 0;
    let entryZ = 0;
    let entryYaw = 0;
    let edgeLeft = -30;
    let groundY = 0;
    let stopX = 4;
    let logoW = 6;
    let logoH = 1.6;
    let logoRest = 0;
    let logoTop = 6;

    const resize = () => {
      const w = section.clientWidth;
      const h = section.clientHeight;
      renderer.setSize(w, h, false);
      camera.aspect = w / h;
      camera.updateProjectionMatrix();
      const halfH = 3.1;
      const dist = halfH / Math.tan(T.MathUtils.degToRad(camera.fov / 2));
      camera.position.set(0, 0.5, dist);
      camera.lookAt(0, 0.5, 0);
      const halfW = halfH * camera.aspect;
      edgeLeft = -halfW - 3;
      groundY = 0.5 - halfH * 0.44;
      floor.position.y = groundY;
      const logoZoom = (dist - LOGO_Z) / dist;
      const logoHalfW = halfW * logoZoom;
      logoW = Math.min(12, logoHalfW * 0.9);
      logoH = logoW * logoAspect;
      stopX = Math.max(1.5, logoHalfW * 0.51);
      logoRest = groundY + logoH * 0.36;
      logoTop = 0.5 + halfH * logoZoom + logoH * 0.5 + 0.3;
      logoShadow.scale.set(logoW * 0.5, logoH * 0.35, 1);
      // Perspective makes the distant character small; the route stays on the ground.
      entryZ = dist * (1 - 1 / ENTRY_SIZE);
      entryX = halfW * 0.96 / ENTRY_SIZE;
      entryYaw = Math.atan2(stopX - entryX, -entryZ);
    };
    resize();
    window.addEventListener('resize', resize);

    const FEET = 0;
    const FACE_LEFT = -Math.PI / 2;
    const reset = () => {
      state.phase = 'enter';
      state.t = 0;
      state.x = entryX;
      state.z = entryZ;
      state.shotsLeft = SHOTS;
      state.nextShot = 0.35;
      state.recoil = 0;
      state.recoilT = 1;
      carry = 1;
      shotPending = false;
      flash.visible = false;
      flashLight.intensity = 0;
      body.position.set(state.x, groundY + FEET, state.z);
      body.quaternion.identity();
      floor.material.opacity = 0.45;
      body.scale.set(1, 1, 1);
      actor.rotation.y = entryYaw;
      state.squash = 0;
      state.squashT = 0;
      state.struggle = 0;
      state.crawl = 0;
      state.reach = 0;
      state.collapse = 0;
      logo.rotation.z = 0;
      if (rifleFree) {
        arm.add(rifle);
        rifleFree = false;
      }
      rifle.scale.setScalar(1.2);
      rifle.renderOrder = 0;
      logo.visible = false;
      logo.material.opacity = 1;
      logoShadow.material.opacity = 0;
      fadeTo(sprint, 0);
    };
    reset();

    const fire = () => {
      flash.visible = true;
      flash.material.opacity = 1;
      flash.material.rotation = Math.random() * Math.PI;
      flash.scale.setScalar(0.75 + Math.random() * 0.35);
      flashLight.intensity = 6;
      state.recoilT = 0;
      shotPending = true;
      state.shotsLeft -= 1;
      state.nextShot = 0.16 + Math.random() * 0.05;
    };

    const clock = new T.Clock();
    let visible = true;
    const io = new IntersectionObserver((entries) => { visible = entries[0].isIntersecting; }, { threshold: 0 });
    io.observe(section);

    const tick = () => {
      requestAnimationFrame(tick);
      const dt = Math.min(clock.getDelta(), 0.1);
      if (!visible || document.hidden) return;
      state.t += dt;

      if (state.phase === 'enter') {
        const progress = T.MathUtils.smoothstep(state.t, 0, ENTRY_DURATION);
        state.x = T.MathUtils.lerp(entryX, stopX, progress);
        state.z = T.MathUtils.lerp(entryZ, 0, progress);
        if (state.t >= ENTRY_DURATION) {
          state.x = stopX;
          state.z = 0;
          state.phase = 'shoot';
          state.t = 0;
          fadeTo(idle, 0.22);
        }
      } else if (state.phase === 'shoot') {
        state.nextShot -= dt;
        if (state.nextShot <= 0 && state.shotsLeft > 0) fire();
        if (state.shotsLeft <= 3) {
          state.phase = 'drop';
          state.t = 0;
          state.logoY = logoTop;
          state.logoVy = -8;
          logo.visible = true;
          logo.scale.set(logoW, logoW, 1);
          logo.position.set(state.x, state.logoY, LOGO_Z);
        }
      } else if (state.phase === 'drop') {
        state.nextShot -= dt;
        if (state.nextShot <= 0 && state.shotsLeft > 0) fire();
        state.logoVy -= 90 * dt;
        state.logoY += state.logoVy * dt;
        const landY = logoRest;
        const fall = Math.max(0, Math.min(1, 1 - (state.logoY - landY) / (logoTop - landY)));
        logoShadow.material.opacity = 0.5 * fall;
        if (state.logoY <= landY) {
          state.logoY = landY;
          state.phase = 'squash';
          state.t = 0;
          state.squash = 1;
          state.squashT = 0;
          shotPending = false;
          flash.visible = false;
          dropRifle();
          state.shake = 1;
          puff(state.x, groundY);
          wave(state.x, logoW * 0.5, groundY);
          head.rotation.set(-0.6, 0, 0);
          arm.rotation.set(pinnedArm, 0, 0);
          armFar.rotation.set(pinnedArm, 0, 0);
          legNear.rotation.set(0, 0, 0);
          legFar.rotation.set(0, 0, 0);
        }
        logo.position.set(state.x, state.logoY, LOGO_Z);
      } else if (state.phase === 'squash') {
        const wobble = Math.exp(-state.t * 5) * Math.sin(state.t * 22) * 0.2;
        const since = Math.max(0, state.t - 0.9);
        const attempts = Math.min(2, Math.floor(since / 1.6));
        const cycle = attempts < 2 ? since % 1.6 : since - 3.2;
        const pull = T.MathUtils.smoothstep(cycle, 0.32, 1.04);
        // Reach, pull and recover share one cycle; completed pulls retain their distance.
        if (attempts < 2) {
          state.reach = T.MathUtils.smoothstep(cycle, 0, 0.32) * (1 - T.MathUtils.smoothstep(cycle, 1.04, 1.44));
          state.struggle = Math.pow(Math.sin(pull * Math.PI), 2);
          state.crawl = (attempts + pull) * 0.24;
        } else {
          state.collapse = T.MathUtils.smoothstep(cycle, 0.95, 1.4);
          state.reach = T.MathUtils.smoothstep(cycle, 0, 0.5) * (1 - state.collapse);
          state.struggle = T.MathUtils.smoothstep(cycle, 0.35, 0.85) * (1 - state.collapse);
          state.crawl = 0.48 + state.struggle * 0.04;
        }
        logo.position.y = logoRest + Math.max(0, wobble) * logoH + state.struggle * 0.015;
        logo.rotation.z = -state.struggle * 0.004;
        logo.scale.set(logoW * (1 + wobble * 0.6), logoW * (1 - wobble * 0.8), 1);
        if (state.collapse === 1) {
          state.struggle = 0;
          state.reach = 0;
          logo.position.y = logoRest;
          logo.rotation.z = 0;
          logo.scale.set(logoW, logoW, 1);
          state.phase = 'hold';
          state.t = 0;
        }
      } else if (state.phase === 'hold') {
        if (state.t < REST_HOLD) {
          renderer.render(scene, camera);
          return;
        }
        state.phase = 'leave';
        state.t = 0;
      } else if (state.phase === 'leave') {
        const k = Math.min(1, state.t / 0.5);
        logo.material.opacity = 1 - k;
        logoShadow.material.opacity = 0.5 * (1 - k);
        fadeMats.forEach((m) => { m.opacity = 1 - k; });
        floor.material.opacity = 0.45 * (1 - k);
        if (state.t >= 0.5) reset();
      }

      state.recoilT += dt;
      const kick = state.recoilT * 32;
      state.recoil = state.recoilT < 0.3 ? kick * Math.exp(1 - kick) : 0;
      if (state.squash > 0) {
        if (state.phase === 'squash') state.squashT += dt;
        const k = 1 - Math.pow(1 - Math.min(1, state.squashT / 0.16), 3);
        const bounce = Math.exp(-state.squashT * 6) * Math.sin(state.squashT * 30) * 0.05;
        const g = state.struggle;
        qFall.setFromAxisAngle(Z_AXIS, Math.PI / 2 * k);
        body.quaternion.copy(qFall);
        body.scale.set(1 - 0.8 * k + bounce, 1 + 0.1 * k, 1 + 0.2 * k);
        body.position.x = state.x + 1.5 * k - state.crawl;
        body.position.y = groundY + 0.09 * k;
        arm.rotation.x = T.MathUtils.lerp(pinnedArm + state.reach * 0.28 - g * 0.4, Math.PI + 0.3, state.collapse);
        armFar.rotation.x = T.MathUtils.lerp(pinnedArm + state.reach * 0.22 - g * 0.32, Math.PI + 0.15, state.collapse);
        legNear.rotation.x = -g * 0.12;
        legFar.rotation.x = g * 0.08;
        head.rotation.x = -0.6 + state.reach * 0.08 - state.collapse * 0.2;
        head.rotation.z = -g * 0.06 - state.collapse * 0.16;
      } else {
        body.position.x = state.x + state.recoil * 0.025;
        body.position.y = groundY + FEET;
        body.position.z = state.z;
      }
      logoShadow.position.set(state.x, groundY + 0.01, LOGO_Z);

      if (!state.squash) {
        carry += ((state.phase === 'enter' ? 1 : 0) - carry) * (1 - Math.exp(-dt * 12));
        actor.rotation.y = T.MathUtils.lerp(FACE_LEFT + 0.15, entryYaw, carry);
      }
      if (state.phase !== 'leave') fadeMats.forEach((m) => { m.opacity = 1; });

      flashLight.intensity = Math.max(0, flashLight.intensity - dt * 60);
      if (flash.visible) {
        flash.material.opacity = Math.max(0, flash.material.opacity - dt * 14);
        if (flash.material.opacity <= 0) { flash.visible = false; flash.material.opacity = 1; }
      }

      for (let i = tracers.length - 1; i >= 0; i--) {
        const tr = tracers[i];
        tr.life += dt;
        tr.mesh.position.addScaledVector(tr.velocity, dt);
        tr.mesh.material.opacity = Math.max(0, 0.95 - tr.life * 3);
        if (tr.life > 0.35 || tr.mesh.position.x < edgeLeft - 10) { scene.remove(tr.mesh); tr.mesh.geometry.dispose(); tracers.splice(i, 1); }
      }
      dust.forEach((p) => {
        if (!p.mesh.visible) return;
        p.life += dt;
        p.vy -= p.gravity * dt;
        p.vx *= Math.max(0, 1 - p.drag * dt);
        p.mesh.position.x += p.vx * dt;
        p.mesh.position.y += p.vy * dt;
        p.mesh.scale.x += dt * 1.4;
        p.mesh.scale.y += dt * 1.4;
        p.mesh.material.rotation += p.spin * dt;
        const k = p.life / p.ttl;
        p.mesh.material.opacity = Math.max(0, p.peak * (1 - k * k));
        if (p.life > p.ttl) p.mesh.visible = false;
      });
      for (let i = shells.length - 1; i >= 0; i--) {
        const s = shells[i];
        s.life += dt;
        s.vy -= 8 * dt;
        s.mesh.position.x += s.vx * dt;
        s.mesh.position.y += s.vy * dt;
        s.mesh.rotation.z += s.spin * dt;
        if (s.mesh.position.y < groundY) { s.mesh.position.y = groundY; s.vy = -s.vy * 0.35; s.vx *= 0.6; }
        if (s.life > 1.2) { scene.remove(s.mesh); s.mesh.geometry.dispose(); shells.splice(i, 1); }
      }

      state.shake = Math.max(0, state.shake - dt * 4);
      const shakeAmp = state.shake * state.shake * 0.14;
      camera.position.x = (Math.random() - 0.5) * shakeAmp;
      camera.position.y = 0.5 + (Math.random() - 0.5) * shakeAmp;

      if (!state.squash) {
        mixer.update(dt);
        const stride = sprint.time / sprint.getClip().duration * Math.PI * 2;
        const sway = Math.sin(stride) * carry;
        const step = Math.sin(stride * 2) * carry;
        // Keep the grip fixed while shoulders and both arms carry the weapon together.
        torso.rotation.set(0.06 + carry * 0.12 + step * 0.018 - state.recoil * 0.045, sway * 0.018, sway * 0.012);
        arm.rotation.set(-Math.PI / 2 - 0.06 + carry * 0.12 + step * 0.012 - state.recoil * 0.035, 0, -0.5);
        armFar.rotation.set(-Math.PI / 2 + 0.06 + carry * 0.12 + step * 0.012 - state.recoil * 0.035, 0, 0.65);
        head.rotation.set(-torso.rotation.x * 0.65, carry * 0.12, -torso.rotation.z * 0.5);
      }
      if (rifleFree && state.phase !== 'leave') {
        rifleVel.y -= 24 * dt;
        rifle.position.addScaledVector(rifleVel, dt);
        const floorY = groundY + 0.42;
        if (rifle.position.y < floorY) {
          rifle.position.y = floorY;
          rifleVel.y = Math.abs(rifleVel.y) * 0.35;
          rifleVel.x *= 0.5;
          rifleVel.z = 0;
          rifleSettle = true;
          if (rifleVel.y < 1) rifleVel.set(0, 0, 0);
        }
        if (rifleSettle) {
          const target = Math.round(rifleAngle / (Math.PI * 2)) * Math.PI * 2;
          rifleAngle += (target - rifleAngle) * Math.min(1, dt * 9);
        } else {
          rifleAngle += rifleOmega * dt;
        }
        spinQ.setFromAxisAngle(Z_AXIS, rifleAngle);
        rifle.quaternion.copy(restQ).premultiply(spinQ);
      } else if (!rifleFree) {
        rifle.quaternion.copy(gripQ);
        rifle.position.copy(GRIP);
      }
      if (shotPending && !rifleFree) {
        spawnTracer();
        spawnShell();
        shotPending = false;
      }
      renderer.render(scene, camera);
    };
    tick();

    window.keepClipHero = { state, reset };
  };

  const kick = () => start().catch((error) => { console.error('KeepClip hero animation failed:', error); canvas.remove(); });
  if ('requestIdleCallback' in window) requestIdleCallback(kick, { timeout: 1500 });
  else setTimeout(kick, 300);
})();
