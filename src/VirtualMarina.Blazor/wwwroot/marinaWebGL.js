// VirtualMarina WebGL 2 backend.
//
// Deliberately "dumb": it uploads buffers, draws what C# (VirtualMarina.Core, running in WebAssembly)
// describes, and forwards input. Scene logic, camera, picking and shaders all live in shared .NET code.
//
// Binary data arrives as Uint8Array (Blazor sends byte[] that way): meshes, layer instances and the per-frame buffer.
// The frame loop asks .NET for a frame only while something changes or moves; otherwise it sleeps until .NET, the
// input, a resize or a slow poll wakes it, and it stops altogether while the canvas is off screen or the tab hidden.

const FRAME = {
    VIEW: 0, PROJ: 16, CAM: 32, TIME: 35,
    SUN_DIR: 36, SUN_COLOR: 39, AMBIENT: 42, SPECULAR: 45, SHININESS: 46,
    SKY: 47, FOG: 50, FOG_DENSITY: 53,
    DEEP: 54, SHALLOW: 57, WAVE_AMP: 60, WAVE_FREQ: 61, WAVE_SPEED: 62,
    SKY_REFLECTION: 63, RIPPLES: 64, SUN_GLINTS: 65, FLOAT_MOTION: 66,
    WATER_CENTER: 67, DETAIL_RADIUS: 69,
    // Reference image: shown (0/1), key (int32), min x/z, max x/z, height, opacity, above the scene (0/1).
    IMAGE: 70, IMAGE_KEY: 71, IMAGE_MIN: 72, IMAGE_MAX: 74, IMAGE_HEIGHT: 76, IMAGE_OPACITY: 77, IMAGE_ABOVE: 78,
    // Popup anchor: shown (0/1), x, y in CSS pixels relative to the canvas.
    ANCHOR: 79,
};
const VERTEX_STRIDE_BYTES = 9 * 4;
const INSTANCE_STRIDE_BYTES = 20 * 4;
const INSTANCE_FIRST_ATTRIBUTE = 3;
const INSTANCE_ATTRIBUTES = 5;
const PASS = { OPAQUE: 0, TRANSPARENT: 1 };

// How long an idle view waits before asking .NET whether anything changed that it was not told about (the camera moved
// by code, the lighting changed). Changes made through the visualizer wake it at once.
const IDLE_POLL_MS = 250;

// How much one wheel event scrolls, in notches, by WheelEvent.deltaMode: 1 is lines, 2 is pages; pixels (0) and
// anything else count 100 to the notch.
const WHEEL_UNITS = { 1: 3, 2: 1 };
// Frames that fail one after another before the loop gives up and reports the error.
const MAX_FRAME_FAILURES = 3;

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
        layers: new Map(),
        layerOrder: [],
        // The transparent instances of every layer, farthest first, as .NET sorted them, and their runs of one mesh.
        transparent: { buffer: null, capacity: 0, runs: new Int32Array(0) },
        waterMeshId: -1,
        model: null,
        water: null,
        image: null,
        imageQuad: null,
        imageTexture: null,
        imageKey: -1,
        imagePendingKey: -1,
        decoded: new Map(),
        nextDecodeToken: 1,
        // Loop state.
        started: false,
        rafHandle: 0,
        pollHandle: 0,
        failures: 0,
        failed: false,
        visible: true,
        // Sizes measured by the observers rather than read from the layout every frame.
        cssWidth: canvas.clientWidth,
        cssHeight: canvas.clientHeight,
        pixelWidth: 0,
        pixelHeight: 0,
        popupWidth: 0,
        popupHeight: 0,
        popupShown: false,
        popupTransform: '',
        caretX: '',
        caretDisplay: '',
        listeners: [],
        observers: [],
        sources: null,
        contextLost: false,
        uploadsLost: false,
        pointerButton: -1,
        cursor: 'grab',
    };
    views.set(id, view);
    attachInput(view);
    attachContextLoss(view);
    attachObservers(view);
    return id;
}

/** shaders: { modelVertex, modelFragment, waterVertex, waterFragment, imageVertex, imageFragment } */
export function initRenderer(id, shaders, imageQuadCorners) {
    const view = views.get(id);
    if (!view) return 'Unknown view.';
    // Kept so the programs can be built again if the browser takes the context away and later gives it back.
    view.sources = { ...shaders, imageQuadCorners };
    return createGlResources(view);
}

