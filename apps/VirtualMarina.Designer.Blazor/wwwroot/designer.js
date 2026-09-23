// Browser side of the VirtualMarina Blazor designer.
//
//   * vmDesigner.downloadText  — saves a .marina.json design as a file the desktop designer can reopen.
//   * vmDesigner.fontFamilies  — the fonts on this machine that a berth label could be lettered in.
//   * vmDesigner.captureFont   — traces a font's glyph outlines so the design carries its own lettering.
//
// The desktop designer captures glyph outlines through GDI+; the browser has no equivalent, so the font is
// drawn large onto a canvas and its outlines are read back from the pixels with marching squares. The result
// is the same shape LabelFontDefinition wants: closed contours with the baseline at y = 0 and the cap height
// at y = 1, the pen starting at x = 0, which the C# side turns into LabelGlyph values.
(() => {
    "use strict";

    // Everything printable in ASCII, the characters the desktop designer captures too.
    const CHARACTERS =
        "!\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    // Font size the outlines are taken at. Big enough that the traced pixels lose nothing a label shows.
    const RENDER_SIZE = 200;

    // How far a traced point may stray from the line it sits on before it is kept, in pixels of the render.
    const SIMPLIFY_EPSILON = 0.75;

    // Families probed for, since a browser will not list the installed fonts. Only the ones actually present
    // are returned. A design authored on another machine still reads: its captured outlines travel in the file.
    const CANDIDATES = [
        "Arial", "Arial Black", "Helvetica", "Helvetica Neue", "Times New Roman", "Times", "Georgia",
        "Courier New", "Courier", "Verdana", "Tahoma", "Trebuchet MS", "Segoe UI", "Calibri", "Cambria",
        "Consolas", "Comic Sans MS", "Impact", "Palatino Linotype", "Book Antiqua", "Garamond",
        "Franklin Gothic Medium", "Century Gothic", "Lucida Console", "Lucida Sans Unicode", "Arial Narrow",
        "Roboto", "Open Sans", "Lato", "Montserrat", "Noto Sans", "Noto Serif", "DejaVu Sans", "DejaVu Serif",
        "DejaVu Sans Mono", "Liberation Sans", "Liberation Serif", "Liberation Mono", "Ubuntu", "Cantarell",
        "FreeSans", "FreeSerif", "Nimbus Sans", "Source Sans Pro", "PT Sans", "Fira Sans", "Cascadia Code",
    ];

    let probeCtx = null;
    function probeContext() {
        if (!probeCtx) {
            probeCtx = document.createElement("canvas").getContext("2d");
        }
        return probeCtx;
    }

    // A family is present when it draws a known string to a different width than the generic it falls back to.
    const BASELINE_FONTS = ["monospace", "serif", "sans-serif"];
    const PROBE_TEXT = "mmmmmwwwwwiiiii0123456789MW@";

    function widthWith(font, generic) {
        const ctx = probeContext();
        ctx.font = `72px ${font}, ${generic}`;
        return ctx.measureText(PROBE_TEXT).width;
    }

    function isFontAvailable(family) {
        for (const generic of BASELINE_FONTS) {
            const ctx = probeContext();
            ctx.font = `72px ${generic}`;
            const baseline = ctx.measureText(PROBE_TEXT).width;
            if (Math.abs(widthWith(`"${family}"`, generic) - baseline) > 0.5) {
                return true;
            }
        }
        return false;
    }

    // ---- Marching squares ------------------------------------------------------------------------

    // Traces every closed boundary in a binary grid. Each boundary is one loop of points on the half-grid,
    // outer shapes and the holes inside them alike; which is which is left to the C# side to work out.
    function traceContours(grid, width, height) {
        // Segments between edge midpoints, keyed by a quantised endpoint so they can be stitched into loops.
        const nodes = new Map();       // key -> { x, y, links: [key, key, ...] }
        const key = (x, y) => `${x},${y}`;

        function point(x2, y2) {        // coordinates are held doubled, so half-grid midpoints stay integers
            const k = key(x2, y2);
            let node = nodes.get(k);
            if (!node) {
                node = { x: x2, y: y2, links: [] };
                nodes.set(k, node);
            }
            return node;
        }

        function segment(ax, ay, bx, by) {
            const a = point(ax, ay);
            const b = point(bx, by);
            a.links.push(key(bx, by));
            b.links.push(key(ax, ay));
        }

        const at = (x, y) => (x < 0 || y < 0 || x >= width || y >= height ? 0 : grid[y * width + x]);

        for (let y = -1; y < height; y++) {
            for (let x = -1; x < width; x++) {
                const tl = at(x, y);
                const tr = at(x + 1, y);
                const br = at(x + 1, y + 1);
                const bl = at(x, y + 1);
                const idx = (tl << 3) | (tr << 2) | (br << 1) | bl;
                if (idx === 0 || idx === 15) continue;

                // Edge midpoints, doubled: T=(2x+1,2y) R=(2x+2,2y+1) B=(2x+1,2y+2) L=(2x,2y+1).
                const T = [2 * x + 1, 2 * y];
                const R = [2 * x + 2, 2 * y + 1];
                const B = [2 * x + 1, 2 * y + 2];
                const L = [2 * x, 2 * y + 1];

                const link = (e1, e2) => segment(e1[0], e1[1], e2[0], e2[1]);
                switch (idx) {
                    case 1: link(B, L); break;
                    case 2: link(R, B); break;
                    case 3: link(R, L); break;
                    case 4: link(T, R); break;
                    case 5: link(T, R); link(B, L); break;      // saddle: group by the two inside corners
                    case 6: link(T, B); break;
                    case 7: link(T, L); break;
                    case 8: link(T, L); break;
                    case 9: link(T, B); break;
                    case 10: link(T, L); link(R, B); break;     // saddle
                    case 11: link(T, R); break;
                    case 12: link(R, L); break;
                    case 13: link(R, B); break;
                    case 14: link(B, L); break;
                    default: break;
                }
            }
        }

        // Stitch the segments into closed loops.
        const loops = [];
        const used = new Set();
        const edgeKey = (a, b) => (a < b ? `${a}|${b}` : `${b}|${a}`);

        for (const start of nodes.keys()) {
            const startNode = nodes.get(start);
            for (const first of startNode.links) {
                if (used.has(edgeKey(start, first))) continue;

                const loop = [];
                let prev = start;
                let cur = first;
                used.add(edgeKey(prev, cur));
                loop.push([startNode.x, startNode.y]);

                while (cur !== start) {
                    const node = nodes.get(cur);
                    if (!node) break;
                    loop.push([node.x, node.y]);
                    let next = null;
                    for (const candidate of node.links) {
                        if (candidate !== prev && !used.has(edgeKey(cur, candidate))) {
                            next = candidate;
                            break;
                        }
                    }
                    if (next === null) break;
                    used.add(edgeKey(cur, next));
                    prev = cur;
                    cur = next;
                }

                if (loop.length >= 3) loops.push(loop);
            }
        }

        return loops;
    }

    // Ramer–Douglas–Peucker: drops the points that sit on the line between the ones that matter.
    function simplify(points, epsilon) {
        if (points.length < 3) return points;

        const keep = new Array(points.length).fill(false);
        keep[0] = keep[points.length - 1] = true;
        const stack = [[0, points.length - 1]];

        while (stack.length > 0) {
            const [first, last] = stack.pop();
            let maxDist = 0;
            let index = -1;
            const [ax, ay] = points[first];
            const [bx, by] = points[last];
            const dx = bx - ax;
            const dy = by - ay;
            const length = Math.hypot(dx, dy) || 1;

            for (let i = first + 1; i < last; i++) {
                const [px, py] = points[i];
                const dist = Math.abs((px - ax) * dy - (py - ay) * dx) / length;
                if (dist > maxDist) {
                    maxDist = dist;
                    index = i;
                }
            }

            if (maxDist > epsilon && index !== -1) {
                keep[index] = true;
                stack.push([first, index], [index, last]);
            }
        }

        return points.filter((_, i) => keep[i]);
    }

    // ---- Capture ---------------------------------------------------------------------------------

    function captureFont(family, bold) {
        const weight = bold ? "bold " : "";
        const canvas = document.createElement("canvas");
        const margin = 24;
        canvas.width = RENDER_SIZE * 2;
        canvas.height = RENDER_SIZE * 2;
        const ctx = canvas.getContext("2d", { willReadFrequently: true });
        ctx.font = `${weight}${RENDER_SIZE}px "${family}"`;
        ctx.textBaseline = "alphabetic";
        ctx.textAlign = "left";
        ctx.fillStyle = "#000";

        // Cap height, from the drawn height of a capital letter — what a label's height is measured against.
        const capHeight = measureCapHeight(ctx, canvas, weight, family, margin);
        if (!(capHeight > 1)) return null;

        const baselineY = margin + capHeight * 1.4;   // room for descenders below the baseline
        const originX = margin;
        const glyphs = [];

        // A space has no outline; its advance is measured like any other character.
        glyphs.push({ c: " ", a: ctx.measureText(" ").width / capHeight, o: [] });

        for (const character of CHARACTERS) {
            const advance = ctx.measureText(character).width / capHeight;
            const contours = captureGlyph(ctx, canvas, character, originX, baselineY, capHeight, margin);
            glyphs.push({ c: character, a: advance, o: contours });
        }

        return { name: bold ? `${family} Bold` : family, isBold: !!bold, glyphs };
    }

    function measureCapHeight(ctx, canvas, weight, family, margin) {
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        ctx.font = `${weight}${RENDER_SIZE}px "${family}"`;
        ctx.textBaseline = "alphabetic";
        ctx.textAlign = "left";
        ctx.fillStyle = "#000";
        const baseline = margin + RENDER_SIZE;
        ctx.fillText("H", margin, baseline);

        const data = ctx.getImageData(0, 0, canvas.width, canvas.height).data;
        let top = canvas.height;
        let bottom = 0;
        for (let y = 0; y < canvas.height; y++) {
            for (let x = 0; x < canvas.width; x++) {
                if (data[(y * canvas.width + x) * 4 + 3] > 128) {
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;
                    break;
                }
            }
        }
        return bottom >= top ? bottom - top + 1 : 0;
    }

    function captureGlyph(ctx, canvas, character, originX, baselineY, capHeight, margin) {
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        ctx.fillText(character, originX, baselineY);

        // Only the region the glyph could occupy, padded by one cell so its boundary always closes.
        const advanceWidth = ctx.measureText(character).width;
        const left = Math.max(0, originX - 4);
        const right = Math.min(canvas.width, originX + Math.ceil(advanceWidth) + capHeight + 4);
        const top = Math.max(0, margin - 4);
        const bottom = Math.min(canvas.height, Math.ceil(baselineY + capHeight) + 4);
        const width = right - left;
        const height = bottom - top;
        if (width <= 0 || height <= 0) return [];

        const image = ctx.getImageData(left, top, width, height).data;
        const grid = new Uint8Array(width * height);
        let ink = false;
        for (let i = 0; i < width * height; i++) {
            if (image[i * 4 + 3] > 128) {
                grid[i] = 1;
                ink = true;
            }
        }
        if (!ink) return [];

        const loops = traceContours(grid, width, height);
        const contours = [];
        for (const loop of loops) {
            // Points come back doubled and in the cropped region; bring them home and normalise.
            const pixels = loop.map(([x2, y2]) => [left + x2 / 2, top + y2 / 2]);
            const simplified = simplify(pixels, SIMPLIFY_EPSILON);
            if (simplified.length < 3) continue;

            // Glyph space: pen at x = 0, baseline at y = 0, cap height = 1, y up.
            const flat = [];
            for (const [px, py] of simplified) {
                flat.push((px - originX) / capHeight, (baselineY - py) / capHeight);
            }
            contours.push(flat);
        }

        return contours;
    }

    let dirty = false;
    window.addEventListener("beforeunload", (e) => {
        if (dirty) {
            e.preventDefault();
            e.returnValue = "";
        }
    });

    // The menu and toolbar shortcuts, matched here and reported to the app so the browser does not act on them.
    const SHORTCUTS = [
        { ctrl: true, shift: true, key: "s", name: "saveAs" },
        { ctrl: true, key: "s", name: "save" },
        { ctrl: true, key: "o", name: "open" },
        { ctrl: true, key: "n", name: "new" },
        { ctrl: true, key: "i", name: "loadImage" },
        { ctrl: true, key: "t", name: "topView" },
        { ctrl: true, key: "f", name: "fitMarina" },
        { key: "F1", name: "shortcuts" },
        { key: "F2", name: "rename" },
    ];

    window.vmDesigner = {
        clickElement(id) {
            document.getElementById(id)?.click();
        },

        setDirty(flag) {
            dirty = !!flag;
        },

        registerShortcuts(dotNet) {
            window.addEventListener("keydown", (e) => {
                const target = e.target;
                const typing = target && (target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.tagName === "SELECT");
                for (const s of SHORTCUTS) {
                    if (!!s.ctrl !== (e.ctrlKey || e.metaKey)) continue;
                    if (!!s.shift !== e.shiftKey) continue;
                    if (s.key.toLowerCase() !== e.key.toLowerCase()) continue;
                    if (typing && s.ctrl !== true && s.key.length === 1) continue;
                    e.preventDefault();
                    dotNet.invokeMethodAsync("OnShortcut", s.name);
                    return;
                }
            });
        },

        downloadText(name, text) {
            const url = URL.createObjectURL(new Blob([text], { type: "application/json" }));
            const link = document.createElement("a");
            link.href = url;
            link.download = name;
            link.click();
            URL.revokeObjectURL(url);
        },

        fontFamilies() {
            const found = [];
            for (const family of CANDIDATES) {
                if (isFontAvailable(family)) found.push(family);
            }
            found.sort((a, b) => a.localeCompare(b, undefined, { sensitivity: "base" }));
            return found;
        },

        captureFont(family, bold) {
            try {
                return captureFont(family, bold);
            } catch {
                return null;
            }
        },
    };
})();
