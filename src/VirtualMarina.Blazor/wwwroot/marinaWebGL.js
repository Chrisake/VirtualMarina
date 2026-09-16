// VirtualMarina WebGL 2 backend.
//
// Deliberately "dumb": it uploads buffers, draws what C# (VirtualMarina.Core, running in WebAssembly)
// describes, and forwards input. Scene logic, camera, picking and shaders all live in shared .NET code.

const FRAME = {
    VIEW: 0, PROJ: 16, CAM: 32, TIME: 35,
    SUN_DIR: 36, SUN_COLOR: 39, AMBIENT: 42, SPECULAR: 45, SHININESS: 46,
    SKY: 47, FOG: 50, FOG_DENSITY: 53,
    DEEP: 54, SHALLOW: 57, WAVE_AMP: 60, WAVE_FREQ: 61, WAVE_SPEED: 62,
};
const OBJECT_STRIDE = 25;
const VERTEX_STRIDE_BYTES = 9 * 4;

const views = new Map();
let nextViewId = 1;

export function createView(canvas, dotnetRef, popup) {
    const gl = canvas.getContext('webgl2', { antialias: true, alpha: false, powerPreference: 'high-performance' });
    if (!gl) return -1;

    ensureStyles();
    const id = nextViewId++;
    const view = {
        id, canvas, gl, dotnetRef, popup,
        meshes: new Map(),
        waterMeshId: -1,
        objects: new Float32Array(0),
        objectCount: 0,
        model: null,
        water: null,
        running: false,
        rafHandle: 0,
        errorLogged: false,
        listeners: [],
    };
    views.set(id, view);
    attachInput(view);
    return id;
}

export function initRenderer(id, modelVertex, modelFragment, waterVertex, waterFragment) {
    const view = views.get(id);
    if (!view) return 'Unknown view.';
    const gl = view.gl;
    try {
        view.model = createProgram(gl, modelVertex, modelFragment);
        view.water = createProgram(gl, waterVertex, waterFragment);
    } catch (e) {
        return String(e && e.message ? e.message : e);
    }

    gl.enable(gl.DEPTH_TEST);
    gl.depthFunc(gl.LEQUAL);
    gl.disable(gl.CULL_FACE);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    return null;
}

export function getDeviceDescription(id) {
    const gl = views.get(id)?.gl;
    if (!gl) return null;
    const info = gl.getExtension('WEBGL_debug_renderer_info');
    const renderer = info ? gl.getParameter(info.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER);
    return `${renderer} — ${gl.getParameter(gl.VERSION)}`;
}

export function uploadMesh(id, meshId, verticesBase64, indicesBase64, isWater) {
    const view = views.get(id);
    if (!view) return;
    const gl = view.gl;

    const existing = view.meshes.get(meshId);
    if (existing) {
        gl.deleteVertexArray(existing.vao);
        gl.deleteBuffer(existing.vbo);
        gl.deleteBuffer(existing.ebo);
    }

    const vertices = new Float32Array(decodeBase64(verticesBase64));
    const indices = new Uint32Array(decodeBase64(indicesBase64));

    const vao = gl.createVertexArray();
    gl.bindVertexArray(vao);
    const vbo = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
    gl.bufferData(gl.ARRAY_BUFFER, vertices, gl.STATIC_DRAW);
    const ebo = gl.createBuffer();
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ebo);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, indices, gl.STATIC_DRAW);

    gl.vertexAttribPointer(0, 3, gl.FLOAT, false, VERTEX_STRIDE_BYTES, 0);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(1, 3, gl.FLOAT, false, VERTEX_STRIDE_BYTES, 12);
    gl.enableVertexAttribArray(1);
    gl.vertexAttribPointer(2, 3, gl.FLOAT, false, VERTEX_STRIDE_BYTES, 24);
    gl.enableVertexAttribArray(2);
    gl.bindVertexArray(null);

    view.meshes.set(meshId, { vao, vbo, ebo, count: indices.length });
    if (isWater) view.waterMeshId = meshId;
}

