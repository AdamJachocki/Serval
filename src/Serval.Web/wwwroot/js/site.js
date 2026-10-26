// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(() => {
    "use strict";

    const errorSummary = document.querySelector("[data-error-summary]");
    if (errorSummary) {
        errorSummary.focus();
    }

    const loginForm = document.querySelector("[data-login-form]");
    if (loginForm) {
        const password = loginForm.querySelector("[data-password-input]");
        const toggle = loginForm.querySelector("[data-password-toggle]");
        const submit = loginForm.querySelector("[data-login-submit]");
        const status = loginForm.querySelector("[data-login-status]");
        const submitLabel = submit?.textContent ?? "Zaloguj się";

        if (toggle) toggle.hidden = false;
        toggle?.addEventListener("click", () => {
            const show = password.type === "password";
            password.type = show ? "text" : "password";
            toggle.setAttribute("aria-pressed", show ? "true" : "false");
            toggle.setAttribute("aria-label", show ? "Ukryj hasło" : "Pokaż hasło");
            toggle.focus();
        });

        loginForm.addEventListener("submit", event => {
            if (!loginForm.checkValidity() || submit.disabled) {
                if (submit.disabled) event.preventDefault();
                return;
            }

            submit.disabled = true;
            submit.textContent = submit.dataset.pendingLabel;
            status.textContent = submit.dataset.pendingLabel;
        });

        window.addEventListener("pageshow", () => {
            submit.disabled = false;
            submit.textContent = submitLabel;
            status.textContent = "";
        });
    }

    const filters = document.querySelector("[data-service-filters]");
    const resultCount = document.querySelector("[data-result-count]");
    const noResults = document.querySelector("[data-no-results]");
    if (filters && resultCount && noResults) {
        const query = filters.querySelector("[data-filter-query]");
        const state = filters.querySelector("[data-filter-state]");
        const rows = [...document.querySelectorAll("[data-service-row]")];
        const clear = document.querySelector("[data-clear-filters]");
        const refresh = document.querySelector("[data-filter-refresh]");
        const serviceLinks = [...document.querySelectorAll("[data-service-link]")];

        filters.classList.add("is-enhanced");

        const asciiFold = value => value.replace(/[A-Z]/g, character => character.toLowerCase());
        const applyFilters = () => {
            const currentUrl = new URL(window.location.href);
            const serverQuery = currentUrl.searchParams.get("q") ?? "";
            const serverState = currentUrl.searchParams.get("state") ?? "";
            clear.hidden = !query.value && !state.value;
            if (/[^\x00-\x7f]/.test(query.value)) {
                if (query.value !== serverQuery || state.value !== serverState) filters.requestSubmit();
                return;
            }

            const text = asciiFold(query.value);
            const selectedState = state.value;
            let visible = 0;

            for (const row of rows) {
                const matchesText = !text || asciiFold(row.dataset.id).includes(text) ||
                    asciiFold(row.dataset.description).includes(text);
                const matchesState = !selectedState || row.dataset.state === selectedState;
                row.hidden = !(matchesText && matchesState);
                if (!row.hidden) visible += 1;
            }

            resultCount.textContent = `Wyświetlono ${visible} z ${rows.length} usług`;
            noResults.hidden = visible !== 0;
            const url = currentUrl;
            query.value ? url.searchParams.set("q", query.value) : url.searchParams.delete("q");
            state.value ? url.searchParams.set("state", state.value) : url.searchParams.delete("state");
            window.history.replaceState(null, "", url);

            for (const link of [...serviceLinks, refresh].filter(Boolean)) {
                const target = new URL(link.href, window.location.href);
                query.value ? target.searchParams.set("q", query.value) : target.searchParams.delete("q");
                state.value ? target.searchParams.set("state", state.value) : target.searchParams.delete("state");
                link.href = `${target.pathname}${target.search}`;
            }
        };

        query.addEventListener("input", applyFilters);
        state.addEventListener("change", applyFilters);
        clear?.addEventListener("click", () => {
            query.value = "";
            state.value = "";
            applyFilters();
            query.focus();
        });
        applyFilters();
    }

    const pendingAnnouncement = document.querySelector("[data-pending-announcement]");
    document.querySelectorAll("[data-pending-link]").forEach(link => {
        link.addEventListener("click", () => {
            if (pendingAnnouncement) pendingAnnouncement.textContent = "Pobieranie usług…";
            link.setAttribute("aria-disabled", "true");
        });
    });

    if (document.body.classList.contains("authenticated-page")) {
        window.addEventListener("pageshow", event => {
            if (event.persisted) window.location.reload();
        });
    }
})();
