// Aperture's forms are about many clients and places, not about the person typing, so the browser shouldn't offer to
// save or fill them in ("Save address?" after every client or location). Forms get autocomplete="off", which Firefox
// and Safari follow; fields get a value browsers don't recognize, since Chrome and Edge ignore "off" on address fields
// but don't autofill fields they can't classify. Radzen renders autocomplete="on" on its inputs, and Blazor leaves an
// attribute alone until its rendered value changes, so this sticks. Sign-in forms opt out (data-autofill), so password
// managers keep working there.
(() => {
    const fieldValue = "aperture-no-autofill";
    const outside = "[data-autofill] *";

    function optOut() {
        for (const form of document.querySelectorAll("form:not([data-autofill])")) {
            if (form.getAttribute("autocomplete") !== "off") form.setAttribute("autocomplete", "off");
        }
        for (const field of document.querySelectorAll("input, textarea")) {
            if (field.matches(outside) || field.type === "hidden" || field.type === "file") continue;
            if (field.getAttribute("autocomplete") !== fieldValue) field.setAttribute("autocomplete", fieldValue);
        }
    }

    // Pages render and change without reloading: check again after each batch of changes, once per frame.
    let scheduled = false;
    new MutationObserver(() => {
        if (scheduled) return;
        scheduled = true;
        requestAnimationFrame(() => { scheduled = false; optOut(); });
    }).observe(document.documentElement, { childList: true, subtree: true });
    optOut();
})();
