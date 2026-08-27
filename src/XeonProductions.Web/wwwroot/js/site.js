/*
 * Progressive enhancement for the public theme.
 *
 * Blazor's enhanced navigation swaps the DOM in place, so everything here is bound once on
 * document and works by delegation. Re-binding per page would silently stop working after
 * the first client-side navigation.
 */
(function () {
    'use strict';

    var THEME_KEY = 'xeon-theme';

    function storedTheme() {
        try {
            var t = localStorage.getItem(THEME_KEY);
            return (t === 'dark' || t === 'light') ? t : null;
        } catch (e) {
            return null;
        }
    }

    function systemPrefersDark() {
        return !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
    }

    function activeTheme() {
        return storedTheme() || (systemPrefersDark() ? 'dark' : 'light');
    }

    /*
     * Puts the stored choice back on the root element.
     *
     * This has to be re-applied rather than set once. Enhanced navigation patches the live
     * DOM to match the server's response, and the server renders <html> with no data-theme
     * attribute, so every navigation would otherwise strip the visitor's choice and snap the
     * page back to the system default.
     */
    function restoreTheme() {
        var stored = storedTheme();
        if (!stored) return;

        if (document.documentElement.getAttribute('data-theme') !== stored) {
            document.documentElement.setAttribute('data-theme', stored);
        }
    }

    function applyTheme(theme) {
        document.documentElement.setAttribute('data-theme', theme);

        try {
            localStorage.setItem(THEME_KEY, theme);
        } catch (e) {
            /* private mode: the choice just will not persist */
        }

        syncToggles(theme);
    }

    function syncToggles(theme) {
        var label = theme === 'dark' ? 'Switch to the light theme' : 'Switch to the dark theme';

        document.querySelectorAll('[data-theme-toggle]').forEach(function (button) {
            button.setAttribute('aria-label', label);
            button.setAttribute('title', label);
            button.setAttribute('aria-pressed', theme === 'dark' ? 'true' : 'false');
        });
    }

    document.addEventListener('click', function (event) {
        var toggle = event.target.closest('[data-theme-toggle]');
        if (toggle) {
            event.preventDefault();
            applyTheme(activeTheme() === 'dark' ? 'light' : 'dark');
            return;
        }

        var menuButton = event.target.closest('[data-menu-toggle]');
        if (menuButton) {
            event.preventDefault();
            var menu = document.getElementById(menuButton.getAttribute('aria-controls'));
            if (!menu) return;

            var open = menu.classList.toggle('is-open');
            menuButton.setAttribute('aria-expanded', open ? 'true' : 'false');
        }
    });

    // Close the mobile menu when a link inside it is followed.
    document.addEventListener('click', function (event) {
        var link = event.target.closest('.main-nav a');
        if (!link) return;

        var menu = link.closest('.main-nav');
        if (menu && menu.classList.contains('is-open')) {
            menu.classList.remove('is-open');
            var button = document.querySelector('[data-menu-toggle][aria-controls="' + menu.id + '"]');
            if (button) button.setAttribute('aria-expanded', 'false');
        }
    });

    /*
     * Watching the attribute is what makes this reliable. Blazor's enhancedload event is
     * only available once the framework has started, and registering for it from here is a
     * race; an observer catches the attribute being removed however it happens.
     */
    function watchForThemeReset() {
        if (!window.MutationObserver) return;

        new MutationObserver(function () {
            restoreTheme();
            syncToggles(activeTheme());
        }).observe(document.documentElement, {
            attributes: true,
            attributeFilter: ['data-theme']
        });
    }

    function onReady() {
        restoreTheme();
        syncToggles(activeTheme());
    }

    document.addEventListener('DOMContentLoaded', onReady);

    // Enhanced navigation replaces the header, so the toggle label needs re-stamping.
    document.addEventListener('blazor-enhanced-load', onReady);

    if (window.Blazor && window.Blazor.addEventListener) {
        window.Blazor.addEventListener('enhancedload', onReady);
    }

    watchForThemeReset();
    onReady();
})();