/** Builds the programs and the image quad from view.sources. Returns an error message, or null. */
function createGlResources(view) {
    const gl = view.gl;
    const s = view.sources;
    try {
        view.model = createProgram(gl, s.modelVertex, s.modelFragment);
        view.water = createProgram(gl, s.waterVertex, s.waterFragment);
        view.image = createProgram(gl, s.imageVertex, s.imageFragment);
    } catch (e) {
        view.model = view.water = view.image = null;
        return String(e?.message ? e.message : e);
    }

    const imageQuadCorners = s.imageQuadCorners;
    const quadVao = gl.createVertexArray();
    gl.bindVertexArray(quadVao);
    const quadVbo = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, quadVbo);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(imageQuadCorners), gl.STATIC_DRAW);
    gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 8, 0);
    gl.enableVertexAttribArray(0);
    gl.bindVertexArray(null);
    view.imageQuad = { vao: quadVao, vbo: quadVbo };
    return null;
}

/**
 * The browser can take the WebGL context away (GPU reset, driver update, too many contexts) and give it back later. Every
 * buffer, texture and program is gone then, so drawing stops while it is lost; once restored, the programs are rebuilt here
 * and the next renderFrame tells .NET that everything it uploaded has to be sent again.
 */
function attachContextLoss(view) {
    const lost = (e) => {
        e.preventDefault(); // without this the browser never restores the context
        view.contextLost = true;
    };
    const restored = () => {
        view.contextLost = false;
        view.meshes.clear();
        view.layers.clear();
        view.layerOrder = [];
        forgetTransparent(view);
        view.waterMeshId = -1;
        view.model = view.water = view.image = null;
        view.imageQuad = null;
        view.imageTexture = null;
        view.imageKey = -1;
        view.imagePendingKey = -1;
        view.uploadsLost = true;
        if (view.sources) {
            const error = createGlResources(view);
            if (error) console.error('[VirtualMarina] WebGL could not be restored', error);
        }
        wake(view);
    };
    view.canvas.addEventListener('webglcontextlost', lost);
    view.canvas.addEventListener('webglcontextrestored', restored);
    view.listeners.push([view.canvas, 'webglcontextlost', lost, undefined], [view.canvas, 'webglcontextrestored', restored, undefined]);
}

/** Takes in a new size of the canvas (in CSS and device pixels, where the browser reports them) or of the popup. */
function resized(view, entry) {
    if (entry.target === view.canvas) {
        const box = entry.contentBoxSize?.[0];
        view.cssWidth = box ? box.inlineSize : entry.contentRect.width;
        view.cssHeight = box ? box.blockSize : entry.contentRect.height;
        const pixels = entry.devicePixelContentBoxSize?.[0];
        view.pixelWidth = pixels ? pixels.inlineSize : 0;
        view.pixelHeight = pixels ? pixels.blockSize : 0;
    } else if (entry.target === view.popup) {
        view.popupWidth = view.popup.offsetWidth;
        view.popupHeight = view.popup.offsetHeight;
    }
}

/**
 * Sizes are measured when they change instead of being read from the layout every frame, and drawing stops while the
 * canvas is scrolled out of sight or the tab is hidden.
 */
function attachObservers(view) {
    const canvas = view.canvas;
    if (typeof ResizeObserver !== 'undefined') {
        const sizes = new ResizeObserver((entries) => {
            for (const entry of entries) resized(view, entry);
            wake(view);
        });
        sizes.observe(canvas);
        if (view.popup) sizes.observe(view.popup);
        view.observers.push(sizes);
    }

    if (typeof IntersectionObserver !== 'undefined') {
        const sight = new IntersectionObserver((entries) => {
            view.visible = entries.at(-1).isIntersecting;
            wake(view);
        });
        sight.observe(canvas);
        view.observers.push(sight);
    }

    const shown = () => wake(view);
    document.addEventListener('visibilitychange', shown);
    view.listeners.push([document, 'visibilitychange', shown, undefined]);
}

/** Forgets every mesh, layer and the reference image, e.g. when the view is given a different marina. */
export function resetScene(id) {
    const view = views.get(id);
    if (!view) return;
    const gl = view.gl;
    if (!view.contextLost) {
        for (const mesh of view.meshes.values()) deleteMeshResources(gl, mesh);
        for (const layer of view.layers.values()) gl.deleteBuffer(layer.buffer);
        if (view.transparent.buffer) gl.deleteBuffer(view.transparent.buffer);
    }
    view.meshes.clear();
    view.layers.clear();
    view.layerOrder = [];
    forgetTransparent(view);
    view.waterMeshId = -1;
    view.imageKey = -1;
    view.imagePendingKey = -1;
    wake(view);
}

