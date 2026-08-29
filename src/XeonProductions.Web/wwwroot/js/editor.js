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

    /*
     * Whether the editor's own markup is still in the document. An editor left behind by a
     * navigation answers no, and throws rather than answering once it is far enough gone.
     */
    function isAttached(editor) {
        try {
            var container = editor.getContainer();
            return !!container && container.isConnected;
        } catch (e) {
            return false;
        }
    }

    /*
     * Removes the editor with this id, along with any whose markup has already left the
     * document.
     *
     * The Blazor wrapper tears down from a synchronous Dispose that drops the resulting
     * task, and the call it makes identifies the editor by an element reference that no
     * longer resolves once a navigation has removed the element. The editor stays
     * registered with a detached iframe, and the next one to start throws out of TinyMCE's
     * resize handling and never appears. Addressing by id is what survives the element
     * going away.
     */
    function releaseEditor(id) {
        var tinymce = window.tinymce;
        if (!tinymce || !tinymce.editors) return;

        Array.prototype.slice.call(tinymce.editors).forEach(function (editor) {
            if (editor.id !== id && isAttached(editor)) return;

            try {
                editor.remove();
            } catch (e) {
                // A detached editor throws on the way out. It is going regardless, and
                // TinyMCE marks it removed before it reaches anything that can fail.
            }
        });
    }

    return { prefersDark: prefersDark, releaseEditor: releaseEditor };
})();