export function setObjects(id, objectsBase64, count) {
    const view = views.get(id);
    if (!view) return;
    view.objects = new Float32Array(decodeBase64(objectsBase64));
    view.objectCount = count;
}

export function renderFrame(id, frameValues) {
    const view = views.get(id);
    if (!view || !view.model) return;
    const gl = view.gl;
    const f = Float32Array.from(frameValues);

    gl.viewport(0, 0, view.canvas.width, view.canvas.height);
    gl.clearColor(f[FRAME.FOG], f[FRAME.FOG + 1], f[FRAME.FOG + 2], 1);
    gl.depthMask(true);
    gl.disable(gl.BLEND);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);

    // 1. Opaque objects
    const model = view.model;
    gl.useProgram(model.program);
    applyFrameUniforms(gl, model, f);
    drawObjects(view, false);

    // 2. Water
    const water = view.meshes.get(view.waterMeshId);
    if (water) {
        const wp = view.water;
        gl.useProgram(wp.program);
        applyFrameUniforms(gl, wp, f);
        setVec3(gl, wp, 'uWaterDeep', f, FRAME.DEEP);
        setVec3(gl, wp, 'uWaterShallow', f, FRAME.SHALLOW);
        gl.bindVertexArray(water.vao);
        gl.drawElements(gl.TRIANGLES, water.count, gl.UNSIGNED_INT, 0);
    }

    // 3. Transparent overlays
    gl.enable(gl.BLEND);
    gl.depthMask(false);
    gl.useProgram(model.program);
    drawObjects(view, true);
    gl.depthMask(true);
    gl.disable(gl.BLEND);
    gl.bindVertexArray(null);
}

export function start(id) {
    const view = views.get(id);
    if (!view || view.running) return;
    view.running = true;

    const tick = (timestamp) => {
        if (!view.running) return;
        try {
            resizeCanvas(view.canvas);
            const anchor = view.dotnetRef.invokeMethod('OnAnimationFrame', timestamp, view.canvas.clientWidth, view.canvas.clientHeight);
            positionPopup(view, anchor);
        } catch (e) {
            if (!view.errorLogged) {
                console.error('[VirtualMarina] frame failed', e);
                view.errorLogged = true;
            }
        }
        view.rafHandle = requestAnimationFrame(tick);
    };
    view.rafHandle = requestAnimationFrame(tick);
}

export function destroyView(id) {
    const view = views.get(id);
    if (!view) return;
    view.running = false;
    cancelAnimationFrame(view.rafHandle);
    for (const [target, type, handler, options] of view.listeners) {
        target.removeEventListener(type, handler, options);
    }
    const gl = view.gl;
    for (const mesh of view.meshes.values()) {
        gl.deleteVertexArray(mesh.vao);
        gl.deleteBuffer(mesh.vbo);
        gl.deleteBuffer(mesh.ebo);
    }
    if (view.model) gl.deleteProgram(view.model.program);
    if (view.water) gl.deleteProgram(view.water.program);
    views.delete(id);
}

// ---- Drawing helpers ---------------------------------------------------------------------------

function drawObjects(view, transparentPass) {
    const gl = view.gl;
    const u = view.model.uniforms;
    const objects = view.objects;
    let boundMesh = -1;
    let mesh = null;

    for (let i = 0; i < view.objectCount; i++) {
        const base = i * OBJECT_STRIDE;
        const isTransparent = objects[base + 19] < 0.999;
        if (isTransparent !== transparentPass) continue;

        const meshId = objects[base + 23];
        if (meshId !== boundMesh) {
            mesh = view.meshes.get(meshId);
            if (!mesh) continue;
            gl.bindVertexArray(mesh.vao);
            boundMesh = meshId;
        }

        gl.uniformMatrix4fv(u.uModel, false, objects, base, 16);
        gl.uniform4f(u.uTint, objects[base + 16], objects[base + 17], objects[base + 18], objects[base + 19]);
        if (u.uEmissive) gl.uniform1f(u.uEmissive, objects[base + 20]);
        if (u.uDesaturation) gl.uniform1f(u.uDesaturation, objects[base + 24]);
        if (u.uAnimation) gl.uniform1i(u.uAnimation, objects[base + 21] | 0);
        if (u.uPhase) gl.uniform1f(u.uPhase, objects[base + 22]);
        gl.drawElements(gl.TRIANGLES, mesh.count, gl.UNSIGNED_INT, 0);
    }
}