export function getDeviceDescription(id) {
    const gl = views.get(id)?.gl;
    if (!gl) return null;
    const info = gl.getExtension('WEBGL_debug_renderer_info');
    const renderer = info ? gl.getParameter(info.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER);
    return `${renderer} — ${gl.getParameter(gl.VERSION)}`;
}

/** vertices: interleaved position/normal/color floats; indices: uint32 triangle list; both as bytes. */
export function uploadMesh(id, meshId, vertices, indices, isWater) {
    const view = views.get(id);
    if (!view || view.contextLost) return;
    const gl = view.gl;

    const existing = view.meshes.get(meshId);
    if (existing) deleteMeshResources(gl, existing);

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

    // The instance attributes advance once per instance. Where they read from is set at each draw, since the same mesh
    // is drawn from several layers' buffers.
    if (!isWater) {
        for (let i = 0; i < INSTANCE_ATTRIBUTES; i++) {
            gl.enableVertexAttribArray(INSTANCE_FIRST_ATTRIBUTE + i);
            gl.vertexAttribDivisor(INSTANCE_FIRST_ATTRIBUTE + i, 1);
        }
    }
    gl.bindVertexArray(null);

    view.meshes.set(meshId, { vao, vbo, ebo, count: indices.byteLength / 4, isWater });
    if (isWater) view.waterMeshId = meshId;
}

export function deleteMesh(id, meshId) {
    const view = views.get(id);
    if (!view || view.contextLost) return;
    const mesh = view.meshes.get(meshId);
    if (!mesh) return;
    deleteMeshResources(view.gl, mesh);
    view.meshes.delete(meshId);
}

function deleteMeshResources(gl, mesh) {
    gl.deleteVertexArray(mesh.vao);
    gl.deleteBuffer(mesh.vbo);
    gl.deleteBuffer(mesh.ebo);
}

// ---- Layers ------------------------------------------------------------------------------------
// A layer is one instance buffer (20 floats per instance) plus its batches: [meshId, pass, start, count] per batch.

/** Replaces a layer: all of its instances and batches, as bytes. */
export function setLayer(id, kind, instances, batches) {
    const view = views.get(id);
    if (!view || view.contextLost) return;
    const gl = view.gl;
    let layer = view.layers.get(kind);
    if (!layer) {
        layer = { buffer: gl.createBuffer(), capacity: 0, batches: new Int32Array(0) };
        view.layers.set(kind, layer);
        orderLayers(view);
    }

    gl.bindBuffer(gl.ARRAY_BUFFER, layer.buffer);
    if (instances.byteLength > layer.capacity) {
        // Room to grow, so a layer that gains a few instances does not reallocate every time.
        layer.capacity = Math.max(instances.byteLength, Math.floor(layer.capacity * 1.5));
        gl.bufferData(gl.ARRAY_BUFFER, layer.capacity, gl.DYNAMIC_DRAW);
    }
    if (instances.byteLength > 0) gl.bufferSubData(gl.ARRAY_BUFFER, 0, instances);
    gl.bindBuffer(gl.ARRAY_BUFFER, null);
    layer.batches = int32s(batches);
}

/** Rewrites some of a layer's instances in place: ranges = [start, count] pairs, instances = theirs end to end. */
export function patchLayer(id, kind, ranges, instances) {
    const view = views.get(id);
    if (!view || view.contextLost) return;
    const layer = view.layers.get(kind);
    if (!layer) return;
    const gl = view.gl;
    const bounds = int32s(ranges);
    gl.bindBuffer(gl.ARRAY_BUFFER, layer.buffer);
    let from = 0;
    for (let i = 0; i < bounds.length; i += 2) {
        const bytes = bounds[i + 1] * INSTANCE_STRIDE_BYTES;
        gl.bufferSubData(gl.ARRAY_BUFFER, bounds[i] * INSTANCE_STRIDE_BYTES, instances, from, bytes);
        from += bytes;
    }
    gl.bindBuffer(gl.ARRAY_BUFFER, null);
}

export function deleteLayer(id, kind) {
    const view = views.get(id);
    if (!view) return;
    const layer = view.layers.get(kind);
    if (!layer) return;
    if (!view.contextLost) view.gl.deleteBuffer(layer.buffer);
    view.layers.delete(kind);
    orderLayers(view);
}

/**
 * Replaces the transparent instances: every layer's, farthest from the camera first, as bytes, with their runs as
 * [meshId, start, count] triples. Blending needs them back to front where they overlap, so they are drawn from here
 * rather than from the layers; .NET sends them again when the camera or the scene changed the order.
 */
