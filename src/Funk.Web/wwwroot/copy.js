// Copy buttons: <button data-copy="text to copy" data-copy-status="id of an element for "Copied">. Handled here in the browser, inside the tap itself: iPhones only
// allow copying during the user's gesture, and a round trip to the server (Blazor) would be too late.
(() => {
    const fallbackCopy = text => {
        // Older browsers and plain-HTTP pages have no clipboard API: copy from a temporary, selected text field.
        const field = document.createElement("textarea");
        field.value = text;
        field.setAttribute("readonly", "");
        field.style.position = "fixed";
        field.style.opacity = "0";
        document.body.appendChild(field);
        field.select();
        field.setSelectionRange(0, text.length);
        let ok = false;
        try { ok = document.execCommand("copy"); } catch { ok = false; }
        field.remove();
        return ok;
    };

    const show = (button, ok) => {
        const status = document.getElementById(button.getAttribute("data-copy-status") || "");
        if (status) {
            status.textContent = ok ? "Copied" : "Couldn't copy: select the link and copy it";
        }
        button.classList.toggle("copied", ok);
        clearTimeout(button._copyTimer);
        button._copyTimer = setTimeout(() => {
            button.classList.remove("copied");
            if (status) status.textContent = "";
        }, 2500);
    };

    document.addEventListener("click", event => {
        const button = event.target.closest?.("[data-copy]");
        if (!button) return;
        event.preventDefault();
        const text = button.getAttribute("data-copy");
        if (navigator.clipboard?.writeText && window.isSecureContext) {
            navigator.clipboard.writeText(text).then(() => show(button, true), () => show(button, fallbackCopy(text)));
        } else {
            show(button, fallbackCopy(text));
        }
    });
})();
