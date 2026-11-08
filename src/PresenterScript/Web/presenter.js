(() => {
    "use strict";

    const host = window.chrome && window.chrome.webview;
    const content = document.getElementById("content");
    let current = 0;
    let mode = "edit";

    const post = (msg) => host && host.postMessage(msg);
    const sections = () => Array.from(content.querySelectorAll(".sect"));

    function setMode(newMode) {
        const next = newMode === "present" ? "present" : "edit";
        if (next === "present" && mode !== "present") checklistExpanded = false;
        mode = next;
        document.body.className = mode;
        applyChecklistCollapse();
    }

    function scrollToSection(section) {
        // Empty sections sit right before the next snippet; center that snippet instead.
        let target = section;
        if (section.classList.contains("empty") && section.nextElementSibling) {
            target = section.nextElementSibling;
        }
        const rect = target.getBoundingClientRect();
        const top = rect.height < window.innerHeight * 0.9
            ? window.scrollY + rect.top - (window.innerHeight - rect.height) / 2
            : window.scrollY + rect.top - 24;
        window.scrollTo({ top: Math.max(0, top), behavior: "smooth" });
    }

    function setCurrent(index, scroll, notify) {
        const all = sections();
        if (all.length === 0) return;
        current = Math.max(0, Math.min(index, all.length - 1));
        all.forEach((s, i) => s.classList.toggle("current", i === current));
        if (scroll) {
            requestAnimationFrame(() => scrollToSection(all[current]));
        }
        if (notify) {
            const visible = all.filter((s) => !s.classList.contains("empty"));
            const position = all.slice(0, current + 1).filter((s) => !s.classList.contains("empty")).length;
            post({ type: "current", index: current, position: Math.max(1, position), count: visible.length });
        }
    }

    function step(direction) {
        const all = sections();
        let i = current + direction;
        while (i >= 0 && i < all.length && all[i].classList.contains("empty")) {
            i += direction;
        }
        if (i >= 0 && i < all.length) {
            setCurrent(i, true, true);
        }
    }

    function markClipboard(index) {
        content.querySelectorAll(".snippet").forEach((s) => {
            const queued = parseInt(s.dataset.snippet, 10) === index;
            s.classList.toggle("queued", queued);
            const badge = s.querySelector(".copy-badge");
            if (badge && !s.classList.contains("flash")) {
                badge.textContent = queued ? "On clipboard" : "Copy";
            }
        });
    }

    function snippetAt(index) {
        return content.querySelector(`.snippet[data-snippet="${index}"]`);
    }

    // Setup checklist state survives re-renders while editing (keyed by item text).
    const checklistState = new Map();
    // In presenter mode the checklist starts collapsed; the heading toggles it.
    let checklistExpanded = false;
    const checklistHeadingSelector = ":scope > h1, :scope > h2, :scope > h3, :scope > h4";

    function applyChecklistCollapse() {
        const list = content.querySelector(".checklist");
        if (list) list.classList.toggle("collapsed", !checklistExpanded);
    }

    const checklistItemKey = (li) => {
        const clone = li.cloneNode(true);
        clone.querySelectorAll("ul, ol").forEach((n) => n.remove());
        return clone.textContent.trim();
    };

    const checklistBox = (li) => li.querySelector(":scope > input[type=checkbox], :scope > p > input[type=checkbox]");

    function setupChecklist() {
        const list = content.querySelector(".checklist");
        if (!list) return;
        list.querySelectorAll("li.task-list-item").forEach((li) => {
            const box = checklistBox(li);
            if (!box) return;
            box.disabled = false;
            box.tabIndex = -1;
            const key = checklistItemKey(li);
            if (checklistState.has(key)) box.checked = checklistState.get(key);
            li.classList.toggle("done", box.checked);
        });
        const heading = list.querySelector(checklistHeadingSelector);
        if (heading && !heading.querySelector(".checklist-count")) {
            const toggle = document.createElement("span");
            toggle.className = "checklist-toggle";
            toggle.textContent = "▾";
            heading.prepend(toggle);
            const count = document.createElement("span");
            count.className = "checklist-count";
            heading.appendChild(count);
        }
        updateChecklistCount(list);
        applyChecklistCollapse();
    }

    function updateChecklistCount(list) {
        const boxes = Array.from(list.querySelectorAll("li.task-list-item")).map(checklistBox).filter(Boolean);
        const done = boxes.filter((b) => b.checked).length;
        const count = list.querySelector(".checklist-count");
        if (count) count.textContent = `${done} / ${boxes.length}`;
        list.classList.toggle("complete", boxes.length > 0 && done === boxes.length);
    }

    function toggleChecklistItem(li, clickedBox) {
        const box = checklistBox(li);
        if (!box) return;
        // A click on the checkbox itself has already toggled it natively.
        if (!clickedBox) box.checked = !box.checked;
        checklistState.set(checklistItemKey(li), box.checked);
        li.classList.toggle("done", box.checked);
        updateChecklistCount(li.closest(".checklist"));
    }

    function copySnippet(snippet) {
        const code = snippet.querySelector("code");
        const index = parseInt(snippet.dataset.snippet, 10);
        post({ type: "copy", code: code ? code.textContent : "", snippet: index });

        snippet.classList.add("copied", "flash");
        const badge = snippet.querySelector(".copy-badge");
        if (badge) badge.textContent = "Copied";
        setTimeout(() => {
            snippet.classList.remove("flash");
            if (badge) badge.textContent = snippet.classList.contains("queued") ? "On clipboard" : "Copy";
        }, 1200);

        // The snippet's own section becomes current; the highlight moves on once the snippet is pasted.
        setCurrent(nextSectionAfter(snippet) - 1, false, true);
    }

    function nextSectionAfter(snippet) {
        return parseInt(snippet.dataset.next, 10);
    }

    function render(msg) {
        const y = window.scrollY;
        content.innerHTML = msg.html;
        setupChecklist();
        let base = document.querySelector("base");
        if (msg.baseUrl) {
            if (!base) {
                base = document.createElement("base");
                document.head.appendChild(base);
            }
            base.href = msg.baseUrl;
        } else if (base) {
            base.remove();
        }
        setMode(msg.mode);
        markClipboard(msg.clipboardSnippet);
        setCurrent(typeof msg.current === "number" ? msg.current : current, false, true);
        window.scrollTo(0, y);
    }

    host && host.addEventListener("message", (e) => {
        const msg = e.data;
        switch (msg.type) {
            case "render":
                render(msg);
                break;
            case "mode":
                setMode(msg.mode);
                setCurrent(current, true, false);
                break;
            case "setCurrent":
                setCurrent(msg.index, true, true);
                break;
            case "reset":
                content.querySelectorAll(".snippet.copied").forEach((s) => s.classList.remove("copied"));
                setCurrent(0, true, true);
                break;
            case "step":
                step(msg.direction);
                break;
            case "clipboard":
                markClipboard(msg.snippet);
                break;
            case "pasted": {
                const pasted = snippetAt(msg.snippet);
                if (pasted) {
                    pasted.classList.add("copied");
                    setCurrent(nextSectionAfter(pasted), true, true);
                }
                break;
            }
        }
    });

    document.addEventListener("click", (e) => {
        if (e.button !== 0) return;

        const link = e.target.closest("a[href]");
        if (link) {
            e.preventDefault();
            const href = link.getAttribute("href");
            if (href.startsWith("#")) {
                const el = document.getElementById(decodeURIComponent(href.substring(1)));
                if (el) el.scrollIntoView({ behavior: "smooth", block: "start" });
            } else {
                post({ type: "openLink", href: link.href });
            }
            return;
        }

        const checkHeading = e.target.closest(".checklist > h1, .checklist > h2, .checklist > h3, .checklist > h4");
        if (checkHeading) {
            if (mode === "present") {
                checklistExpanded = !checklistExpanded;
                applyChecklistCollapse();
            }
            return;
        }

        const checkItem = e.target.closest(".checklist li.task-list-item");
        if (checkItem) {
            toggleChecklistItem(checkItem, e.target === checklistBox(checkItem));
            return;
        }

        const snippet = e.target.closest(".snippet");
        if (snippet) {
            copySnippet(snippet);
            return;
        }

        // Don't steal clicks that are text selections.
        if (String(window.getSelection())) return;

        const section = e.target.closest(".sect:not(.empty)");
        if (section) {
            setCurrent(parseInt(section.dataset.idx, 10), false, true);
        }
    });

    document.addEventListener("keydown", (e) => {
        const ctrl = e.ctrlKey || e.metaKey;

        // Keys the native app handles (mode switch, file commands).
        if (e.key === "F1" || e.key === "F5" || e.key === "F11" || e.key === "Escape" ||
            (ctrl && ["s", "o", "n"].includes(e.key.toLowerCase()))) {
            e.preventDefault();
            post({ type: "key", key: e.key, ctrl, shift: e.shiftKey });
            return;
        }

        if (mode !== "present" || ctrl || e.altKey) return;

        switch (e.key) {
            case "ArrowDown":
            case "ArrowRight":
            case "PageDown":
            case " ":
                e.preventDefault();
                step(1);
                break;
            case "ArrowUp":
            case "ArrowLeft":
            case "PageUp":
                e.preventDefault();
                step(-1);
                break;
            case "Home":
                e.preventDefault();
                setCurrent(0, true, true);
                break;
        }
    });

    post({ type: "ready" });
})();
