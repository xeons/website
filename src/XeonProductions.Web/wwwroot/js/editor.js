/*
 * Support for the admin rich text editor.
 *
 * The editor skin has to be chosen before TinyMCE starts, and only the browser knows which
 * theme is actually in effect, so the component asks this first and creates the editor on
 * the following render.
 */
window.xeonEditor = (function () {
    'use strict';

    function prefersDark() {
        try {
            var explicit = document.documentElement.getAttribute('data-theme');
            if (explicit === 'dark') return true;
            if (explicit === 'light') return false;

            return !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
        } catch (e) {
            return false;
        }
    }

    return { prefersDark: prefersDark };
})();