function applyFrameUniforms(gl, program, f) {
    const u = program.uniforms;
    if (u.uView) gl.uniformMatrix4fv(u.uView, false, f, FRAME.VIEW, 16);
    if (u.uProjection) gl.uniformMatrix4fv(u.uProjection, false, f, FRAME.PROJ, 16);
    setVec3(gl, program, 'uCameraPos', f, FRAME.CAM);
    setFloat(gl, program, 'uTime', f[FRAME.TIME]);
    setVec3(gl, program, 'uSunDirection', f, FRAME.SUN_DIR);
    setVec3(gl, program, 'uSunColor', f, FRAME.SUN_COLOR);
    setVec3(gl, program, 'uAmbientColor', f, FRAME.AMBIENT);
    setFloat(gl, program, 'uSpecularStrength', f[FRAME.SPECULAR]);
    setFloat(gl, program, 'uShininess', f[FRAME.SHININESS]);
    setVec3(gl, program, 'uSkyColor', f, FRAME.SKY);
    setVec3(gl, program, 'uFogColor', f, FRAME.FOG);
    setFloat(gl, program, 'uFogDensity', f[FRAME.FOG_DENSITY]);
    setFloat(gl, program, 'uWaveAmplitude', f[FRAME.WAVE_AMP]);
    setFloat(gl, program, 'uWaveFrequency', f[FRAME.WAVE_FREQ]);
    setFloat(gl, program, 'uWaveSpeed', f[FRAME.WAVE_SPEED]);
}

function setFloat(gl, program, name, value) {
    const location = program.uniforms[name];
    if (location) gl.uniform1f(location, value);
}

function setVec3(gl, program, name, array, offset) {
    const location = program.uniforms[name];
    if (location) gl.uniform3f(location, array[offset], array[offset + 1], array[offset + 2]);
}

function createProgram(gl, vertexSource, fragmentSource) {
    const compile = (type, source) => {
        const shader = gl.createShader(type);
        gl.shaderSource(shader, source);
        gl.compileShader(shader);
        if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
            const log = gl.getShaderInfoLog(shader);
            gl.deleteShader(shader);
            throw new Error(`${type === gl.VERTEX_SHADER ? 'Vertex' : 'Fragment'} shader: ${log}`);
        }
        return shader;
    };

    const vs = compile(gl.VERTEX_SHADER, vertexSource);
    const fs = compile(gl.FRAGMENT_SHADER, fragmentSource);
    const program = gl.createProgram();
    gl.attachShader(program, vs);
    gl.attachShader(program, fs);
    gl.linkProgram(program);
    gl.deleteShader(vs);
    gl.deleteShader(fs);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
        throw new Error('Program link: ' + gl.getProgramInfoLog(program));
    }

    const uniforms = {};
    const count = gl.getProgramParameter(program, gl.ACTIVE_UNIFORMS);
    for (let i = 0; i < count; i++) {
        const info = gl.getActiveUniform(program, i);
        uniforms[info.name] = gl.getUniformLocation(program, info.name);
    }
    return { program, uniforms };
}

function decodeBase64(base64) {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes.buffer;
}

function resizeCanvas(canvas) {
    const dpr = window.devicePixelRatio || 1;
    const width = Math.max(1, Math.round(canvas.clientWidth * dpr));
    const height = Math.max(1, Math.round(canvas.clientHeight * dpr));
    if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
    }
}

// ---- Selection popup ---------------------------------------------------------------------------
// Content is rendered by Blazor; only its position is updated here, every frame, so it follows the
// slip while the camera moves. anchor = [visible (0/1), x, y] in CSS pixels relative to the canvas.

const POPUP_MARGIN = 8;
const POPUP_GAP = 12;

