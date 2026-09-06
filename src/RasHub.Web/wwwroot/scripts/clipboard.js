window.rasHubClipboard = {
    async writeText(text) {
        if (navigator.clipboard?.writeText) {
            try {
                await navigator.clipboard.writeText(text);
                return;
            } catch {
                // Fall back for denied clipboard permissions and older browsers.
            }
        }

        const textArea = document.createElement("textarea");
        textArea.value = text;
        textArea.setAttribute("readonly", "");
        textArea.style.position = "fixed";
        textArea.style.opacity = "0";
        textArea.style.pointerEvents = "none";

        document.body.appendChild(textArea);
        textArea.focus();
        textArea.select();
        textArea.setSelectionRange(0, textArea.value.length);

        try {
            if (!document.execCommand("copy")) {
                throw new Error("The browser rejected the clipboard operation.");
            }
        } finally {
            textArea.remove();
        }
    }
};