export function setTransparent(id, instances, runs) {
    const view = views.get(id);
    if (!view || view.contextLost) return;
    const gl = view.gl;
    const transparent = view.transparent;
    if (!transparent.buffer) transparent.buffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, transparent.buffer);
    if (instances.byteLength > transparent.capacity) {
        transparent.capacity = Math.max(instances.byteLength, Math.floor(transparent.capacity * 1.5));
        gl.bufferData(gl.ARRAY_BUFFER, transparent.capacity, gl.STREAM_DRAW);
    }
    if (instances.byteLength > 0) gl.bufferSubData(gl.ARRAY_BUFFER, 0, instances);
    gl.bindBuffer(gl.ARRAY_BUFFER, null);
    transparent.runs = int32s(runs);
}

/** Forgets the sorted transparent instances; the buffer itself is deleted by the caller, or went with the context. */
function forgetTransparent(view) {
    view.transparent = { buffer: null, capacity: 0, runs: new Int32Array(0) };
}

/** Layers are drawn in the order of their kinds, which is the order .NET gives them in. */
function orderLayers(view) {
    view.layerOrder = [...view.layers.keys()].sort((a, b) => a - b).map((kind) => view.layers.get(kind));
}

// ---- Reference image (designer) -----------------------------------------------------------------

/** Uploads decoded RGBA pixels (top row first) as the reference image texture. */
export function setReferenceImageRgba(id, key, rgba, width, height) {
    const view = views.get(id);
    if (!view || view.contextLost) return;
    // Supersedes any encoded image still being decoded, which would otherwise replace this one when it finishes.
    view.imagePendingKey = key;
    uploadImageTexture(view, key, width, height, new Uint8Array(rgba.buffer ?? rgba, rgba.byteOffset ?? 0, width * height * 4));
}

/** Decodes PNG/JPEG/WebP bytes in the browser and uploads them as the reference image texture (asynchronously). */
export function setReferenceImageEncoded(id, key, bytes, contentType) {
    const view = views.get(id);
    if (!view) return;
    view.imagePendingKey = key;
    createImageBitmap(new Blob([bytes], { type: contentType || 'image/png' }))
        .then((bitmap) => {
            if (views.get(id) === view && view.imagePendingKey === key && !view.contextLost) {
                uploadImageTexture(view, key, bitmap.width, bitmap.height, bitmap);
                wake(view);
            }
            bitmap.close?.();
        })
        .catch((e) => console.error('[VirtualMarina] reference image could not be decoded', e));
}

/**
 * Decodes PNG/JPEG/WebP bytes and keeps the picture, so loading an image decodes it once: returns [width, height, token],
 * or null when the browser can't decode them. adoptImage then makes it the reference image texture.
 */
export async function decodeImage(id, bytes, contentType) {
    const view = views.get(id);
    if (!view) return null;
    try {
        const bitmap = await createImageBitmap(new Blob([bytes], { type: contentType || 'image/png' }));
        const token = view.nextDecodeToken++;
        // Only the latest decoded picture is kept: an older one was never adopted and never will be.
        for (const old of view.decoded.values()) old.close?.();
        view.decoded.clear();
        view.decoded.set(token, bitmap);
        return [bitmap.width, bitmap.height, token];
    } catch {
        return null;
    }
}

/** Uploads a picture decodeImage kept as the texture of the image with this key. */
export function adoptImage(id, token, key) {
    const view = views.get(id);
    if (!view) return;
    const bitmap = view.decoded.get(token);
    if (!bitmap) return;
    view.decoded.delete(token);
    view.imagePendingKey = key;
    if (!view.contextLost) uploadImageTexture(view, key, bitmap.width, bitmap.height, bitmap);
    bitmap.close?.();
    wake(view);
}

function uploadImageTexture(view, key, width, height, source) {
    const gl = view.gl;
    if (!view.imageTexture) view.imageTexture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, view.imageTexture);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
    if (source instanceof Uint8Array) {
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, source);
    } else {
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, gl.RGBA, gl.UNSIGNED_BYTE, source);
    }
    gl.generateMipmap(gl.TEXTURE_2D);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.bindTexture(gl.TEXTURE_2D, null);
    view.imageKey = key;
}

/** Frees the texture once no image is shown: a large one holds a lot of video memory. */
function releaseImageTexture(view) {
    if (!view.imageTexture) return;
    view.gl.deleteTexture(view.imageTexture);
    view.imageTexture = null;
    view.imageKey = -1;
}

