/*
 * Reports how long a page stayed open.
 *
 * The server records that the page was served; only the browser knows how long it was read.
 * The two halves are deliberate: capture works without this file, so a blocked or failed
 * script costs the dwell time and nothing else.
 *
 * Time is counted only while the page is visible, so a tab left in the background does not
 * accumulate. The report is sent with sendBeacon, which survives the page being closed;
 * ordinary requests are cancelled at that moment.
 */
(function () {
    'use strict';

    var ENDPOINT = '/api/stats/ping';

    var viewId = null;
    var visibleSince = null;
    var accumulated = 0;
    var lastSent = 0;

    function readViewId() {
        var meta = document.querySelector('meta[name="x-view-id"]');
        return meta ? meta.getAttribute('content') : null;
    }

    function elapsed() {
        var total = accumulated;
        if (visibleSince !== null) total += (Date.now() - visibleSince) / 1000;
        return Math.round(total);
    }

    function send() {
        if (!viewId) return;

        var seconds = elapsed();

        // Nothing new to say. Re-reporting the same figure on every visibility change would
        // triple the requests for no extra information.
        if (seconds <= lastSent || seconds < 1) return;

        lastSent = seconds;

        var url = ENDPOINT + '?v=' + encodeURIComponent(viewId) + '&s=' + seconds;

        try {
            if (navigator.sendBeacon) {
                navigator.sendBeacon(url);
                return;
            }
            // Older browsers: a keepalive fetch is the next best thing.
            fetch(url, { method: 'POST', keepalive: true, credentials: 'omit' });
        } catch (e) {
            /* reporting is optional; never let it surface */
        }
    }

    function pause() {
        if (visibleSince === null) return;
        accumulated += (Date.now() - visibleSince) / 1000;
        visibleSince = null;
    }

    function resume() {
        if (visibleSince === null) visibleSince = Date.now();
    }

    function start() {
        var id = readViewId();

        // Enhanced navigation swaps the DOM without a reload, so a new page means a new id.
        // Report what the previous page earned before switching to it.
        if (id !== viewId) {
            send();
            viewId = id;
            accumulated = 0;
            lastSent = 0;
            visibleSince = null;
        }

        if (document.visibilityState !== 'hidden') resume();
    }

    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'hidden') {
            pause();
            send();
        } else {
            resume();
        }
    });

    // pagehide is the reliable one on mobile Safari, where unload often never fires.
    window.addEventListener('pagehide', function () {
        pause();
        send();
    });

    document.addEventListener('DOMContentLoaded', start);
    document.addEventListener('blazor-enhanced-load', start);

    if (window.Blazor && window.Blazor.addEventListener) {
        window.Blazor.addEventListener('enhancedload', start);
    }

    start();
})();
