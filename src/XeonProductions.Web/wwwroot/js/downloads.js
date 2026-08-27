/*
 * Upload for the downloads admin.
 *
 * Posts the file directly to the upload endpoint as multipart form data, bypassing the Blazor
 * circuit so the server can stream it to disk. Uses XHR rather than fetch because only XHR
 * reports upload progress.
 */
(function () {
    'use strict';

    var UPLOAD_URL = '/admin/api/downloads/upload';
    var TOKEN_URL = '/admin/api/downloads/token';

    function element(id) {
        return id ? document.getElementById(id) : null;
    }

    function setStatus(node, text) {
        if (node) node.textContent = text;
    }

    function setProgress(node, value, max) {
        if (!node) return;

        node.max = max;
        node.value = value;
        node.hidden = false;
    }

    function formatSize(bytes) {
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
        if (bytes < 1073741824) return (bytes / 1048576).toFixed(1) + ' MB';
        return (bytes / 1073741824).toFixed(2) + ' GB';
    }

    /* Fetched immediately before the post, so a page left open for hours still has a valid one. */
    function requestToken() {
        return fetch(TOKEN_URL, { credentials: 'same-origin' })
            .then(function (response) {
                if (!response.ok) throw new Error('Could not start the upload. Try reloading the page.');
                return response.json();
            })
            .then(function (body) { return body.token; });
    }

    function send(token, file, title, replaceId, progressNode, statusNode) {
        return new Promise(function (resolve) {
            var form = new FormData();

            /* The title must precede the file: the server reads the body once, forwards. */
            if (title) form.append('title', title);
            form.append('file', file, file.name);

            var url = replaceId ? UPLOAD_URL + '?id=' + encodeURIComponent(replaceId) : UPLOAD_URL;
            var xhr = new XMLHttpRequest();

            xhr.open('POST', url, true);
            xhr.setRequestHeader('RequestVerificationToken', token);
            xhr.withCredentials = true;

            xhr.upload.onprogress = function (event) {
                if (!event.lengthComputable) {
                    setStatus(statusNode, 'Uploading ' + file.name + '...');
                    return;
                }

                setProgress(progressNode, event.loaded, event.total);

                var percent = Math.floor((event.loaded / event.total) * 100);
                setStatus(statusNode,
                    'Uploading ' + file.name + ' - ' + percent + '% of ' + formatSize(event.total));
            };

            // The server still has to finish writing and hashing after the last byte arrives.
            xhr.upload.onload = function () {
                setStatus(statusNode, 'Storing ' + file.name + '...');
            };

            xhr.onload = function () {
                var body = null;

                try {
                    body = JSON.parse(xhr.responseText);
                } catch (e) {
                    body = null;
                }

                if (xhr.status >= 200 && xhr.status < 300 && body) {
                    resolve({ ok: true, id: body.id, slug: body.slug, url: body.url, size: body.size });
                    return;
                }

                resolve({
                    ok: false,
                    message: (body && body.message)
                        || 'The upload failed with status ' + xhr.status + '.'
                });
            };

            xhr.onerror = function () {
                resolve({ ok: false, message: 'The connection dropped during the upload.' });
            };

            xhr.onabort = function () {
                resolve({ ok: false, message: 'The upload was cancelled.' });
            };

            xhr.send(form);
        });
    }

    /*
     * Called from the admin component. Writes progress straight into the DOM rather than
     * through interop, which would put a round trip on the circuit per progress event.
     *
     * Resolves to { ok, id, slug, url, size } or { ok: false, message }.
     */
    window.xeonUploadDownload = function (inputId, progressId, statusId, title, replaceId) {
        var input = element(inputId);
        var progressNode = element(progressId);
        var statusNode = element(statusId);

        if (!input || !input.files || input.files.length === 0) {
            return Promise.resolve({ ok: false, message: 'Choose a file first.' });
        }

        var file = input.files[0];

        setProgress(progressNode, 0, file.size);
        setStatus(statusNode, 'Preparing ' + file.name + '...');

        return requestToken()
            .then(function (token) {
                return send(token, file, title, replaceId, progressNode, statusNode);
            })
            .then(function (result) {
                if (progressNode) progressNode.hidden = true;
                setStatus(statusNode, '');

                // Clear the selection so a second click cannot re-send the same file.
                if (result.ok) input.value = '';

                return result;
            })
            .catch(function (error) {
                if (progressNode) progressNode.hidden = true;
                setStatus(statusNode, '');

                return { ok: false, message: error.message || 'The upload failed.' };
            });
    };

    /* Falls back to a throwaway textarea where the clipboard API is unavailable. */
    window.xeonCopyText = function (text) {
        if (navigator.clipboard && window.isSecureContext) {
            return navigator.clipboard.writeText(text)
                .then(function () { return true; })
                .catch(function () { return false; });
        }

        var scratch = document.createElement('textarea');

        scratch.value = text;
        scratch.setAttribute('readonly', '');
        scratch.style.position = 'fixed';
        scratch.style.opacity = '0';

        document.body.appendChild(scratch);

        try {
            scratch.select();
            return Promise.resolve(document.execCommand('copy'));
        } catch (e) {
            return Promise.resolve(false);
        } finally {
            document.body.removeChild(scratch);
        }
    };
})();