function drawReferenceImage(view, f, keys) {
    const gl = view.gl;
    if (!view.image || !view.imageTexture || view.imageKey !== keys[FRAME.IMAGE_KEY]) return;
    const p = view.image;
    const u = p.uniforms;
    if (f[FRAME.IMAGE_ABOVE] > 0) gl.disable(gl.DEPTH_TEST);
    gl.useProgram(p.program);
    if (u.uView) gl.uniformMatrix4fv(u.uView, false, f, FRAME.VIEW, 16);
    if (u.uProjection) gl.uniformMatrix4fv(u.uProjection, false, f, FRAME.PROJ, 16);
    if (u.uImageMin) gl.uniform2f(u.uImageMin, f[FRAME.IMAGE_MIN], f[FRAME.IMAGE_MIN + 1]);
    if (u.uImageMax) gl.uniform2f(u.uImageMax, f[FRAME.IMAGE_MAX], f[FRAME.IMAGE_MAX + 1]);
    if (u.uImageHeight) gl.uniform1f(u.uImageHeight, f[FRAME.IMAGE_HEIGHT]);
    if (u.uOpacity) gl.uniform1f(u.uOpacity, f[FRAME.IMAGE_OPACITY]);
    if (u.uImage) gl.uniform1i(u.uImage, 0);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, view.imageTexture);
    gl.bindVertexArray(view.imageQuad.vao);
    gl.drawArrays(gl.TRIANGLES, 0, 6);
    gl.bindTexture(gl.TEXTURE_2D, null);
    gl.enable(gl.DEPTH_TEST);
}

// ---- Frames ------------------------------------------------------------------------------------

/**
 * Draws a frame from the per-frame buffer (uniforms, image placement, popup anchor, as bytes). Returns true once after the
 * context was restored: everything uploaded before must be sent again.
 */
export function renderFrame(id, frame) {
    const view = views.get(id);
    if (!view) return false;
    if (view.uploadsLost) {
        view.uploadsLost = false;
        return true;
    }
    if (view.contextLost || !view.model) return false;
    const gl = view.gl;
    const f = float32s(frame);
    const keys = new Int32Array(f.buffer, f.byteOffset, f.length);

    // The state this needs is set every frame rather than trusted to have stayed as it was.
    gl.viewport(0, 0, view.canvas.width, view.canvas.height);
    gl.enable(gl.DEPTH_TEST);
    gl.depthFunc(gl.LEQUAL);
    gl.disable(gl.CULL_FACE);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.clearColor(f[FRAME.FOG], f[FRAME.FOG + 1], f[FRAME.FOG + 2], 1);
    gl.depthMask(true);
    gl.disable(gl.BLEND);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);

    // 1. Opaque instances, layer by layer, one draw call per batch.
    const model = view.model;
    gl.useProgram(model.program);
    applyFrameUniforms(gl, model, f);
    drawPass(view, PASS.OPAQUE);

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

    // 3. Reference image (designer), then the transparent instances (status pads, ghost boats,
    // drawing previews) farthest first, in the order .NET sorted them.
    gl.enable(gl.BLEND);
    gl.depthMask(false);
    if (f[FRAME.IMAGE] > 0) drawReferenceImage(view, f, keys);
    else releaseImageTexture(view);
    gl.useProgram(model.program);
    drawTransparent(view);
    gl.depthMask(true);
    gl.disable(gl.BLEND);
    gl.bindVertexArray(null);

    positionPopup(view, f);
    return false;
}

/** Starts the frame loop. */
export function start(id) {
    const view = views.get(id);
    if (!view || view.started) return;
    view.started = true;
    wake(view);
}

/** Asks for a frame: .NET calls this when something changed, so a sleeping view draws it. */
export function wakeView(id) {
    const view = views.get(id);
    if (view) wake(view);
}

function wake(view) {
    if (!view.started || view.failed || view.rafHandle) return;
    if (view.pollHandle) {
        clearTimeout(view.pollHandle);
        view.pollHandle = 0;
    }
    view.rafHandle = requestAnimationFrame((timestamp) => tick(view, timestamp));
}

/** Goes to sleep: no frames until woken, with a slow poll for changes .NET could not announce. */
function sleep(view) {
    if (view.pollHandle || view.failed || !view.started) return;
    const poll = () => {
        view.pollHandle = 0;
        if (!views.has(view.id) || !isShown(view)) return; // woken again by the observers when shown
        try {
            if (view.dotnetRef.invokeMethod('NeedsFrame')) {
                wake(view);
                return;
            }
        } catch (e) {
            console.error('[VirtualMarina] frame check failed', e);
        }
        view.pollHandle = setTimeout(poll, IDLE_POLL_MS);
    };
    view.pollHandle = setTimeout(poll, IDLE_POLL_MS);
}

