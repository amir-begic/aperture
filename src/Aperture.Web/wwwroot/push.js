// Notifications on this device, for Settings → Notifications (Features/Notifications/NotificationSettings.razor).
// Imported as a module. Turning them on happens here in the browser, inside the click itself: Safari only asks for
// permission during the user's gesture, and a round trip to the server (Blazor) would be too late.

let component = null;

const supported = () => "serviceWorker" in navigator && "PushManager" in window && "Notification" in window;

const keyBytes = base64Url => {
    const base64 = base64Url.replace(/-/g, "+").replace(/_/g, "/");
    return Uint8Array.from(atob(base64 + "=".repeat((4 - base64.length % 4) % 4)), c => c.charCodeAt(0));
};

const sameKey = (buffer, base64Url) => {
    if (!buffer) return false;
    const a = new Uint8Array(buffer), b = keyBytes(base64Url);
    return a.length === b.length && a.every((byte, i) => byte === b[i]);
};

const toJson = subscription => {
    const json = subscription.toJSON();
    return { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth };
};

// "iPhone", "Chrome on Windows": so the list in Settings says which device is which.
const deviceName = () => {
    const ua = navigator.userAgent;
    if (/iPhone/.test(ua)) return "iPhone";
    if (/iPad/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1)) return "iPad";
    if (/Android/.test(ua)) return "Android";
    const browser = /Edg\//.test(ua) ? "Edge" : /Firefox\//.test(ua) ? "Firefox" : /Chrome\//.test(ua) ? "Chrome"
        : /Safari\//.test(ua) ? "Safari" : "Browser";
    const os = /Windows/.test(ua) ? "Windows" : /Mac OS X/.test(ua) ? "Mac" : /Linux/.test(ua) ? "Linux" : "";
    return os ? `${browser} on ${os}` : browser;
};

const subscribe = async key => {
    const registration = await navigator.serviceWorker.ready;
    return registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes(key) });
};

// The settings component, to hand it the new subscription.
export const connect = dotnet => { component = dotnet; };

export const disconnect = () => { component = null; };

/**
 * What this device can do and whether it's subscribed. A subscription made with an older key of Aperture's
 * is renewed, since push services refuse messages for it.
 */
export async function state(key) {
    const result = {
        supported: supported(),
        secure: window.isSecureContext,
        permission: "default",
        subscription: null,
        device: deviceName(),
    };
    if (!result.supported) return result;

    result.permission = Notification.permission;
    const registration = await navigator.serviceWorker.getRegistration();
    let subscription = await registration?.pushManager.getSubscription();
    if (subscription && !sameKey(subscription.options.applicationServerKey, key)) {
        await subscription.unsubscribe();
        subscription = result.permission === "granted" ? await subscribe(key).catch(() => null) : null;
    }
    result.subscription = subscription ? toJson(subscription) : null;
    return result;
}

export async function unsubscribe() {
    const registration = await navigator.serviceWorker.getRegistration();
    const subscription = await registration?.pushManager.getSubscription();
    await subscription?.unsubscribe();
}

// <button data-push-enable="Aperture's public key">: asks for permission, subscribes and reports back.
document.addEventListener("click", async event => {
    const button = event.target.closest?.("[data-push-enable]");
    if (!button || !supported()) return;
    event.preventDefault();
    button.disabled = true;
    try {
        // Called first, before anything else is awaited, so it still counts as part of the click.
        const permission = await Notification.requestPermission();
        if (permission !== "granted") {
            await component?.invokeMethodAsync("PermissionChanged", permission);
            return;
        }
        const subscription = await subscribe(button.getAttribute("data-push-enable"));
        await component?.invokeMethodAsync("SubscribedAsync", toJson(subscription), deviceName());
    } catch (error) {
        await component?.invokeMethodAsync("Failed", String(error?.message ?? error));
    } finally {
        button.disabled = false;
    }
});
