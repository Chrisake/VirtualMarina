// Browser side of the VirtualMarina Blazor designer.
//
//   * vmDesigner.registerShortcuts — listens for the keys of the command table the app hands over (DesignerCommands).
//   * vmDesigner.attachMenuKeys    — arrow keys, Home, End and Esc in the menu bar.
//   * vmDesigner.showModal         — opens the app's <dialog> as a modal; modalClosed puts the focus back.
//   * vmDesigner.openDesign / saveDesign — the File System Access API where there is one, so Save writes back to
//     the same file; a file input and a download where there is not.
//   * vmDesigner.fontFamilies  — the fonts on this machine that a berth label could be lettered in.
//   * vmDesigner.captureFont   — traces a font's glyph outlines so the design carries its own lettering.
//   * The launcher link        — when the desktop launcher served the page, keeps a connection open to it so it
//     knows the window is still there, and shuts down once it is closed.
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

    // Which edge midpoints each of the sixteen cell cases joins, indexed by (tl << 3) | (tr << 2) | (br << 1) | bl.
    // The two saddles, 5 and 10, join two pairs, grouped by their two inside corners.
    const CELL_LINKS = [
        [], [["B", "L"]], [["R", "B"]], [["R", "L"]],
        [["T", "R"]], [["T", "R"], ["B", "L"]], [["T", "B"]], [["T", "L"]],
        [["T", "L"]], [["T", "B"]], [["T", "L"], ["R", "B"]], [["T", "R"]],
        [["R", "L"]], [["R", "B"]], [["B", "L"]], [],
    ];

    const nodeKey = (x, y) => `${x},${y}`;
    const edgeKey = (a, b) => (a < b ? `${a}|${b}` : `${b}|${a}`);

    // Traces every closed boundary in a binary grid. Each boundary is one loop of points on the half-grid,
    // outer shapes and the holes inside them alike; which is which is left to the C# side to work out.
    function traceContours(grid, width, height) {
        return stitchLoops(collectSegments(grid, width, height));
    }

    // Segments between edge midpoints, keyed by a quantised endpoint so they can be stitched into loops:
    // key -> { x, y, links: [key, key, ...] }. Coordinates are held doubled, so half-grid midpoints stay integers.
    function collectSegments(grid, width, height) {
        const nodes = new Map();
        const point = (x2, y2) => {
            const k = nodeKey(x2, y2);
            let node = nodes.get(k);
            if (!node) {
                node = { x: x2, y: y2, links: [] };
                nodes.set(k, node);
            }
            return node;
        };
        const segment = ([ax, ay], [bx, by]) => {
            point(ax, ay).links.push(nodeKey(bx, by));
            point(bx, by).links.push(nodeKey(ax, ay));
        };
        const at = (x, y) => (x < 0 || y < 0 || x >= width || y >= height ? 0 : grid[y * width + x]);

        for (let y = -1; y < height; y++) {
            for (let x = -1; x < width; x++) {
                const idx = (at(x, y) << 3) | (at(x + 1, y) << 2) | (at(x + 1, y + 1) << 1) | at(x, y + 1);

                // Edge midpoints, doubled: T=(2x+1,2y) R=(2x+2,2y+1) B=(2x+1,2y+2) L=(2x,2y+1).
                const mid = { T: [2 * x + 1, 2 * y], R: [2 * x + 2, 2 * y + 1], B: [2 * x + 1, 2 * y + 2], L: [2 * x, 2 * y + 1] };
                for (const [from, to] of CELL_LINKS[idx]) segment(mid[from], mid[to]);
            }
        }

        return nodes;
    }

    // Stitches the segments into closed loops, each edge used once.
    function stitchLoops(nodes) {
        const loops = [];
        const used = new Set();
        for (const [start, startNode] of nodes) {
            for (const first of startNode.links) {
                if (used.has(edgeKey(start, first))) continue;
                const loop = followLoop(nodes, used, start, first);
                if (loop.length >= 3) loops.push(loop);
            }
        }
        return loops;
    }

    // Walks from start along first until it comes back round, or runs out of unused edges.
    function followLoop(nodes, used, start, first) {
        const startNode = nodes.get(start);
        const loop = [[startNode.x, startNode.y]];
        let prev = start;
        let cur = first;
        used.add(edgeKey(prev, cur));

        while (cur !== start) {
            const node = nodes.get(cur);
            if (!node) break;
            loop.push([node.x, node.y]);
            const next = nextLink(node, prev, cur, used);
            if (next === null) break;
            used.add(edgeKey(cur, next));
            prev = cur;
            cur = next;
        }

        return loop;
    }

    // The first way on from a node that is not straight back and not already walked.
    function nextLink(node, prev, cur, used) {
        for (const candidate of node.links) {
            if (candidate !== prev && !used.has(edgeKey(cur, candidate))) return candidate;
        }
        return null;
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
        // A family the browser does not have is drawn in a fallback font without complaint, which would store
        // the fallback's letters under the requested name. document.fonts.check cannot tell (it answers true for
        // any font that needs no downloading), so the family is measured against the generics instead.
        if (!isFontAvailable(family)) return null;

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
        if (!Number.isFinite(capHeight) || capHeight <= 1) return null;

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
        }
    });

    // ---- Shortcuts -------------------------------------------------------------------------------

    // Inputs that take typing. A tick box, slider, colour well or button only takes a click, so shortcuts still
    // apply while one of those has the focus.
    const TEXT_INPUTS = new Set(["", "text", "search", "url", "tel", "email", "password", "number",
        "date", "datetime-local", "month", "time", "week"]);

    function isTyping(element) {
        if (element?.nodeType !== 1) return false;
        if (element.isContentEditable) return true;
        switch (element.tagName) {
            case "TEXTAREA":
            case "SELECT":
                return true;
            case "INPUT":
                return TEXT_INPUTS.has((element.getAttribute("type") || "").toLowerCase());
            default:
                return false;
        }
    }

    // True when the key pressed is the table's key. A letter follows the character printed on the key, as every
    // application's Ctrl+Z does, so it is found on an AZERTY or a Dvorak keyboard where the letter is; only on a layout
    // without Latin letters (Greek, Cyrillic), or when Option on a Mac turned it into another character, does the
    // key's position count instead. The same rule as VirtualMarina.Core's MarinaKeyMap. Other keys go by their name.
    function keyMatches(shortcut, e) {
        if (shortcut.key.length === 1) {
            const typed = e.key;
            if (typeof typed === "string" && typed.length === 1 && /[a-z]/i.test(typed)) {
                return typed.toUpperCase() === shortcut.key;
            }
            return e.code === `Key${shortcut.key}`;
        }
        return e.key === shortcut.key;
    }

    function inView(element) {
        return !!element?.closest?.(".vm-view");
    }

    function modalOpen() {
        return document.querySelector("dialog.vm-modal[open]") !== null;
    }

    // The first shortcut of the table with these modifiers and this key, or undefined.
    function findShortcut(shortcuts, e) {
        const ctrl = e.ctrlKey || e.metaKey;
        return shortcuts.find((s) => s.ctrl === ctrl && s.alt === e.altKey && s.shift === e.shiftKey && keyMatches(s, e));
    }

    // False when the page keeps the key for itself rather than handing it to the shortcut.
    function shortcutApplies(s, typing, target) {
        // A field being typed in keeps the keys it edits with.
        if (typing && !s.editing) return false;
        // An Alt+letter chord types a character on some layouts (a Mac's Option key), so a text box keeps it.
        if (typing && !s.ctrl && s.key.length === 1) return false;
        // A tool's letter is only a tool while the view has the focus; anywhere else it is just a letter.
        return !s.viewOnly || inView(target);
    }

    // A field being typed in only hands its value over when it loses the focus, so it is made to now, and the
    // command waits a turn for Blazor to take the change in: Ctrl+S straight after typing a name saves the new
    // name, not the old one.
    function runShortcut(dotNet, s, typing, target) {
        if (typing && typeof target.blur === "function") {
            target.blur();
            setTimeout(() => dotNet.invokeMethodAsync("OnShortcut", s.name), 0);
        } else {
            dotNet.invokeMethodAsync("OnShortcut", s.name);
        }
    }

    let shortcutHandler = null;

    // ---- Modal -----------------------------------------------------------------------------------

    let focusBeforeModal = null;

    // ---- Menu bar keys ---------------------------------------------------------------------------

    function menuLabels(menubar) {
        return Array.from(menubar.querySelectorAll(":scope > .vm-menu__item > .vm-menu__label"));
    }

    function menuEntries(menubar) {
        return Array.from(menubar.querySelectorAll(".vm-menu__drop > .vm-menu__entry:not(:disabled)"));
    }

    // Opens (or moves to) a menu, then puts the focus on its first entry once Blazor has drawn it.
    function openAndFocus(menubar, label, last) {
        if (label.getAttribute("aria-expanded") !== "true") label.click();
        const focusEntry = (tries) => {
            const entries = menuEntries(menubar);
            if (entries.length > 0) {
                (last ? entries.at(-1) : entries[0]).focus();
            } else if (tries > 0) {
                requestAnimationFrame(() => focusEntry(tries - 1));
            }
        };
        requestAnimationFrame(() => focusEntry(10));
    }

    function roveTo(labels, index) {
        labels.forEach((label, i) => label.setAttribute("tabindex", i === index ? "0" : "-1"));
        labels[index].focus();
    }

    // The menu bar around a key press: its titles, the open menu's entries, and which of them has the focus.
    function menuState(menubar, target) {
        const labels = menuLabels(menubar);
        const entries = menuEntries(menubar);
        const labelIndex = labels.indexOf(target);
        const openLabel = labels.find((label) => label.getAttribute("aria-expanded") === "true");
        return {
            menubar, target, labels, entries, labelIndex, openLabel,
            entryIndex: entries.indexOf(target),
            current: labelIndex >= 0 ? labelIndex : labels.indexOf(openLabel),
        };
    }

    // Left and Right: the next menu title, carrying an open menu along.
    function moveAcross(m, forward) {
        if (m.current < 0) return false;
        const next = (m.current + (forward ? 1 : m.labels.length - 1)) % m.labels.length;
        roveTo(m.labels, next);
        if (m.openLabel) openAndFocus(m.menubar, m.labels[next], false);
        return true;
    }

    // Down and Up: from a title, open its menu at the first or last entry; in a menu, the next or previous entry.
    function moveAlong(m, up) {
        if (m.labelIndex >= 0) {
            openAndFocus(m.menubar, m.target, up);
            return true;
        }
        if (m.entryIndex < 0) return false;
        m.entries[(m.entryIndex + (up ? m.entries.length - 1 : 1)) % m.entries.length].focus();
        return true;
    }

    // Home and End: the first or last entry of the open menu, or the first or last title.
    function jumpTo(m, end) {
        if (m.entryIndex >= 0 && m.entries.length) {
            (end ? m.entries.at(-1) : m.entries[0]).focus();
            return true;
        }
        if (m.labelIndex < 0) return false;
        roveTo(m.labels, end ? m.labels.length - 1 : 0);
        return true;
    }

    // Enter and Space on a closed menu's title open it.
    function openFromTitle(m) {
        if (m.labelIndex < 0 || m.target.getAttribute("aria-expanded") === "true") return false;
        openAndFocus(m.menubar, m.target, false);
        return true;
    }

    // Esc closes the open menu and goes back to its title.
    function closeMenu(m) {
        if (!m.openLabel) return false;
        m.openLabel.click();
        m.openLabel.focus();
        return true;
    }

    // Answers a key the menu bar has; returns false for one it leaves to the page.
    function menuKey(m, key) {
        switch (key) {
            case "ArrowRight": return moveAcross(m, true);
            case "ArrowLeft": return moveAcross(m, false);
            case "ArrowDown": return moveAlong(m, false);
            case "ArrowUp": return moveAlong(m, true);
            case "Home": return jumpTo(m, false);
            case "End": return jumpTo(m, true);
            case "Enter":
            case " ": return openFromTitle(m);
            case "Escape": return closeMenu(m);
            case "Tab":
                // Tab leaves the menu bar as it would any other control, closing the menu on the way.
                if (m.openLabel) m.openLabel.click();
                return false;
            default: return false;
        }
    }

    // ---- Files -----------------------------------------------------------------------------------

    // Files picked through the File System Access API, by the key handed to .NET, so Save can write back to them.
    const handles = new Map();
    let nextHandle = 1;

    const DESIGN_TYPES = [{ description: "Marina design", accept: { "application/json": [".json"] } }];

    // The File System Access API (Chromium browsers only), which the DOM typings the checker uses do not describe yet.
    const fileSystem = /** @type {any} */ (window);

    function keep(handle) {
        const key = `h${nextHandle++}`;
        handles.set(key, handle);
        return key;
    }

    function isAbort(error) {
        return !!error && (error.name === "AbortError" || (error.name === "NotAllowedError" && /abort/i.test(error.message || "")));
    }

    function downloadText(name, text) {
        const url = URL.createObjectURL(new Blob([text], { type: "application/json" }));
        const link = document.createElement("a");
        link.href = url;
        link.download = name;
        link.style.display = "none";

        // Some browsers only follow a link that is in the document, and the download starts after click()
        // returns, so the URL is kept alive a while rather than revoked on the spot.
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 60000);
    }

    // A file input where there is no picker API. Resolves null when the user cancels: through the input's cancel
    // event where the browser has one, and otherwise when the window gets the focus back with nothing picked, so a
    // cancelled Open never leaves the app waiting for an answer that will not come.
    let pendingInput = null;
    function pickWithInput() {
        if (pendingInput) pendingInput(null);
        return new Promise((resolve) => {
            const input = document.createElement("input");
            input.type = "file";
            input.accept = ".json,application/json";
            input.style.display = "none";
            const finish = (value) => {
                if (pendingInput === finish) pendingInput = null;
                input.remove();
                resolve(value);
            };
            pendingInput = finish;
            input.addEventListener("change", async () => {
                const file = input.files?.[0];
                finish(file ? { name: file.name, key: null, text: await file.text() } : null);
            });
            input.addEventListener("cancel", () => finish(null));
            window.addEventListener("focus", () => {
                setTimeout(() => {
                    if (pendingInput === finish && !input.files?.length) finish(null);
                }, 1000);
            }, { once: true });
            document.body.appendChild(input);
            input.click();
        });
    }

    // ---- The launcher ------------------------------------------------------------------------------

    // The desktop launcher opens the page with ?launcher=<token>. The token is kept for the tab, so a reload finds it
    // again. A page not opened by the launcher (served by any static host) never asks, so nothing fails there.
    const LAUNCHER_KEY = "vmDesigner.launcher";

    function launcherToken() {
        try {
            const fromUrl = new URLSearchParams(window.location.search).get("launcher");
            if (fromUrl) {
                sessionStorage.setItem(LAUNCHER_KEY, fromUrl);
                return fromUrl;
            }
            return sessionStorage.getItem(LAUNCHER_KEY);
        } catch {
            return null;
        }
    }

    // Keeps a Server-Sent Events stream open to the launcher for as long as the page lives; the launcher counts the
    // open streams and stops a few seconds after the last one closes. Leaving the page says goodbye at once, so a
    // closed window stops the launcher without waiting for the connection to time out; a reload reconnects in time.
    async function connectLauncher() {
        const token = launcherToken();
        if (!token) return;

        const query = `token=${encodeURIComponent(token)}`;
        try {
            const probe = await fetch(`launcher/heartbeat?${query}`, { cache: "no-store" });
            const info = probe.ok && (probe.headers.get("content-type") || "").includes("json") ? await probe.json() : null;
            if (info?.app !== "VirtualMarina.Designer.Launcher") return;
        } catch {
            return;
        }

        // randomUUID needs a secure context; getRandomValues does not, so the fallback is just as unguessable.
        const client = crypto.randomUUID
            ? crypto.randomUUID()
            : Array.from(crypto.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, "0")).join("");
        const events = new EventSource(`launcher/events?${query}&client=${encodeURIComponent(client)}`);
        events.addEventListener("shutdown", () => events.close());
        window.addEventListener("pagehide", () => {
            navigator.sendBeacon(`launcher/bye?${query}&client=${encodeURIComponent(client)}`);
            events.close();
        }, { once: true });
    }

    connectLauncher();

    // A page kept in the back-forward cache said goodbye as it went; coming back, it says hello again.
    window.addEventListener("pageshow", (e) => {
        if (e.persisted) connectLauncher();
    });

    window.vmDesigner = {
        clickElement(id) {
            document.getElementById(id)?.click();
        },

        setDirty(flag) {
            dirty = !!flag;
        },

        /**
         * Listens for the keys of the command table and reports the command to the app, so the browser does not act on
         * them. Registering again replaces the earlier listener; unregisterShortcuts takes it away.
         *
         * Each entry: { name, key, ctrl, shift, alt, editing, repeats, viewOnly }. editing: false marks a key a field
         * being typed in keeps for itself — Ctrl+Z undoes the typing, Esc and F2 leave the tool alone. repeats: true
         * lets a held key run its command again with every repeat. viewOnly: true is a tool's letter, which only counts
         * while the 3D view has the focus.
         */
        registerShortcuts(dotNet, table) {
            window.vmDesigner.unregisterShortcuts();
            const shortcuts = Array.isArray(table) ? table : [];
            shortcutHandler = (e) => {
                // Taken already (the view moving the camera), or part of an input method composing a character.
                if (e.defaultPrevented || e.isComposing || e.keyCode === 229) return;

                const target = e.target;
                const typing = isTyping(target);
                const s = findShortcut(shortcuts, e);
                if (!s || !shortcutApplies(s, typing, target)) return;

                // Esc is never taken from the browser, which uses it to close a picker or leave full screen.
                if (s.name !== "escape") e.preventDefault();
                // The modal answers its own keys, Esc included; the rest wait until it is gone.
                if (modalOpen()) return;
                // Holding the keys down would otherwise save, or open, over and over.
                if (e.repeat && !s.repeats) return;

                runShortcut(dotNet, s, typing, target);
            };
            window.addEventListener("keydown", shortcutHandler);
        },

        unregisterShortcuts() {
            if (!shortcutHandler) return;
            window.removeEventListener("keydown", shortcutHandler);
            shortcutHandler = null;
        },

        /**
         * The menu bar's keys, as a menu bar has them: Left and Right move between the menus (and carry an open one
         * along), Down, Enter and Space open one, Up and Down move through its entries, Home and End jump, and Esc
         * closes it and goes back to its title. Tab leaves the menu bar as it would any other control.
         */
        attachMenuKeys(menubar) {
            if (!menubar || menubar.vmMenuKeys) return;
            menubar.vmMenuKeys = true;
            menubar.addEventListener("keydown", (e) => {
                if (!menuKey(menuState(menubar, e.target), e.key)) return;
                e.preventDefault();
                e.stopPropagation();
            });
        },

        /** Opens the app's dialog as a modal: the focus stays inside it and the page behind is inert. */
        showModal(dialog) {
            if (!dialog || typeof dialog.showModal !== "function") return;
            if (!dialog.open) {
                focusBeforeModal = document.activeElement;
                // Esc is answered by the app (it cancels the question), not by the browser closing the dialog
                // behind Blazor's back.
                dialog.addEventListener("cancel", (e) => e.preventDefault());
                dialog.showModal();
            }
            const first = dialog.querySelector("input, .vm-btn--primary");
            if (first) {
                first.focus();
                if (typeof first.select === "function") first.select();
            }
        },

        /** The last question was answered: the focus goes back to wherever it was before it was asked. */
        modalClosed() {
            const previous = focusBeforeModal;
            focusBeforeModal = null;
            if (previous?.isConnected && typeof previous.focus === "function") previous.focus();
        },

        /**
         * Asks for a design to open. Returns { name, key, text }, key being a handle to write back to (or null), or
         * null when the user cancelled.
         */
        async openDesign() {
            if (typeof fileSystem.showOpenFilePicker === "function") {
                try {
                    const [handle] = await fileSystem.showOpenFilePicker({ types: DESIGN_TYPES, multiple: false });
                    const file = await handle.getFile();
                    return { name: file.name, key: keep(handle), text: await file.text() };
                } catch (error) {
                    if (isAbort(error)) return null;
                    throw error;
                }
            }
            return pickWithInput();
        },

        /**
         * Writes a design: back to the file a key names, or to one the user picks, or — in a browser without the
         * File System Access API — as a download. Returns { name, key }, or null when the user cancelled the picker.
         * A download cannot tell whether it was kept, so it counts as saved.
         */
        async saveDesign(name, text, key) {
            let handle = key ? handles.get(key) : null;
            if (!handle && typeof fileSystem.showSaveFilePicker === "function") {
                try {
                    handle = await fileSystem.showSaveFilePicker({ suggestedName: name, types: DESIGN_TYPES });
                    key = keep(handle);
                } catch (error) {
                    if (isAbort(error)) return null;
                    throw error;
                }
            }

            if (!handle) {
                downloadText(name, text);
                return { name, key: null };
            }

            if (typeof handle.requestPermission === "function" &&
                (await handle.requestPermission({ mode: "readwrite" })) !== "granted") {
                throw new Error("Permission to write the file was refused.");
            }
            const writable = await handle.createWritable();
            await writable.write(text);
            await writable.close();
            return { name: handle.name, key };
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