function positionPopup(view, anchor) {
    const popup = view.popup;
    if (!popup) return;

    const width = view.canvas.clientWidth;
    const height = view.canvas.clientHeight;
    const visible = anchor && anchor[0] > 0 && popup.childElementCount > 0 &&
        anchor[1] >= -20 && anchor[1] <= width + 20 && anchor[2] >= -20 && anchor[2] <= height + 60;
    if (!visible) {
        if (popup.style.visibility !== 'hidden') popup.style.visibility = 'hidden';
        return;
    }

    const x = anchor[1];
    const y = anchor[2];
    const w = popup.offsetWidth;
    const h = popup.offsetHeight;
    const left = Math.round(Math.min(Math.max(x - w / 2, POPUP_MARGIN), Math.max(POPUP_MARGIN, width - w - POPUP_MARGIN)));
    const top = Math.round(Math.max(y - h - POPUP_GAP, POPUP_MARGIN));
    const caret = Math.min(Math.max(x - left, 16), w - 16);
    // The caret only makes sense while the popup actually sits above the anchor.
    const caretVisible = top === Math.round(y - h - POPUP_GAP);

    const transform = `translate(${left}px, ${top}px)`;
    if (popup.style.transform !== transform) popup.style.transform = transform;
    popup.style.setProperty('--vm-caret-x', `${Math.round(caret)}px`);
    popup.style.setProperty('--vm-caret-display', caretVisible ? 'block' : 'none');
    if (popup.style.visibility !== 'visible') popup.style.visibility = 'visible';
}

function ensureStyles() {
    if (document.getElementById('vm-marina-styles')) return;
    const style = document.createElement('style');
    style.id = 'vm-marina-styles';
    style.textContent = POPUP_CSS;
    document.head.appendChild(style);
}

const POPUP_CSS = `
.vm-popup {
    position: absolute; left: 0; top: 0; z-index: 10;
    visibility: hidden;
    min-width: 220px; max-width: 320px;
    background: #ffffff; color: #1c2631;
    border-radius: 10px;
    box-shadow: 0 10px 28px rgba(12, 30, 48, .22), 0 2px 6px rgba(12, 30, 48, .12);
    font: 12.5px/1.35 "Segoe UI", system-ui, -apple-system, sans-serif;
    user-select: none;
    pointer-events: auto;
}
.vm-popup__accent { height: 4px; border-radius: 10px 10px 0 0; background: var(--vm-accent, #5b7a99); }
.vm-popup__head { display: flex; align-items: flex-start; gap: 8px; padding: 9px 12px 0; }
.vm-popup__titles { flex: 1; min-width: 0; }
.vm-popup__title { font-size: 14px; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.vm-popup__subtitle { color: #647383; font-size: 12px; margin-top: 1px; }
.vm-popup__close {
    border: 0; background: transparent; color: #8795a3; cursor: pointer;
    font-size: 16px; line-height: 1; padding: 0 2px; border-radius: 4px;
}
.vm-popup__close:hover { color: #1c2631; background: #eef2f5; }
.vm-popup__lines {
    display: grid; grid-template-columns: auto 1fr; gap: 3px 12px;
    margin: 8px 0 0; padding: 0 12px;
}
.vm-popup__lines dt { color: #647383; white-space: nowrap; }
.vm-popup__lines dd { margin: 0; min-width: 0; overflow-wrap: anywhere; }
.vm-popup__lines dd.vm-strong { font-weight: 600; }
.vm-popup__lines dd.vm-wide { grid-column: 1 / -1; }
.vm-popup__footer { color: #647383; padding: 6px 12px 0; font-style: italic; }
.vm-popup__actions { margin-top: 8px; padding: 4px; border-top: 1px solid #e6ebf0; display: flex; flex-direction: column; }
.vm-popup__sep { height: 1px; background: #e6ebf0; margin: 4px 6px; }
.vm-popup__action {
    display: flex; align-items: center; gap: 8px; width: 100%;
    border: 0; background: transparent; color: inherit; font: inherit; text-align: left;
    padding: 6px 8px; border-radius: 6px; cursor: pointer;
}
.vm-popup__action:hover:not(:disabled) { background: #eef3f8; }
.vm-popup__action:disabled { opacity: .45; cursor: default; }
.vm-popup__action--primary { color: #1f5fd1; font-weight: 600; }
.vm-popup__action--danger { color: #c62f28; }
.vm-popup__action--danger:hover:not(:disabled) { background: #fdeeee; }
.vm-popup__icon { width: 18px; text-align: center; flex: none; }
.vm-popup__caption { flex: 1; }
.vm-popup__shortcut { color: #8795a3; font-size: 11px; }
.vm-popup--tooltip .vm-popup__body { padding-bottom: 10px; }
.vm-popup__caret {
    display: var(--vm-caret-display, block);
    position: absolute; bottom: -6px; left: var(--vm-caret-x, 50%);
    width: 12px; height: 12px; margin-left: -6px;
    background: #ffffff; transform: rotate(45deg);
    box-shadow: 3px 3px 6px rgba(12, 30, 48, .10);
}
`;

