const darkModeQuery = window.matchMedia("(prefers-color-scheme: dark)");
let changeHandler;

export function getSystemDarkMode() {
    return darkModeQuery.matches;
}

export function watchSystemDarkMode(dotNetReference) {
    stopWatchingSystemDarkMode();

    changeHandler = event =>
        dotNetReference.invokeMethodAsync(
            "OnSystemDarkModeChanged",
            event.matches);

    darkModeQuery.addEventListener("change", changeHandler);
}

export function stopWatchingSystemDarkMode() {
    if (!changeHandler) {
        return;
    }

    darkModeQuery.removeEventListener("change", changeHandler);
    changeHandler = undefined;
}
