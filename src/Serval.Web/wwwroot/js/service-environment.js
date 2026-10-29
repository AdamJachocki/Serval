(() => {
    "use strict";
    const section = document.getElementById("service-environment");
    if (!section) return;
    const status = document.getElementById("environment-status");
    const rows = document.getElementById("environment-rows");
    const retry = document.getElementById("environment-retry");
    const mask = "••••••••";
    let generation = 0;
    let metadataRequest;
    const resetRows = new Set();
    const endpoint = handler => {
        const url = new URL(window.location.href);
        url.search = "";
        url.searchParams.set("handler", handler);
        return url;
    };
    const sourceLabel = source => source.kind === 0 || source.Kind === 0
        ? "Deklaracje systemd (łącznie)" : `Plik środowiska #${source.id ?? source.Id}`;
    function clear() {
        generation++;
        metadataRequest?.abort();
        for (const reset of resetRows) reset();
        resetRows.clear();
        rows.replaceChildren();
    }
    function authentication(response) {
        if (response.status === 401) {
            clear();
            window.location.assign("/Login");
            return false;
        }
        return true;
    }
    function addRow(variable, sources, canReveal) {
        const row = document.createElement("div");
        row.className = "environment-row";
        const name = document.createElement("strong");
        name.textContent = variable.name;
        name.style.overflowWrap = "anywhere";
        const source = document.createElement("p");
        source.className = "environment-row__source";
        source.textContent = sourceLabel(sources.find(item => item.id === variable.winningSourceId));
        const value = document.createElement("pre");
        value.className = "environment-row__value";
        value.style.whiteSpace = "pre-wrap";
        value.style.overflowWrap = "anywhere";
        value.textContent = mask;
        const message = document.createElement("p");
        message.className = "environment-row__message";
        message.setAttribute("role", "status");
        row.append(name, source, value, message);
        if (canReveal && variable.name.length <= 4096) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "button button--secondary button--compact";
            button.textContent = "Pokaż";
            let sequence = 0;
            let request;
            let active = false;
            const hide = () => {
                sequence++;
                request?.abort();
                request = undefined;
                active = false;
                value.textContent = mask;
                message.textContent = "";
                button.textContent = "Pokaż";
            };
            resetRows.add(hide);
            button.addEventListener("click", async () => {
                if (active) { hide(); return; }
                active = true;
                const current = ++sequence;
                const page = generation;
                request = new AbortController();
                button.textContent = "Ukryj";
                message.textContent = "Pobieranie wartości…";
                const form = new URLSearchParams({ service: section.dataset.service, name: variable.name,
                    __RequestVerificationToken: document.querySelector("#environment-token input[name='__RequestVerificationToken']").value });
                try {
                    const response = await fetch(endpoint("Reveal"), { method: "POST", body: form,
                        signal: request.signal, cache: "no-store", credentials: "same-origin" });
                    if (current !== sequence || page !== generation || !authentication(response)) return;
                    if (!response.ok) throw new Error("unavailable");
                    const data = await response.json();
                    if (current !== sequence || page !== generation) return;
                    value.textContent = data.value;
                    source.textContent = sourceLabel(data.source);
                    message.textContent = data.value === "" ? "Pusta wartość" : "";
                    data.value = null;
                } catch {
                    if (current !== sequence || page !== generation) return;
                    hide();
                    message.textContent = "Nie można ujawnić wartości. Sprawdź uprawnienia i spróbuj ponownie.";
                }
            });
            row.append(button);
        } else if (canReveal) {
            message.textContent = "Nazwa przekracza limit ujawniania (4096 znaków).";
        }
        rows.append(row);
    }
    async function load() {
        clear();
        const page = generation;
        retry.hidden = true;
        status.textContent = "Wczytywanie środowiska…";
        metadataRequest = new AbortController();
        try {
            const url = endpoint("Environment");
            url.searchParams.set("service", section.dataset.service);
            const response = await fetch(url, { signal: metadataRequest.signal, cache: "no-store", credentials: "same-origin" });
            if (page !== generation || !authentication(response)) return;
            if (response.status === 404) { status.textContent = "Brak dostępu do środowiska usługi."; return; }
            const data = await response.json();
            if (page !== generation) return;
            if (!response.ok) {
                status.textContent = data.error || "Środowisko jest chwilowo niedostępne.";
                retry.hidden = false;
                return;
            }
            for (const variable of data.variables) addRow(variable, data.sources, data.canReveal);
            status.textContent = data.variables.length ? "Wartości są ukryte." : "Brak deklaracji środowiska.";
        } catch {
            if (page !== generation) return;
            rows.replaceChildren();
            status.textContent = "Środowisko jest chwilowo niedostępne.";
            retry.hidden = false;
        }
    }
    retry.addEventListener("click", load);
    window.addEventListener("pagehide", clear);
    window.addEventListener("pageshow", event => { if (event.persisted) { clear(); window.location.reload(); } });
    load();
})();