function isShown(view) {
    return view.visible && document.visibilityState !== 'hidden';
}

function tick(view, timestamp) {
    view.rafHandle = 0;
    if (!views.has(view.id) || view.failed) return;
    // Nothing can be drawn while the context is lost (restoring it wakes the view), nor seen while hidden.
    if (view.contextLost || !isShown(view)) return;

    let again;
    try {
        resizeCanvas(view);
        again = view.dotnetRef.invokeMethod('OnAnimationFrame', timestamp, view.cssWidth, view.cssHeight);
        view.failures = 0;
    } catch (e) {
        // A frame that throws is logged and retried; one that keeps throwing stops the loop and is reported, rather than
        // failing sixty times a second forever with nothing on screen to say so.
        view.failures++;
        console.error('[VirtualMarina] frame failed', e);
        if (view.failures >= MAX_FRAME_FAILURES) {
            view.failed = true;
            view.dotnetRef.invokeMethodAsync('OnRenderLoopFailed', String(e?.message ? e.message : e))
                .catch((err) => console.error('[VirtualMarina] could not report the failure', err));
            return;
        }
        again = true;
    }

    if (again) wake(view);
    else sleep(view);
}

export function destroyView(id) {
    const view = views.get(id);
    if (!view) return;
    view.started = false;
    if (view.rafHandle) cancelAnimationFrame(view.rafHandle);
    if (view.pollHandle) clearTimeout(view.pollHandle);
    for (const [target, type, handler, options] of view.listeners) {
        target.removeEventListener(type, handler, options);
    }
    for (const observer of view.observers) observer.disconnect();
    for (const bitmap of view.decoded.values()) bitmap.close?.();
    views.delete(id);
    if (view.contextLost) return; // the resources are already gone

    const gl = view.gl;
    for (const mesh of view.meshes.values()) deleteMeshResources(gl, mesh);
    for (const layer of view.layers.values()) gl.deleteBuffer(layer.buffer);
    if (view.transparent.buffer) gl.deleteBuffer(view.transparent.buffer);
    if (view.model) gl.deleteProgram(view.model.program);
    if (view.water) gl.deleteProgram(view.water.program);
    if (view.image) gl.deleteProgram(view.image.program);
    if (view.imageQuad) {
        gl.deleteVertexArray(view.imageQuad.vao);
        gl.deleteBuffer(view.imageQuad.vbo);
    }
    if (view.imageTexture) gl.deleteTexture(view.imageTexture);
}

// ---- Drawing helpers ---------------------------------------------------------------------------

/** Draws every batch of one pass, layer by layer in the order .NET numbered them. */
function drawPass(view, pass) {
    for (const layer of view.layerOrder) {
        const batches = layer.batches;
        for (let b = 0; b < batches.length; b += 4) {
            if (batches[b + 1] === pass) drawInstances(view, layer.buffer, batches[b], batches[b + 2], batches[b + 3]);
        }
    }
}

/** The transparent instances, back to front, one draw call per run of one mesh. */
function drawTransparent(view) {
    const { buffer, runs } = view.transparent;
    if (!buffer) return;
    for (let r = 0; r < runs.length; r += 3) drawInstances(view, buffer, runs[r], runs[r + 1], runs[r + 2]);
}