// ---- Input -------------------------------------------------------------------------------------

function attachInput(view) {
    const canvas = view.canvas;
    const ref = view.dotnetRef;
    const on = (target, type, handler, options) => {
        target.addEventListener(type, handler, options);
        view.listeners.push([target, type, handler, options]);
    };
    const position = (e) => {
        const rect = canvas.getBoundingClientRect();
        return [e.clientX - rect.left, e.clientY - rect.top];
    };
    // Cmd counts as Ctrl so multi-select works on macOS.
    const modifiers = (e) => (e.shiftKey ? 1 : 0) | (e.ctrlKey || e.metaKey ? 2 : 0) | (e.altKey ? 4 : 0);
    const safe = (fn) => (e) => {
        try { fn(e); } catch (err) { console.error('[VirtualMarina] input failed', err); }
    };

    on(canvas, 'pointerdown', safe((e) => {
        canvas.focus();
        canvas.setPointerCapture(e.pointerId);
        canvas.style.cursor = 'grabbing';
        const [x, y] = position(e);
        ref.invokeMethod('OnPointerDown', x, y, e.button, modifiers(e));
    }));
    on(canvas, 'pointermove', safe((e) => {
        const [x, y] = position(e);
        ref.invokeMethod('OnPointerMove', x, y, modifiers(e));
    }));
    on(canvas, 'pointerup', safe((e) => {
        if (canvas.hasPointerCapture(e.pointerId)) canvas.releasePointerCapture(e.pointerId);
        canvas.style.cursor = 'grab';
        const [x, y] = position(e);
        ref.invokeMethod('OnPointerUp', x, y, e.button, modifiers(e));
    }));
    on(canvas, 'pointerleave', safe(() => ref.invokeMethod('OnPointerLeave')));
    on(canvas, 'dblclick', safe((e) => {
        const [x, y] = position(e);
        ref.invokeMethod('OnDoubleClick', x, y, e.button, modifiers(e));
    }));
    on(canvas, 'wheel', safe((e) => {
        e.preventDefault();
        const unit = e.deltaMode === 1 ? 3 : e.deltaMode === 2 ? 1 : 100;
        const [x, y] = position(e);
        ref.invokeMethod('OnWheel', -e.deltaY / unit, x, y);
    }), { passive: false });
    on(canvas, 'contextmenu', (e) => e.preventDefault());
    if (view.popup) {
        on(view.popup, 'contextmenu', (e) => e.preventDefault());
        // Keep wheel over the popup from scrolling the page; zoom the view instead.
        on(view.popup, 'wheel', safe((e) => {
            e.preventDefault();
            const unit = e.deltaMode === 1 ? 3 : e.deltaMode === 2 ? 1 : 100;
            const [x, y] = position(e);
            ref.invokeMethod('OnWheel', -e.deltaY / unit, x, y);
        }), { passive: false });
    }
    on(canvas, 'keydown', safe((e) => {
        if (ref.invokeMethod('OnKeyDown', e.key, modifiers(e))) e.preventDefault();
    }));
}