/** One instanced draw: the mesh, with its instances read from the layer's buffer starting at start. */
function drawInstances(view, buffer, meshId, start, count) {
    const mesh = view.meshes.get(meshId);
    if (!mesh || mesh.isWater || count === 0) return;
    const gl = view.gl;
    gl.bindVertexArray(mesh.vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    const offset = start * INSTANCE_STRIDE_BYTES;
    for (let i = 0; i < INSTANCE_ATTRIBUTES; i++) {
        gl.vertexAttribPointer(INSTANCE_FIRST_ATTRIBUTE + i, 4, gl.FLOAT, false, INSTANCE_STRIDE_BYTES, offset + i * 16);
    }
    gl.drawElementsInstanced(gl.TRIANGLES, mesh.count, gl.UNSIGNED_INT, 0, count);
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
    setFloat(gl, program, 'uSkyReflection', f[FRAME.SKY_REFLECTION]);
    setFloat(gl, program, 'uRipples', f[FRAME.RIPPLES]);
    setFloat(gl, program, 'uSunGlints', f[FRAME.SUN_GLINTS]);
    setFloat(gl, program, 'uFloatMotion', f[FRAME.FLOAT_MOTION]);
    setVec2(gl, program, 'uWaterCenter', f, FRAME.WATER_CENTER);
    setFloat(gl, program, 'uDetailRadius', f[FRAME.DETAIL_RADIUS]);
}

function setVec2(gl, program, name, array, offset) {
    const location = program.uniforms[name];
    if (location) gl.uniform2f(location, array[offset], array[offset + 1]);
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

    // Every uniform location is looked up once, here.
    const uniforms = {};
    const count = gl.getProgramParameter(program, gl.ACTIVE_UNIFORMS);
    for (let i = 0; i < count; i++) {
        const info = gl.getActiveUniform(program, i);
        uniforms[info.name] = gl.getUniformLocation(program, info.name);
    }
    return { program, uniforms };
}

/** The bytes as float32s, without a copy when they are aligned (Blazor's are). */
function float32s(bytes) {
    return bytes.byteOffset % 4 === 0
        ? new Float32Array(bytes.buffer, bytes.byteOffset, bytes.byteLength >> 2)
        : new Float32Array(bytes.slice().buffer);
}

/** The bytes as int32s, without a copy when they are aligned. */
function int32s(bytes) {
    return bytes.byteOffset % 4 === 0
        ? new Int32Array(bytes.buffer, bytes.byteOffset, bytes.byteLength >> 2)
        : new Int32Array(bytes.slice().buffer);
}

/** Sizes the drawing buffer from the size the ResizeObserver measured, touching the canvas only when it changed. */
function resizeCanvas(view) {
    const canvas = view.canvas;
    const dpr = window.devicePixelRatio || 1;
    const width = Math.max(1, view.pixelWidth || Math.round(view.cssWidth * dpr));
    const height = Math.max(1, view.pixelHeight || Math.round(view.cssHeight * dpr));
    if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
    }
}

// ---- Selection popup ---------------------------------------------------------------------------
// Content is rendered by Blazor; only its position is updated here, with each frame, so it follows the berth while the
// camera moves. Its size comes from the ResizeObserver, and styles are written only when they change.

const POPUP_MARGIN = 8;
const POPUP_GAP = 12;

function positionPopup(view, f) {
    const popup = view.popup;
    if (!popup) return;

    const width = view.cssWidth;
    const height = view.cssHeight;
    const x = f[FRAME.ANCHOR + 1];
    const y = f[FRAME.ANCHOR + 2];
    const visible = f[FRAME.ANCHOR] > 0 && popup.childElementCount > 0 &&
        x >= -20 && x <= width + 20 && y >= -20 && y <= height + 60;
    if (!visible) {
        if (view.popupShown) {
            popup.style.visibility = 'hidden';
            view.popupShown = false;
        }
        return;
    }

    // Measured on the first showing, before the observer has reported a size.
    if (!view.popupWidth) {
        view.popupWidth = popup.offsetWidth;
        view.popupHeight = popup.offsetHeight;
    }
    const w = view.popupWidth;
    const h = view.popupHeight;
    const left = Math.round(Math.min(Math.max(x - w / 2, POPUP_MARGIN), Math.max(POPUP_MARGIN, width - w - POPUP_MARGIN)));
    const top = Math.round(Math.max(y - h - POPUP_GAP, POPUP_MARGIN));
    const caret = `${Math.round(Math.min(Math.max(x - left, 16), w - 16))}px`;
    // The caret only makes sense while the popup actually sits above the anchor.
    const caretDisplay = top === Math.round(y - h - POPUP_GAP) ? 'block' : 'none';

    const transform = `translate(${left}px, ${top}px)`;
    if (view.popupTransform !== transform) {
        popup.style.transform = transform;
        view.popupTransform = transform;
    }
    if (view.caretX !== caret) {
        popup.style.setProperty('--vm-caret-x', caret);
        view.caretX = caret;
    }
    if (view.caretDisplay !== caretDisplay) {
        popup.style.setProperty('--vm-caret-display', caretDisplay);
        view.caretDisplay = caretDisplay;
    }
    if (!view.popupShown) {
        popup.style.visibility = 'visible';
        view.popupShown = true;
    }
}

/**
 * The popup's look lives in marinaView.css next to this script, inside a cascade layer so that any style of the host
 * page wins over it. It is linked first thing in the head, once, so hosts need not add it themselves.
 */
function ensureStyles() {
    const href = new URL('./marinaView.css', import.meta.url).href;
    if ([...document.querySelectorAll('link')].some((link) => link.rel === 'stylesheet' && link.href === href)) return;
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = href;
    link.dataset.virtualmarina = '';
    document.head.insertBefore(link, document.head.firstChild);
}

// ---- Input -------------------------------------------------------------------------------------
// Every event that reaches .NET wakes the loop, so a view that is asleep (nothing animating) still answers input at once.

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
        try {
            fn(e);
        } catch (err) {
            console.error('[VirtualMarina] input failed', err);
        }
        wake(view);
    };
    // Ends a press the browser took away (a touch turned into a scroll, capture lost to another element) as if the
    // button had been let go, so a drag never sticks.
    const abandon = (e) => {
        if (view.pointerButton < 0) return;
        const button = view.pointerButton;
        view.pointerButton = -1;
        canvas.style.cursor = view.cursor || 'grab';
        const [x, y] = position(e);
        ref.invokeMethod('OnPointerUp', x, y, button, modifiers(e));
        ref.invokeMethod('OnPointerLeave');
    };

    on(canvas, 'pointerdown', safe((e) => {
        canvas.focus({ preventScroll: true });
        canvas.setPointerCapture(e.pointerId);
        view.pointerButton = e.button;
        if (view.cursor !== 'crosshair') canvas.style.cursor = 'grabbing';
        const [x, y] = position(e);
        ref.invokeMethod('OnPointerDown', x, y, e.button, modifiers(e));
    }));
    on(canvas, 'pointermove', safe((e) => {
        const [x, y] = position(e);
        // C# picks the cursor: a hand over selectable berths/boats, a crosshair or move cursor for designer tools.
        const cursor = ref.invokeMethod('OnPointerMove', x, y, modifiers(e)) || 'grab';
        view.cursor = cursor;
        // While a button is held the cursor stays 'grabbing'.
        if (!canvas.hasPointerCapture(e.pointerId) && canvas.style.cursor !== cursor) canvas.style.cursor = cursor;
    }));
    on(canvas, 'pointerup', safe((e) => {
        view.pointerButton = -1;
        if (canvas.hasPointerCapture(e.pointerId)) canvas.releasePointerCapture(e.pointerId);
        canvas.style.cursor = view.cursor || 'grab';
        const [x, y] = position(e);
        ref.invokeMethod('OnPointerUp', x, y, e.button, modifiers(e));
    }));
    on(canvas, 'pointercancel', safe(abandon));
    on(canvas, 'lostpointercapture', safe(abandon));
    on(canvas, 'pointerleave', safe(() => {
        if (view.pointerButton < 0) ref.invokeMethod('OnPointerLeave');
    }));
    on(canvas, 'dblclick', safe((e) => {
        const [x, y] = position(e);
        ref.invokeMethod('OnDoubleClick', x, y, e.button, modifiers(e));
    }));
    on(canvas, 'wheel', safe((e) => {
        e.preventDefault();
        const unit = WHEEL_UNITS[e.deltaMode] ?? 100;
        const [x, y] = position(e);
        ref.invokeMethod('OnWheel', -e.deltaY / unit, x, y);
    }), { passive: false });
    on(canvas, 'contextmenu', (e) => e.preventDefault());
    if (view.popup) {
        on(view.popup, 'contextmenu', (e) => e.preventDefault());
        // Keep wheel over the popup from scrolling the page; zoom the view instead.
        on(view.popup, 'wheel', safe((e) => {
            e.preventDefault();
            const unit = WHEEL_UNITS[e.deltaMode] ?? 100;
            const [x, y] = position(e);
            ref.invokeMethod('OnWheel', -e.deltaY / unit, x, y);
        }), { passive: false });
    }
    // A key the view acted on goes no further: not to the browser, and not to the page's own shortcuts, which
    // would otherwise act on it a second time. Everything else — Ctrl+S, the browser's Ctrl+/Ctrl- zoom — carries on.
    const keydown = (e) => {
        if (e.isComposing) return false;
        if (!ref.invokeMethod('OnKeyDown', e.code || '', e.key || '', modifiers(e))) return false;
        e.preventDefault();
        e.stopPropagation();
        return true;
    };
    on(canvas, 'keydown', safe(keydown));
    if (view.popup) {
        // The popup is not inside the canvas, so a key pressed on one of its buttons never reaches the listener
        // above. Only Esc is passed on, as the close button promises: Enter and Space belong to the buttons.
        on(view.popup, 'keydown', safe((e) => {
            if (e.key !== 'Escape' || !keydown(e)) return;
            // The button that had the focus goes with the popup; give the focus back to the view.
            canvas.focus({ preventScroll: true });
        }));
    }
}
